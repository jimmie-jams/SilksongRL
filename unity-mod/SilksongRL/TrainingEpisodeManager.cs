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
            Resetting,       // Savestate load requested, waiting for the arena to come back
            Failed           // Reset could not be performed; training stops
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

        // DebugMod logs a full stack trace when a load fails, and a failed load clears
        // loadingSavestate immediately, so retrying every tick turns one failure into hundreds of
        // log lines. Space the attempts out and give up after a few.
        private const float LOAD_RETRY_INTERVAL_SECONDS = 1f;
        private const int MAX_LOAD_ATTEMPTS = 5;
        private int loadAttempts = 0;
        private float lastLoadAttemptTime = 0f;

        private bool hasTriggeredReset = false;
        private float resetSequenceStartTime = 0f;

        public System.Action OnResetComplete;
        public System.Action OnResetFailed;

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

                case EpisodeState.Failed:
                    return true;

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
            loadAttempts = 0;
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
                loadAttempts = 0;
                lastLoadAttemptTime = float.NegativeInfinity;
                RLManager.StaticLogger?.LogInfo($"[TrainingEpisodeManager] {reason} - loading savestate...");
            }

            if (Time.unscaledTime - lastLoadAttemptTime < LOAD_RETRY_INTERVAL_SECONDS)
                return true; // waiting out the gap between attempts

            lastLoadAttemptTime = Time.unscaledTime;
            loadAttempts++;

            if (saveState.TryLoad())
            {
                BeginWaitingForLoad();
                return true;
            }

            // The load was refused (hero transitioning) or it started and threw straight away.
            // Either way DebugMod has already said why, so do not keep hammering it.
            if (loadAttempts >= MAX_LOAD_ATTEMPTS ||
                Time.unscaledTime - resetSequenceStartTime >= LOAD_TIMEOUT_SECONDS)
            {
                FailReset(
                    $"savestate would not load after {loadAttempts} attempt(s). If DebugMod logged an " +
                    "error above, the savestate cannot be loaded from where the hero currently is - " +
                    "get into the arena before enabling agent control.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Stop trying to reset. Resuming would just re-detect the same condition and loop, so the
        /// episode manager parks in Failed and lets RLManager turn agent control off.
        /// </summary>
        private void FailReset(string message)
        {
            CurrentState = EpisodeState.Failed;
            hasTriggeredReset = false;
            resetLoadRequested = false;
            RLManager.StaticLogger?.LogError($"[TrainingEpisodeManager] Reset failed: {message}");
            OnResetFailed?.Invoke();
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
                    FailReset($"savestate load did not finish within {LOAD_TIMEOUT_SECONDS}s");
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
                    FailReset(
                        $"the boss did not appear within {LOAD_TIMEOUT_SECONDS}s of the savestate load. " +
                        "Does this savestate drop the hero into the encounter?");
                    return false;
                }
                return true;
            }

            ResetEpisode();
            return false;
        }
    }
}
