using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Boss encounter configuration for Lace 2 with hybrid observations.
    /// Combines vector state (positions, velocities, HP, attack) with greyscale screen capture.
    /// 
    /// Observation layout: [vector_obs (N floats) | visual_obs (W*H floats)]
    /// Python side should split and route visual through CNN, vector through MLP.
    /// </summary>
    public class LaceSecondEncounter : IBossEncounter
    {
        private const float MIN_POS_X = 33f;
        private const float MAX_POS_X = 77f;
        private const float MIN_POS_Y = 96f;
        private const float MAX_POS_Y = 110f;
        private const float MAX_HERO_VELOCITY = 30f;
        private const float MAX_BOSS_VELOCITY = 70f;
        private const float MAX_HERO_HP = 10f;
        private const float MAX_BOSS_HP = 800f;
        private const string BOSS_NAME = "Lace Boss2 New";

        private int stuckPreviousHealth = 10;

        private const int VISUAL_WIDTH = 100;
        private const int VISUAL_HEIGHT = 100;
        private const int CROP_TOP = 200;
        private const int CROP_BOTTOM = 200;
        private const int CROP_LEFT = 0;
        private const int CROP_RIGHT = 0;

        public ScreenCapture ScreenCapture { get; } = new ScreenCapture(
            VISUAL_WIDTH,
            VISUAL_HEIGHT,
            CROP_TOP,
            CROP_BOTTOM,
            CROP_LEFT,
            CROP_RIGHT
        );

        // Seconds since Lace's current attack started, scaled so 1 means this long or more. Her attacks
        // run up to about 1.9s.
        private const float MAX_ATTACK_TIME = 2f;

        // 17 values about Hornet and Lace, then Lace's attack (one-hot) and how far into it she is
        private readonly int vectorObsSize = 17 + LaceEncounter.NUM_ATTACK_CATEGORIES + 1;

        public string GetEncounterName()
        {
            return BOSS_NAME;
        }

        public ActionSpaceType GetActionSpaceType()
        {
            return ActionSpaceType.Extended;
        }

        public ObservationType GetObservationType()
        {
            return ObservationType.Hybrid;
        }

        public int GetVectorObservationSize()
        {
            return vectorObsSize;
        }

        public (int width, int height) GetVisualObservationSize()
        {
            return (VISUAL_WIDTH, VISUAL_HEIGHT);
        }

        public bool IsEncounterMatch(HealthManager hm)
        {
            return hm != null && hm.name == BOSS_NAME;
        }

        public float[] ExtractObservationArray(HeroController hero, HealthManager boss)
        {
            if (hero == null || boss == null)
                return null;

            float[] visualObs = ScreenCapture.GetCachedObservation();
            if (visualObs == null)
            {
                return null;
            }

            float[] vectorObs = ExtractVectorObservations(hero, boss);
            if (vectorObs == null)
                return null;

            float[] combined = new float[vectorObsSize + visualObs.Length];
            System.Array.Copy(vectorObs, 0, combined, 0, vectorObsSize);
            System.Array.Copy(visualObs, 0, combined, vectorObsSize, visualObs.Length);

            return combined;
        }

        private float[] ExtractVectorObservations(HeroController hero, HealthManager boss)
        {
            Vector2 heroPos = hero.transform.position;
            Rigidbody2D heroRb = hero.GetComponent<Rigidbody2D>();
            Vector2 heroVel = heroRb ? heroRb.velocity : Vector2.zero;
            int heroHealth = hero.playerData.health;

            Vector2 bossPos = boss.transform.position;
            Rigidbody2D bossRb = boss.GetComponent<Rigidbody2D>();
            Vector2 bossVel = bossRb ? bossRb.velocity : Vector2.zero;
            int bossHealth = boss.hp;

            float heroFacing = hero.cState.facingRight ? 1f : 0f;
            float heroDashing = hero.cState.dashing ? 1f : 0f;
            float heroSprinting = hero.cState.isSprinting ? 1f : 0f;
            float heroAttacking = hero.cState.attacking ? 1f : 0f;
            float heroJumping = hero.cState.jumping ? 1f : 0f;
            float heroRecoiling = hero.cState.recoiling ? 1f : 0f;
            float heroInvulnerable = hero.cState.Invulnerable ? 1f : 0f;

            float heroX = Mathf.Clamp01((heroPos.x - MIN_POS_X) / (MAX_POS_X - MIN_POS_X));
            float heroY = Mathf.Clamp01((heroPos.y - MIN_POS_Y) / (MAX_POS_Y - MIN_POS_Y));
            float bossX = Mathf.Clamp01((bossPos.x - MIN_POS_X) / (MAX_POS_X - MIN_POS_X));
            float bossY = Mathf.Clamp01((bossPos.y - MIN_POS_Y) / (MAX_POS_Y - MIN_POS_Y));

            float heroVelX = Mathf.Clamp01((heroVel.x + MAX_HERO_VELOCITY) / (2f * MAX_HERO_VELOCITY));
            float heroVelY = Mathf.Clamp01((heroVel.y + MAX_HERO_VELOCITY) / (2f * MAX_HERO_VELOCITY));
            float bossVelX = Mathf.Clamp01((bossVel.x + MAX_BOSS_VELOCITY) / (2f * MAX_BOSS_VELOCITY));
            float bossVelY = Mathf.Clamp01((bossVel.y + MAX_BOSS_VELOCITY) / (2f * MAX_BOSS_VELOCITY));

            float heroHP = Mathf.Clamp01(heroHealth / MAX_HERO_HP);
            float bossHP = Mathf.Clamp01(bossHealth / MAX_BOSS_HP);

            float[] vectorObs = new float[vectorObsSize];
            vectorObs[0] = heroX;
            vectorObs[1] = heroY;
            vectorObs[2] = heroVelX;
            vectorObs[3] = heroVelY;
            vectorObs[4] = heroHP;
            vectorObs[5] = bossX;
            vectorObs[6] = bossY;
            vectorObs[7] = bossVelX;
            vectorObs[8] = bossVelY;
            vectorObs[9] = bossHP;
            vectorObs[10] = heroFacing;
            vectorObs[11] = heroDashing;
            vectorObs[12] = heroSprinting;
            vectorObs[13] = heroAttacking;
            vectorObs[14] = heroJumping;
            vectorObs[15] = heroRecoiling;
            vectorObs[16] = heroInvulnerable;

            AttackTracker attack = boss.GetComponent<AttackTracker>();
            if (attack == null)
                attack = boss.gameObject.AddComponent<AttackTracker>();
            vectorObs[17 + (int)attack.Attack] = 1f;
            vectorObs[17 + LaceEncounter.NUM_ATTACK_CATEGORIES] = Mathf.Clamp01((Time.time - attack.Since) / MAX_ATTACK_TIME);

            return vectorObs;
        }

        public int GetObservationSize()
        {
            return vectorObsSize + (VISUAL_WIDTH * VISUAL_HEIGHT);
        }

        /// <summary>
        /// Returns observation shape info for the Python side.
        /// </summary>
        public (int vectorSize, int visualWidth, int visualHeight) GetObservationShape()
        {
            return (vectorObsSize, VISUAL_WIDTH, VISUAL_HEIGHT);
        }

        public float CalculateReward(float[] previousObs, float[] currentObs, int whoDied)
        {
            if (previousObs == null || currentObs == null ||
                previousObs.Length < vectorObsSize || currentObs.Length < vectorObsSize)
            {
                return 0f;
            }

            // Terminal rewards
            if (whoDied == 0)
                return -100f;
            if (whoDied == 1)
                return 500f;

            float reward = 0f;

            // HP indices: 4 = heroHP, 9 = bossHP
            float prevHeroHP = previousObs[4] * MAX_HERO_HP;
            float currHeroHP = currentObs[4] * MAX_HERO_HP;
            float prevBossHP = previousObs[9] * MAX_BOSS_HP;
            float currBossHP = currentObs[9] * MAX_BOSS_HP;

            float bossHPLoss = prevBossHP - currBossHP;
            float heroHPLoss = prevHeroHP - currHeroHP;

            reward += bossHPLoss * 1.2f;
            reward -= heroHPLoss * 15.0f;

            // Position-based shaping
            float prevHeroX = previousObs[0] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float prevHeroY = previousObs[1] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;
            float prevBossX = previousObs[5] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float prevBossY = previousObs[6] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;

            float currHeroX = currentObs[0] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float currHeroY = currentObs[1] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;
            float currBossX = currentObs[5] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float currBossY = currentObs[6] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;

            float prevDistance = Vector2.Distance(new Vector2(prevHeroX, prevHeroY), new Vector2(prevBossX, prevBossY));
            float currDistance = Vector2.Distance(new Vector2(currHeroX, currHeroY), new Vector2(currBossX, currBossY));

            if (heroHPLoss == 0)
            {
                float distanceChange = prevDistance - currDistance;
                reward += distanceChange * 0.02f;
            }

            // Discourage going below 5 (ends up in void)
            if (currHeroY < 100f)
            {
                reward -= 0.05f;
            }

            // Discourage running away too far from the boss
            if (currDistance > 15f)
            {
                reward -= 0.05f;
            }

            reward += 0.01f;  // Survival reward

            return reward;
        }

        // If hornet has not taken damage for a certain number of steps
        // assume she is stuck
        public bool IsHeroStuck(HeroController hero)
        {
            int currentHealth = hero.playerData.health;
            
            if (currentHealth == stuckPreviousHealth)
            {
                return true;
            }

            stuckPreviousHealth = currentHealth;
            return false;
        }

        public ScreenCapture GetScreenCapture()
        {
            return ScreenCapture;
        }

        public float GetMaxHP()
        {
            return MAX_BOSS_HP;
        }

        // Savestate we reset to, in the savestates folder next to SilksongRL.dll.
        // Recorded at a position that triggers the Lace 2 fight.
        public string GetSavestateFile()
        {
            return "lace_2.json";
        }

        /// <summary>
        /// Follows Lace's Control FSM every frame, since observations only come every StepInterval
        /// and would otherwise see an attack start up to a step late. Lives on the boss, so every
        /// reset starts a fresh one.
        /// </summary>
        private class AttackTracker : MonoBehaviour
        {
            public LaceEncounter.AttackCategory Attack = LaceEncounter.AttackCategory.Idle;
            public float Since;

            private PlayMakerFSM control;
            private string state;

            private void Awake()
            {
                control = GetComponents<PlayMakerFSM>().FirstOrDefault(fsm => fsm.FsmName == "Control");
                Since = Time.time;
            }

            private void Update()
            {
                if (control == null || control.ActiveStateName == state) return;
                state = control.ActiveStateName;

                // Every wind-up (Antic) starts an attack, even the same one again: she goes straight
                // from Slash Slam back into CrossSlash Antic, for one.
                LaceEncounter.AttackCategory attack = MapBossState(state);
                if (attack != Attack || state.Contains("Antic"))
                {
                    Attack = attack;
                    Since = Time.time;
                }
            }
        }

        private static readonly HashSet<string> unknownStates = new HashSet<string>();

        /// <summary>
        /// Maps Lace 2's Control FSM states onto Lace 1's attack categories. The names come from a
        /// LogBossStates run through the whole fight.
        /// </summary>
        private static LaceEncounter.AttackCategory MapBossState(string stateName)
        {
            stateName = stateName?.Trim() ?? "";

            // Repositioning, landing after aerial attacks, and the phase shifts (they push Hornet
            // back, but Lace can still be hit)
            if (stateName == "" || StartsWithAny(stateName, "Idle", "Pause", "Start Battle", "Refight",
                    "Hop", "Bounce Back", "Land", "P2 Shift", "P3 Roar"))
                return LaceEncounter.AttackCategory.Idle;
            // Pose Swish attacks too
            if (StartsWithAny(stateName, "ComboSlash", "Combo Strike", "Pose"))
                return LaceEncounter.AttackCategory.ComboSlash;
            if (StartsWithAny(stateName, "Counter"))
                return LaceEncounter.AttackCategory.Counter;
            if (StartsWithAny(stateName, "RapidSlash", "Slash End"))
                return LaceEncounter.AttackCategory.RapidSlash;
            if (StartsWithAny(stateName, "J Slash"))
                return LaceEncounter.AttackCategory.JSlash;
            if (StartsWithAny(stateName, "Downstab", "Dstab"))
                return LaceEncounter.AttackCategory.Downstab;
            if (StartsWithAny(stateName, "Charge", "Crossup"))
                return LaceEncounter.AttackCategory.Charge;
            if (StartsWithAny(stateName, "Evade"))
                return LaceEncounter.AttackCategory.Evade;
            if (StartsWithAny(stateName, "CrossSlash", "Slash Slam"))
                return LaceEncounter.AttackCategory.CrossSlash;
            if (StartsWithAny(stateName, "Multihit"))
                return LaceEncounter.AttackCategory.Multihit;
            if (StartsWithAny(stateName, "Stun"))
                return LaceEncounter.AttackCategory.Stun;

            if (unknownStates.Add(stateName))
                RLManager.StaticLogger?.LogWarning($"[LaceSecondEncounter] Unknown boss state: {stateName}, treating it as Idle");
            return LaceEncounter.AttackCategory.Idle;
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            return prefixes.Any(value.StartsWith);
        }
    }
}

