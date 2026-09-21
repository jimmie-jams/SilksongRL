using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Observation types for different encounters.
    /// Tells Python how to process the observation array.
    /// </summary>
    public enum ObservationType
    {
        // Vector only: flat array of state values
        Vector,

        // Hybrid: [vector_obs | visual_obs] - split and process separately
        Hybrid
    }

    /// <summary>
    /// Interface for boss encounter configurations.
    /// Each boss encounter implements this to define its specific behavior,
    /// observation space, action space, and reset mechanics.
    /// </summary>
    public interface IBossEncounter
    {
        /// <summary>
        /// Gets the human-readable name of this encounter.
        /// </summary>
        string GetEncounterName();

        /// <summary>
        /// Gets the action space type for this encounter.
        /// Some bosses might require more actions to be beatable.
        /// </summary>
        ActionSpaceType GetActionSpaceType();

        /// <summary>
        /// Gets the observation type for this encounter.
        /// Vector = flat state values only.
        /// Hybrid = [vector_obs | visual_obs] for CNN processing.
        /// </summary>
        ObservationType GetObservationType();

        /// <summary>
        /// Gets the size of the vector portion of observations.
        /// For Vector type, this equals GetObservationSize().
        /// For Hybrid type, this is just the vector part (before visual data).
        /// </summary>
        int GetVectorObservationSize();

        /// <summary>
        /// Gets the visual observation dimensions.
        /// Returns (0, 0) for vector-only observations.
        /// </summary>
        (int width, int height) GetVisualObservationSize();

        /// <summary>
        /// Checks if the given HealthManager matches this encounter.
        /// </summary>
        bool IsEncounterMatch(HealthManager hm);

        /// <summary>
        /// Extracts observations from the current game state.
        /// This allows each encounter to define its own observation space
        /// (e.g., base observations, projectiles, summons, environmental hazards).
        /// </summary>
        float[] ExtractObservationArray(HeroController hero, HealthManager boss);

        /// <summary>
        /// Returns the size of the observation array for this encounter.
        /// Must match the length of the array returned by ExtractObservationArray().
        /// </summary>
        int GetObservationSize();

        /// <summary>
        /// Calculates the reward for the current transition.
        /// Each encounter can define its own reward function.
        /// </summary>
        float CalculateReward(float[] previousObservations, float[] currentObservations, int whoDied);

        /// <summary>
        /// Checks if the hero is stuck.
        /// Conditions are arena/boss specific.
        /// Might not be required for all encounters.
        /// </summary>
        bool IsHeroStuck(HeroController hero);

        /// <summary>
        /// Gets the screen capture instance for hybrid observations.
        /// Returns null for vector-only encounters.
        /// </summary>
        ScreenCapture GetScreenCapture();

        /// <summary>
        /// Gets the maximum HP of the boss.
        /// </summary>
        float GetMaxHP();

        /// <summary>
        /// Name of the savestate JSON this encounter resets to, found in the "savestates" folder
        /// next to SilksongRL.dll. Override with SavestateFile in BepInEx/config/silksongrl.cfg.
        ///
        /// These are SilksongRL's own savestates, kept separate from DebugMod's savestate slots.
        /// </summary>
        string GetSavestateFile();


        // NOTE:
        // The following three methods are not currently used.
        // They were needed because with the previous resetting mechanism
        // the fight would not trigger immediately. This may still happen
        // in certain encounters where we cannot reset straight into the fight.
        // but would first need to move a bit to trigger it so keeping them
        // just in case.

        /*
        /// <summary>
        /// Checks if the boss is in a dormant/inactive state.
        /// Used during reset sequences to determine when the fight can resume.
        /// </summary>
        bool IsBossDormant(HealthManager boss);

        /// <summary>
        /// Returns the action the hero should take during reset to initiate the fight.
        /// </summary>
        Action GetResetAction(HeroController hero, HealthManager boss);

        /// <summary>
        /// Checks if the reset sequence is complete and training can resume.
        /// </summary>
        bool IsResetComplete(HeroController hero, HealthManager boss);
        */
    }
}

