using UnityEngine;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;

namespace SilksongRL
{
    [Serializable]
    public class SocketConfig
    {
        public string Host = "localhost";
        public int Port = 8000;
        public float Timeout = 10f;
        public int MaxReconnectAttempts = 5;
        public float ReconnectDelay = 1f;
    }

    [Serializable]
    public class StateRequest
    {
        public float[] state;
    }

    [Serializable]
    public class ActionResponse
    {
        public int[] action;
    }

    [Serializable]
    public class TransitionRequest
    {
        public float[] state;
        public int[] action;
        public float reward;
        public float[] next_state;
        public bool done;
    }

    [Serializable]
    public class InitRequest
    {
        public string boss_name;
        public int observation_size;
        public int[] action_space_shape;
        public string observation_type;  // "vector" or "hybrid"
        public int vector_obs_size;      // Size of vector portion (for hybrid, this is before visual data)
        public int visual_width;         // Width of visual observation (0 if vector-only)
        public int visual_height;        // Height of visual observation (0 if vector-only)
    }

    [Serializable]
    public class InitResponse
    {
        public bool initialized;
        public string boss_name;
        public int observation_size;
        public bool checkpoint_loaded;
    }

    // Message types for protocol
    public enum MessageType : byte
    {
        Initialize = 0,
        GetAction = 1,
        StoreTransition = 2,
        InitResponse = 10,
        ActionResponse = 11,
        TransitionAck = 12,
        Error = 255
    }

    public class SocketClient
    {
        private SocketConfig config;
        private TcpClient client;
        private NetworkStream stream;
        private bool isConnected = false;
        
        private const int READ_TIMEOUT_MS = 30000;
        
        private readonly SemaphoreSlim socketLock = new SemaphoreSlim(1, 1);
        
        public float lastPingMs = 0f;

        public bool IsConnected => isConnected && client?.Connected == true;

        public SocketClient(SocketConfig config = null)
        {
            this.config = config ?? new SocketConfig();
        }

        public async Task<bool> ConnectAsync()
        {
            if (IsConnected) return true;

            for (int attempt = 0; attempt < config.MaxReconnectAttempts; attempt++)
            {
                try
                {
                    RLManager.StaticLogger?.LogInfo($"[SocketClient] Connecting to {config.Host}:{config.Port} (attempt {attempt + 1}/{config.MaxReconnectAttempts})");

                    client = new TcpClient();
                    client.NoDelay = true; // Disable Nagle's algorithm for lower latency
                    
                    var connectTask = client.ConnectAsync(config.Host, config.Port);
                    var timeoutTask = Task.Delay((int)(config.Timeout * 1000));

                    if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
                    {
                        throw new TimeoutException("Connection timed out");
                    }

                    await connectTask.ConfigureAwait(false); // Propagate any exceptions

                    stream = client.GetStream();
                    isConnected = true;

                    RLManager.StaticLogger?.LogInfo("[SocketClient] Connected successfully");
                    return true;
                }
                catch (Exception e)
                {
                    RLManager.StaticLogger?.LogWarning($"[SocketClient] Connection failed: {e.Message}");
                    Disconnect();

                    if (attempt < config.MaxReconnectAttempts - 1)
                    {
                        await Task.Delay((int)(config.ReconnectDelay * 1000 * Math.Pow(2, attempt))).ConfigureAwait(false);
                    }
                }
            }

            RLManager.StaticLogger?.LogError("[SocketClient] Failed to connect after all attempts");
            return false;
        }

        public void Disconnect()
        {
            try
            {
                stream?.Close();
                client?.Close();
            }
            catch (Exception e)
            {
                RLManager.StaticLogger?.LogWarning($"[SocketClient] Error during disconnect: {e.Message}");
            }
            finally
            {
                stream = null;
                client = null;
                isConnected = false;
            }
        }

        public async Task<InitResponse> InitializeAsync(string bossName, int observationSize, int[] actionSpaceShape, ObservationType observationType, int vectorObsSize, int visualWidth = 0, int visualHeight = 0)
        {
            await socketLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!await EnsureConnectedAsync().ConfigureAwait(false)) return null;

                InitRequest request = new InitRequest
                {
                    boss_name = bossName,
                    observation_size = observationSize,
                    action_space_shape = actionSpaceShape,
                    observation_type = observationType == ObservationType.Hybrid ? "hybrid" : "vector",
                    vector_obs_size = vectorObsSize,
                    visual_width = visualWidth,
                    visual_height = visualHeight
                };

                string json = JsonUtility.ToJson(request);
                await SendMessageAsync(MessageType.Initialize, json).ConfigureAwait(false);

                var (msgType, responseJson) = await ReceiveMessageAsync().ConfigureAwait(false);

                if (msgType == MessageType.InitResponse)
                {
                    InitResponse response = JsonUtility.FromJson<InitResponse>(responseJson);
                    RLManager.StaticLogger?.LogInfo($"[SocketClient] Initialized for boss '{response.boss_name}' with observation size {response.observation_size}");
                    return response;
                }
                else if (msgType == MessageType.Error)
                {
                    RLManager.StaticLogger?.LogError($"[SocketClient] Server error: {responseJson}");
                    return null;
                }

                return null;
            }
            catch (Exception e)
            {
                RLManager.StaticLogger?.LogError($"[SocketClient] Initialize failed: {e.Message}");
                HandleConnectionErrorInternal();
                return null;
            }
            finally
            {
                socketLock.Release();
            }
        }

        public async Task<Action> GetActionAsync(float[] observations)
        {
            // Acquire lock to prevent concurrent socket operations
            await socketLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!await EnsureConnectedAsync().ConfigureAwait(false)) return null;

                StateRequest request = new StateRequest { state = observations };
                string json = JsonUtility.ToJson(request);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                await SendMessageAsync(MessageType.GetAction, json).ConfigureAwait(false);

                var (msgType, responseJson) = await ReceiveMessageAsync().ConfigureAwait(false);
                
                stopwatch.Stop();
                lastPingMs = (float)stopwatch.Elapsed.TotalMilliseconds * Time.timeScale;

                if (msgType == MessageType.ActionResponse)
                {
                    ActionResponse response = JsonUtility.FromJson<ActionResponse>(responseJson);
                    return ActionManager.ArrayToAction(response, RLManager.CurrentActionSpaceType);
                }
                else if (msgType == MessageType.Error)
                {
                    RLManager.StaticLogger?.LogError($"[SocketClient] Server error: {responseJson}");
                    return null;
                }

                return null;
            }
            catch (TimeoutException e)
            {
                RLManager.StaticLogger?.LogWarning($"[SocketClient] GetAction timed out: {e.Message}");
                HandleConnectionErrorInternal();
                return null;
            }
            catch (Exception e)
            {
                RLManager.StaticLogger?.LogError($"[SocketClient] GetAction failed: {e.Message}");
                HandleConnectionErrorInternal();
                return null;
            }
            finally
            {
                socketLock.Release();
            }
        }

        public async Task<bool> StoreTransitionAsync(float[] observations, Action action, float reward, float[] nextObservations, bool done)
        {
            // Acquire lock to prevent concurrent socket operations
            await socketLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!await EnsureConnectedAsync().ConfigureAwait(false)) return false;

                TransitionRequest request = new TransitionRequest
                {
                    state = observations,
                    action = ActionManager.ActionToArray(action, RLManager.CurrentActionSpaceType),
                    reward = reward,
                    next_state = nextObservations,
                    done = done
                };

                string json = JsonUtility.ToJson(request);
                await SendMessageAsync(MessageType.StoreTransition, json).ConfigureAwait(false);

                var (msgType, responseJson) = await ReceiveMessageAsync().ConfigureAwait(false);

                if (msgType == MessageType.TransitionAck)
                {
                    return true;
                }
                else if (msgType == MessageType.Error)
                {
                    RLManager.StaticLogger?.LogError($"[SocketClient] Server error: {responseJson}");
                    return false;
                }

                return false;
            }
            catch (TimeoutException e)
            {
                RLManager.StaticLogger?.LogWarning($"[SocketClient] StoreTransition timed out: {e.Message}");
                HandleConnectionErrorInternal();
                return false;
            }
            catch (Exception e)
            {
                RLManager.StaticLogger?.LogError($"[SocketClient] StoreTransition failed: {e.Message}");
                HandleConnectionErrorInternal();
                return false;
            }
            finally
            {
                socketLock.Release();
            }
        }

        // Message format:
        // [4 bytes: length (big-endian)] [1 byte: message type] [N bytes: JSON payload]

        private async Task SendMessageAsync(MessageType msgType, string payload)
        {
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
            int totalLength = 1 + payloadBytes.Length; // 1 byte for message type + payload

            byte[] lengthBytes = BitConverter.GetBytes(totalLength);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes); // Convert to big-endian
            }

            // Combine all data into single buffer to avoid multiple TCP packets
            byte[] fullMessage = new byte[4 + 1 + payloadBytes.Length];
            Buffer.BlockCopy(lengthBytes, 0, fullMessage, 0, 4);
            fullMessage[4] = (byte)msgType;
            Buffer.BlockCopy(payloadBytes, 0, fullMessage, 5, payloadBytes.Length);

            await stream.WriteAsync(fullMessage, 0, fullMessage.Length).ConfigureAwait(false);
        }

        private async Task<(MessageType, string)> ReceiveMessageAsync()
        {
            byte[] lengthBytes = await ReadExactAsync(4).ConfigureAwait(false);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }
            int length = BitConverter.ToInt32(lengthBytes, 0);

            if (length <= 0 || length > 1024 * 1024)
            {
                throw new InvalidDataException($"Invalid message length: {length}");
            }

            byte[] msgTypeBytes = await ReadExactAsync(1).ConfigureAwait(false);
            MessageType msgType = (MessageType)msgTypeBytes[0];

            int payloadLength = length - 1;
            string payload = "";
            
            if (payloadLength > 0)
            {
                byte[] payloadBytes = await ReadExactAsync(payloadLength).ConfigureAwait(false);
                payload = Encoding.UTF8.GetString(payloadBytes);
            }

            return (msgType, payload);
        }

        // Helper to read exact number of bytes (TCP can deliver partial data)
        private async Task<byte[]> ReadExactAsync(int count)
        {
            byte[] buffer = new byte[count];
            int totalRead = 0;

            using (var cts = new System.Threading.CancellationTokenSource(READ_TIMEOUT_MS))
            {
                try
                {
                    while (totalRead < count)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, totalRead, count - totalRead, cts.Token).ConfigureAwait(false);
                        
                        if (bytesRead == 0)
                        {
                            throw new IOException("Connection closed by server");
                        }
                        
                        totalRead += bytesRead;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"Read timed out after {READ_TIMEOUT_MS}ms - connection may be stale");
                }
            }

            return buffer;
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (IsConnected) return true;
            return await ConnectAsync().ConfigureAwait(false);
        }

        private void HandleConnectionErrorInternal()
        {
            RLManager.StaticLogger?.LogWarning("[SocketClient] Connection error detected - marking for reconnection");
            isConnected = false;
            
            // Force close the socket to ensure clean reconnection
            try
            {
                stream?.Close();
                client?.Close();
            }
            catch { }
            finally
            {
                stream = null;
                client = null;
            }
        }

    }
}

