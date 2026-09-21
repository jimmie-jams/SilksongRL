using System;
using System.IO;
using System.Reflection;

namespace SilksongRL
{
    /// <summary>
    /// Owns the savestate SilksongRL resets to, kept as JSON in our own folder rather than in
    /// DebugMod's savestate store.
    ///
    /// Layout, next to SilksongRL.dll:
    ///   BepInEx/plugins/SilksongRL/SilksongRL.dll
    ///   BepInEx/plugins/SilksongRL/savestates/lace_1.json
    ///
    /// The JSON is exactly what DebugMod writes for a savestate, so one recorded in game can be
    /// copied straight out of DebugMod's savestate directory and used here.
    ///
    /// The state object is built once and reused for every reset, since building it means parsing
    /// a whole PlayerData and we do that thousands of times a run otherwise.
    /// </summary>
    public class EncounterSaveState
    {
        public const string SaveStateFolderName = "savestates";

        private object state;

        /// <summary>Path we actually loaded from, for logging.</summary>
        public string SourcePath { get; private set; }

        /// <summary>Name recorded inside the savestate.</summary>
        public string StateName { get; private set; }

        /// <summary>True once a savestate has been parsed and is ready to load.</summary>
        public bool IsReady => state != null;

        /// <summary>
        /// Directory we look for savestate JSON in: a "savestates" folder beside this assembly.
        /// </summary>
        public static string SaveStateDirectory
        {
            get
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(assemblyDir))
                {
                    // Should not happen for a BepInEx plugin loaded off disk, but do not crash.
                    assemblyDir = BepInEx.Paths.PluginPath;
                }
                return Path.Combine(assemblyDir, SaveStateFolderName);
            }
        }

        /// <summary>
        /// Reads and parses the savestate for an encounter.
        ///
        /// fileOverride comes from silksongrl.cfg and wins when set. It may be a bare file name
        /// (resolved against the savestates folder) or an absolute path.
        /// </summary>
        public bool TryLoadFromDisk(IBossEncounter encounter, string fileOverride)
        {
            state = null;
            SourcePath = null;
            StateName = null;

            if (!DebugModSaveStates.IsAvailable)
                return false;

            string fileName = !string.IsNullOrEmpty(fileOverride)
                ? fileOverride
                : encounter.GetSavestateFile();

            if (string.IsNullOrEmpty(fileName))
            {
                RLManager.StaticLogger?.LogError(
                    $"[SaveStates] {encounter.GetEncounterName()} does not name a savestate file");
                return false;
            }

            string path = Path.IsPathRooted(fileName)
                ? fileName
                : Path.Combine(SaveStateDirectory, fileName);

            if (!File.Exists(path))
            {
                RLManager.StaticLogger?.LogError(
                    $"[SaveStates] No savestate at {path}. Make sure the savestates folder sits next to " +
                    "SilksongRL.dll, or point SavestateFile in silksongrl.cfg at one.");
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                RLManager.StaticLogger?.LogError($"[SaveStates] Could not read {path}: {e.Message}");
                return false;
            }

            object built;
            if (!DebugModSaveStates.TryBuildState(json, out built))
            {
                RLManager.StaticLogger?.LogError($"[SaveStates] Could not parse savestate at {path}");
                return false;
            }

            state = built;
            SourcePath = path;
            StateName = DebugModSaveStates.GetStateName(built);
            return true;
        }

        /// <summary>
        /// Asks DebugMod to load our savestate. Returns false if the request was refused this frame
        /// (hero transitioning, or another load already running) - retry on a later frame.
        /// </summary>
        public bool TryLoad()
        {
            return IsReady && DebugModSaveStates.TryLoad(state);
        }
    }
}
