using HarmonyLib;

namespace SilksongRL
{
    /// <summary>
    /// Cancels damage that would kill the hero while the agent is in control, and flags the episode
    /// as finished so TrainingEpisodeManager can reset to our savestate.
    ///
    /// This is the same interception DebugMod uses for its "load savestate on death" - see
    /// PlayerDamaged in DebugMod.cs, hooked onto PlayerData.TakeHealth via ModHooks.TakeHealthHook
    /// (https://github.com/hk-speedrunning/Silksong.DebugMod). We do it ourselves for two reasons:
    /// we reset to SilksongRL's own savestate rather than DebugMod's quickslot, and we never have to
    /// flip DebugMod.stateOnDeath on the player's behalf.
    ///
    /// [HarmonyBefore] puts this ahead of DebugMod's prefix. If the player has load-on-death on,
    /// ours zeroes the damage first, so DebugMod's own lethal test (health - damage &lt;= 0) comes out
    /// false and it stands down. Exactly one reset happens whichever order they end up in.
    /// </summary>
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.TakeHealth))]
    [HarmonyBefore(DebugModSaveStates.DebugModGuid)]
    public static class LethalDamagePatch
    {
        /// <summary>
        /// Set when a killing blow was cancelled. TrainingEpisodeManager consumes it and clears it.
        /// </summary>
        public static bool LethalDamageBlocked;

        public static void Prefix(ref int amount)
        {
            if (!RLManager.isAgentControlEnabled) return;

            // Without a savestate of our own we have no way to reset, so making the hero unkillable
            // here would be worse than letting them die. Leave it to DebugMod's own load-on-death.
            if (!RLManager.saveStateResetsActive) return;

            if (amount <= 0) return;

            PlayerData pd = PlayerData.instance;
            if (pd == null) return;

            if (pd.health - amount > 0) return; // survivable, let it through

            // Killing blow. Cancel it and let the reset path load our savestate. We keep cancelling
            // while the reset is pending, so the hero cannot die in the gap.
            amount = 0;
            LethalDamageBlocked = true;
        }
    }
}
