using System;
using System.Collections.Generic;
using System.Text;
using DurakGame.Core;
using DurakGame.Gameplay;
using DurakGame.Network;
using Unity.Netcode;
using UnityEngine;

namespace DurakGame.UI
{
    public class DurakAppController : MonoBehaviour
    {
        public enum AppScreen
        {
            Menu = 0,
            Lobby = 1,
            Match = 2,
        }

        private const int MaxOnlinePlayers = 4;
        [SerializeField] private bool enableLegacyOnGui;

        private IGameRulesEngine _engine;
        private IBotStrategy _botStrategy;
        private UnityNetworkSessionService _sessionService;
        private DurakNetcodeBridge _netcodeBridge;

        private AppScreen _screen = AppScreen.Menu;
        private bool _isOnline;
        private bool _isHost;
        private int _localPlayerId;
        private string _joinCodeInput = string.Empty;
        private string _status = "Ready.";
        private Vector2 _scroll;
        private float _nextBotActionAt;
        private int _lastAuthoritativeSequence;
        private bool _awaitingResync;
        private bool _localLobbyReady;
        private LobbyStateSnapshot _lobbyState = new LobbyStateSnapshot();
        private float _nextMatchResumeRequestAt;

        private GUIStyle _titleStyle;
        private GUIStyle _boxStyle;

        public AppScreen CurrentScreen => _screen;
        public bool IsOnline => _isOnline;
        public bool IsHost => _isHost;
        public int LocalPlayerId => _localPlayerId;
        public string Status => _status;
        public string JoinCode => _sessionService != null ? _sessionService.CurrentJoinCode : string.Empty;
        public int ConnectedPlayers => _netcodeBridge != null ? _netcodeBridge.ConnectedPlayerCount : 0;
        public GameState State => _engine != null ? _engine.State : null;
        public bool LocalLobbyReady => _localLobbyReady;
        public bool CanStartOnlineMatch => _isHost && _netcodeBridge != null && _netcodeBridge.CanStartMatch(MaxOnlinePlayers);

        private void Awake()
        {
            _engine = new DurakGameRulesEngine();
            _botStrategy = new SimpleBotStrategy();

            _sessionService = GetComponent<UnityNetworkSessionService>();
            if (_sessionService == null)
            {
                _sessionService = gameObject.AddComponent<UnityNetworkSessionService>();
            }

            _netcodeBridge = GetComponent<DurakNetcodeBridge>();
            if (_netcodeBridge == null)
            {
                _netcodeBridge = gameObject.AddComponent<DurakNetcodeBridge>();
            }

            _netcodeBridge.Initialize(_sessionService.NetworkManager);
            _netcodeBridge.ServerIntentHandler = HandleServerIntent;
            _netcodeBridge.ServerSnapshotProvider = BuildServerSnapshot;
            _netcodeBridge.MatchStarted += OnMatchStarted;
            _netcodeBridge.AuthoritativeIntentReceived += OnAuthoritativeIntentReceived;
            _netcodeBridge.StateSnapshotReceived += OnStateSnapshotReceived;
            _netcodeBridge.LobbyStateChanged += OnLobbyStateChanged;
            _netcodeBridge.ConnectedPlayerCountChanged += OnConnectedPlayerCountChanged;
            _netcodeBridge.SessionTerminated += OnSessionTerminated;
        }

        private void OnDestroy()
        {
            if (_netcodeBridge == null)
            {
                return;
            }

            _netcodeBridge.MatchStarted -= OnMatchStarted;
            _netcodeBridge.AuthoritativeIntentReceived -= OnAuthoritativeIntentReceived;
            _netcodeBridge.StateSnapshotReceived -= OnStateSnapshotReceived;
            _netcodeBridge.LobbyStateChanged -= OnLobbyStateChanged;
            _netcodeBridge.ConnectedPlayerCountChanged -= OnConnectedPlayerCountChanged;
            _netcodeBridge.SessionTerminated -= OnSessionTerminated;
            _netcodeBridge.ServerIntentHandler = null;
            _netcodeBridge.ServerSnapshotProvider = null;
        }

        private void Update()
        {
            if (_isOnline && !_isHost && _screen == AppScreen.Lobby && _netcodeBridge != null &&
                Time.unscaledTime >= _nextMatchResumeRequestAt)
            {
                _netcodeBridge.RequestMatchResumeFromServer();
                _netcodeBridge.RequestStateSnapshotFromServer();
                _nextMatchResumeRequestAt = Time.unscaledTime + 1.0f;
            }

            if (_screen != AppScreen.Match || _engine.State.Phase != GamePhase.InRound)
            {
                return;
            }

            if (_isOnline && !_isHost)
            {
                return;
            }

            if (Time.unscaledTime < _nextBotActionAt)
            {
                return;
            }

            var currentPlayerId = _engine.State.CurrentTurnPlayerId;
            if (currentPlayerId < 0)
            {
                return;
            }

            var currentPlayer = _engine.State.GetPlayer(currentPlayerId);
            if (currentPlayer == null || !currentPlayer.IsBot)
            {
                return;
            }

            var legalIntents = _engine.GetLegalIntents(currentPlayerId);
            var botIntent = _botStrategy.ChooseIntent(_engine.State, legalIntents);
            if (botIntent == null)
            {
                return;
            }

            SubmitIntent(botIntent);
            _nextBotActionAt = Time.unscaledTime + 0.4f;
        }

        public void RequestStartOfflineMatch(int totalPlayers)
        {
            StartOfflineMatch(totalPlayers);
        }

        public void RequestHostSession()
        {
            StartHostSession();
        }

        public void RequestJoinSession(string joinCode)
        {
            _joinCodeInput = (joinCode ?? string.Empty).Trim().ToUpperInvariant();
            JoinSession();
        }

        public void RequestStartOnlineMatch()
        {
            StartOnlineMatch();
        }

        public void RequestToggleLobbyReady()
        {
            if (!_isOnline || _screen != AppScreen.Lobby || _netcodeBridge == null)
            {
                return;
            }

            var nextReady = !_localLobbyReady;
            if (!_netcodeBridge.SetLocalReady(nextReady))
            {
                _status = "Could not update ready state.";
                return;
            }

            // Optimistic UI update until next authoritative lobby snapshot arrives.
            _localLobbyReady = nextReady;

            if (!_isHost)
            {
                _netcodeBridge.RequestMatchResumeFromServer();
                _netcodeBridge.RequestStateSnapshotFromServer();
                _nextMatchResumeRequestAt = Time.unscaledTime + 1.0f;
            }
        }

        public string GetLobbyInfoText()
        {
            var builder = new StringBuilder();
            builder.Append("Role: ").Append(_isHost ? "Host" : "Client").AppendLine();
            if (_isHost)
            {
                builder.Append("Join code: ")
                    .Append(!string.IsNullOrEmpty(JoinCode) ? JoinCode : "-")
                    .AppendLine();
            }

            var players = _lobbyState != null && _lobbyState.Players != null ? _lobbyState.Players : new List<LobbyPlayerInfo>();
            var connected = players.Count > 0 ? players.Count : ConnectedPlayers;
            var readyCount = 0;
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i].IsReady)
                {
                    readyCount += 1;
                }
            }

            builder.Append("Connected players: ").Append(connected).AppendLine();
            builder.Append("Ready: ").Append(readyCount).Append('/').Append(connected).AppendLine();
            builder.AppendLine("Slots:");

            if (players.Count == 0)
            {
                builder.Append(" - Waiting for lobby state...");
                return builder.ToString();
            }

            ulong localClientId = 0;
            var localIdentity = string.Empty;
            if (_sessionService != null && _sessionService.NetworkManager != null)
            {
                localClientId = GetReliableLocalClientIdOrUnknown();
            }

            if (_netcodeBridge != null)
            {
                localIdentity = _netcodeBridge.LocalPlayerIdentity;
            }

            LobbyPlayerInfo localLobbyPlayer = null;
            if (_lobbyState != null)
            {
                LobbyIdentityResolver.TryResolveLocalLobbyPlayer(_lobbyState, localClientId, localIdentity, out localLobbyPlayer);
            }

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                var displayName = string.IsNullOrWhiteSpace(player.DisplayName) ? "Player " + (i + 1) : player.DisplayName;
                builder.Append(" - ").Append(displayName);
                if (player.IsHost)
                {
                    builder.Append(" [HOST]");
                }

                if (ReferenceEquals(player, localLobbyPlayer))
                {
                    builder.Append(" [YOU]");
                }

                builder.Append(player.IsReady ? " [READY]" : " [NOT READY]").AppendLine();
            }

            if (_isHost)
            {
                var blockReason = _netcodeBridge.GetStartMatchBlockReason(MaxOnlinePlayers);
                if (string.IsNullOrEmpty(blockReason))
                {
                    builder.Append("Host can start the match.");
                }
                else
                {
                    builder.Append("Start blocked: ").Append(blockReason);
                }
            }
            else
            {
                builder.Append("Wait for host to start when all players are READY.");
            }

            return builder.ToString();
        }

        public void RequestReturnToMenu()
        {
            ReturnToMenu();
        }

        public bool IsLocalHumanTurn()
        {
            if (_engine == null || _engine.State == null || _engine.State.Phase != GamePhase.InRound)
            {
                return false;
            }

            if (_engine.State.CurrentTurnPlayerId != _localPlayerId)
            {
                return false;
            }

            var local = _engine.State.GetPlayer(_localPlayerId);
            return local != null && !local.IsBot;
        }

        public IReadOnlyList<PlayerIntent> GetLocalLegalIntents()
        {
            if (!IsLocalHumanTurn())
            {
                return Array.Empty<PlayerIntent>();
            }

            return _engine.GetLegalIntents(_localPlayerId);
        }

        public void RequestSubmitIntent(PlayerIntent intent)
        {
            if (intent == null)
            {
                return;
            }

            if (_engine == null || _engine.State == null || _engine.State.Phase != GamePhase.InRound)
            {
                _status = "No active round.";
                return;
            }

            if (_engine.State.CurrentTurnPlayerId != _localPlayerId)
            {
                _status = "Not your turn.";
                return;
            }

            if (intent.PlayerId != _localPlayerId)
            {
                _status = "Invalid action owner.";
                return;
            }

            var legal = _engine.GetLegalIntents(_localPlayerId);
            if (!ContainsEquivalentIntent(legal, intent))
            {
                _status = "Invalid action for current turn.";
                return;
            }

            SubmitIntent(intent);
        }

        public string GetIntentLabel(PlayerIntent intent)
        {
            return DescribeIntent(intent);
        }

        public string GetPlayerLabel(int playerId)
        {
            return FormatPlayer(playerId);
        }

        private void OnGUI()
        {
            if (!enableLegacyOnGui)
            {
                return;
            }

            EnsureStyles();

            GUILayout.BeginArea(new Rect(20f, 20f, Mathf.Min(1200f, Screen.width - 40f), Screen.height - 40f));
            GUILayout.Label("Durak Prototype", _titleStyle);
            GUILayout.Label("Status: " + _status);
            GUILayout.Space(8f);

            switch (_screen)
            {
                case AppScreen.Menu:
                    DrawMenu();
                    break;
                case AppScreen.Lobby:
                    DrawLobby();
                    break;
                case AppScreen.Match:
                    DrawMatch();
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawMenu()
        {
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label("Offline");
            if (GUILayout.Button("Start Offline (1 Human + 1 Bot)", GUILayout.Height(32f)))
            {
                StartOfflineMatch(2);
            }

            if (GUILayout.Button("Start Offline (1 Human + 3 Bots)", GUILayout.Height(32f)))
            {
                StartOfflineMatch(4);
            }

            GUILayout.Space(12f);
            GUILayout.Label("Online (Relay + NGO)");
            if (GUILayout.Button("Host Session", GUILayout.Height(32f)))
            {
                StartHostSession();
            }

            GUILayout.BeginHorizontal();
            _joinCodeInput = GUILayout.TextField(_joinCodeInput, GUILayout.Width(220f));
            if (GUILayout.Button("Join Session", GUILayout.Height(28f), GUILayout.Width(140f)))
            {
                JoinSession();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawLobby()
        {
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label(_isHost ? "Lobby (Host)" : "Lobby (Client)");

            if (_isHost)
            {
                GUILayout.Label("Join code: " + (_sessionService.CurrentJoinCode.Length > 0 ? _sessionService.CurrentJoinCode : "-"));
                GUILayout.Label("Connected players: " + _netcodeBridge.ConnectedPlayerCount);
                GUILayout.Label("Start enabled for 2-4 connected players.");

                if (GUILayout.Button("Start Online Match", GUILayout.Height(32f)))
                {
                    StartOnlineMatch();
                }
            }
            else
            {
                GUILayout.Label("Waiting for host to start the match.");
                GUILayout.Label("Connected players: " + _netcodeBridge.ConnectedPlayerCount);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Leave Lobby", GUILayout.Height(30f)))
            {
                ReturnToMenu();
            }

            GUILayout.EndVertical();
        }

        private void DrawMatch()
        {
            var state = _engine.State;
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label("Phase: " + state.Phase);
            GUILayout.Label("Trump: " + SuitToString(state.TrumpSuit));
            GUILayout.Label("Deck: " + state.DeckCount);
            GUILayout.Label("Round: " + state.Round.RoundNumber);
            GUILayout.Label("Attacker: " + FormatPlayer(state.Round.AttackerId));
            GUILayout.Label("Defender: " + FormatPlayer(state.Round.DefenderId));
            GUILayout.Label("Current turn: " + FormatPlayer(state.CurrentTurnPlayerId));
            GUILayout.EndVertical();

            GUILayout.Space(8f);
            DrawTable(state);
            GUILayout.Space(8f);
            DrawPlayers(state);
            GUILayout.Space(8f);

            if (state.Phase == GamePhase.Completed)
            {
                DrawMatchResult(state.MatchResult);
                if (GUILayout.Button("Back to Menu", GUILayout.Height(32f)))
                {
                    ReturnToMenu();
                }

                return;
            }

            if (state.CurrentTurnPlayerId == _localPlayerId)
            {
                var localPlayer = state.GetPlayer(_localPlayerId);
                if (localPlayer != null && !localPlayer.IsBot)
                {
                    DrawLocalActions();
                }
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Abort Match / Return to Menu", GUILayout.Height(30f)))
            {
                ReturnToMenu();
            }
        }

        private void DrawTable(GameState state)
        {
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label("Table");
            if (state.Round.Table.Count == 0)
            {
                GUILayout.Label("(empty)");
            }
            else
            {
                for (var i = 0; i < state.Round.Table.Count; i++)
                {
                    var pair = state.Round.Table[i];
                    var line = "#" + i + "  A: " + CardToShortString(pair.AttackCard);
                    line += pair.IsDefended ? "  |  D: " + CardToShortString(pair.DefenseCard) : "  |  D: -";
                    GUILayout.Label(line);
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawPlayers(GameState state)
        {
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label("Players");

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(220f));
            for (var i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[i];
                var line = FormatPlayer(player.PlayerId) + " | Hand: " + player.Hand.Count;
                if (player.IsBot)
                {
                    line += " | BOT";
                }

                if (player.PlayerId == _localPlayerId)
                {
                    line += " | LOCAL";
                }

                GUILayout.Label(line);

                if (player.PlayerId == _localPlayerId)
                {
                    GUILayout.Label("Cards: " + HandToShortString(player.Hand));
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawLocalActions()
        {
            var legal = _engine.GetLegalIntents(_localPlayerId);
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label("Available Actions");

            if (legal.Count == 0)
            {
                GUILayout.Label("(no legal action)");
            }
            else
            {
                for (var i = 0; i < legal.Count; i++)
                {
                    var intent = legal[i];
                    if (GUILayout.Button(DescribeIntent(intent), GUILayout.Height(28f)))
                    {
                        SubmitIntent(intent);
                    }
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawMatchResult(MatchResult result)
        {
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label("Match Result");
            if (result.DurakPlayerId >= 0)
            {
                GUILayout.Label("Durak (loser): " + FormatPlayer(result.DurakPlayerId));
            }
            else
            {
                GUILayout.Label("No single loser.");
            }

            GUILayout.Label("Winners: " + string.Join(", ", result.Winners.ConvertAll(FormatPlayer)));
            GUILayout.EndVertical();
        }

        private async void StartHostSession()
        {
            _status = "Creating relay host session...";
            try
            {
                var info = await _sessionService.CreateSessionAsync(MaxOnlinePlayers);
                _netcodeBridge.Initialize(_sessionService.NetworkManager);
                _netcodeBridge.ServerIntentHandler = HandleServerIntent;
                _netcodeBridge.ServerSnapshotProvider = BuildServerSnapshot;

                _isOnline = true;
                _isHost = true;
                _screen = AppScreen.Lobby;
                _localLobbyReady = false;
                _lobbyState = new LobbyStateSnapshot();
                _netcodeBridge.SetLocalReady(false);
                _nextMatchResumeRequestAt = 0f;
                if (!info.IsConnected)
                {
                    _status = "Host failed to connect.";
                }
                else if (!string.IsNullOrEmpty(info.JoinCode) &&
                         info.JoinCode.StartsWith("DIRECT:", StringComparison.OrdinalIgnoreCase))
                {
                    _status = "Host started in Direct mode (no Relay). Share join target from lobby.";
                }
                else
                {
                    _status = "Host started via Relay. Share join code and wait for clients.";
                }
            }
            catch (Exception exception)
            {
                _status = "Host error: " + exception.Message;
                _screen = AppScreen.Menu;
                _isOnline = false;
                _isHost = false;
                _localLobbyReady = false;
                _lobbyState = new LobbyStateSnapshot();
                _nextMatchResumeRequestAt = 0f;
            }
        }

        private async void JoinSession()
        {
            _status = "Joining session...";
            try
            {
                var info = await _sessionService.JoinSessionAsync(_joinCodeInput);
                _netcodeBridge.Initialize(_sessionService.NetworkManager);

                _isOnline = true;
                _isHost = false;
                _screen = AppScreen.Lobby;
                _localLobbyReady = false;
                _lobbyState = new LobbyStateSnapshot();
                _netcodeBridge.SetLocalReady(false);
                ScheduleMatchResumeRequests();
                _netcodeBridge.RequestMatchResumeFromServer();
                if (!info.IsConnected)
                {
                    _status = "Client failed to connect.";
                }
                else if (!string.IsNullOrEmpty(info.JoinCode) &&
                         info.JoinCode.StartsWith("DIRECT:", StringComparison.OrdinalIgnoreCase))
                {
                    _status = "Connected in Direct mode. Waiting for host to start.";
                }
                else
                {
                    _status = "Connected via Relay. Waiting for host to start.";
                }
            }
            catch (Exception exception)
            {
                _status = "Join error: " + exception.Message;
                _screen = AppScreen.Menu;
                _isOnline = false;
                _isHost = false;
                _localLobbyReady = false;
                _lobbyState = new LobbyStateSnapshot();
                _nextMatchResumeRequestAt = 0f;
            }
        }

        private void StartOnlineMatch()
        {
            if (!_isHost)
            {
                return;
            }

            var startBlockReason = _netcodeBridge.GetStartMatchBlockReason(MaxOnlinePlayers);
            if (!string.IsNullOrEmpty(startBlockReason))
            {
                _status = startBlockReason;
                return;
            }

            var seed = unchecked((int)DateTime.UtcNow.Ticks);
            NetworkMatchStartData hostData;
            if (!_netcodeBridge.TryStartMatchForConnectedPlayers(seed, MaxOnlinePlayers, out hostData))
            {
                var reason = _netcodeBridge.GetStartMatchBlockReason(MaxOnlinePlayers);
                _status = string.IsNullOrEmpty(reason) ? "Match could not be started." : reason;
                return;
            }

            _status = "Match started.";
        }

        private void StartOfflineMatch(int totalPlayers)
        {
            var seats = new List<PlayerSeat>();
            seats.Add(new PlayerSeat
            {
                PlayerId = 0,
                DisplayName = "You",
                IsBot = false,
                OwnerClientId = 0,
            });

            for (var i = 1; i < totalPlayers; i++)
            {
                seats.Add(new PlayerSeat
                {
                    PlayerId = i,
                    DisplayName = "Bot " + i,
                    IsBot = true,
                    OwnerClientId = 0,
                });
            }

            var seed = unchecked((int)DateTime.UtcNow.Ticks);
            _engine.InitializeMatch(seats, seed);

            _isOnline = false;
            _isHost = true;
            _localPlayerId = 0;
            _screen = AppScreen.Match;
            _status = "Offline match started.";
            _nextBotActionAt = Time.unscaledTime + 0.25f;
            _lastAuthoritativeSequence = 0;
            _awaitingResync = false;
            _localLobbyReady = false;
            _lobbyState = new LobbyStateSnapshot();
            _nextMatchResumeRequestAt = 0f;
        }

        private void ReturnToMenu()
        {
            if (_isOnline)
            {
                _sessionService.LeaveSession();
            }

            _netcodeBridge.ResetRuntimeState();
            _netcodeBridge.ServerIntentHandler = HandleServerIntent;
            _netcodeBridge.ServerSnapshotProvider = BuildServerSnapshot;

            _engine = new DurakGameRulesEngine();
            _screen = AppScreen.Menu;
            _isOnline = false;
            _isHost = false;
            _localPlayerId = 0;
            _status = "Ready.";
            _lastAuthoritativeSequence = 0;
            _awaitingResync = false;
            _localLobbyReady = false;
            _lobbyState = new LobbyStateSnapshot();
            _nextMatchResumeRequestAt = 0f;
        }

        private bool HandleServerIntent(PlayerIntent intent)
        {
            var result = _engine.ApplyIntent(intent);
            if (!result.Accepted)
            {
                _status = "Rejected intent: " + result.Error;
                return false;
            }

            if (_engine.State.Phase == GamePhase.Completed)
            {
                _status = "Match finished.";
            }

            return true;
        }

        private void OnMatchStarted(NetworkMatchStartData startData)
        {
            _engine.InitializeMatch(startData.Seats, startData.Seed);
            _localPlayerId = startData.LocalPlayerId;
            _screen = AppScreen.Match;
            _status = "Online match started. You are " + FormatPlayer(_localPlayerId) + ".";
            _nextBotActionAt = Time.unscaledTime + 0.25f;
            _lastAuthoritativeSequence = 0;
            _awaitingResync = false;
            _localLobbyReady = false;
            _nextMatchResumeRequestAt = 0f;
        }

        private void OnAuthoritativeIntentReceived(PlayerIntent intent, int sequence)
        {
            if (_isHost)
            {
                return;
            }

            if (sequence <= _lastAuthoritativeSequence)
            {
                return;
            }

            if (_awaitingResync)
            {
                return;
            }

            if (sequence != _lastAuthoritativeSequence + 1)
            {
                _status = "Desync detected: missing sequence. Requesting resync.";
                _awaitingResync = true;
                _netcodeBridge.RequestStateSnapshotFromServer();
                return;
            }

            var result = _engine.ApplyIntent(intent);
            if (!result.Accepted)
            {
                _status = "Desync detected: intent rejected (" + result.Error + "). Requesting resync.";
                _awaitingResync = true;
                _netcodeBridge.RequestStateSnapshotFromServer();
                return;
            }

            _lastAuthoritativeSequence = sequence;
            if (_engine.State.Phase == GamePhase.Completed)
            {
                _status = "Match finished.";
            }
        }

        private void OnStateSnapshotReceived(StateSnapshot snapshot)
        {
            if (_isHost || snapshot == null || snapshot.State == null)
            {
                return;
            }

            _engine.RestoreSnapshot(snapshot);
            _lastAuthoritativeSequence = snapshot.Sequence;
            _awaitingResync = false;

            var switchedFromLobby = false;
            if (_screen == AppScreen.Lobby && _isOnline)
            {
                switchedFromLobby = TrySwitchLobbyClientToRunningMatch(snapshot.State);
            }

            if (_engine.State.Phase == GamePhase.Completed)
            {
                _status = "Match finished.";
            }
            else if (_screen == AppScreen.Lobby && _engine.State.Phase != GamePhase.Lobby && !switchedFromLobby)
            {
                _status = "Rejoin in progress. Waiting for seat ownership sync.";
            }
            else if (switchedFromLobby)
            {
                _status = "Rejoined running match as " + FormatPlayer(_localPlayerId) + ".";
            }
            else
            {
                _status = "Resynced with host.";
            }
        }

        private void OnLobbyStateChanged(LobbyStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            _lobbyState = snapshot.Clone();
            UpdateLocalReadyFromLobbyState();

            if (_screen != AppScreen.Lobby)
            {
                return;
            }

            if (_isHost)
            {
                _status = CanStartOnlineMatch
                    ? "All players ready. Host can start the match."
                    : "Lobby updated. Waiting for all players to become READY.";
            }
            else
            {
                _status = "Lobby updated. Waiting for host to start.";
                _netcodeBridge.RequestMatchResumeFromServer();
                _netcodeBridge.RequestStateSnapshotFromServer();
            }
        }

        private void OnConnectedPlayerCountChanged(int count)
        {
            if (_screen == AppScreen.Lobby)
            {
                if (_lobbyState != null && _lobbyState.Players != null && _lobbyState.Players.Count > 0)
                {
                    return;
                }

                _status = _isHost
                    ? "Lobby updated. Connected players: " + count
                    : "Waiting in lobby. Connected players: " + count;
                return;
            }

            if (_screen == AppScreen.Match && _isOnline && _isHost && _engine != null && _engine.State != null &&
                _engine.State.Phase == GamePhase.InRound)
            {
                _status = count < 2
                    ? "Player disconnected. Match can continue; waiting for reconnect."
                    : "Player count updated during match: " + count;
            }
        }

        private void OnSessionTerminated(string reason)
        {
            if (!_isOnline || _isHost)
            {
                return;
            }

            _sessionService.LeaveSession();
            _netcodeBridge.ResetRuntimeState();
            _engine = new DurakGameRulesEngine();
            _screen = AppScreen.Menu;
            _isOnline = false;
            _isHost = false;
            _localPlayerId = 0;
            _status = reason;
            _lastAuthoritativeSequence = 0;
            _awaitingResync = false;
            _localLobbyReady = false;
            _lobbyState = new LobbyStateSnapshot();
            _nextMatchResumeRequestAt = 0f;
        }

        private void SubmitIntent(PlayerIntent intent)
        {
            if (_isOnline)
            {
                if (!_netcodeBridge.SubmitLocalIntent(intent))
                {
                    _status = "Could not submit intent over network.";
                }

                return;
            }

            var result = _engine.ApplyIntent(intent);
            if (!result.Accepted)
            {
                _status = "Intent rejected: " + result.Error;
            }
            else if (_engine.State.Phase == GamePhase.Completed)
            {
                _status = "Match finished.";
            }
        }

        private void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                };
            }

            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(10, 10, 10, 10),
                };
            }
        }

        private string DescribeIntent(PlayerIntent intent)
        {
            switch (intent.Type)
            {
                case PlayerIntentType.Attack:
                    return "Attack " + CardToShortString(intent.Card);
                case PlayerIntentType.AddCard:
                    return "Add " + CardToShortString(intent.Card);
                case PlayerIntentType.Defend:
                    return "Defend #" + intent.TargetPairIndex + " with " + CardToShortString(intent.Card);
                case PlayerIntentType.TakeCards:
                    return "Take cards";
                case PlayerIntentType.EndAttack:
                    return "End attack";
                default:
                    return intent.Type.ToString();
            }
        }

        private string FormatPlayer(int playerId)
        {
            if (playerId < 0)
            {
                return "-";
            }

            var player = _engine.State.GetPlayer(playerId);
            if (player == null)
            {
                return "P" + (playerId + 1);
            }

            return player.DisplayName + " (P" + (playerId + 1) + ")";
        }

        private static string HandToShortString(List<Card> hand)
        {
            if (hand == null || hand.Count == 0)
            {
                return "(empty)";
            }

            var parts = new string[hand.Count];
            for (var i = 0; i < hand.Count; i++)
            {
                parts[i] = CardToShortString(hand[i]);
            }

            return string.Join(", ", parts);
        }

        private static string CardToShortString(Card card)
        {
            return RankToShortString(card.Rank) + SuitToShortString(card.Suit);
        }

        private static string RankToShortString(Rank rank)
        {
            switch (rank)
            {
                case Rank.Six:
                    return "6";
                case Rank.Seven:
                    return "7";
                case Rank.Eight:
                    return "8";
                case Rank.Nine:
                    return "9";
                case Rank.Ten:
                    return "10";
                case Rank.Jack:
                    return "J";
                case Rank.Queen:
                    return "Q";
                case Rank.King:
                    return "K";
                case Rank.Ace:
                    return "A";
                default:
                    return ((int)rank).ToString();
            }
        }

        private static string SuitToShortString(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs:
                    return "C";
                case Suit.Diamonds:
                    return "D";
                case Suit.Hearts:
                    return "H";
                case Suit.Spades:
                    return "S";
                default:
                    return "?";
            }
        }

        private static string SuitToString(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs:
                    return "Clubs";
                case Suit.Diamonds:
                    return "Diamonds";
                case Suit.Hearts:
                    return "Hearts";
                case Suit.Spades:
                    return "Spades";
                default:
                    return suit.ToString();
            }
        }

        private static bool ContainsEquivalentIntent(IReadOnlyList<PlayerIntent> legal, PlayerIntent candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            for (var i = 0; i < legal.Count; i++)
            {
                var intent = legal[i];
                if (intent.Type != candidate.Type || intent.PlayerId != candidate.PlayerId)
                {
                    continue;
                }

                if (intent.TargetPairIndex != candidate.TargetPairIndex)
                {
                    continue;
                }

                if (intent.HasCard != candidate.HasCard)
                {
                    continue;
                }

                if (!intent.HasCard || intent.Card.Equals(candidate.Card))
                {
                    return true;
                }
            }

            return false;
        }

        private StateSnapshot BuildServerSnapshot()
        {
            if (_engine == null)
            {
                return new StateSnapshot
                {
                    Sequence = 0,
                    State = new GameState(),
                };
            }

            return _engine.CreateSnapshot();
        }

        private void UpdateLocalReadyFromLobbyState()
        {
            if (_lobbyState == null || _lobbyState.Players == null || _sessionService == null || _sessionService.NetworkManager == null)
            {
                return;
            }

            var localClientId = GetReliableLocalClientIdOrUnknown();
            var localIdentity = _netcodeBridge != null ? _netcodeBridge.LocalPlayerIdentity : string.Empty;
            if (LobbyIdentityResolver.TryGetLocalLobbyReady(_lobbyState, localClientId, localIdentity, out var localReady))
            {
                _localLobbyReady = localReady;
                return;
            }

            _localLobbyReady = false;
        }

        private void ScheduleMatchResumeRequests()
        {
            _nextMatchResumeRequestAt = 0f;
        }

        private bool TrySwitchLobbyClientToRunningMatch(GameState state)
        {
            if (state == null || state.Phase == GamePhase.Lobby || _sessionService == null || _sessionService.NetworkManager == null)
            {
                return false;
            }

            var localClientId = GetReliableLocalClientIdOrUnknown();
            var localIdentity = _netcodeBridge != null ? _netcodeBridge.LocalPlayerIdentity : string.Empty;
            var localPlayerId = LobbyIdentityResolver.ResolveLocalPlayerId(state, localClientId, localIdentity);

            if (localPlayerId < 0)
            {
                return false;
            }

            _localPlayerId = localPlayerId;
            _screen = AppScreen.Match;
            _nextMatchResumeRequestAt = 0f;
            return true;
        }

        private ulong GetReliableLocalClientIdOrUnknown()
        {
            if (_sessionService == null || _sessionService.NetworkManager == null)
            {
                return ulong.MaxValue;
            }

            var networkManager = _sessionService.NetworkManager;
            var localClientId = networkManager.LocalClientId;
            var isPureClient = networkManager.IsClient && !networkManager.IsServer;

            if (isPureClient &&
                (!networkManager.IsConnectedClient || localClientId == NetworkManager.ServerClientId))
            {
                return ulong.MaxValue;
            }

            return localClientId;
        }
    }
}
