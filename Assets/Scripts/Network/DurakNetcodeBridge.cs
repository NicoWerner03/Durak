using System;
using System.Collections.Generic;
using DurakGame.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DurakGame.Network
{
    public class DurakNetcodeBridge : MonoBehaviour
    {
        private const string StartMatchMessage = "durak_start_match";
        private const string ClientIntentMessage = "durak_client_intent";
        private const string AuthoritativeIntentMessage = "durak_authoritative_intent";
        private const string StateSnapshotMessage = "durak_state_snapshot";
        private const string StateSnapshotRequestMessage = "durak_state_snapshot_request";
        private const string LobbyReadyMessage = "durak_lobby_ready";
        private const string LobbyStateMessage = "durak_lobby_state";
        private const string MatchResumeRequestMessage = "durak_match_resume_request";
        private const string ClientHelloMessage = "durak_client_hello";
        private const string LocalIdentityPlayerPrefsKey = "durak.local.player.identity.v1";

        private readonly Dictionary<ulong, int> _clientToPlayerId = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, bool> _clientReady = new Dictionary<ulong, bool>();
        private readonly Dictionary<ulong, string> _clientIdentity = new Dictionary<ulong, string>();
        private readonly Dictionary<string, int> _identityToPlayerId = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<PlayerSeat> _activeSeats = new List<PlayerSeat>();
        private NetworkManager _networkManager;
        private bool _handlersRegistered;
        private bool _namedMessageHandlersRegistered;
        private bool _clientHelloSent;
        private int _sequence;
        private int _activeMatchSeed;
        private bool _hasActiveMatch;
        private string _localPlayerIdentity = string.Empty;
        private LobbyStateSnapshot _latestLobbyState = new LobbyStateSnapshot();

        public Func<PlayerIntent, bool> ServerIntentHandler { get; set; }
        public Func<StateSnapshot> ServerSnapshotProvider { get; set; }

        public event Action<NetworkMatchStartData> MatchStarted;
        public event Action<PlayerIntent, int> AuthoritativeIntentReceived;
        public event Action<StateSnapshot> StateSnapshotReceived;
        public event Action<LobbyStateSnapshot> LobbyStateChanged;
        public event Action<int> ConnectedPlayerCountChanged;
        public event Action<string> SessionTerminated;

        public bool IsServer => _networkManager != null && _networkManager.IsServer;
        public bool IsClient => _networkManager != null && _networkManager.IsClient;
        public bool IsHost => _networkManager != null && _networkManager.IsHost;
        public string LocalPlayerIdentity => _localPlayerIdentity;

        public int ConnectedPlayerCount
        {
            get
            {
                if (_networkManager == null)
                {
                    return 0;
                }

                return _networkManager.ConnectedClientsIds.Count;
            }
        }

        public LobbyStateSnapshot CurrentLobbyState => _latestLobbyState != null ? _latestLobbyState.Clone() : new LobbyStateSnapshot();

        public bool CanStartMatch(int maxPlayers)
        {
            return string.IsNullOrEmpty(GetStartMatchBlockReason(maxPlayers));
        }

        public string GetStartMatchBlockReason(int maxPlayers)
        {
            if (!IsServer || _networkManager == null)
            {
                return "Only host can start the match.";
            }

            var connectedCount = _networkManager.ConnectedClientsIds.Count;
            if (connectedCount < 2)
            {
                return "Need at least 2 connected players.";
            }

            if (connectedCount > maxPlayers)
            {
                return "Too many connected players for this match.";
            }

            var connected = new List<ulong>(_networkManager.ConnectedClientsIds);
            for (var i = 0; i < connected.Count; i++)
            {
                var clientId = connected[i];
                bool isReady;
                if (!_clientReady.TryGetValue(clientId, out isReady) || !isReady)
                {
                    return "All connected players must be READY.";
                }
            }

            return string.Empty;
        }

        public void Initialize(NetworkManager networkManager)
        {
            if (networkManager == null)
            {
                ResetRuntimeState();
                return;
            }

            EnsureLocalIdentity();

            if (_networkManager == networkManager && _handlersRegistered)
            {
                TryRegisterNamedMessageHandlers();
                if (!IsServer && !IsClient)
                {
                    ResetSessionState();
                    return;
                }

                if (IsServer)
                {
                    RegisterClientIdentity(_networkManager.LocalClientId, _localPlayerIdentity);
                    if (!_clientReady.ContainsKey(_networkManager.LocalClientId))
                    {
                        _clientReady[_networkManager.LocalClientId] = false;
                    }

                    PublishLobbyState();
                }
                else if (IsClient)
                {
                    TrySendClientHello();
                }

                return;
            }

            UnregisterHandlers();
            ResetSessionState();
            _networkManager = networkManager;
            RegisterHandlers();
        }

        public void ResetRuntimeState()
        {
            UnregisterHandlers();
            _networkManager = null;
            ResetSessionState();
        }

        public void RequestStateSnapshotFromServer()
        {
            if (!IsClient || IsServer || _networkManager == null || _networkManager.CustomMessagingManager == null)
            {
                return;
            }

            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                writer.WriteValueSafe(1);
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    StateSnapshotRequestMessage,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        public bool SetLocalReady(bool ready)
        {
            if (_networkManager == null || (!IsServer && !IsClient))
            {
                return false;
            }

            if (IsServer)
            {
                _clientReady[_networkManager.LocalClientId] = ready;
                PublishLobbyState();
                return true;
            }

            if (_networkManager.CustomMessagingManager == null)
            {
                return false;
            }

            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(ready);
                writer.WriteValueSafe(new FixedString64Bytes(_localPlayerIdentity ?? string.Empty));
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    LobbyReadyMessage,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            return true;
        }

        public void RequestMatchResumeFromServer()
        {
            if (!IsClient || IsServer || _networkManager == null || _networkManager.CustomMessagingManager == null)
            {
                return;
            }

            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                writer.WriteValueSafe(1);
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    MatchResumeRequestMessage,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        private void Update()
        {
            if (_handlersRegistered && !_namedMessageHandlersRegistered)
            {
                TryRegisterNamedMessageHandlers();
            }

            if (_handlersRegistered && IsClient && !IsServer)
            {
                TrySendClientHello();
            }
        }

        public bool TryStartMatchForConnectedPlayers(int seed, int maxPlayers, out NetworkMatchStartData hostStartData)
        {
            hostStartData = null;
            if (!IsServer)
            {
                return false;
            }

            if (!CanStartMatch(maxPlayers))
            {
                return false;
            }

            var clientIds = new List<ulong>(_networkManager.ConnectedClientsIds);
            clientIds.Sort();

            _clientToPlayerId.Clear();
            _identityToPlayerId.Clear();
            var seatCount = clientIds.Count;
            var seats = new List<PlayerSeat>(seatCount);
            for (var i = 0; i < seatCount; i++)
            {
                var clientId = clientIds[i];
                _clientToPlayerId[clientId] = i;
                var identity = ResolveClientIdentity(clientId);
                if (!string.IsNullOrEmpty(identity))
                {
                    _identityToPlayerId[identity] = i;
                }

                var displayName = ResolveLobbyDisplayName(clientId, i);
                seats.Add(new PlayerSeat
                {
                    PlayerId = i,
                    DisplayName = displayName,
                    IsBot = false,
                    OwnerClientId = clientId,
                    PlayerIdentity = identity,
                });
            }

            _sequence = 0;
            _activeMatchSeed = seed;
            _hasActiveMatch = true;
            _activeSeats.Clear();
            _activeSeats.AddRange(CloneSeats(seats));

            for (var i = 0; i < seatCount; i++)
            {
                var clientId = clientIds[i];
                if (clientId == _networkManager.LocalClientId)
                {
                    continue;
                }

                var remoteStartData = new NetworkMatchStartData
                {
                    Seed = seed,
                    LocalPlayerId = _clientToPlayerId[clientId],
                    Seats = CloneSeats(seats),
                };
                SendStartMatchMessage(clientId, remoteStartData);
            }

            hostStartData = new NetworkMatchStartData
            {
                Seed = seed,
                LocalPlayerId = _clientToPlayerId[_networkManager.LocalClientId],
                Seats = CloneSeats(seats),
            };

            MatchStarted?.Invoke(hostStartData);

            if (TryCreateServerSnapshot(out var snapshot))
            {
                snapshot.Sequence = 0;
                snapshot.State.TurnSequence = 0;
                BroadcastStateSnapshotToRemoteClients(snapshot);
            }

            return true;
        }

        public bool SubmitLocalIntent(PlayerIntent intent)
        {
            if (intent == null)
            {
                return false;
            }

            if (IsServer)
            {
                return HandleServerAuthoritativeIntent(intent.Clone());
            }

            if (!IsClient)
            {
                return false;
            }

            SendIntentToServer(intent);
            return true;
        }

        private void RegisterHandlers()
        {
            if (_networkManager == null || _handlersRegistered)
            {
                return;
            }

            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _networkManager.OnClientDisconnectCallback += OnClientDisconnected;

            _handlersRegistered = true;
            _namedMessageHandlersRegistered = false;
            TryRegisterNamedMessageHandlers();

            if (IsServer)
            {
                RegisterClientIdentity(_networkManager.LocalClientId, _localPlayerIdentity);
                if (!_clientReady.ContainsKey(_networkManager.LocalClientId))
                {
                    _clientReady[_networkManager.LocalClientId] = false;
                }

                PublishLobbyState();
            }

            ConnectedPlayerCountChanged?.Invoke(ConnectedPlayerCount);
        }

        private void UnregisterHandlers()
        {
            if (_networkManager == null || !_handlersRegistered)
            {
                return;
            }

            _networkManager.OnClientConnectedCallback -= OnClientConnected;
            _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;

            if (_networkManager.CustomMessagingManager != null && _namedMessageHandlersRegistered)
            {
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StartMatchMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ClientIntentMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(AuthoritativeIntentMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StateSnapshotMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StateSnapshotRequestMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyReadyMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyStateMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MatchResumeRequestMessage);
                _networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ClientHelloMessage);
            }

            _handlersRegistered = false;
            _namedMessageHandlersRegistered = false;
        }

        private void TryRegisterNamedMessageHandlers()
        {
            if (_networkManager == null || _namedMessageHandlersRegistered)
            {
                return;
            }

            var messaging = _networkManager.CustomMessagingManager;
            if (messaging == null)
            {
                return;
            }

            messaging.RegisterNamedMessageHandler(StartMatchMessage, OnStartMatchMessage);
            messaging.RegisterNamedMessageHandler(ClientIntentMessage, OnClientIntentMessage);
            messaging.RegisterNamedMessageHandler(AuthoritativeIntentMessage, OnAuthoritativeIntentMessage);
            messaging.RegisterNamedMessageHandler(StateSnapshotMessage, OnStateSnapshotMessage);
            messaging.RegisterNamedMessageHandler(StateSnapshotRequestMessage, OnStateSnapshotRequestMessage);
            messaging.RegisterNamedMessageHandler(LobbyReadyMessage, OnLobbyReadyMessage);
            messaging.RegisterNamedMessageHandler(LobbyStateMessage, OnLobbyStateMessage);
            messaging.RegisterNamedMessageHandler(MatchResumeRequestMessage, OnMatchResumeRequestMessage);
            messaging.RegisterNamedMessageHandler(ClientHelloMessage, OnClientHelloMessage);
            _namedMessageHandlersRegistered = true;
        }

        private void OnDestroy()
        {
            UnregisterHandlers();
        }

        private void ResetSessionState()
        {
            _clientToPlayerId.Clear();
            _clientReady.Clear();
            _clientIdentity.Clear();
            _identityToPlayerId.Clear();
            _activeSeats.Clear();
            _clientHelloSent = false;
            _sequence = 0;
            _activeMatchSeed = 0;
            _hasActiveMatch = false;
            _latestLobbyState = new LobbyStateSnapshot();
        }

        private void OnClientConnected(ulong clientId)
        {
            if (IsServer)
            {
                RegisterClientIdentity(clientId, ResolveClientIdentity(clientId));
                if (!_clientReady.ContainsKey(clientId))
                {
                    _clientReady[clientId] = false;
                }

                PublishLobbyState();
                if (_hasActiveMatch)
                {
                    TryHandleClientRejoin(clientId);
                }
            }

            ConnectedPlayerCountChanged?.Invoke(ConnectedPlayerCount);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            _clientToPlayerId.Remove(clientId);
            _clientReady.Remove(clientId);
            _clientIdentity.Remove(clientId);

            if (IsServer)
            {
                PublishLobbyState();
            }

            ConnectedPlayerCountChanged?.Invoke(ConnectedPlayerCount);

            if (!IsServer && _networkManager != null)
            {
                if (clientId == NetworkManager.ServerClientId)
                {
                    SessionTerminated?.Invoke("Host disconnected. Session ended.");
                }
                else if (clientId == _networkManager.LocalClientId)
                {
                    _clientHelloSent = false;
                    SessionTerminated?.Invoke("Disconnected from host. Match ended.");
                }
            }
        }

        private void SendStartMatchMessage(ulong targetClientId, NetworkMatchStartData data)
        {
            using (var writer = new FastBufferWriter(2048, Allocator.Temp))
            {
                writer.WriteValueSafe(data.Seed);
                writer.WriteValueSafe(data.LocalPlayerId);
                writer.WriteValueSafe(data.Seats.Count);

                for (var i = 0; i < data.Seats.Count; i++)
                {
                    var seat = data.Seats[i];
                    var name = new FixedString64Bytes(seat.DisplayName ?? string.Empty);
                    var playerIdentity = new FixedString64Bytes(seat.PlayerIdentity ?? string.Empty);
                    writer.WriteValueSafe(seat.PlayerId);
                    writer.WriteValueSafe(seat.IsBot);
                    writer.WriteValueSafe(seat.OwnerClientId);
                    writer.WriteValueSafe(name);
                    writer.WriteValueSafe(playerIdentity);
                }

                _networkManager.CustomMessagingManager.SendNamedMessage(
                    StartMatchMessage,
                    targetClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnStartMatchMessage(ulong senderClientId, FastBufferReader reader)
        {
            var startData = new NetworkMatchStartData();
            reader.ReadValueSafe(out startData.Seed);
            reader.ReadValueSafe(out startData.LocalPlayerId);

            int seatCount;
            reader.ReadValueSafe(out seatCount);

            startData.Seats = new List<PlayerSeat>(seatCount);
            for (var i = 0; i < seatCount; i++)
            {
                var seat = new PlayerSeat();
                FixedString64Bytes fixedName;
                reader.ReadValueSafe(out seat.PlayerId);
                reader.ReadValueSafe(out seat.IsBot);
                reader.ReadValueSafe(out seat.OwnerClientId);
                reader.ReadValueSafe(out fixedName);
                FixedString64Bytes fixedIdentity;
                reader.ReadValueSafe(out fixedIdentity);
                seat.DisplayName = fixedName.ToString();
                seat.PlayerIdentity = fixedIdentity.ToString();
                startData.Seats.Add(seat);
            }

            MatchStarted?.Invoke(startData);
        }

        private void SendIntentToServer(PlayerIntent intent)
        {
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                WriteIntentPayload(writer, intent);
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    ClientIntentMessage,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnClientIntentMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsServer)
            {
                return;
            }

            if (!_clientToPlayerId.TryGetValue(senderClientId, out var playerId))
            {
                return;
            }

            var intent = ReadIntentPayload(reader);
            intent.PlayerId = playerId;
            HandleServerAuthoritativeIntent(intent);
        }

        private bool HandleServerAuthoritativeIntent(PlayerIntent intent)
        {
            if (!IsServer)
            {
                return false;
            }

            if (ServerIntentHandler != null && !ServerIntentHandler(intent))
            {
                return false;
            }

            _sequence += 1;
            BroadcastAuthoritativeIntent(intent, _sequence);

            if (TryCreateServerSnapshot(out var snapshot))
            {
                snapshot.Sequence = _sequence;
                snapshot.State.TurnSequence = _sequence;
                BroadcastStateSnapshotToRemoteClients(snapshot);
            }

            return true;
        }

        private void BroadcastAuthoritativeIntent(PlayerIntent intent, int sequence)
        {
            var remoteClients = new List<ulong>(_networkManager.ConnectedClientsIds);
            for (var i = 0; i < remoteClients.Count; i++)
            {
                var clientId = remoteClients[i];
                if (clientId == _networkManager.LocalClientId)
                {
                    continue;
                }

                using (var writer = new FastBufferWriter(256, Allocator.Temp))
                {
                    writer.WriteValueSafe(sequence);
                    WriteIntentPayload(writer, intent);
                    _networkManager.CustomMessagingManager.SendNamedMessage(
                        AuthoritativeIntentMessage,
                        clientId,
                        writer,
                        NetworkDelivery.ReliableSequenced);
                }
            }
        }

        private void BroadcastStateSnapshotToRemoteClients(StateSnapshot snapshot)
        {
            var remoteClients = new List<ulong>(_networkManager.ConnectedClientsIds);
            for (var i = 0; i < remoteClients.Count; i++)
            {
                var clientId = remoteClients[i];
                if (clientId == _networkManager.LocalClientId)
                {
                    continue;
                }

                SendStateSnapshotMessage(clientId, snapshot);
            }
        }

        private void SendStateSnapshotMessage(ulong targetClientId, StateSnapshot snapshot)
        {
            using (var writer = new FastBufferWriter(16384, Allocator.Temp))
            {
                WriteStateSnapshotPayload(writer, snapshot);
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    StateSnapshotMessage,
                    targetClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        private void OnAuthoritativeIntentMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (IsServer)
            {
                return;
            }

            int sequence;
            reader.ReadValueSafe(out sequence);
            var intent = ReadIntentPayload(reader);
            AuthoritativeIntentReceived?.Invoke(intent, sequence);
        }

        private void OnStateSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (IsServer)
            {
                return;
            }

            var snapshot = ReadStateSnapshotPayload(reader);
            StateSnapshotReceived?.Invoke(snapshot);
        }

        private void OnStateSnapshotRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsServer)
            {
                return;
            }

            int ignored;
            reader.ReadValueSafe(out ignored);

            // Rejoin safety: if a client requests a snapshot while a match is active,
            // force rejoin handling first so ownership is rebound before snapshot send.
            if (_hasActiveMatch)
            {
                TryHandleClientRejoin(senderClientId);
            }

            if (TryCreateServerSnapshot(out var snapshot))
            {
                SendStateSnapshotMessage(senderClientId, snapshot);
            }
        }

        private void OnLobbyReadyMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsServer)
            {
                return;
            }

            bool isReady;
            reader.ReadValueSafe(out isReady);
            FixedString64Bytes fixedIdentity;
            reader.ReadValueSafe(out fixedIdentity);
            RegisterClientIdentity(senderClientId, fixedIdentity.ToString());
            _clientReady[senderClientId] = isReady;
            PublishLobbyState();

            // If client is in lobby while match is already running, use this as a
            // reliable signal to push rejoin state.
            if (_hasActiveMatch)
            {
                TryHandleClientRejoin(senderClientId);
            }
        }

        private void OnLobbyStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (IsServer)
            {
                return;
            }

            var snapshot = ReadLobbyStatePayload(reader);
            _latestLobbyState = snapshot.Clone();
            LobbyStateChanged?.Invoke(snapshot);
        }

        private void OnMatchResumeRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsServer)
            {
                return;
            }

            int ignored;
            reader.ReadValueSafe(out ignored);
            TryHandleClientRejoin(senderClientId);
        }

        private void OnClientHelloMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsServer)
            {
                return;
            }

            FixedString64Bytes fixedIdentity;
            reader.ReadValueSafe(out fixedIdentity);
            RegisterClientIdentity(senderClientId, fixedIdentity.ToString());

            if (!_clientReady.ContainsKey(senderClientId))
            {
                _clientReady[senderClientId] = false;
            }

            PublishLobbyState();
            if (_hasActiveMatch)
            {
                TryHandleClientRejoin(senderClientId);
            }
        }

        private bool TryCreateServerSnapshot(out StateSnapshot snapshot)
        {
            snapshot = null;
            if (!IsServer || ServerSnapshotProvider == null)
            {
                return false;
            }

            snapshot = ServerSnapshotProvider();
            if (snapshot == null || snapshot.State == null)
            {
                return false;
            }

            ApplySeatOwnershipToSnapshot(snapshot);

            if (snapshot.Sequence < 0)
            {
                snapshot.Sequence = 0;
            }

            if (snapshot.Sequence == 0 && _sequence > 0)
            {
                snapshot.Sequence = _sequence;
            }

            if (snapshot.State.TurnSequence != snapshot.Sequence)
            {
                snapshot.State.TurnSequence = snapshot.Sequence;
            }

            return true;
        }

        private void ApplySeatOwnershipToSnapshot(StateSnapshot snapshot)
        {
            if (snapshot == null || snapshot.State == null || snapshot.State.Players == null || _activeSeats.Count == 0)
            {
                return;
            }

            for (var i = 0; i < snapshot.State.Players.Count; i++)
            {
                var player = snapshot.State.Players[i];
                if (player == null || player.IsBot)
                {
                    continue;
                }

                for (var seatIndex = 0; seatIndex < _activeSeats.Count; seatIndex++)
                {
                    var seat = _activeSeats[seatIndex];
                    if (seat.PlayerId != player.PlayerId)
                    {
                        continue;
                    }

                    player.OwnerClientId = seat.OwnerClientId;
                    player.PlayerIdentity = seat.PlayerIdentity ?? string.Empty;
                    player.IsConnected = IsConnectedClient(seat.OwnerClientId);
                    break;
                }
            }
        }

        private void PublishLobbyState()
        {
            if (!IsServer || _networkManager == null)
            {
                return;
            }

            var snapshot = BuildLobbyStateSnapshot();
            _latestLobbyState = snapshot.Clone();
            LobbyStateChanged?.Invoke(snapshot);
            SendLobbyStateToRemoteClients(snapshot);
        }

        private void SendLobbyStateToRemoteClients(LobbyStateSnapshot snapshot)
        {
            var remoteClients = new List<ulong>(_networkManager.ConnectedClientsIds);
            for (var i = 0; i < remoteClients.Count; i++)
            {
                var clientId = remoteClients[i];
                if (clientId == _networkManager.LocalClientId)
                {
                    continue;
                }

                SendLobbyStateMessage(clientId, snapshot);
            }
        }

        private void SendLobbyStateMessage(ulong targetClientId, LobbyStateSnapshot snapshot)
        {
            if (_networkManager == null || _networkManager.CustomMessagingManager == null)
            {
                return;
            }

            using (var writer = new FastBufferWriter(2048, Allocator.Temp))
            {
                WriteLobbyStatePayload(writer, snapshot);
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    LobbyStateMessage,
                    targetClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        private LobbyStateSnapshot BuildLobbyStateSnapshot()
        {
            var snapshot = new LobbyStateSnapshot();
            if (_networkManager == null)
            {
                return snapshot;
            }

            var connected = new List<ulong>(_networkManager.ConnectedClientsIds);
            connected.Sort();
            for (var i = 0; i < connected.Count; i++)
            {
                var clientId = connected[i];
                bool isReady;
                if (!_clientReady.TryGetValue(clientId, out isReady))
                {
                    isReady = false;
                }

                snapshot.Players.Add(new LobbyPlayerInfo
                {
                    ClientId = clientId,
                    DisplayName = ResolveLobbyDisplayName(clientId, i),
                    PlayerIdentity = ResolveClientIdentity(clientId),
                    IsHost = clientId == NetworkManager.ServerClientId,
                    IsReady = isReady,
                });
            }

            return snapshot;
        }

        private void TryHandleClientRejoin(ulong clientId)
        {
            if (!IsServer || _networkManager == null || !_hasActiveMatch)
            {
                return;
            }

            if (clientId == _networkManager.LocalClientId)
            {
                return;
            }

            if (_clientToPlayerId.TryGetValue(clientId, out var existingPlayerId))
            {
                SendActiveMatchStateToClient(clientId, existingPlayerId);
                return;
            }

            if (TryRebindClientByIdentity(clientId, out var reboundPlayerId))
            {
                SendActiveMatchStateToClient(clientId, reboundPlayerId);
                return;
            }

            var seatIndex = FindReconnectSeatIndex();
            if (seatIndex < 0 || seatIndex >= _activeSeats.Count)
            {
                return;
            }

            var seat = _activeSeats[seatIndex];
            seat.OwnerClientId = clientId;
            _activeSeats[seatIndex] = seat;
            _clientToPlayerId[clientId] = seat.PlayerId;
            if (!string.IsNullOrEmpty(seat.PlayerIdentity))
            {
                _identityToPlayerId[seat.PlayerIdentity] = seat.PlayerId;
            }

            SendActiveMatchStateToClient(clientId, seat.PlayerId);
        }

        private void SendActiveMatchStateToClient(ulong clientId, int localPlayerId)
        {
            if (!IsServer || _networkManager == null || !_hasActiveMatch)
            {
                return;
            }

            RebindClientToPlayerId(clientId, localPlayerId);

            for (var i = 0; i < _activeSeats.Count; i++)
            {
                var seat = _activeSeats[i];
                if (seat.PlayerId != localPlayerId)
                {
                    continue;
                }

                seat.OwnerClientId = clientId;
                if (string.IsNullOrEmpty(seat.PlayerIdentity))
                {
                    seat.PlayerIdentity = ResolveClientIdentity(clientId);
                }

                _activeSeats[i] = seat;
                break;
            }

            var startData = new NetworkMatchStartData
            {
                Seed = _activeMatchSeed,
                LocalPlayerId = localPlayerId,
                Seats = CloneSeats(_activeSeats),
            };

            SendStartMatchMessage(clientId, startData);

            if (TryCreateServerSnapshot(out var snapshot))
            {
                SendStateSnapshotMessage(clientId, snapshot);
            }

            PublishLobbyState();
        }

        private int FindReconnectSeatIndex()
        {
            for (var i = 0; i < _activeSeats.Count; i++)
            {
                var seat = _activeSeats[i];
                if (seat.OwnerClientId == NetworkManager.ServerClientId)
                {
                    continue;
                }

                if (!IsConnectedClient(seat.OwnerClientId))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool IsConnectedClient(ulong clientId)
        {
            if (_networkManager == null)
            {
                return false;
            }

            var connected = _networkManager.ConnectedClientsIds;
            for (var i = 0; i < connected.Count; i++)
            {
                if (connected[i] == clientId)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureLocalIdentity()
        {
            if (!string.IsNullOrWhiteSpace(_localPlayerIdentity))
            {
                return;
            }

            var existing = PlayerPrefs.GetString(LocalIdentityPlayerPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _localPlayerIdentity = existing.Trim();
                return;
            }

            _localPlayerIdentity = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(LocalIdentityPlayerPrefsKey, _localPlayerIdentity);
            PlayerPrefs.Save();
        }

        private void TrySendClientHello()
        {
            if (_clientHelloSent || !IsClient || IsServer || _networkManager == null || _networkManager.CustomMessagingManager == null)
            {
                return;
            }

            EnsureLocalIdentity();
            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(new FixedString64Bytes(_localPlayerIdentity ?? string.Empty));
                _networkManager.CustomMessagingManager.SendNamedMessage(
                    ClientHelloMessage,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableSequenced);
            }

            _clientHelloSent = true;
        }

        private void RegisterClientIdentity(ulong clientId, string playerIdentity)
        {
            var normalizedIdentity = string.IsNullOrWhiteSpace(playerIdentity) ? string.Empty : playerIdentity.Trim();
            if (string.IsNullOrEmpty(normalizedIdentity))
            {
                return;
            }

            _clientIdentity[clientId] = normalizedIdentity;
        }

        private string ResolveClientIdentity(ulong clientId)
        {
            if (_clientIdentity.TryGetValue(clientId, out var identity) && !string.IsNullOrWhiteSpace(identity))
            {
                return identity;
            }

            if (_networkManager != null && clientId == _networkManager.LocalClientId && !string.IsNullOrWhiteSpace(_localPlayerIdentity))
            {
                return _localPlayerIdentity;
            }

            return string.Empty;
        }

        private bool TryRebindClientByIdentity(ulong clientId, out int playerId)
        {
            playerId = -1;
            var identity = ResolveClientIdentity(clientId);
            if (string.IsNullOrEmpty(identity))
            {
                return false;
            }

            if (!_identityToPlayerId.TryGetValue(identity, out playerId))
            {
                return false;
            }

            RebindClientToPlayerId(clientId, playerId);
            return true;
        }

        private void RebindClientToPlayerId(ulong clientId, int playerId)
        {
            var staleClientIds = new List<ulong>();
            foreach (var mapping in _clientToPlayerId)
            {
                if (mapping.Value == playerId && mapping.Key != clientId)
                {
                    staleClientIds.Add(mapping.Key);
                }
            }

            for (var i = 0; i < staleClientIds.Count; i++)
            {
                _clientToPlayerId.Remove(staleClientIds[i]);
            }

            _clientToPlayerId[clientId] = playerId;

            var identity = ResolveClientIdentity(clientId);
            if (!string.IsNullOrEmpty(identity))
            {
                _identityToPlayerId[identity] = playerId;
            }
        }

        private static void WriteIntentPayload(FastBufferWriter writer, PlayerIntent intent)
        {
            writer.WriteValueSafe(intent.PlayerId);
            writer.WriteValueSafe((int)intent.Type);
            writer.WriteValueSafe(intent.HasCard);
            writer.WriteValueSafe(intent.TargetPairIndex);

            var suit = intent.HasCard ? (int)intent.Card.Suit : -1;
            var rank = intent.HasCard ? (int)intent.Card.Rank : -1;
            writer.WriteValueSafe(suit);
            writer.WriteValueSafe(rank);
        }

        private static PlayerIntent ReadIntentPayload(FastBufferReader reader)
        {
            int playerId;
            int intentType;
            bool hasCard;
            int targetPairIndex;
            int suit;
            int rank;

            reader.ReadValueSafe(out playerId);
            reader.ReadValueSafe(out intentType);
            reader.ReadValueSafe(out hasCard);
            reader.ReadValueSafe(out targetPairIndex);
            reader.ReadValueSafe(out suit);
            reader.ReadValueSafe(out rank);

            var intent = new PlayerIntent
            {
                PlayerId = playerId,
                Type = (PlayerIntentType)intentType,
                HasCard = hasCard,
                TargetPairIndex = targetPairIndex,
            };

            if (hasCard)
            {
                intent.Card = new Card((Suit)suit, (Rank)rank);
            }

            return intent;
        }

        private static void WriteStateSnapshotPayload(FastBufferWriter writer, StateSnapshot snapshot)
        {
            writer.WriteValueSafe(snapshot.Sequence);
            WriteGameStatePayload(writer, snapshot.State);
        }

        private static StateSnapshot ReadStateSnapshotPayload(FastBufferReader reader)
        {
            var snapshot = new StateSnapshot();
            reader.ReadValueSafe(out snapshot.Sequence);
            snapshot.State = ReadGameStatePayload(reader);
            return snapshot;
        }

        private static void WriteLobbyStatePayload(FastBufferWriter writer, LobbyStateSnapshot snapshot)
        {
            var value = snapshot ?? new LobbyStateSnapshot();
            var players = value.Players ?? new List<LobbyPlayerInfo>();
            writer.WriteValueSafe(players.Count);
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                writer.WriteValueSafe(player.ClientId);
                writer.WriteValueSafe(player.IsHost);
                writer.WriteValueSafe(player.IsReady);
                writer.WriteValueSafe(new FixedString64Bytes(player.DisplayName ?? string.Empty));
                writer.WriteValueSafe(new FixedString64Bytes(player.PlayerIdentity ?? string.Empty));
            }
        }

        private static LobbyStateSnapshot ReadLobbyStatePayload(FastBufferReader reader)
        {
            int count;
            reader.ReadValueSafe(out count);
            var snapshot = new LobbyStateSnapshot
            {
                Players = new List<LobbyPlayerInfo>(count),
            };

            for (var i = 0; i < count; i++)
            {
                var player = new LobbyPlayerInfo();
                FixedString64Bytes displayName;
                reader.ReadValueSafe(out player.ClientId);
                reader.ReadValueSafe(out player.IsHost);
                reader.ReadValueSafe(out player.IsReady);
                reader.ReadValueSafe(out displayName);
                FixedString64Bytes playerIdentity;
                reader.ReadValueSafe(out playerIdentity);
                player.DisplayName = displayName.ToString();
                player.PlayerIdentity = playerIdentity.ToString();
                snapshot.Players.Add(player);
            }

            return snapshot;
        }

        private static void WriteGameStatePayload(FastBufferWriter writer, GameState state)
        {
            var value = state ?? new GameState();
            writer.WriteValueSafe((int)value.Phase);
            writer.WriteValueSafe((int)value.TrumpSuit);
            writer.WriteValueSafe(value.DeckCount);
            writer.WriteValueSafe(value.CurrentTurnPlayerId);
            writer.WriteValueSafe(value.TurnSequence);

            var playerOrder = value.PlayerOrder ?? new List<int>();
            writer.WriteValueSafe(playerOrder.Count);
            for (var i = 0; i < playerOrder.Count; i++)
            {
                writer.WriteValueSafe(playerOrder[i]);
            }

            var players = value.Players ?? new List<PlayerState>();
            writer.WriteValueSafe(players.Count);
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                writer.WriteValueSafe(player.PlayerId);
                writer.WriteValueSafe(new FixedString64Bytes(player.DisplayName ?? string.Empty));
                writer.WriteValueSafe(player.IsBot);
                writer.WriteValueSafe(player.OwnerClientId);
                writer.WriteValueSafe(new FixedString64Bytes(player.PlayerIdentity ?? string.Empty));
                writer.WriteValueSafe(player.IsConnected);

                var hand = player.Hand ?? new List<Card>();
                writer.WriteValueSafe(hand.Count);
                for (var cardIndex = 0; cardIndex < hand.Count; cardIndex++)
                {
                    WriteCard(writer, hand[cardIndex]);
                }
            }

            var round = value.Round ?? new RoundState();
            writer.WriteValueSafe(round.RoundNumber);
            writer.WriteValueSafe(round.AttackerId);
            writer.WriteValueSafe(round.DefenderId);
            writer.WriteValueSafe(round.AttackLimit);
            writer.WriteValueSafe(round.DefenderInitialHandCount);
            writer.WriteValueSafe(round.ActiveAttackerIndex);

            var attackerOrder = round.AttackerOrder ?? new List<int>();
            writer.WriteValueSafe(attackerOrder.Count);
            for (var i = 0; i < attackerOrder.Count; i++)
            {
                writer.WriteValueSafe(attackerOrder[i]);
            }

            var table = round.Table ?? new List<TablePair>();
            writer.WriteValueSafe(table.Count);
            for (var i = 0; i < table.Count; i++)
            {
                var pair = table[i];
                WriteCard(writer, pair.AttackCard);
                writer.WriteValueSafe(pair.IsDefended);
                WriteCard(writer, pair.DefenseCard);
            }

            var result = value.MatchResult ?? new MatchResult();
            writer.WriteValueSafe(result.DurakPlayerId);
            var winners = result.Winners ?? new List<int>();
            writer.WriteValueSafe(winners.Count);
            for (var i = 0; i < winners.Count; i++)
            {
                writer.WriteValueSafe(winners[i]);
            }
        }

        private static GameState ReadGameStatePayload(FastBufferReader reader)
        {
            var state = new GameState();

            int phase;
            int trumpSuit;
            reader.ReadValueSafe(out phase);
            reader.ReadValueSafe(out trumpSuit);
            reader.ReadValueSafe(out state.DeckCount);
            reader.ReadValueSafe(out state.CurrentTurnPlayerId);
            reader.ReadValueSafe(out state.TurnSequence);
            state.Phase = (GamePhase)phase;
            state.TrumpSuit = (Suit)trumpSuit;

            int playerOrderCount;
            reader.ReadValueSafe(out playerOrderCount);
            state.PlayerOrder = new List<int>(playerOrderCount);
            for (var i = 0; i < playerOrderCount; i++)
            {
                int playerId;
                reader.ReadValueSafe(out playerId);
                state.PlayerOrder.Add(playerId);
            }

            int playersCount;
            reader.ReadValueSafe(out playersCount);
            state.Players = new List<PlayerState>(playersCount);
            for (var i = 0; i < playersCount; i++)
            {
                var player = new PlayerState();
                FixedString64Bytes displayName;
                FixedString64Bytes playerIdentity;
                reader.ReadValueSafe(out player.PlayerId);
                reader.ReadValueSafe(out displayName);
                reader.ReadValueSafe(out player.IsBot);
                reader.ReadValueSafe(out player.OwnerClientId);
                reader.ReadValueSafe(out playerIdentity);
                reader.ReadValueSafe(out player.IsConnected);
                player.DisplayName = displayName.ToString();
                player.PlayerIdentity = playerIdentity.ToString();

                int handCount;
                reader.ReadValueSafe(out handCount);
                player.Hand = new List<Card>(handCount);
                for (var cardIndex = 0; cardIndex < handCount; cardIndex++)
                {
                    player.Hand.Add(ReadCard(reader));
                }

                state.Players.Add(player);
            }

            state.Round = new RoundState();
            reader.ReadValueSafe(out state.Round.RoundNumber);
            reader.ReadValueSafe(out state.Round.AttackerId);
            reader.ReadValueSafe(out state.Round.DefenderId);
            reader.ReadValueSafe(out state.Round.AttackLimit);
            reader.ReadValueSafe(out state.Round.DefenderInitialHandCount);
            reader.ReadValueSafe(out state.Round.ActiveAttackerIndex);

            int attackerOrderCount;
            reader.ReadValueSafe(out attackerOrderCount);
            state.Round.AttackerOrder = new List<int>(attackerOrderCount);
            for (var i = 0; i < attackerOrderCount; i++)
            {
                int attackerId;
                reader.ReadValueSafe(out attackerId);
                state.Round.AttackerOrder.Add(attackerId);
            }

            int tableCount;
            reader.ReadValueSafe(out tableCount);
            state.Round.Table = new List<TablePair>(tableCount);
            for (var i = 0; i < tableCount; i++)
            {
                var pair = new TablePair
                {
                    AttackCard = ReadCard(reader),
                };
                reader.ReadValueSafe(out pair.IsDefended);
                pair.DefenseCard = ReadCard(reader);
                state.Round.Table.Add(pair);
            }

            state.MatchResult = new MatchResult();
            reader.ReadValueSafe(out state.MatchResult.DurakPlayerId);
            int winnersCount;
            reader.ReadValueSafe(out winnersCount);
            state.MatchResult.Winners = new List<int>(winnersCount);
            for (var i = 0; i < winnersCount; i++)
            {
                int winnerId;
                reader.ReadValueSafe(out winnerId);
                state.MatchResult.Winners.Add(winnerId);
            }

            return state;
        }

        private static void WriteCard(FastBufferWriter writer, Card card)
        {
            writer.WriteValueSafe((int)card.Suit);
            writer.WriteValueSafe((int)card.Rank);
        }

        private static Card ReadCard(FastBufferReader reader)
        {
            int suit;
            int rank;
            reader.ReadValueSafe(out suit);
            reader.ReadValueSafe(out rank);
            return new Card((Suit)suit, (Rank)rank);
        }

        private string ResolveLobbyDisplayName(ulong clientId, int sortedIndex)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                return "Host";
            }

            return "Player " + (sortedIndex + 1);
        }

        private static List<PlayerSeat> CloneSeats(List<PlayerSeat> seats)
        {
            var result = new List<PlayerSeat>(seats.Count);
            for (var i = 0; i < seats.Count; i++)
            {
                result.Add(seats[i].Clone());
            }

            return result;
        }
    }
}
