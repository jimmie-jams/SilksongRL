using System.Reflection;
using GlobalEnums;
using HarmonyLib;

namespace SilksongRL
{
    /// <summary>
    /// Whether the game is in a state where DebugMod can load a savestate.
    ///
    /// DebugMod's SaveState.LoadImpl dereferences DialogueBox, DialogueYesNoBox and QuestYesNoBox's
    /// private static _instance fields without null checks, and those only exist once the in-game
    /// HUD has been built. Loading before then throws a NullReferenceException inside DebugMod.
    /// Checking the instances directly targets that failure rather than guessing with a delay.
    /// </summary>
    internal static class GameReadiness
    {
        private static readonly FieldInfo[] HudSingletons =
        {
            AccessTools.Field(typeof(DialogueBox), "_instance"),
            AccessTools.Field(typeof(DialogueYesNoBox), "_instance"),
            AccessTools.Field(typeof(QuestYesNoBox), "_instance"),
        };

        private static bool warnedAboutMissingFields;

        /// <summary>Why a savestate cannot be loaded right now, or null if it can.</summary>
        public static string NotReadyReason()
        {
            GameManager gm = GameManager.instance;
            if (gm == null)
                return "no GameManager yet";
            if (gm.GameState != GameState.PLAYING)
                return $"game state is {gm.GameState}";
            if (gm.IsInSceneTransition)
                return "scene transition in progress";
            if (!gm.IsGameplayScene())
                return "not in a gameplay scene";

            HeroController hero = HeroController.instance;
            if (hero == null)
                return "no hero yet";
            if (hero.cState.transitioning)
                return "hero is transitioning";

            foreach (FieldInfo field in HudSingletons)
            {
                if (field == null)
                {
                    // A game update renamed one. Skip the check rather than wait forever on it.
                    if (!warnedAboutMissingFields)
                    {
                        warnedAboutMissingFields = true;
                        RLManager.StaticLogger?.LogWarning(
                            "[Readiness] A HUD singleton field was not found - the game may have changed. " +
                            "Savestate loads may fail if started too early.");
                    }
                    continue;
                }

                // Unity's == null also catches destroyed objects, so compare as UnityEngine.Object.
                if (field.GetValue(null) as UnityEngine.Object == null)
                    return $"{field.DeclaringType.Name} not created yet";
            }

            return null;
        }
    }
}
