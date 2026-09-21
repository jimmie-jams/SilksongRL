using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Manages the lifecycle of training episodes including death detection,
    /// reset sequences, and state transitions.
    ///
    /// Resets ask DebugMod to load SilksongRL's own savestate - see EncounterSaveState and
    /// DebugModSaveStates. Hero deaths, boss deaths and stuck heroes all reset the same way.
    /// </summary>
    public class TrainingEpisodeManager
    {
        // Episode state machine
        public enum EpisodeState
        {
            Training,        // Normal training mode
            HeroDead,        // Hero died, need to reset
            BossDead,        // Boss died, need to reset
            HeroStuck,       // Hero stuck (e.g., below ground), need to force reset
            Resetting        // Savestate load requested, waiting for the arena to come back
        }

        public EpisodeState CurrentState { get; private set; }

        private IBossEncounter encounter;
        private EncounterSaveState saveState;

        // TO DO 
        // MAKE STUCK STEP THRESHOLD CONFIGURABLE BY EACH ENCOUNTER
        private const int STUCK_STEP_THRESHOLD = 5000;
        private int consecutiveStuckSteps = 0;

        // Give up waiting on a savestate load after this many real-time seconds.
        // Real time rather than Time.time because training often runs at a raised timescale.
        private const float LOAD_TIMEOUT_SECONDS = 15f;
        private float loadWaitStartTime = 0f;
        private bool resetLoadRequested = false;

        private bool hasTriggeredReset = false;
        private float resetSequenceStartTime = 0f;

        public System.Action OnResetComplete;

        public TrainingEpisodeManager(IBossEncounter encounter, EncounterSaveState saveState)
        {
            this.encounter = encounter;
            this.saveState = saveState;
            CurrentState = EpisodeState.Training;
        }

        /// <summary>
        /// Updates the episode state based on current game conditions.
        /// Should be called every fixed update.
        /// </summary>
        public void UpdateEpisodeState(HeroController hero, HealthManager boss)
        {
            if (hero == null)
                return;

            if (CurrentState != EpisodeState.Training)
                return;

            // Our own patch cancelled a killing blow, so the hero "died". This is exact - no
            // inferring it from health or boss HP.
            if (LethalDamagePatch.LethalDamageBlocked)
            {
                LethalDamagePatch.LethalDamageBlocked = false;
                CurrentState = EpisodeState.HeroDead;
                RLManager.StaticLogger?.LogInfo("[TrainingEpisodeManager] Hero died detected - lethal damage cancelled");
                consecutiveStuckSteps = 0;
                return;
            }

            // Something outside SilksongRL loaded a savestate (most likely the player). Ride it out
            // rather than stepping into a load.
            if (!resetLoadRequested && DebugModSaveStates.IsLoading)
            {
                CurrentState = EpisodeState.HeroDead;
                RLManager.StaticLogger?.LogWarning(
                    "[TrainingEpisodeManager] A savestate we did not request is loading - treating the episode as over");
                consecutiveStuckSteps = 0;
                return;
            }

            if (encounter.IsHeroStuck(hero))
            {
                consecutiveStuckSteps++;
                if (consecutiveStuckSteps >= STUCK_STEP_THRESHOLD)
                {
                    CurrentState = EpisodeState.HeroStuck;
                    RLManager.StaticLogger?.LogInfo($"[TrainingEpisodeManager] Hero stuck for {consecutiveStuckSteps} steps - triggering reset");
                    consecutiveStuckSteps = 0;
                }
            }
            else
            {
                consecutiveStuckSteps = 0;
            }

            // If the boss is null, it means the boss has died
            // This is a guarantee as with the new SaveState respawn
            // handling the boss does not go null in between as it did before
            if (boss == null)
            {
                CurrentState = EpisodeState.BossDead;
                RLManager.StaticLogger?.LogInfo($"[TrainingEpisodeManager] Boss died detected - boss is null");
                consecutiveStuckSteps = 0; // Reset stuck counter on death
            }
        }

        /// <summary>
        /// Handles the reset sequence. Returns true if reset is in progress (skip normal step processing).
        /// </summary>
        public bool HandleResetSequence(HeroController hero, HealthManager boss)
        {
            switch (CurrentState)
            {
                case EpisodeState.HeroDead:
                    // If a load is already running it is the unsolicited one detected above,
                    // so wait it out instead of stacking another request on top.
                    if (DebugModSaveStates.IsLoading)
                    {
                        BeginWaitingForLoad();
                        return true;
                    }
                    return RequestSaveStateLoad(hero, "Hero died");

                case EpisodeState.BossDead:
                    return RequestSaveStateLoad(hero, "Boss defeated");

                case EpisodeState.HeroStuck:
                    return RequestSaveStateLoad(hero, "Hero stuck");

                case EpisodeState.Resetting:
                    return WaitForSaveStateLoad(hero, boss);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Resets the episode manager state after a successful reset.
        /// </summary>
        public void ResetEpisode()
        {
            CurrentState = EpisodeState.Training;
            hasTriggeredReset = false;
            resetLoadRequested = false;
            LethalDamagePatch.LethalDamageBlocked = false;
            consecutiveStuckSteps = 0;

            RLManager.StaticLogger?.LogInfo("[TrainingEpisodeManager] Episode reset complete, resuming training");

            OnResetComplete?.Invoke();
        }

        /// <summary>
        /// Asks DebugMod to load the encounter's savestate. DebugMod refuses while the hero is
        /// transitioning or while another load is running, so we retry every tick until it takes.
        /// </summary>
        private bool RequestSaveStateLoad(HeroController hero, string reason)
        {
            if (!hasTriggeredReset)
            {
                hasTriggeredReset = true;
                resetSequenceStartTime = Time.unscaledTime;
                RLManager.StaticLogger?.LogInfo($"[TrainingEpisodeManager] {reason} - loading savestate...");
            }

            if (saveState.TryLoad())
            {
                BeginWaitingForLoad();
                return true;
            }

            // Refused this frame (usually mid-transition). Keep trying, but do not hang forever.
            if (Time.unscaledTime - resetSequenceStartTime >= LOAD_TIMEOUT_SECONDS)
            {
                RLManager.StaticLogger?.LogError(
                    $"[TrainingEpisodeManager] DebugMod would not load a savestate within {LOAD_TIMEOUT_SECONDS}s - giving up and resuming");
                ResetEpisode();
                return false;
            }

            return true;
        }

        private void BeginWaitingForLoad()
        {
            resetLoadRequested = true;
            loadWaitStartTime = Time.unscaledTime;
            CurrentState = EpisodeState.Resetting;
        }

        /// <summary>
        /// Waits for DebugMod to finish loading and for the boss to exist again.
        /// SaveState.loadingSavestate tells us exactly when the load is done, so there is nothing
        /// to guess at with delays.
        /// </summary>
        private bool WaitForSaveStateLoad(HeroController hero, HealthManager boss)
        {
            bool timedOut = Time.unscaledTime - loadWaitStartTime >= LOAD_TIMEOUT_SECONDS;

            if (DebugModSaveStates.IsLoading)
            {
                if (timedOut)
                {
                    RLManager.StaticLogger?.LogError(
                        $"[TrainingEpisodeManager] Savestate load did not finish within {LOAD_TIMEOUT_SECONDS}s - resuming anyway");
                    ResetEpisode();
                    return false;
                }
                return true;
            }

            // Load finished. The boss respawns a frame or two later (RLManager picks it up through
            // the HealthManager.Awake patch), and we cannot step without it.
            if (boss == null)
            {
                if (timedOut)
                {
                    RLManager.StaticLogger?.LogError(
                        $"[TrainingEpisodeManager] Boss did not respawn within {LOAD_TIMEOUT_SECONDS}s of the savestate load - resuming anyway");
                    ResetEpisode();
                    return false;
                }
                return true;
            }

            ResetEpisode();
            return false;
        }
    }
}
