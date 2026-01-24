using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System;
using System.Threading.Tasks;
using HutongGames.PlayMaker.Actions;

namespace SilksongRL
{
    [BepInPlugin("silksongrl", "SilksongRL", "1.0.0")]
    public class RLManager : BaseUnityPlugin
    {
        // Config entries
        private ConfigEntry<string> configHost;
        private ConfigEntry<int> configPort;
        private ConfigEntry<string> configTargetBoss;
        private ConfigEntry<float> configStepInterval;
        private ConfigEntry<bool> configEvalMode;

        public static bool isAgentControlEnabled = false;
        private bool isInEval;

        // Hero and Boss references (tracked via Harmony patches)
        public static HeroController Hero { get; private set; }
        public static HealthManager Boss { get; private set; }
        
        // Static logger reference for use in Harmony patches and other classes
        public static BepInEx.Logging.ManualLogSource StaticLogger;

        private SocketClient client;
        private float stepInterval;

        private static IBossEncounter currentEncounter;
        
        public static ActionSpaceType CurrentActionSpaceType => 
            currentEncounter?.GetActionSpaceType() ?? ActionSpaceType.Basic;
        
        private TrainingEpisodeManager episodeManager;

        private float[] previousObservations;
        private Action previousAction;
        private bool hasPreviousStep = false;
        private bool pendingDoneTransition = false; // Set when episode ends, cleared after storing final transition
        private int whoDied = -1; // 0: Hornet, 1: Boss (same use as above ^^^)

        private bool isProcessingStep = false;

        public static Action currentAction = new Action();

        private float lastStepTime = 0f;

        private void Awake()
        {
            StaticLogger = Logger;
            StaticLogger.LogInfo("SilksongRL Mod loaded.");
            
            configHost = Config.Bind("Connection", "Host", "localhost", 
                "Server hostname to connect to");
            configPort = Config.Bind("Connection", "Port", 8000, 
                "Server port to connect to");
            configTargetBoss = Config.Bind("Training", "TargetBoss", "Lace_1",
                "Target boss encounter (e.g., Lace_1)");
            configStepInterval = Config.Bind("Training", "StepInterval", 0.1f,
                "Time interval between RL steps in seconds");
            configEvalMode = Config.Bind("Training", "EvalMode", false,
                "If true, runs in evaluation mode (no training, just inference)");
            
            stepInterval = configStepInterval.Value;
            isInEval = configEvalMode.Value;
            
            var harmony = new Harmony("silksongrl");
            harmony.PatchAll();
            
            SocketConfig socketConfig = new SocketConfig
            {
                Host = configHost.Value,
                Port = configPort.Value,
                Timeout = 10f,
                MaxReconnectAttempts = 5,
                ReconnectDelay = 1f
            };
            client = new SocketClient(socketConfig);
            StaticLogger.LogInfo($"[RL] Connecting to {configHost.Value}:{configPort.Value}");

            // Initialize encounter based on config
            currentEncounter = CreateEncounter(configTargetBoss.Value);
            if (currentEncounter == null)
            {
                StaticLogger.LogError($"[RL] Unknown boss encounter: {configTargetBoss.Value}");
                return;
            }
            
            episodeManager = new TrainingEpisodeManager(currentEncounter);
            episodeManager.OnSimulateKeyPress = SimulateKeyPress;
            episodeManager.OnResetComplete = ResetRL;

            StaticLogger.LogInfo($"[RL] Initialized with encounter: {currentEncounter.GetEncounterName()}");
            StaticLogger.LogInfo($"[RL] Observation size: {currentEncounter.GetObservationSize()}");
            StaticLogger.LogInfo($"[RL] Action space: {CurrentActionSpaceType} ({ActionManager.GetActionSpaceShape(CurrentActionSpaceType).Length} actions)");
            StaticLogger.LogInfo($"[RL] Mode: {(isInEval ? "Evaluation" : "Training")}");
            
            // Initialize screen capture updater for hybrid encounters
            if (currentEncounter.GetObservationType() == ObservationType.Hybrid)
            {
                var screenCapture = currentEncounter.GetScreenCapture();
                if (screenCapture != null)
                {
                    var updater = gameObject.AddComponent<ScreenCaptureUpdater>();
                    updater.Initialize(screenCapture);
                    StaticLogger.LogInfo("[RL] Screen capture updater initialized for hybrid observation");
                }
            }
            
            _ = InitializeClientAsync();
        }

        private IBossEncounter CreateEncounter(string bossName)
        {
            switch (bossName)
            {
                case "Lace_1":
                    return new LaceEncounter();
                case "Lace_2":
                    return new LaceSecondEncounter();
                case "Savage_Beastfly":
                    return new SavageBeastflyEncounter();
                default:
                    return null;
            }
        }

        private void OnDestroy()
        {
            client?.Disconnect();
            StaticLogger.LogInfo("[RL] Client disconnected");
        }

        private async Task InitializeClientAsync()
        {
            try
            {
                // Connect first (no-op for HTTP, establishes connection for sockets)
                bool connected = await client.ConnectAsync();
                if (!connected)
                {
                    StaticLogger.LogError("[RL] Failed to connect to server!");
                    return;
                }

                string bossName = currentEncounter.GetEncounterName();
                int obsSize = currentEncounter.GetObservationSize();
                int[] actionSpaceShape = ActionManager.GetActionSpaceShape(CurrentActionSpaceType);
                ObservationType obsType = currentEncounter.GetObservationType();
                int vectorObsSize = currentEncounter.GetVectorObservationSize();
                var (visualWidth, visualHeight) = currentEncounter.GetVisualObservationSize();
                
                StaticLogger.LogInfo($"[RL] Initializing client for boss: {bossName}");
                StaticLogger.LogInfo($"[RL]   Observation size: {obsSize}, type: {obsType}, vector size: {vectorObsSize}");
                if (obsType == ObservationType.Hybrid)
                    StaticLogger.LogInfo($"[RL]   Visual size: {visualWidth}x{visualHeight}");
                
                var response = await client.InitializeAsync(bossName, obsSize, actionSpaceShape, obsType, vectorObsSize, visualWidth, visualHeight);
                
                if (response != null && response.initialized)
                {
                    StaticLogger.LogInfo($"[RL] Client initialized successfully. Checkpoint loaded: {response.checkpoint_loaded}");
                }
                else
                {
                    StaticLogger.LogError("[RL] Client initialization failed!");
                }
            }
            catch (Exception e)
            {
                StaticLogger.LogError($"[RL] Error initializing client: {e.Message}");
            }
        }

        private void Update()
        {
            // Toggle control when pressing P
            if (Input.GetKeyDown(KeyCode.P))
            {
                isAgentControlEnabled = !isAgentControlEnabled;
                
                StaticLogger.LogInfo($"[RL] Agent control {(isAgentControlEnabled ? "enabled" : "disabled")}. Hero: {(Hero != null ? "Found" : "Not found")}, Boss: {(Boss != null ? "Found" : "Not found")}");
            }
            
            // Log resolution diagnostics when pressing L
            if (Input.GetKeyDown(KeyCode.L))
            {
                ResolutionDiagnostics.LogResolutionInfo(StaticLogger);
                ResolutionDiagnostics.CheckForPotentialIssues(StaticLogger);
            }
        }

        private void FixedUpdate()
        {

            if (!isAgentControlEnabled)
            {
                currentAction = new Action();
                return;
            }

            // Ensure currentAction is never null
            if (currentAction == null)
            {
                currentAction = new Action();
            }

            // Update episode state (death detection, etc.)
            var previousState = episodeManager.CurrentState;
            episodeManager.UpdateEpisodeState(Hero, Boss);
            
            // If we just transitioned to a death state, mark that we need to store a done transition
            if (previousState == TrainingEpisodeManager.EpisodeState.Training && 
                (episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.HeroDead || 
                 episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.BossDead ||
                 episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.HeroStuck))
            {
                pendingDoneTransition = true;
                // HeroDead or HeroStuck = hero died (0), BossDead = boss died (1)
                whoDied = (episodeManager.CurrentState == TrainingEpisodeManager.EpisodeState.BossDead) ? 1 : 0;
                StaticLogger.LogInfo($"[RL] Episode ended - will store final transition with done=true");
            }

            // Handle reset sequence if needed
            if (episodeManager.HandleResetSequence(Hero, Boss))
            {
                return; // Skip normal step processing during reset
            }

            // Step on a **frame independent** fixed time interval 
            if (Time.fixedTime - lastStepTime >= stepInterval)
            {
                lastStepTime = Time.fixedTime;
                _ = StepRLAsync();
            }
        }

        private async Task StepRLAsync()
        {
            if (isProcessingStep) return;

            isProcessingStep = true;

            try
            {
                if (Hero == null)
                {
                    StaticLogger.LogWarning("[RL] Hero is null - waiting for hero to spawn");
                    return;
                }
                if (Boss == null)
                {
                    StaticLogger.LogWarning("[RL] Boss is null - waiting for boss to spawn");
                    return;
                }
                
                float[] currentObservations = currentEncounter.ExtractObservationArray(Hero, Boss);

                // Store transition from previous step (training mode only)
                if (!isInEval && hasPreviousStep && previousObservations != null)
                {
                    float reward = currentEncounter.CalculateReward(previousObservations, currentObservations, whoDied);
                    bool done = pendingDoneTransition;

                    // Run socket call off the main thread
                    await Task.Run(async () =>
                    {
                        await client.StoreTransitionAsync(previousObservations, previousAction, reward, currentObservations, done).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                    
                    // If this was a terminal transition, clear previous step data and don't get new action
                    if (done)
                    {
                        StaticLogger.LogInfo($"[RL] Stored final transition with done=true");
                        previousObservations = null;
                        previousAction = null;
                        hasPreviousStep = false;
                        pendingDoneTransition = false;
                        whoDied = -1;
                        return; // Don't get new action, we're in reset
                    }
                }

                // Get action from the RL agent
                Action action = await Task.Run(async () =>
                {
                    return await client.GetActionAsync(currentObservations).ConfigureAwait(false);
                }).ConfigureAwait(false);

                if (action != null)
                {
                    currentAction = action;

                    // Only track previous state during training (needed for storing transitions)
                    if (!isInEval)
                    {
                        previousObservations = currentObservations;
                        previousAction = action;
                        hasPreviousStep = true;
                    }
                }
            }
            catch (Exception e)
            {
                StaticLogger.LogError($"[RL] Error in StepRL: {e.Message}");
            }
            finally
            {
                isProcessingStep = false;
            }
        }


        private void ResetRL()
        {
            // Clear current action and processing flag
            // Note: We DON'T clear previousObservations/previousAction/hasPreviousStep here
            // because we need to store the final transition with done=true on the first step of the new episode
            currentAction = new Action();
            isProcessingStep = false;
        }

        // Static flag for F5 simulation
        public static bool simulateF5Press = false;

        private void SimulateKeyPress(KeyCode key)
        {
            if (key == KeyCode.F5)
            {
                simulateF5Press = true;
                StaticLogger.LogInfo("[RL] Simulating F5 key press");
            }
        }

        /// <summary>
        /// Harmony patch to automatically catch Hero spawns.
        /// </summary>
        [HarmonyPatch(typeof(HeroController), "Awake")]
        public static class HeroController_Awake_Patch
        {
            static void Postfix(HeroController __instance)
            {
                Hero = __instance;
                StaticLogger.LogInfo("[RL] Hero found and assigned (Harmony patch)");
            }
        }

        /// <summary>
        /// Harmony patch to automatically catch Boss spawns.
        /// </summary>
        [HarmonyPatch(typeof(HealthManager), "Awake")]
        public static class HealthManager_Awake_Patch
        {
            static void Postfix(HealthManager __instance)
            {
                // Only assign if we have an encounter configured and this matches
                if (currentEncounter != null && currentEncounter.IsEncounterMatch(__instance))
                {
                    Boss = __instance;
                    StaticLogger.LogInfo($"[RL] Boss locked: {__instance.name} (Harmony patch)");
                }
            }
        }
    }
}
