using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace SilksongRL
{
    /// <summary>
    /// Bridge to the savestate loader of the Silksong DebugMod.
    ///
    /// ATTRIBUTION / SOURCE
    /// --------------------
    /// Everything this class touches belongs to DebugMod, not to SilksongRL:
    ///   Repository: https://github.com/hk-speedrunning/Silksong.DebugMod  (MIT licensed)
    ///   Plugin GUID: io.github.hk-speedrunning.debugmod
    ///   Verified against DebugMod 1.1.2
    ///
    /// The specific members we use, and where they live in that repository:
    ///   SaveStates/SaveStateManager.cs
    ///     public static void LoadState(SaveState state)   -- starts the load coroutine
    ///   SaveStates/SaveState.cs
    ///     internal SaveState()                            -- parameterless, non-public
    ///     public SaveStateData data                       -- the serialized payload
    ///     public bool IsSet()                             -- false unless saveStateIdentifier is set
    ///     public static SaveState loadingSavestate { get; } -- non-null while a load is in flight
    ///   SaveStates/SaveState.cs, nested type SaveStateData
    ///     internal SaveStateData()                        -- parameterless, non-public
    ///     public void AfterDeserialize()                  -- rebuilds customData from customDataList
    ///     public string saveStateIdentifier
    ///
    /// WE DELIBERATELY DO NOT TOUCH DEBUGMOD'S SAVESTATE STORE.
    /// No calls to GetQuickState/SetQuickState/GetFileState/SetFileState/ImportPack, and we never
    /// write DebugMod.stateOnDeath. SilksongRL keeps its own savestates as JSON under savestates/
    /// next to this plugin and only borrows DebugMod's loader, so the player's own savestates,
    /// quickslot, pages and settings are left exactly as they found them and both mods can be
    /// active at once. Storage is ours; loading is theirs.
    ///
    /// WHY REFLECTION INSTEAD OF AN ASSEMBLY REFERENCE
    /// ----------------------------------------------
    /// 1. DebugMod targets netstandard2.1; this project targets .NET Framework 4.7.2.
    /// 2. DebugMod.dll lands in different folders depending on how it was installed
    ///    (Thunderstore uses BepInEx/plugins/hk_speedrunning-DebugMod/, a manual install
    ///    uses BepInEx/plugins/DebugMod/), so a csproj HintPath would break other people's builds.
    /// 3. If DebugMod changes its internals, we log a clear error once and training refuses to
    ///    start, rather than the whole plugin failing to load.
    ///
    /// This mirrors the pattern DebugMod itself uses for its optional dependencies
    /// (see Interop/I18NInterop.cs and Helpers/InteropHelper.cs in that repository).
    /// </summary>
    internal static class DebugModSaveStates
    {
        internal const string DebugModGuid = "io.github.hk-speedrunning.debugmod";

        private static bool initialized;

        private static Type saveStateType;
        private static Type saveStateDataType;
        private static MethodInfo loadStateMethod;
        private static PropertyInfo loadingSavestateProperty;
        private static MethodInfo isSetMethod;
        private static MethodInfo afterDeserializeMethod;
        private static FieldInfo dataField;
        private static FieldInfo identifierField;

        /// <summary>True when DebugMod is present and every member we need was resolved.</summary>
        public static bool IsAvailable { get; private set; }

        /// <summary>
        /// Resolves the DebugMod members. Safe to call repeatedly; only the first call does work.
        /// Call this after all plugins have loaded (scene load is late enough), not from Awake.
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            try
            {
                PluginInfo info;
                if (!Chainloader.PluginInfos.TryGetValue(DebugModGuid, out info) || info?.Instance == null)
                {
                    Fail("DebugMod is not loaded");
                    return;
                }

                Assembly asm = info.Instance.GetType().Assembly;

                Type managerType = asm.GetType("DebugMod.SaveStates.SaveStateManager");
                saveStateType = asm.GetType("DebugMod.SaveStates.SaveState");

                if (managerType == null || saveStateType == null)
                {
                    Fail("could not find DebugMod's savestate types");
                    return;
                }

                // SaveStateData is a public class nested inside SaveState.
                saveStateDataType = saveStateType.GetNestedType("SaveStateData", BindingFlags.Public);

                loadStateMethod = managerType.GetMethod("LoadState",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { saveStateType }, null);
                loadingSavestateProperty = saveStateType.GetProperty("loadingSavestate",
                    BindingFlags.Public | BindingFlags.Static);
                isSetMethod = saveStateType.GetMethod("IsSet",
                    BindingFlags.Public | BindingFlags.Instance);
                dataField = saveStateType.GetField("data",
                    BindingFlags.Public | BindingFlags.Instance);

                if (saveStateDataType != null)
                {
                    afterDeserializeMethod = saveStateDataType.GetMethod("AfterDeserialize",
                        BindingFlags.Public | BindingFlags.Instance);
                    identifierField = saveStateDataType.GetField("saveStateIdentifier",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (saveStateDataType == null || loadStateMethod == null || loadingSavestateProperty == null ||
                    isSetMethod == null || dataField == null || afterDeserializeMethod == null ||
                    identifierField == null)
                {
                    Fail("DebugMod's savestate API does not look the way we expect (version mismatch?)");
                    return;
                }

                IsAvailable = true;
                RLManager.StaticLogger?.LogInfo($"[SaveStates] Hooked DebugMod savestate loader (version {info.Metadata.Version})");
            }
            catch (Exception e)
            {
                Fail($"error hooking DebugMod: {e.Message}");
            }
        }

        private static void Fail(string reason)
        {
            IsAvailable = false;
            RLManager.StaticLogger?.LogError(
                $"[SaveStates] {reason}. Episodes cannot reset, so training is unavailable.");
        }

        /// <summary>
        /// True while DebugMod is loading a savestate. Goes false once the load has fully finished,
        /// so this is the signal to wait on rather than guessing with a fixed delay.
        /// </summary>
        public static bool IsLoading
        {
            get
            {
                if (!IsAvailable) return false;
                try
                {
                    return loadingSavestateProperty.GetValue(null, null) != null;
                }
                catch (Exception e)
                {
                    Fail($"error reading loadingSavestate: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Builds a DebugMod SaveState from our own JSON, without going near DebugMod's savestate
        /// store. The JSON is the same shape DebugMod writes to disk, so a state recorded in game
        /// can be copied straight into savestates/ and used here.
        ///
        /// Both constructors involved are non-public (but parameterless), hence Activator with
        /// nonPublic: true. We populate with FromJsonOverwrite rather than FromJson so Unity never
        /// has to construct the type itself.
        ///
        /// The returned object is opaque to us - pass it back to TryLoad.
        /// </summary>
        public static bool TryBuildState(string json, out object state)
        {
            state = null;
            if (!IsAvailable) return false;

            try
            {
                object data = Activator.CreateInstance(saveStateDataType, nonPublic: true);
                JsonUtility.FromJsonOverwrite(json, data);

                // Rebuilds the customData dictionary from the serialized "key:::value" list.
                afterDeserializeMethod.Invoke(data, null);

                string identifier = identifierField.GetValue(data) as string;
                if (string.IsNullOrEmpty(identifier))
                {
                    RLManager.StaticLogger?.LogError(
                        "[SaveStates] Savestate JSON has no \"saveStateIdentifier\". DebugMod treats such a " +
                        "state as unset and refuses to load it - give it any non-empty name.");
                    return false;
                }

                object built = Activator.CreateInstance(saveStateType, nonPublic: true);
                dataField.SetValue(built, data);

                if (!(bool)isSetMethod.Invoke(built, null))
                {
                    RLManager.StaticLogger?.LogError("[SaveStates] Built savestate reports itself as unset");
                    return false;
                }

                state = built;
                return true;
            }
            catch (Exception e)
            {
                Fail($"error building savestate from JSON: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Asks DebugMod to load a state previously built by TryBuildState.
        ///
        /// Returns false when the request was refused, which DebugMod does while the hero is
        /// transitioning or while another load is already running (see SaveStateManager.LoadLockout).
        /// The caller should retry on a later frame rather than assuming success.
        /// </summary>
        public static bool TryLoad(object state)
        {
            if (!IsAvailable || state == null) return false;

            try
            {
                loadStateMethod.Invoke(null, new[] { state });

                // LoadState is a no-op when DebugMod refuses, so the only way to know whether it
                // took is to check whether a load is now in flight.
                return IsLoading;
            }
            catch (Exception e)
            {
                Fail($"error loading savestate: {e.Message}");
                return false;
            }
        }

        /// <summary>Name stored in a built state, for logging.</summary>
        public static string GetStateName(object state)
        {
            if (!IsAvailable || state == null) return null;
            try
            {
                object data = dataField.GetValue(state);
                return data == null ? null : identifierField.GetValue(data) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
