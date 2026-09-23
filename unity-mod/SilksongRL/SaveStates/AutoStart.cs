using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Boots straight from the title screen into the encounter, with no save file involved.
    ///
    /// UIManager.UIContinueGame has an overload taking a SaveGameData, which the game then plays from
    /// without reading any save slot. A savestate already carries a full PlayerData and SceneData, so
    /// we hand it those and the game starts in the savestate's world. RLManager then arms agent
    /// control, and the first episode reset loads the savestate proper, dropping the hero in the arena.
    ///
    /// Prior art: deeean/silksong-agent's SkipIntroPatch (animator speed-up, UIContinueGame on the
    /// title screen) - https://github.com/deeean/silksong-agent
    /// </summary>
    internal static class AutoStart
    {
        /// <summary>Set from config in RLManager.Awake, before the intro scene runs.</summary>
        public static bool Enabled;

        // Only a label: SaveSuppression is active before we continue, so nothing is ever written to
        // it. Slot 4 is the last one the menu shows, as a last line of defence if that ever failed.
        private const int SaveSlot = 4;

        private const float MenuTimeoutSeconds = 30f;

        [HarmonyPatch(typeof(StartManager), "Start")]
        private static class SkipIntroPatch
        {
            private static void Postfix(StartManager __instance)
            {
                if (Enabled && __instance.startManagerAnimator != null)
                    __instance.startManagerAnimator.speed = 9999f;
            }
        }

        /// <summary>
        /// Enter the game from the savestate's data. Calls onEnteredGame once the game has been asked
        /// to load, so the caller can arm agent control.
        /// </summary>
        public static IEnumerator ContinueFromSavestate(EncounterSaveState saveState,
            System.Action onEnteredGame)
        {
            float deadline = Time.unscaledTime + MenuTimeoutSeconds;

            // UIManager wires up its GameManager, settings and input refs in Start(), which runs after
            // the scene-loaded callback we are started from. Calling UIContinueGame before then throws.
            while (UIManager.instance == null || GameManager.instance == null ||
                   GameManager.instance.gameSettings == null)
            {
                if (Time.unscaledTime > deadline)
                {
                    Fail("the title screen never finished loading");
                    yield break;
                }
                yield return null;
            }
            yield return null;
            yield return new WaitForEndOfFrame();

            // Until the first-launch calibration screens have been completed, UIContinueGame routes to
            // them instead of loading - and does so without any error.
            GameSettings settings = GameManager.instance.gameSettings;
            if (settings.overscanAdjusted != 1 || settings.brightnessAdjusted != 1)
            {
                Fail("Silksong's first-launch screen calibration has not been completed. Start the game " +
                     "once, finish the overscan and brightness screens, then relaunch");
                yield break;
            }

            SaveGameData saveGameData;
            if (!saveState.TryCreateSaveGameData(out saveGameData))
            {
                Fail("could not read the world data out of the savestate");
                yield break;
            }

            // Before continuing, not after: from here on the in-memory game is training state.
            SaveSuppression.Activate("booting from a savestate");

            RLManager.StaticLogger?.LogInfo($"[AutoStart] Entering the game from \"{saveState.StateName}\"");
            UIManager.instance.UIContinueGame(SaveSlot, saveGameData);
            onEnteredGame?.Invoke();
        }

        private static void Fail(string reason)
        {
            RLManager.StaticLogger?.LogError(
                $"[AutoStart] Could not start automatically: {reason}. You can still load a save and press P.");
        }
    }
}
