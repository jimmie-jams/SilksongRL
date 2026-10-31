using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Boss encounter configuration for Skarrsinger Karmelita, with hybrid observations like Lace 2.
    ///
    /// Her fight does not start by itself: she dances in the background until Hornet challenges her,
    /// then sends a gauntlet first. StartFight skips both.
    /// </summary>
    public class KarmelitaEncounter : IBossEncounter
    {
        // Wall to wall and floor to roof, from the arena's colliders
        private const float MIN_POS_X = 133f;
        private const float MAX_POS_X = 164f;
        private const float MIN_POS_Y = 18f;
        private const float MAX_POS_Y = 34f;
        private const float MAX_HERO_VELOCITY = 30f;
        private const float MAX_BOSS_VELOCITY = 90f;     // her long evade
        private const float MAX_HERO_HP = 10f;
        private const float MAX_BOSS_HP = 1500f;
        private const string BOSS_NAME = "Hunter Queen Boss";

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

        // Attack categories for Karmelita, from her Control FSM
        public enum AttackCategory
        {
            Idle = 0,
            Reposition = 1,  // dashes and evades
            Block = 2,
            SlashCombo = 3,
            Cyclone = 4,
            Throw = 5,
            AirThrow = 6,
            JumpSpin = 7,
            DashGrind = 8,
            Roar = 9,        // phase changes
            Stun = 10,
        }

        private const int NUM_ATTACK_CATEGORIES = 11;

        // Seconds since her current attack started, scaled so 1 means this long or more. A guess at her
        // longest chains (the full slash combo, the dash grind): LogBossStates shows the real lengths.
        private const float MAX_ATTACK_TIME = 3f;

        // 17 values about Hornet and Karmelita, then Karmelita's attack (one-hot) and how far into it
        // she is, then Hornet's silk
        private readonly int vectorObsSize = 17 + NUM_ATTACK_CATEGORIES + 1 + 1;

        // Her states from the background dance to the fight
        private static readonly HashSet<string> IntroStates = new HashSet<string>
        {
            "Init", "BG Dance", "Launch In Antic", "Launch In", "Entry Pos", "Enter L", "Enter R",
            "Entry Fall", "Entry Land", "Roar Antic", "Roar", "Battle Start"
        };

        public string GetEncounterName()
        {
            return BOSS_NAME;
        }

        public ActionSpaceType GetActionSpaceType()
        {
            return ActionSpaceType.ExtendedBind;
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
            vectorObs[17 + NUM_ATTACK_CATEGORIES] = Mathf.Clamp01((Time.time - attack.Since) / MAX_ATTACK_TIME);

            // Bind costs 9 of this savestate's 17 silk
            vectorObs[17 + NUM_ATTACK_CATEGORIES + 1] =
                Mathf.Clamp01((float)hero.playerData.silk / Mathf.Max(1, hero.playerData.silkMax));

            return vectorObs;
        }

        public int GetObservationSize()
        {
            return vectorObsSize + (VISUAL_WIDTH * VISUAL_HEIGHT);
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

            // A whole kill is worth about what it is for Lace 2 (800 HP * 1.2)
            reward += bossHPLoss * 0.65f;
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

            // Discourage running away too far from the boss
            if (currDistance > 15f)
            {
                reward -= 0.05f;
            }

            reward += 0.01f;  // Survival reward

            return reward;
        }

        // A closed arena with nothing to fall into
        public bool IsHeroStuck(HeroController hero)
        {
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
        // Recorded in her arena before challenging her.
        public string GetSavestateFile()
        {
            return "karmelita.json";
        }

        // Launch In Antic is where she jumps in once the gauntlet is over, and where the game's own
        // debug shortcut goes, so sending her there from the background dance skips the challenge and
        // the gauntlet. It closes the gates and locks the camera itself.
        public bool StartFight(HealthManager boss)
        {
            PlayMakerFSM control = boss.GetComponents<PlayMakerFSM>().FirstOrDefault(fsm => fsm.FsmName == "Control");
            if (control == null)
                return false;

            if (control.ActiveStateName == "BG Dance")
            {
                control.SetState("Launch In Antic");
                // A real challenge switches this off. Left on, pressing up in it does the challenge stance
                boss.transform.parent.Find("Challenge Region")?.gameObject.SetActive(false);
            }

            // Her entrance and roar take about 5s, and Hornet can do nothing during the roar
            return !string.IsNullOrEmpty(control.ActiveStateName) && !IntroStates.Contains(control.ActiveStateName);
        }

        /// <summary>
        /// Follows Karmelita's Control FSM every frame, since observations only come every StepInterval
        /// and would otherwise see an attack start up to a step late. Lives on the boss, so every
        /// reset starts a fresh one.
        /// </summary>
        private class AttackTracker : MonoBehaviour
        {
            public AttackCategory Attack = AttackCategory.Idle;
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
                string previous = state;
                state = control.ActiveStateName;

                // A wind-up (Antic) starts an attack, even the same one again. Some wind-ups are
                // several Antic states in a row (Rethrow Antic 1-5), which count once.
                AttackCategory attack = MapBossState(state);
                bool windUp = state.Contains("Antic") && (previous == null || !previous.Contains("Antic"));
                if (attack != Attack || windUp)
                {
                    Attack = attack;
                    Since = Time.time;
                }
            }
        }

        private static readonly HashSet<string> unknownStates = new HashSet<string>();

        /// <summary>
        /// Maps Karmelita's Control FSM states onto attack categories. The names come from her FSM in
        /// the Memory_Ant_Queen scene. Order matters where names share a prefix.
        /// </summary>
        private static AttackCategory MapBossState(string stateName)
        {
            stateName = stateName?.Trim() ?? "";

            // Picking and setting up her next move, and hopping about between attacks
            if (stateName == "" || StartsWithAny(stateName, "Start Idle", "Movement", "Attack Choice", "Set ",
                    "Move To", "Attack Move", "Distance", "Force Evade", "Check Near Wall", "Face Away",
                    "Restart Singing", "Hornet Dead"))
                return AttackCategory.Idle;
            if (StartsWithAny(stateName, "Jump Back", "Wall", "Dash Grind"))
                return AttackCategory.DashGrind;
            if (StartsWithAny(stateName, "Block", "Approach Block"))
                return AttackCategory.Block;
            if (StartsWithAny(stateName, "Approach", "Long Approach", "Evade", "Long Evade", "Air Evade", "Dash"))
                return AttackCategory.Reposition;
            if (StartsWithAny(stateName, "Launch", "Air Throw", "Air Sickles", "Air Double", "Air Rethrow",
                    "A Throw", "A Rethrow", "Throw Fall", "Throw Land", "Spin Dash"))
                return AttackCategory.AirThrow;
            if (StartsWithAny(stateName, "Throw", "Rethrow", "Double Throw"))
                return AttackCategory.Throw;
            if (StartsWithAny(stateName, "Cyclone", "To Cyclone"))
                return AttackCategory.Cyclone;
            if (StartsWithAny(stateName, "Slash", "Cross Up"))
                return AttackCategory.SlashCombo;
            if (StartsWithAny(stateName, "Jump", "Spin", "Soft Land", "Spear Slam", "To Jump Dive"))
                return AttackCategory.JumpSpin;
            if (StartsWithAny(stateName, "P2 Roar", "P3 Roar"))
                return AttackCategory.Roar;
            if (StartsWithAny(stateName, "Stun", "Damage Recover"))
                return AttackCategory.Stun;

            if (unknownStates.Add(stateName))
                RLManager.StaticLogger?.LogWarning($"[KarmelitaEncounter] Unknown boss state: {stateName}, treating it as Idle");
            return AttackCategory.Idle;
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            return prefixes.Any(value.StartsWith);
        }
    }
}
