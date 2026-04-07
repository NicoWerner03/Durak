using System;
using System.Globalization;
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
            if (_forceDirectMode)
            {
                return StartDirectHost(_forcedDirectPort);
            }

            try
            {
                await EnsureServicesReadyAsync();

                var allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1));
                CurrentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                _transport.SetRelayServerData(new RelayServerData(allocation, TransportProtocol));

                var started = _networkManager.StartHost();
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
                return BuildSessionInfo(directStarted, isHost: false);
            }

            try
            {
                await EnsureServicesReadyAsync();

                var joinAllocation = await RelayService.Instance.JoinAllocationAsync(CurrentJoinCode);
                _transport.SetRelayServerData(new RelayServerData(joinAllocation, TransportProtocol));

                var started = _networkManager.StartClient();
                return BuildSessionInfo(started, isHost: false);
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

            try
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
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Unity Services initialization failed: " + FlattenException(exception), exception);
            }
        }

        private NetworkSessionInfo BuildSessionInfo(bool started, bool isHost)
        {
            var connected = _networkManager != null && (_networkManager.IsClient || _networkManager.IsServer);
            var connectedCount = 0;
            if (_networkManager != null)
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
            try
            {
                return StartDirectHost(DefaultDirectPort);
            }
            catch (Exception directException)
            {
                throw new InvalidOperationException(
                    "Relay host failed: " + FlattenException(relayException) +
                    " | Direct host fallback also failed: " + FlattenException(directException),
                    relayException);
            }
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
