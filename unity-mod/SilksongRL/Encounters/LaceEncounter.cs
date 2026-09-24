using HutongGames.PlayMaker;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Boss encounter configuration for Lace 1.
    /// Contains all Lace-specific logic including state extraction, normalization, and reset behavior.
    /// </summary>
    public class LaceEncounter : IBossEncounter
    {
        // Normalization constants (based on observed gameplay data for Lace arena)
        private const float MIN_POS_X = 77.5f;           // X positions range ~78-110
        private const float MAX_POS_X = 110.5f;
        private const float MIN_POS_Y = 2f;              // Y positions range ~2-17 (with leeway for pogos)
        private const float MAX_POS_Y = 25f;
        private const float MAX_HERO_VELOCITY = 30f;     // Hero velocity range ~-27 to 27
        private const float MAX_BOSS_VELOCITY = 70f;     // Boss velocity range ~-71 to 71
        private const float MAX_HERO_HP = 10f;
        private const float MAX_BOSS_HP = 250f;
        private const float STUCK_Y_THRESHOLD = 5f;
        private const string BOSS_NAME = "Lace Boss1";

        // Attack categories for Lace
        public enum AttackCategory
        {
            Idle = 0,
            ComboSlash = 1,
            Counter = 2,
            RapidSlash = 3,
            JSlash = 4,
            Downstab = 5,
            Charge = 6,
            Evade = 7,
            CrossSlash = 8,
            Stun = 9,
            Multihit = 10,
        }

        internal const int NUM_ATTACK_CATEGORIES = 11;
        private readonly int vectorObsSize = 10 + NUM_ATTACK_CATEGORIES;

        public string GetEncounterName()
        {
            return BOSS_NAME;
        }

        public ActionSpaceType GetActionSpaceType()
        {
            return ActionSpaceType.Basic;
        }

        public ObservationType GetObservationType()
        {
            return ObservationType.Vector;
        }

        public int GetVectorObservationSize()
        {
            return vectorObsSize;
        }

        public (int width, int height) GetVisualObservationSize()
        {
            return (0, 0);
        }

        public bool IsEncounterMatch(HealthManager hm)
        {
            return hm != null && hm.name == BOSS_NAME;
        }

        public float[] ExtractObservationArray(HeroController hero, HealthManager boss)
        {
            if (hero == null || boss == null)
                return null;

            Vector2 heroPos = hero.transform.position;
            Rigidbody2D heroRb = hero.GetComponent<Rigidbody2D>();
            Vector2 heroVel = heroRb ? heroRb.velocity : Vector2.zero;
            int heroHealth = hero.playerData.health;

            Vector2 bossPos = boss.transform.position;
            Rigidbody2D bossRb = boss.GetComponent<Rigidbody2D>();
            Vector2 bossVel = bossRb ? bossRb.velocity : Vector2.zero;
            int bossHealth = boss.hp;

            AttackCategory attackCategory = AttackCategory.Idle;
            var fsm = boss.GetComponent<PlayMakerFSM>();
            if (fsm != null)
            {
                attackCategory = MapBossState(fsm.ActiveStateName);
            }

            // Normalize all values to [0, 1]
            
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
            
            float[] attackOneHot = new float[NUM_ATTACK_CATEGORIES];
            int attackIdx = (int)attackCategory;
            if (attackIdx >= 0 && attackIdx < NUM_ATTACK_CATEGORIES)
            {
                attackOneHot[attackIdx] = 1.0f;
            }
            
            return new float[]
            {
                heroX, heroY,
                heroVelX, heroVelY,
                heroHP,
                bossX, bossY,
                bossVelX, bossVelY,
                bossHP,
                attackOneHot[0], attackOneHot[1], attackOneHot[2], attackOneHot[3],
                attackOneHot[4], attackOneHot[5], attackOneHot[6], attackOneHot[7],
                attackOneHot[8], attackOneHot[9], attackOneHot[10]
            };
        }

        public int GetObservationSize()
        {
            return vectorObsSize;
        }

        public float CalculateReward(float[] previousObs, float[] currentObs, int whoDied)
        {
            if (previousObs == null || currentObs == null || 
                previousObs.Length != vectorObsSize || currentObs.Length != vectorObsSize)
            {
                return 0f;
            }

            // 0. Terminal rewards
            if (whoDied == 0)
            {
                return -100f;

            }
            else if (whoDied == 1)
            {
                return 500f;
            }

            float reward = 0;
            
            // Indices: 0-1: hero pos, 2-3: hero vel, 4: hero HP, 5-6: boss pos, 7-8: boss vel, 9: boss HP
            float prevHeroHP = previousObs[4] * MAX_HERO_HP;
            float currHeroHP = currentObs[4] * MAX_HERO_HP;
            float prevBossHP = previousObs[9] * MAX_BOSS_HP;
            float currBossHP = currentObs[9] * MAX_BOSS_HP;
            
            // HP Change Rewards
            float bossHPLoss = prevBossHP - currBossHP;
            float heroHPLoss = prevHeroHP - currHeroHP;
            
            reward += bossHPLoss * 2.0f;
            reward -= heroHPLoss * 15.0f;

            // Distance and position-based shaping
            float prevHeroX = previousObs[0] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float prevHeroY = previousObs[1] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;
            float prevBossX = previousObs[5] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float prevBossY = previousObs[6] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;
            
            float currHeroX = currentObs[0] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float currHeroY = currentObs[1] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;
            float currBossX = currentObs[5] * (MAX_POS_X - MIN_POS_X) + MIN_POS_X;
            float currBossY = currentObs[6] * (MAX_POS_Y - MIN_POS_Y) + MIN_POS_Y;
            
            Vector2 prevHeroPos = new Vector2(prevHeroX, prevHeroY);
            Vector2 prevBossPos = new Vector2(prevBossX, prevBossY);
            Vector2 currHeroPos = new Vector2(currHeroX, currHeroY);
            Vector2 currBossPos = new Vector2(currBossX, currBossY);
            
            float prevDistance = Vector2.Distance(prevHeroPos, prevBossPos);
            float currDistance = Vector2.Distance(currHeroPos, currBossPos);
            
            // Encourage moving closer to boss (but only if not taking damage)
            if (heroHPLoss == 0)
            {
                float distanceChange = prevDistance - currDistance;
                reward += distanceChange * 0.02f;
            }

            // Discourage going below 5 (ends up in lava)
            if (currHeroY < 5f)
            {
                reward -= 0.05f;
            }

            // Discourage running away too far from the boss
            if (currDistance > 15f)
            {
                reward -= 0.05f;
            }
            
            // Survival reward
            reward += 0.01f;
            
            return reward;
        }

        /// <summary>
        /// Maps Lace's FSM state names to attack categories.
        /// </summary>
        private AttackCategory MapBossState(string stateName)
        {
            stateName = stateName?.Trim() ?? "";

            if (string.IsNullOrEmpty(stateName))
            {
                return AttackCategory.Idle;
            }

            if (stateName.StartsWith("Idle") || stateName.StartsWith("Hop") ||
                stateName.StartsWith("Wallcling") || stateName.StartsWith("Refight"))
            {
                return AttackCategory.Idle;
            }

            if (stateName.StartsWith("ComboSlash") || stateName.StartsWith("Pose"))
            {
                return AttackCategory.ComboSlash;
            }

            if (stateName.StartsWith("Counter"))
            {
                return AttackCategory.Counter;
            }

            if (stateName.StartsWith("RapidSlash"))
            {
                return AttackCategory.RapidSlash;
            }

            if (stateName.StartsWith("J Slash"))
            {
                return AttackCategory.JSlash;
            }

            if (stateName.StartsWith("Downstab"))
            {
                return AttackCategory.Downstab;
            }

            if (stateName.StartsWith("Charge"))
            {
                return AttackCategory.Charge;
            }

            if (stateName.StartsWith("Evade"))
            {
                return AttackCategory.Evade;
            }

            if (stateName.StartsWith("CrossSlash") || stateName.StartsWith("Slash Slam"))
            {
                return AttackCategory.CrossSlash;
            }

            if (stateName.StartsWith("Multihit"))
            {
                return AttackCategory.Multihit;
            }

            if (stateName.StartsWith("Stun"))
            {
                return AttackCategory.Stun;
            }

            RLManager.StaticLogger?.LogWarning($"[LaceEncounter] Unknown boss state: {stateName}, defaulting to Idle");
            return AttackCategory.Idle;
        }

        public bool IsHeroStuck(HeroController hero)
        {
            float heroY = hero.transform.position.y;
            return heroY < STUCK_Y_THRESHOLD;
        }

        public ScreenCapture GetScreenCapture()
        {
            return null;
        }

        public float GetMaxHP()
        {
            return MAX_BOSS_HP;
        }

        // Savestate we reset to, in the savestates folder next to SilksongRL.dll.
        // Recorded at a position that triggers the Lace 1 fight.
        public string GetSavestateFile()
        {
            return "lace_1.json";
        }
    }

}

