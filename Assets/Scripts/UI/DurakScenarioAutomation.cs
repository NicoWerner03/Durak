using System;
using System.Collections.Generic;
using System.IO;
using DurakGame.Core;
using UnityEngine;

namespace DurakGame.UI
{
    [DefaultExecutionOrder(200)]
    public class DurakScenarioAutomation : MonoBehaviour
    {
        private const string EnableArg = "-durak-auto";
        private const string RoleArg = "-durak-role";
        private const string JoinArg = "-durak-join";
        private const string CyclesArg = "-durak-cycles";
        private const string LogArg = "-durak-log";
        private const string DropAfterArg = "-durak-drop-after-seconds";

        private DurakAppController _controller;
        private bool _enabled;
        private bool _initialized;
        private bool _inMatchTracked;
        private bool _dropTriggered;
        private int _completedMatches;
        private float _nextActionAt;
        private float _matchEnteredAt;
        private float _quitAt = -1f;
        private string _lastLoggedJoinCode = string.Empty;
        private string _role = "client";
        private string _joinCode = "DIRECT:127.0.0.1:7777";
        private string _logPath = string.Empty;
        private int _cycles = 1;
        private float _dropAfterSeconds = -1f;

        private void Awake()
        {
            ParseArgs(Environment.GetCommandLineArgs());
            _enabled = _enabled && Application.isBatchMode;
            if (!_enabled)
            {
                return;
            }

            _controller = GetComponent<DurakAppController>();
            Log("Automation enabled. Role=" + _role + ", Cycles=" + _cycles + ", Join=" + _joinCode);
        }

        private void Update()
        {
            if (!_enabled || _controller == null)
            {
                return;
            }

            if (_quitAt > 0f)
            {
                if (Time.unscaledTime >= _quitAt)
                {
                    Log("Exiting with success after grace period.");
                    Application.Quit(0);
                }

                return;
            }

            if (Time.unscaledTime < _nextActionAt)
            {
                return;
            }

            if (!_initialized)
            {
                _initialized = true;
                _nextActionAt = Time.unscaledTime + 0.5f;
                return;
            }

            var screen = _controller.CurrentScreen;
            switch (screen)
            {
                case DurakAppController.AppScreen.Menu:
                    HandleMenu();
                    break;
                case DurakAppController.AppScreen.Lobby:
                    HandleLobby();
                    break;
                case DurakAppController.AppScreen.Match:
                    HandleMatch();
                    break;
            }
        }

        private void HandleMenu()
        {
            if (_inMatchTracked)
            {
                _inMatchTracked = false;
                _completedMatches += 1;
                Log("Match ended after disconnect/menu transition. Count=" + _completedMatches);
                if (_completedMatches >= _cycles)
                {
                    Log("Automation completed successfully. Waiting for final network flush.");
                    _quitAt = Time.unscaledTime + 1.0f;
                    _nextActionAt = Time.unscaledTime + 0.1f;
                    return;
                }
            }

            if (string.Equals(_role, "host", StringComparison.OrdinalIgnoreCase))
            {
                _controller.RequestHostSession();
                Log("Requested host session.");
            }
            else
            {
                _controller.RequestJoinSession(_joinCode);
                Log("Requested join session with code " + _joinCode + ".");
            }

            _nextActionAt = Time.unscaledTime + 1.2f;
        }

        private void HandleLobby()
        {
            if (string.Equals(_role, "host", StringComparison.OrdinalIgnoreCase))
            {
                var joinCode = _controller.JoinCode;
                if (!string.IsNullOrWhiteSpace(joinCode) &&
                    !string.Equals(joinCode, _lastLoggedJoinCode, StringComparison.Ordinal))
                {
                    _lastLoggedJoinCode = joinCode;
                    Log("JOIN_CODE:" + joinCode);
                }
            }

            if (!_controller.LocalLobbyReady)
            {
                _controller.RequestToggleLobbyReady();
                Log("Ensured READY in lobby.");
                _nextActionAt = Time.unscaledTime + 0.4f;
                return;
            }

            if (!string.Equals(_role, "host", StringComparison.OrdinalIgnoreCase))
            {
                _nextActionAt = Time.unscaledTime + 0.7f;
                return;
            }

            if (_controller.CanStartOnlineMatch)
            {
                _controller.RequestStartOnlineMatch();
                Log("Requested online match start.");
                _nextActionAt = Time.unscaledTime + 0.5f;
                return;
            }

            _nextActionAt = Time.unscaledTime + 0.4f;
        }

        private void HandleMatch()
        {
            var state = _controller.State;
            if (state == null)
            {
                _nextActionAt = Time.unscaledTime + 0.2f;
                return;
            }

            if (!_inMatchTracked)
            {
                _inMatchTracked = true;
                _matchEnteredAt = Time.unscaledTime;
                Log("Entered match. LocalPlayer=" + _controller.LocalPlayerId);
            }

            if (!_dropTriggered && _dropAfterSeconds > 0f &&
                Time.unscaledTime - _matchEnteredAt >= _dropAfterSeconds)
            {
                _dropTriggered = true;
                Log("Triggering scripted client drop.");
                Application.Quit(42);
                return;
            }

            if (state.Phase == GamePhase.Completed)
            {
                _completedMatches += 1;
                Log("Match completed. Count=" + _completedMatches);
                _inMatchTracked = false;

                if (_completedMatches >= _cycles)
                {
                    Log("Automation completed successfully. Waiting for final network flush.");
                    _quitAt = Time.unscaledTime + 1.0f;
                    _nextActionAt = Time.unscaledTime + 0.1f;
                    return;
                }

                _controller.RequestReturnToMenu();
                Log("Returning to menu for next cycle.");
                _nextActionAt = Time.unscaledTime + 1.0f;
                return;
            }

            if (_controller.IsLocalHumanTurn())
            {
                var legal = _controller.GetLocalLegalIntents();
                if (legal.Count > 0)
                {
                    _controller.RequestSubmitIntent(legal[0]);
                }
            }

            _nextActionAt = Time.unscaledTime + 0.05f;
        }

        private void ParseArgs(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var token = args[i];
                if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = "1";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    i += 1;
                }

                values[token] = value;
            }

            _enabled = values.ContainsKey(EnableArg);
            if (!_enabled)
            {
                return;
            }

            if (values.TryGetValue(RoleArg, out var role) && !string.IsNullOrWhiteSpace(role))
            {
                _role = role.Trim();
            }

            if (values.TryGetValue(JoinArg, out var join) && !string.IsNullOrWhiteSpace(join))
            {
                _joinCode = join.Trim();
            }

            if (values.TryGetValue(CyclesArg, out var cyclesRaw) &&
                int.TryParse(cyclesRaw, out var cycles) &&
                cycles > 0)
            {
                _cycles = cycles;
            }

            if (values.TryGetValue(DropAfterArg, out var dropRaw) &&
                float.TryParse(dropRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dropAfter))
            {
                _dropAfterSeconds = dropAfter;
            }

            if (values.TryGetValue(LogArg, out var logPath) && !string.IsNullOrWhiteSpace(logPath))
            {
                _logPath = logPath.Trim();
            }
        }

        private void Log(string message)
        {
            var line = "[" + DateTime.UtcNow.ToString("O") + "] " + message;
            Debug.Log("[DurakScenarioAutomation] " + message);
            if (string.IsNullOrWhiteSpace(_logPath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // Keep runtime automation alive even if file logging fails.
            }
        }
    }
}
