using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using UnityEngine;

namespace DurakGame.Network
{
    public class UnityNetworkSessionService : MonoBehaviour, INetworkSessionService
    {
        private const string TransportProtocol = "dtls";
        private const ushort DefaultDirectPort = 7777;
        private const string DirectPrefix = "DIRECT:";
        private const int RelayOperationAttempts = 3;
        private const int RelayRetryDelayMs = 400;
        private const int SessionShutdownTimeoutMs = 2500;
        private const int ClientConnectTimeoutMs = 7000;
        private const int DirectFallbackPortRange = 6;

        private NetworkManager _networkManager;
        private UnityTransport _transport;
        private bool _servicesReady;
        private bool _forceDirectMode;
        private ushort _forcedDirectPort = DefaultDirectPort;

        public NetworkManager NetworkManager => _networkManager;

        public string CurrentJoinCode { get; private set; } = string.Empty;

        private void Awake()
        {
            ApplyCommandLineOverrides();
            EnsureNetworkRuntime();
        }

        public async Task<NetworkSessionInfo> CreateSessionAsync(int maxPlayers)
        {
            EnsureNetworkRuntime();
            await EnsureNoActiveSessionAsync();
            if (_forceDirectMode)
            {
                return StartDirectHost(_forcedDirectPort);
            }

            try
            {
                await EnsureServicesReadyAsync();

                var allocation = await ExecuteWithRetryAsync(
                    "relay allocation",
                    () => RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1)));
                CurrentJoinCode = await ExecuteWithRetryAsync(
                    "relay join code",
                    () => RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId));
                _transport.SetRelayServerData(new RelayServerData(allocation, TransportProtocol));

                var started = _networkManager.StartHost();
                if (!started)
                {
                    throw new InvalidOperationException("Relay host start failed for join code " + CurrentJoinCode + ".");
                }

                return BuildSessionInfo(started, isHost: true);
            }
            catch (Exception relayException)
            {
                Debug.LogWarning("Relay host failed, using direct transport fallback. " + FlattenException(relayException));
                return StartDirectHostFallback(relayException);
            }
        }

        public async Task<NetworkSessionInfo> JoinSessionAsync(string joinCode)
        {
            EnsureNetworkRuntime();
            await EnsureNoActiveSessionAsync();

            CurrentJoinCode = (joinCode ?? string.Empty).Trim().ToUpperInvariant();
            if (CurrentJoinCode.Length == 0)
            {
                throw new InvalidOperationException("Join code is required.");
            }

            string directAddress;
            ushort directPort;
            if (TryParseDirectJoinTarget(CurrentJoinCode, out directAddress, out directPort))
            {
                ConfigureDirectClientTransport(directAddress, directPort);
                var directStarted = _networkManager.StartClient();
                var directConnected = await WaitForClientConnectionAsync(ClientConnectTimeoutMs);
                return BuildSessionInfo(directStarted && directConnected, isHost: false);
            }

            try
            {
                await EnsureServicesReadyAsync();

                var joinAllocation = await ExecuteWithRetryAsync(
                    "relay join",
                    () => RelayService.Instance.JoinAllocationAsync(CurrentJoinCode));
                _transport.SetRelayServerData(new RelayServerData(joinAllocation, TransportProtocol));

                var started = _networkManager.StartClient();
                var connected = await WaitForClientConnectionAsync(ClientConnectTimeoutMs);
                return BuildSessionInfo(started && connected, isHost: false);
            }
            catch (Exception relayException)
            {
                throw new InvalidOperationException(
                    "Join via Relay failed: " + FlattenException(relayException) +
                    " | For local test use DIRECT:127.0.0.1:" + DefaultDirectPort,
                    relayException);
            }
        }

        public void LeaveSession()
        {
            EnsureNetworkRuntime();
            if (_networkManager.IsClient || _networkManager.IsServer)
            {
                _networkManager.Shutdown();
            }

            CurrentJoinCode = string.Empty;
        }

        public bool StartMatch()
        {
            EnsureNetworkRuntime();
            return _networkManager != null && (_networkManager.IsServer || _networkManager.IsHost);
        }

        private void EnsureNetworkRuntime()
        {
            if (_networkManager != null && _transport != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<NetworkManager>();
            if (existing != null)
            {
                _networkManager = existing;
                _transport = existing.GetComponent<UnityTransport>();
                if (_transport == null)
                {
                    _transport = existing.gameObject.AddComponent<UnityTransport>();
                }

                EnsureNetworkConfig();
            }
            else
            {
                var root = new GameObject("DurakNetworkRuntime");
                DontDestroyOnLoad(root);

                _transport = root.AddComponent<UnityTransport>();
                _networkManager = root.AddComponent<NetworkManager>();
                EnsureNetworkConfig();
            }
        }

        private void EnsureNetworkConfig()
        {
            if (_networkManager.NetworkConfig == null)
            {
                _networkManager.NetworkConfig = new NetworkConfig();
            }

            _networkManager.NetworkConfig.EnableSceneManagement = false;
            _networkManager.NetworkConfig.NetworkTransport = _transport;
        }

        private async Task EnsureServicesReadyAsync()
        {
            if (_servicesReady)
            {
                return;
            }

            await ExecuteWithRetryAsync<object>(
                "unity services init/sign-in",
                async () =>
                {
                    if (UnityServices.State == ServicesInitializationState.Uninitialized)
                    {
                        await UnityServices.InitializeAsync();
                    }

                    if (!AuthenticationService.Instance.IsSignedIn)
                    {
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }

                    _servicesReady = true;
                    return null;
                });
        }

        private NetworkSessionInfo BuildSessionInfo(bool started, bool isHost)
        {
            var connected = false;
            if (_networkManager != null)
            {
                if (_networkManager.IsServer || _networkManager.IsHost)
                {
                    connected = true;
                }
                else if (_networkManager.IsClient)
                {
                    connected = _networkManager.IsConnectedClient;
                }
            }

            var connectedCount = 0;
            if (_networkManager != null && connected)
            {
                connectedCount = _networkManager.ConnectedClientsIds.Count;
            }

            return new NetworkSessionInfo
            {
                IsHost = isHost,
                IsConnected = started && connected,
                ConnectedPlayers = connectedCount,
                JoinCode = CurrentJoinCode,
            };
        }

        private NetworkSessionInfo StartDirectHostFallback(Exception relayException)
        {
            Exception lastDirectException = null;
            for (var attempt = 0; attempt < DirectFallbackPortRange; attempt++)
            {
                var port = (ushort)(DefaultDirectPort + attempt);
                try
                {
                    return StartDirectHost(port);
                }
                catch (Exception directException)
                {
                    lastDirectException = directException;
                }
            }

            if (lastDirectException != null)
            {
                throw new InvalidOperationException(
                    "Relay host failed: " + FlattenException(relayException) +
                    " | Direct host fallback also failed: " + FlattenException(lastDirectException),
                    relayException);
            }

            throw new InvalidOperationException("Relay and direct host fallback failed for unknown reasons.", relayException);
        }

        private NetworkSessionInfo StartDirectHost(ushort port)
        {
            ConfigureDirectHostTransport(port);
            CurrentJoinCode = DirectPrefix + "127.0.0.1:" + port;

            var started = _networkManager.StartHost();
            if (!started)
            {
                throw new InvalidOperationException("Direct host start failed for " + CurrentJoinCode + ".");
            }

            return BuildSessionInfo(started, isHost: true);
        }

        private void ConfigureDirectHostTransport(ushort port)
        {
            _transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
        }

        private void ConfigureDirectClientTransport(string address, ushort port)
        {
            _transport.SetConnectionData(address, port);
        }

        private async Task EnsureNoActiveSessionAsync()
        {
            if (_networkManager == null || (!_networkManager.IsClient && !_networkManager.IsServer))
            {
                return;
            }

            _networkManager.Shutdown();
            var deadline = DateTime.UtcNow.AddMilliseconds(SessionShutdownTimeoutMs);
            while ((_networkManager.IsClient || _networkManager.IsServer) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }

        private async Task<bool> WaitForClientConnectionAsync(int timeoutMs)
        {
            if (_networkManager == null)
            {
                return false;
            }

            var cancellation = new CancellationTokenSource(Mathf.Max(1, timeoutMs));
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (_networkManager.IsConnectedClient)
                    {
                        return true;
                    }

                    if (!_networkManager.IsClient)
                    {
                        return false;
                    }

                    await Task.Delay(50, cancellation.Token);
                }
            }
            catch (TaskCanceledException)
            {
                // Timeout is handled by return false below.
            }

            return _networkManager.IsConnectedClient;
        }

        private async Task<T> ExecuteWithRetryAsync<T>(string operationName, Func<Task<T>> operation)
        {
            Exception lastException = null;
            for (var attempt = 1; attempt <= RelayOperationAttempts; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    var canRetry = attempt < RelayOperationAttempts && IsLikelyTransientRelayFailure(exception);
                    if (!canRetry)
                    {
                        break;
                    }

                    Debug.LogWarning(
                        "Retrying " + operationName +
                        " (" + attempt + "/" + RelayOperationAttempts + "): " +
                        FlattenException(exception));
                    await Task.Delay(RelayRetryDelayMs * attempt);
                }
            }

            throw new InvalidOperationException(
                operationName + " failed after retries: " + FlattenException(lastException),
                lastException);
        }

        private static bool IsLikelyTransientRelayFailure(Exception exception)
        {
            var message = FlattenException(exception);
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var value = message.ToLowerInvariant();
            return value.Contains("timeout") ||
                   value.Contains("timed out") ||
                   value.Contains("temporar") ||
                   value.Contains("rate") ||
                   value.Contains("429") ||
                   value.Contains("503") ||
                   value.Contains("unavailable") ||
                   value.Contains("network") ||
                   value.Contains("transport") ||
                   value.Contains("dns");
        }

        private static bool TryParseDirectJoinTarget(string rawJoinCode, out string address, out ushort port)
        {
            address = string.Empty;
            port = DefaultDirectPort;

            if (string.IsNullOrWhiteSpace(rawJoinCode))
            {
                return false;
            }

            var value = rawJoinCode.Trim();
            if (value.StartsWith(DirectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(DirectPrefix.Length);
            }
            else
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var lastColon = value.LastIndexOf(':');
            if (lastColon > 0 && lastColon < value.Length - 1)
            {
                ushort parsedPort;
                if (ushort.TryParse(value.Substring(lastColon + 1), out parsedPort))
                {
                    address = value.Substring(0, lastColon);
                    port = parsedPort;
                }
                else
                {
                    address = value;
                }
            }
            else
            {
                address = value;
            }

            if (address.Equals("LOCALHOST", StringComparison.OrdinalIgnoreCase))
            {
                address = "127.0.0.1";
            }

            return !string.IsNullOrWhiteSpace(address);
        }

        private static string FlattenException(Exception exception)
        {
            if (exception == null)
            {
                return "Unknown error.";
            }

            var messages = new System.Text.StringBuilder();
            var current = exception;
            var depth = 0;
            while (current != null && depth < 6)
            {
                if (depth > 0)
                {
                    messages.Append(" -> ");
                }

                messages.Append(current.GetType().Name).Append(": ").Append(current.Message);
                current = current.InnerException;
                depth++;
            }

            return messages.ToString();
        }

        private void ApplyCommandLineOverrides()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                var token = args[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (string.Equals(token, "-durak-force-direct", StringComparison.OrdinalIgnoreCase))
                {
                    _forceDirectMode = true;
                }
                else if (string.Equals(token, "-durak-direct-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (ushort.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port > 0)
                    {
                        _forcedDirectPort = port;
                    }
                }
            }
        }
    }
}
