using System;
using HarmonyLib;

namespace SilksongRL
{
    /// <summary>
    /// Stops the game writing save files once SilksongRL has loaded a savestate.
    ///
    /// After a savestate load the in-memory PlayerData is the savestate's, not the player's. Any save
    /// from then on - an autosave, a restore point, quitting to the menu - would write training state
    /// over a real save slot. So from the first load until the game closes, saving is off.
    ///
    /// Each patch does what the game itself does when CheatManager.AllowSaving is false: report
    /// success to the caller and write nothing. (Patching AllowSaving directly is not reliable - it
    /// is a constant getter the JIT can inline into callers, bypassing the patch.)
    /// 
    /// Frankly this is very unlikely to be an issue and this is probably overengineering, but whatever.
    /// </summary>
    internal static class SaveSuppression
    {
        public static bool Active { get; private set; }

        public static void Activate(string reason)
        {
            if (Active) return;
            Active = true;
            RLManager.StaticLogger?.LogInfo(
                $"[SaveSuppression] Saving disabled until the game closes ({reason}), so no save slot " +
                "can be overwritten with training state.");
        }

        // Covers SaveGame(Action<bool>) and SaveGameWithAutoSave, which both land here.
        [HarmonyPatch(typeof(GameManager), "SaveGame",
            new[] { typeof(int), typeof(Action<bool>), typeof(bool), typeof(AutoSaveName) })]
        private static class SaveGamePatch
        {
            private static bool Prefix(Action<bool> ogCallback)
            {
                if (!Active) return true;
                ogCallback?.Invoke(true);
                return false;
            }
        }

        // Pinned to the (int, ...) overload: the other one forwards here, and patching by name alone
        // is ambiguous, which makes PatchAll throw and takes every patch in the mod down with it.
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.CreateRestorePoint),
            new[] { typeof(int), typeof(AutoSaveName), typeof(Action<bool>) })]
        private static class CreateRestorePointPatch
        {
            private static bool Prefix(Action<bool> callback)
            {
                if (!Active) return true;
                callback?.Invoke(true);
                return false;
            }
        }

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.SaveGameData))]
        private static class SaveGameDataPatch
        {
            private static bool Prefix(Action<bool> ogCallback)
            {
                if (!Active) return true;
                ogCallback?.Invoke(true);
                return false;
            }
        }
    }
}
