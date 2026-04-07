using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using DurakGame.Build;
using DurakGame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DurakGame.UI
{
    public class DurakCanvasView : MonoBehaviour
    {
        private DurakAppController _controller;
        private Font _font;

        private GameObject _menuPanel;
        private GameObject _lobbyPanel;
        private GameObject _matchPanel;

        private Text _statusText;
        private Text _versionText;
        private Text _lobbyInfoText;
        private Text _summaryText;
        private Text _tableText;
        private Text _playersText;
        private Text _resultText;
        private Text _actionsTitleText;

        private InputField _joinCodeInput;
        private Button _startOnlineMatchButton;
        private Button _toggleReadyButton;
        private Button _leaveLobbyButton;
        private Button _backToMenuButton;
        private GameObject _actionsContainer;
        private GameObject _actionsScrollRoot;
        private ScrollRect _actionsScrollRect;
        private string _actionsSignature = string.Empty;

        private float _nextUiRefreshAt;

        private void Awake()
        {
            _controller = GetComponent<DurakAppController>();
            if (_controller == null)
            {
                enabled = false;
                return;
            }

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildCanvas();
            RefreshStaticBindings();
            RefreshDynamicContent();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextUiRefreshAt)
            {
                return;
            }

            _nextUiRefreshAt = Time.unscaledTime + 0.1f;
            RefreshDynamicContent();
        }

        private void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            EnsureCompatibleInputModule(eventSystem.gameObject);
        }

        private static void EnsureCompatibleInputModule(GameObject eventSystemObject)
        {
            var inputSystemType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemType != null)
            {
                var standalone = eventSystemObject.GetComponent<StandaloneInputModule>();
                if (standalone != null)
                {
                    Destroy(standalone);
                }

                var inputSystemModule = eventSystemObject.GetComponent(inputSystemType);
                if (inputSystemModule == null)
                {
                    inputSystemModule = eventSystemObject.AddComponent(inputSystemType);
                }

                // Ensure keyboard actions are bound when module is created at runtime.
                var assignDefaultActions = inputSystemType.GetMethod("AssignDefaultActions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (assignDefaultActions != null)
                {
                    try
                    {
                        assignDefaultActions.Invoke(inputSystemModule, null);
                    }
                    catch
                    {
                        // Keep running with existing bindings if default assignment is unavailable.
                    }
                }

                return;
            }

            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("DurakCanvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<GraphicRaycaster>();

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = CreatePanel("Root", canvasObject.transform, new Color(0.08f, 0.11f, 0.13f, 0.92f));
            StretchRect(root.GetComponent<RectTransform>());

            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(24, 24, 24, 24);
            rootLayout.spacing = 12f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var header = CreateText("Header", root.transform, "Durak Prototype", 32, FontStyle.Bold);
            header.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(header.gameObject, 42f);

            _statusText = CreateText("Status", root.transform, "Status: -", 20, FontStyle.Normal);
            _statusText.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(_statusText.gameObject, 34f);

            _menuPanel = CreatePanel("MenuPanel", root.transform, new Color(0.16f, 0.20f, 0.24f, 0.95f));
            SetPreferredHeight(_menuPanel, 340f);
            SetMinHeight(_menuPanel, 320f);
            BuildMenuPanel(_menuPanel.transform);

            _lobbyPanel = CreatePanel("LobbyPanel", root.transform, new Color(0.16f, 0.20f, 0.24f, 0.95f));
            SetMinHeight(_lobbyPanel, 320f);
            SetFlexibleHeight(_lobbyPanel, 1f);
            BuildLobbyPanel(_lobbyPanel.transform);

            _matchPanel = CreatePanel("MatchPanel", root.transform, new Color(0.16f, 0.20f, 0.24f, 0.95f));
            SetFlexibleHeight(_matchPanel, 1f);
            BuildMatchPanel(_matchPanel.transform);

            _versionText = CreateText("VersionText", canvasObject.transform, BuildVersionLabel(), 14, FontStyle.Normal);
            _versionText.alignment = TextAnchor.LowerLeft;
            _versionText.color = new Color(1f, 1f, 1f, 0.70f);
            var versionRect = _versionText.rectTransform;
            versionRect.anchorMin = new Vector2(0f, 0f);
            versionRect.anchorMax = new Vector2(0f, 0f);
            versionRect.pivot = new Vector2(0f, 0f);
            versionRect.anchoredPosition = new Vector2(14f, 8f);
            versionRect.sizeDelta = new Vector2(420f, 22f);
        }

        private void BuildMenuPanel(Transform parent)
        {
            ConfigureVerticalContainer(parent, 10f, 14, 14);

            var title = CreateText("MenuTitle", parent, "Menu", 24, FontStyle.Bold);
            title.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(title.gameObject, 30f);

            CreateButton(parent, "Start Offline (1 Human + 1 Bot)", () => _controller.RequestStartOfflineMatch(2));
            CreateButton(parent, "Start Offline (1 Human + 3 Bots)", () => _controller.RequestStartOfflineMatch(4));
            CreateButton(parent, "Host Session", () => _controller.RequestHostSession());

            var joinRow = new GameObject("JoinRow");
            joinRow.transform.SetParent(parent, false);
            var joinRowLayout = joinRow.AddComponent<HorizontalLayoutGroup>();
            joinRowLayout.spacing = 8f;
            joinRowLayout.childControlWidth = true;
            joinRowLayout.childControlHeight = true;
            joinRowLayout.childForceExpandWidth = false;
            joinRowLayout.childForceExpandHeight = false;
            SetPreferredHeight(joinRow, 44f);

            _joinCodeInput = CreateInputField(joinRow.transform, "JoinCodeInput");
            _joinCodeInput.text = string.Empty;
            SetPreferredWidth(_joinCodeInput.gameObject, 320f);

            var joinButton = CreateButton(joinRow.transform, "Join Session", () =>
            {
                _controller.RequestJoinSession(_joinCodeInput.text);
            });
            SetPreferredWidth(joinButton.gameObject, 220f);

            var quickJoinButton = CreateButton(parent, "Join Localhost (DIRECT)", () =>
            {
                _controller.RequestJoinSession("DIRECT:127.0.0.1:7777");
            });
            SetPreferredHeight(quickJoinButton.gameObject, 36f);
        }

        private void BuildLobbyPanel(Transform parent)
        {
            ConfigureVerticalContainer(parent, 8f, 16, 16);

            var title = CreateText("LobbyTitle", parent, "Lobby", 24, FontStyle.Bold);
            title.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(title.gameObject, 30f);
            SetMinHeight(title.gameObject, 30f);

            var infoPanel = CreatePanel("LobbyInfoPanel", parent, new Color(0.10f, 0.15f, 0.20f, 0.55f));
            ConfigureVerticalContainer(infoPanel.transform, 6f, 12, 12);
            SetPreferredHeight(infoPanel, 130f);
            SetMinHeight(infoPanel, 110f);

            _lobbyInfoText = CreateText("LobbyInfo", infoPanel.transform, "-", 18, FontStyle.Normal);
            _lobbyInfoText.alignment = TextAnchor.UpperLeft;
            _lobbyInfoText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lobbyInfoText.verticalOverflow = VerticalWrapMode.Truncate;
            SetFlexibleHeight(_lobbyInfoText.gameObject, 1f);

            _startOnlineMatchButton = CreateButton(parent, "Start Online Match", () => _controller.RequestStartOnlineMatch());
            _toggleReadyButton = CreateButton(parent, "Set READY", () => _controller.RequestToggleLobbyReady());
            _leaveLobbyButton = CreateButton(parent, "Leave Lobby", () => _controller.RequestReturnToMenu());
            SetMinHeight(_startOnlineMatchButton.gameObject, 38f);
            SetMinHeight(_toggleReadyButton.gameObject, 38f);
            SetMinHeight(_leaveLobbyButton.gameObject, 38f);
        }

        private void BuildMatchPanel(Transform parent)
        {
            ConfigureVerticalContainer(parent, 10f, 14, 14);

            var title = CreateText("MatchTitle", parent, "Match", 24, FontStyle.Bold);
            title.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(title.gameObject, 30f);
            SetMinHeight(title.gameObject, 30f);

            var infoRow = new GameObject("InfoRow");
            infoRow.transform.SetParent(parent, false);
            ConfigureHorizontalContainer(infoRow.transform, 10f, 0, 0);
            SetMinHeight(infoRow, 180f);
            SetFlexibleHeight(infoRow, 2f);

            var leftPanel = CreatePanel("InfoLeftPanel", infoRow.transform, new Color(0.10f, 0.15f, 0.20f, 0.55f));
            ConfigureVerticalContainer(leftPanel.transform, 8f, 10, 10);
            SetFlexibleWidth(leftPanel, 2f);

            var rightPanel = CreatePanel("InfoRightPanel", infoRow.transform, new Color(0.10f, 0.15f, 0.20f, 0.55f));
            ConfigureVerticalContainer(rightPanel.transform, 8f, 10, 10);
            SetFlexibleWidth(rightPanel, 1f);

            _summaryText = CreateText("Summary", leftPanel.transform, "-", 18, FontStyle.Normal);
            _summaryText.alignment = TextAnchor.UpperLeft;
            _summaryText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _summaryText.verticalOverflow = VerticalWrapMode.Truncate;
            SetPreferredHeight(_summaryText.gameObject, 120f);

            _tableText = CreateText("Table", leftPanel.transform, "-", 18, FontStyle.Normal);
            _tableText.alignment = TextAnchor.UpperLeft;
            _tableText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tableText.verticalOverflow = VerticalWrapMode.Truncate;
            SetPreferredHeight(_tableText.gameObject, 120f);

            _playersText = CreateText("Players", rightPanel.transform, "-", 16, FontStyle.Normal);
            _playersText.alignment = TextAnchor.UpperLeft;
            _playersText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _playersText.verticalOverflow = VerticalWrapMode.Truncate;
            SetPreferredHeight(_playersText.gameObject, 300f);

            _resultText = CreateText("Result", parent, string.Empty, 18, FontStyle.Bold);
            _resultText.alignment = TextAnchor.UpperLeft;
            _resultText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _resultText.verticalOverflow = VerticalWrapMode.Overflow;
            SetPreferredHeight(_resultText.gameObject, 78f);
            SetMinHeight(_resultText.gameObject, 78f);

            _actionsTitleText = CreateText("ActionsTitle", parent, "Available Actions", 20, FontStyle.Bold);
            _actionsTitleText.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(_actionsTitleText.gameObject, 28f);
            SetMinHeight(_actionsTitleText.gameObject, 28f);

            _actionsScrollRoot = CreatePanel("ActionsScrollRoot", parent, new Color(0.10f, 0.15f, 0.20f, 0.55f));
            SetMinHeight(_actionsScrollRoot, 96f);
            SetFlexibleHeight(_actionsScrollRoot, 1f);
            _actionsScrollRect = _actionsScrollRoot.AddComponent<ScrollRect>();
            _actionsScrollRect.horizontal = false;
            _actionsScrollRect.vertical = true;
            _actionsScrollRect.movementType = ScrollRect.MovementType.Clamped;

            var actionsViewport = CreatePanel("ActionsViewport", _actionsScrollRoot.transform, new Color(0f, 0f, 0f, 0.01f));
            StretchRect(actionsViewport.GetComponent<RectTransform>());
            actionsViewport.AddComponent<Mask>().showMaskGraphic = false;

            _actionsContainer = new GameObject("ActionsContent");
            _actionsContainer.transform.SetParent(actionsViewport.transform, false);
            var actionsContentRect = _actionsContainer.AddComponent<RectTransform>();
            actionsContentRect.anchorMin = new Vector2(0f, 1f);
            actionsContentRect.anchorMax = new Vector2(1f, 1f);
            actionsContentRect.pivot = new Vector2(0.5f, 1f);
            actionsContentRect.anchoredPosition = Vector2.zero;
            actionsContentRect.sizeDelta = Vector2.zero;

            var actionsLayout = _actionsContainer.AddComponent<VerticalLayoutGroup>();
            actionsLayout.spacing = 6f;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = true;
            actionsLayout.childForceExpandHeight = false;

            var fitter = _actionsContainer.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _actionsScrollRect.viewport = actionsViewport.GetComponent<RectTransform>();
            _actionsScrollRect.content = actionsContentRect;

            _backToMenuButton = CreateButton(parent, "Abort Match / Return to Menu", () => _controller.RequestReturnToMenu());
            SetMinHeight(_backToMenuButton.gameObject, 42f);
        }

        private void RefreshStaticBindings()
        {
            _leaveLobbyButton.gameObject.SetActive(true);
            _toggleReadyButton.gameObject.SetActive(true);
            _backToMenuButton.gameObject.SetActive(true);
        }

        private void RefreshDynamicContent()
        {
            _statusText.text = "Status: " + _controller.Status;
            if (_versionText != null)
            {
                _versionText.text = BuildVersionLabel();
            }

            var screen = _controller.CurrentScreen;
            _menuPanel.SetActive(screen == DurakAppController.AppScreen.Menu);
            _lobbyPanel.SetActive(screen == DurakAppController.AppScreen.Lobby);
            _matchPanel.SetActive(screen == DurakAppController.AppScreen.Match);

            if (screen == DurakAppController.AppScreen.Lobby)
            {
                RefreshLobby();
            }

            if (screen == DurakAppController.AppScreen.Match)
            {
                RefreshMatch();
            }
        }

        private void RefreshLobby()
        {
            _lobbyInfoText.text = _controller.GetLobbyInfoText();
            SetButtonLabel(_toggleReadyButton, _controller.LocalLobbyReady ? "Set NOT READY" : "Set READY");
            _toggleReadyButton.interactable = _controller.IsOnline;

            if (_controller.IsHost)
            {
                _startOnlineMatchButton.gameObject.SetActive(true);
                _startOnlineMatchButton.interactable = _controller.CanStartOnlineMatch;
            }
            else
            {
                _startOnlineMatchButton.gameObject.SetActive(false);
            }
        }

        private void RefreshMatch()
        {
            var state = _controller.State;
            if (state == null)
            {
                return;
            }

            _summaryText.text =
                "Phase: " + state.Phase + "\n" +
                "Trump: " + state.TrumpSuit + "\n" +
                "Deck: " + state.DeckCount + "\n" +
                "Round: " + state.Round.RoundNumber + "\n" +
                "Attacker: " + _controller.GetPlayerLabel(state.Round.AttackerId) + "\n" +
                "Defender: " + _controller.GetPlayerLabel(state.Round.DefenderId) + "\n" +
                "Current turn: " + _controller.GetPlayerLabel(state.CurrentTurnPlayerId);

            _tableText.text = BuildTableText(state);
            _playersText.text = BuildPlayersText(state);

            if (state.Phase == GamePhase.Completed)
            {
                _resultText.text = BuildResultText(state.MatchResult);
                if (_actionsTitleText != null)
                {
                    _actionsTitleText.gameObject.SetActive(false);
                }

                if (_actionsScrollRoot != null)
                {
                    _actionsScrollRoot.SetActive(false);
                }

                if (_actionsSignature != "COMPLETED")
                {
                    ClearActionButtons();
                    _actionsSignature = "COMPLETED";
                }

                return;
            }

            if (_actionsTitleText != null)
            {
                _actionsTitleText.gameObject.SetActive(true);
            }

            if (_actionsScrollRoot != null)
            {
                _actionsScrollRoot.SetActive(true);
            }

            _resultText.text = string.Empty;
            RefreshActionButtons();
        }

        private void RefreshActionButtons()
        {
            var state = _controller.State;
            if (state == null)
            {
                if (_actionsSignature.Length > 0)
                {
                    ClearActionButtons();
                    _actionsSignature = string.Empty;
                }

                return;
            }

            if (!_controller.IsLocalHumanTurn())
            {
                if (_actionsSignature != "NO_TURN")
                {
                    ClearActionButtons();
                    _actionsSignature = "NO_TURN";
                }

                return;
            }

            var legal = _controller.GetLocalLegalIntents();
            var signature = BuildActionsSignature(state.TurnSequence, legal);
            if (signature == _actionsSignature)
            {
                return;
            }

            ClearActionButtons();
            for (var i = 0; i < legal.Count; i++)
            {
                var intent = legal[i];
                var capturedIntent = intent;
                CreateButton(_actionsContainer.transform, _controller.GetIntentLabel(intent), () =>
                {
                    _controller.RequestSubmitIntent(capturedIntent);
                }, 34f);
            }

            if (_actionsScrollRect != null)
            {
                _actionsScrollRect.verticalNormalizedPosition = 1f;
            }

            _actionsSignature = signature;
        }

        private void ClearActionButtons()
        {
            for (var i = _actionsContainer.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(_actionsContainer.transform.GetChild(i).gameObject);
            }
        }

        private static string BuildActionsSignature(int turnSequence, IReadOnlyList<PlayerIntent> legalIntents)
        {
            var builder = new StringBuilder();
            builder.Append("SEQ:").Append(turnSequence).Append('|');
            for (var i = 0; i < legalIntents.Count; i++)
            {
                var intent = legalIntents[i];
                builder.Append((int)intent.Type).Append(':')
                    .Append(intent.PlayerId).Append(':')
                    .Append(intent.HasCard ? CardToShortString(intent.Card) : "-").Append(':')
                    .Append(intent.TargetPairIndex)
                    .Append('|');
            }

            return builder.ToString();
        }

        private string BuildTableText(GameState state)
        {
            if (state.Round.Table.Count == 0)
            {
                return "Table:\n(empty)";
            }

            var builder = new StringBuilder();
            builder.AppendLine("Table:");
            for (var i = 0; i < state.Round.Table.Count; i++)
            {
                var pair = state.Round.Table[i];
                builder.Append(" - #").Append(i)
                    .Append(" A: ").Append(CardToShortString(pair.AttackCard))
                    .Append(" | D: ")
                    .Append(pair.IsDefended ? CardToShortString(pair.DefenseCard) : "-")
                    .AppendLine();
            }

            return builder.ToString();
        }

        private string BuildPlayersText(GameState state)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Players:");
            for (var i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[i];
                builder.Append(" - ").Append(_controller.GetPlayerLabel(player.PlayerId))
                    .Append(" | Hand: ").Append(player.Hand.Count);

                if (player.IsBot)
                {
                    builder.Append(" | BOT");
                }

                if (player.PlayerId == _controller.LocalPlayerId)
                {
                    builder.Append(" | LOCAL");
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private string BuildResultText(MatchResult result)
        {
            var winners = result.Winners != null ? result.Winners : new List<int>();
            var winnerNames = new StringBuilder();
            for (var i = 0; i < winners.Count; i++)
            {
                if (i > 0)
                {
                    winnerNames.Append(", ");
                }

                winnerNames.Append(_controller.GetPlayerLabel(winners[i]));
            }

            return "Result:\n" +
                   "Durak: " + _controller.GetPlayerLabel(result.DurakPlayerId) + "\n" +
                   "Winners: " + winnerNames;
        }

        private static string CardToShortString(Card card)
        {
            return RankToShortString(card.Rank) + SuitToShortString(card.Suit);
        }

        private static string RankToShortString(Rank rank)
        {
            switch (rank)
            {
                case Rank.Six: return "6";
                case Rank.Seven: return "7";
                case Rank.Eight: return "8";
                case Rank.Nine: return "9";
                case Rank.Ten: return "10";
                case Rank.Jack: return "J";
                case Rank.Queen: return "Q";
                case Rank.King: return "K";
                case Rank.Ace: return "A";
                default: return ((int)rank).ToString();
            }
        }

        private static string SuitToShortString(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs: return "C";
                case Suit.Diamonds: return "D";
                case Suit.Hearts: return "H";
                case Suit.Spades: return "S";
                default: return "?";
            }
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            var image = panel.AddComponent<Image>();
            image.color = color;
            return panel;
        }

        private Text CreateText(string name, Transform parent, string text, int fontSize, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.font = _font;
            txt.text = text;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = Color.white;
            return txt;
        }

        private Button CreateButton(Transform parent, string label, Action onClick, float height = 42f)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.20f, 0.29f, 0.35f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());

            var text = CreateText("Label", go.transform, label, 18, FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            StretchRect(text.rectTransform);

            SetPreferredHeight(go, height);
            return button;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
            }
        }

        private InputField CreateInputField(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var image = root.AddComponent<Image>();
            image.color = new Color(0.14f, 0.18f, 0.21f, 1f);

            var input = root.AddComponent<InputField>();
            input.interactable = true;
            input.readOnly = false;
            input.lineType = InputField.LineType.SingleLine;

            var text = CreateText("Text", root.transform, string.Empty, 18, FontStyle.Normal);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            StretchRect(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(10f, 6f);
            text.rectTransform.offsetMax = new Vector2(-10f, -6f);

            var placeholder = CreateText("Placeholder", root.transform, "JOIN CODE", 16, FontStyle.Italic);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            StretchRect(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(10f, 6f);
            placeholder.rectTransform.offsetMax = new Vector2(-10f, -6f);

            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterValidation = InputField.CharacterValidation.None;
            input.characterLimit = 64;

            SetPreferredHeight(root, 42f);
            return input;
        }

        private static void ConfigureVerticalContainer(Transform container, float spacing, int padH, int padV)
        {
            var layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padH, padH, padV, padV);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void ConfigureHorizontalContainer(Transform container, float spacing, int padH, int padV)
        {
            var layout = container.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(padH, padH, padV, padV);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }

        private static void SetPreferredHeight(GameObject go, float height)
        {
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.preferredHeight = height;
        }

        private static void SetMinHeight(GameObject go, float height)
        {
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.minHeight = height;
        }

        private static void SetPreferredWidth(GameObject go, float width)
        {
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.preferredWidth = width;
        }

        private static void SetFlexibleHeight(GameObject go, float flexibleHeight)
        {
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.flexibleHeight = flexibleHeight;
        }

        private static void SetFlexibleWidth(GameObject go, float flexibleWidth)
        {
            var layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.flexibleWidth = flexibleWidth;
        }

        private static void StretchRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string BuildVersionLabel()
        {
            return BuildVersionProvider.GetDisplayVersion();
        }
    }
}
