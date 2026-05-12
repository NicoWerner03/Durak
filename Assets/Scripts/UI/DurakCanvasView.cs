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
        // ── Palette ────────────────────────────────────────────────────────────
        private static readonly Color FeltBackgroundColor  = new Color(0.028f, 0.088f, 0.070f, 1.00f);
        private static readonly Color PanelColor           = new Color(0.092f, 0.140f, 0.122f, 0.98f);
        private static readonly Color PanelBorderColor     = new Color(0.195f, 0.272f, 0.232f, 1.00f);
        private static readonly Color SurfaceColor         = new Color(0.046f, 0.125f, 0.105f, 0.95f);
        private static readonly Color SurfaceRaisedColor   = new Color(0.150f, 0.210f, 0.185f, 1.00f);
        private static readonly Color DividerColor         = new Color(0.195f, 0.272f, 0.232f, 0.85f);
        private static readonly Color ButtonColor          = new Color(0.205f, 0.308f, 0.252f, 1.00f);
        private static readonly Color ButtonHighlightColor = new Color(0.308f, 0.450f, 0.350f, 1.00f);
        private static readonly Color ButtonPressedColor   = new Color(0.132f, 0.205f, 0.168f, 1.00f);
        private static readonly Color ButtonDisabledColor  = new Color(0.108f, 0.148f, 0.132f, 0.60f);
        private static readonly Color PrimaryTextColor     = new Color(1.00f, 0.955f, 0.870f, 1.00f);
        private static readonly Color MutedTextColor       = new Color(0.672f, 0.642f, 0.562f, 1.00f);
        private static readonly Color AccentTextColor      = new Color(1.00f, 0.780f, 0.260f, 1.00f);
        private static readonly Color DangerTextColor      = new Color(0.950f, 0.325f, 0.278f, 1.00f);
        private static readonly Color SuccessTextColor     = new Color(0.378f, 0.845f, 0.478f, 1.00f);
        private static readonly Color CardFaceColor        = new Color(0.975f, 0.960f, 0.900f, 1.00f);
        private static readonly Color CardBorderColor      = new Color(0.140f, 0.140f, 0.165f, 1.00f);
        private static readonly Color CardRedColor         = new Color(0.820f, 0.120f, 0.120f, 1.00f);
        private static readonly Color CardBlackColor       = new Color(0.080f, 0.080f, 0.120f, 1.00f);
        private static readonly Color CardSlotColor        = new Color(0.070f, 0.120f, 0.105f, 0.80f);
        private static readonly Color CardBackColor        = new Color(0.105f, 0.180f, 0.330f, 1.00f);
        private static readonly Color CardBackPatternColor = new Color(0.190f, 0.290f, 0.460f, 1.00f);
        private static readonly Color CardBackEdgeColor    = new Color(0.060f, 0.110f, 0.220f, 1.00f);
        private static readonly Color CardBackEmblemColor  = new Color(0.950f, 0.780f, 0.280f, 0.85f);
        private static readonly Color OpponentActiveColor  = new Color(0.180f, 0.320f, 0.220f, 1.00f);

        private const float CardWidth   = 82f;
        private const float CardHeight  = 114f;
        private const float MiniCardW   = 44f;
        private const float MiniCardH   = 60f;

        // ── Fields ─────────────────────────────────────────────────────────────
        private DurakAppController _controller;
        private Font _font;

        private GameObject _menuPanel;
        private GameObject _lobbyPanel;
        private GameObject _matchPanel;

        private Text _statusText;
        private Text _versionText;
        private Text _lobbyHeaderText;
        private Text _lobbyInfoText;
        private Text _summaryText;
        private Text _playersText;
        private Text _resultText;
        private Text _actionsTitleText;

        private InputField _joinCodeInput;
        private Button _copyJoinCodeButton;
        private Button _startOnlineMatchButton;
        private Button _toggleReadyButton;
        private Button _leaveLobbyButton;

        private GameObject _actionsContainer;
        private GameObject _actionsScrollRoot;
        private ScrollRect _actionsScrollRect;
        private string _actionsSignature = string.Empty;

        private GameObject _tableCardsContainer;
        private GameObject _handCardsContainer;
        private GameObject _handSection;
        private string _tableSignature = string.Empty;
        private string _handSignature  = string.Empty;

        private GameObject _opponentsContainer;
        private GameObject _deckTrumpContainer;
        private GameObject _statusInfoContainer;
        private GameObject _turnBannerPanel;
        private Image      _turnBannerImage;
        private Text       _turnBannerTitleText;
        private Text       _turnBannerActionText;
        private Text       _deckCountText;
        private Text       _trumpLabelText;
        private Button     _takeCardsButton;
        private Button     _endAttackButton;
        private GameObject _quickActionBar;
        private string     _opponentsSignature = string.Empty;
        private string     _deckSignature      = string.Empty;

        private bool _hasSelectedHandCard;
        private Card _selectedHandCard;

        // Sound transition tracking
        private GamePhase _prevPhase = GamePhase.Lobby;
        private int       _prevTurnPlayerId = int.MinValue;
        private bool      _prevWasMyTurn;
        private bool      _prevMatchCompleteAnnounced;

        // Pause overlay
        private GameObject _pauseOverlay;
        private GameObject _pauseMenuView;
        private GameObject _pauseOptionsView;
        private Button     _pauseLeaveMatchButton;
        private Text       _volumeValueText;
        private Text       _muteButtonLabel;
        private Slider     _volumeSlider;
        private Func<bool> _isEscPressedThisFrame;

        private float _nextUiRefreshAt;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            _controller = GetComponent<DurakAppController>();
            if (_controller == null) { enabled = false; return; }

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _isEscPressedThisFrame = BuildEscPressedAccessor();
            EnsureEventSystem();
            BuildCanvas();
            RefreshStaticBindings();
            RefreshDynamicContent();
        }

        private void Update()
        {
            if (_isEscPressedThisFrame != null && _isEscPressedThisFrame())
                HandleEscapePressed();

            if (Time.unscaledTime < _nextUiRefreshAt) return;
            _nextUiRefreshAt = Time.unscaledTime + 0.1f;
            RefreshDynamicContent();
        }

        // Builds a delegate that returns true when ESC was pressed this frame.
        // Tries the new Input System first (via reflection so the project doesn't
        // hard-depend on the package), then falls back to legacy UnityEngine.Input.
        private static Func<bool> BuildEscPressedAccessor()
        {
            try
            {
                var keyboardType = Type.GetType(
                    "UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                if (keyboardType != null)
                {
                    var currentProp = keyboardType.GetProperty("current",
                        BindingFlags.Static | BindingFlags.Public);
                    var escapeKeyProp = keyboardType.GetProperty("escapeKey",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (currentProp != null && escapeKeyProp != null)
                    {
                        return () =>
                        {
                            var keyboard = currentProp.GetValue(null);
                            if (keyboard == null) return false;
                            var escapeKey = escapeKeyProp.GetValue(keyboard);
                            if (escapeKey == null) return false;
                            var wasPressedProp = escapeKey.GetType()
                                .GetProperty("wasPressedThisFrame");
                            return wasPressedProp != null
                                && wasPressedProp.GetValue(escapeKey) is bool b && b;
                        };
                    }
                }
            }
            catch { }

            return () =>
            {
                try { return Input.GetKeyDown(KeyCode.Escape); }
                catch { return false; }
            };
        }

        private void HandleEscapePressed()
        {
            if (_pauseOverlay == null) return;

            if (!_pauseOverlay.activeSelf)
            {
                ShowPauseOverlay();
            }
            else if (_pauseOptionsView != null && _pauseOptionsView.activeSelf)
            {
                // ESC inside options — go back to main pause menu instead of closing.
                ShowPauseMainView();
            }
            else
            {
                HidePauseOverlay();
            }
        }

        // ── Event System ───────────────────────────────────────────────────────
        private void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var go = new GameObject("EventSystem");
                eventSystem = go.AddComponent<EventSystem>();
            }
            EnsureCompatibleInputModule(eventSystem.gameObject);
        }

        private static void EnsureCompatibleInputModule(GameObject esGo)
        {
            var isType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (isType != null)
            {
                var standalone = esGo.GetComponent<StandaloneInputModule>();
                if (standalone != null) Destroy(standalone);
                var module = esGo.GetComponent(isType) ?? esGo.AddComponent(isType);
                var assign  = isType.GetMethod("AssignDefaultActions",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (assign != null) { try { assign.Invoke(module, null); } catch { } }
                return;
            }
            if (esGo.GetComponent<StandaloneInputModule>() == null)
                esGo.AddComponent<StandaloneInputModule>();
        }

        // ── Canvas Build ───────────────────────────────────────────────────────
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

            var root = CreatePanel("Root", canvasObject.transform, FeltBackgroundColor);
            StretchRect(root.GetComponent<RectTransform>());
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(32, 32, 28, 28);
            rootLayout.spacing = 12f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            // Header row
            var headerRow = new GameObject("HeaderRow");
            headerRow.transform.SetParent(root.transform, false);
            ConfigureHorizontalContainer(headerRow.transform, 16f, 0, 0);
            SetPreferredHeight(headerRow, 62f);
            SetMinHeight(headerRow, 62f);

            var header = CreateText("Header", headerRow.transform, "♠ DURAK", 40, FontStyle.Bold);
            header.alignment = TextAnchor.MiddleLeft;
            header.color = AccentTextColor;
            SetFlexibleWidth(header.gameObject, 1f);

            var statusPanel = CreateBorderedPanel("StatusPanel", headerRow.transform,
                SurfaceRaisedColor, PanelBorderColor);
            ConfigureVerticalContainer(statusPanel.transform, 0f, 16, 8);
            SetPreferredWidth(statusPanel, 860f);
            SetMinHeight(statusPanel, 46f);

            _statusText = CreateText("Status", statusPanel.transform, "Status: —", 17, FontStyle.Normal);
            _statusText.alignment = TextAnchor.MiddleLeft;
            _statusText.color = MutedTextColor;
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statusText.verticalOverflow = VerticalWrapMode.Truncate;
            SetPreferredHeight(_statusText.gameObject, 30f);

            CreateDivider(root.transform);

            _menuPanel = CreateBorderedPanel("MenuPanel", root.transform, PanelColor, PanelBorderColor);
            SetPreferredHeight(_menuPanel, 390f);
            SetMinHeight(_menuPanel, 350f);
            BuildMenuPanel(_menuPanel.transform);

            _lobbyPanel = CreateBorderedPanel("LobbyPanel", root.transform, PanelColor, PanelBorderColor);
            SetMinHeight(_lobbyPanel, 320f);
            SetFlexibleHeight(_lobbyPanel, 1f);
            BuildLobbyPanel(_lobbyPanel.transform);

            _matchPanel = CreateBorderedPanel("MatchPanel", root.transform, PanelColor, PanelBorderColor);
            SetFlexibleHeight(_matchPanel, 1f);
            BuildMatchPanel(_matchPanel.transform);

            _versionText = CreateText("VersionText", canvasObject.transform, BuildVersionLabel(), 13, FontStyle.Normal);
            _versionText.alignment = TextAnchor.LowerLeft;
            _versionText.color = new Color(MutedTextColor.r, MutedTextColor.g, MutedTextColor.b, 0.50f);
            var vr = _versionText.rectTransform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.zero;
            vr.pivot = Vector2.zero;
            vr.anchoredPosition = new Vector2(16f, 8f);
            vr.sizeDelta = new Vector2(500f, 20f);

            BuildPauseOverlay(canvasObject.transform);
        }

        private void BuildPauseOverlay(Transform canvasParent)
        {
            _pauseOverlay = new GameObject("PauseOverlay");
            _pauseOverlay.transform.SetParent(canvasParent, false);

            // Backdrop — dimmed full-screen image that blocks raycasts behind it
            var backdrop = _pauseOverlay.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.72f);
            backdrop.raycastTarget = true;
            StretchRect(_pauseOverlay.GetComponent<RectTransform>());

            // Centered card holding either the menu or options view
            var dialog = CreateBorderedPanel("PauseDialog", _pauseOverlay.transform,
                PanelColor, PanelBorderColor);
            var dialogRect = dialog.GetComponent<RectTransform>();
            dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRect.pivot     = new Vector2(0.5f, 0.5f);
            dialogRect.sizeDelta = new Vector2(460f, 480f);

            _pauseMenuView    = BuildPauseMainView(dialog.transform);
            _pauseOptionsView = BuildOptionsView(dialog.transform);

            _pauseOverlay.SetActive(false);
        }

        private GameObject BuildPauseMainView(Transform parent)
        {
            var view = new GameObject("PauseMainView");
            view.transform.SetParent(parent, false);
            StretchRect(view.AddComponent<RectTransform>());
            ConfigureVerticalContainer(view.transform, 12f, 28, 26);

            var title = CreateText("Title", view.transform, "PAUSED", 32, FontStyle.Bold);
            title.alignment = TextAnchor.MiddleCenter;
            title.color = AccentTextColor;
            SetPreferredHeight(title.gameObject, 46f);

            CreateDivider(view.transform);
            CreateSpacer(view.transform, 8f);

            CreateButton(view.transform, "Resume",  HidePauseOverlay);
            CreateButton(view.transform, "Options", ShowOptionsView);

            _pauseLeaveMatchButton = CreateButton(view.transform, "Leave Match", () =>
            {
                HidePauseOverlay();
                _controller.RequestReturnToMenu();
            });

            CreateSpacer(view.transform, 4f);
            CreateButton(view.transform, "Quit Game", QuitApplication);

            return view;
        }

        private GameObject BuildOptionsView(Transform parent)
        {
            var view = new GameObject("PauseOptionsView");
            view.transform.SetParent(parent, false);
            StretchRect(view.AddComponent<RectTransform>());
            ConfigureVerticalContainer(view.transform, 10f, 28, 26);

            var title = CreateText("Title", view.transform, "OPTIONS", 28, FontStyle.Bold);
            title.alignment = TextAnchor.MiddleCenter;
            title.color = AccentTextColor;
            SetPreferredHeight(title.gameObject, 40f);

            CreateDivider(view.transform);
            CreateSpacer(view.transform, 6f);

            CreateSectionLabel(view.transform, "SOUND VOLUME");
            _volumeValueText = CreateText("VolumeValue", view.transform, "55%", 17, FontStyle.Bold);
            _volumeValueText.alignment = TextAnchor.MiddleCenter;
            _volumeValueText.color = PrimaryTextColor;
            SetPreferredHeight(_volumeValueText.gameObject, 24f);

            var initialVol = DurakAudioManager.Instance != null
                ? DurakAudioManager.Instance.Volume : 0.55f;
            _volumeSlider = CreateVolumeSlider(view.transform, initialVol, OnVolumeChanged);

            CreateSpacer(view.transform, 8f);

            var muteBtn = CreateButton(view.transform, "Mute: OFF", ToggleMute);
            _muteButtonLabel = muteBtn.GetComponentInChildren<Text>();
            UpdateMuteLabel();

            CreateSpacer(view.transform, 6f);
            CreateButton(view.transform, "Back", ShowPauseMainView);

            view.SetActive(false);
            return view;
        }

        private Slider CreateVolumeSlider(Transform parent, float initial, Action<float> onChanged)
        {
            var root = new GameObject("VolumeSlider");
            root.transform.SetParent(parent, false);
            SetPreferredHeight(root, 30f);

            var bg = new GameObject("Background");
            bg.transform.SetParent(root.transform, false);
            bg.AddComponent<Image>().color = SurfaceRaisedColor;
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.30f);
            bgRect.anchorMax = new Vector2(1f, 0.70f);
            bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(root.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.30f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.70f);
            fillAreaRect.offsetMin = new Vector2(8f, 0f);
            fillAreaRect.offsetMax = new Vector2(-8f, 0f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            fill.AddComponent<Image>().color = AccentTextColor;
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero; fillRect.offsetMax = Vector2.zero;

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(root.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(10f, 0f);
            handleAreaRect.offsetMax = new Vector2(-10f, 0f);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = PrimaryTextColor;
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.sizeDelta = new Vector2(20f, 0f);

            var slider = root.AddComponent<Slider>();
            slider.fillRect      = fillRect;
            slider.handleRect    = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction     = Slider.Direction.LeftToRight;
            slider.minValue      = 0f;
            slider.maxValue      = 1f;
            slider.value         = initial;
            slider.onValueChanged.AddListener(v => onChanged(v));

            return slider;
        }

        private void OnVolumeChanged(float v)
        {
            if (DurakAudioManager.Instance != null) DurakAudioManager.Instance.Volume = v;
            if (_volumeValueText != null)
                _volumeValueText.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        private void ToggleMute()
        {
            if (DurakAudioManager.Instance == null) return;
            DurakAudioManager.Instance.Muted = !DurakAudioManager.Instance.Muted;
            UpdateMuteLabel();
        }

        private void UpdateMuteLabel()
        {
            if (_muteButtonLabel == null || DurakAudioManager.Instance == null) return;
            _muteButtonLabel.text = DurakAudioManager.Instance.Muted ? "Mute: ON" : "Mute: OFF";
        }

        private void ShowPauseOverlay()
        {
            if (_pauseOverlay == null) return;
            ShowPauseMainView();
            _pauseOverlay.SetActive(true);
            if (_controller != null) _controller.IsPaused = true;

            // Adjust Leave button to current screen
            if (_pauseLeaveMatchButton != null)
            {
                var screen = _controller.CurrentScreen;
                var inMatchOrLobby = screen == DurakAppController.AppScreen.Match
                                  || screen == DurakAppController.AppScreen.Lobby;
                _pauseLeaveMatchButton.gameObject.SetActive(inMatchOrLobby);
                SetButtonLabel(_pauseLeaveMatchButton,
                    screen == DurakAppController.AppScreen.Lobby ? "Leave Lobby" : "Leave Match");
            }

            // Sync options controls with current audio state
            if (_volumeSlider != null && DurakAudioManager.Instance != null)
                _volumeSlider.SetValueWithoutNotify(DurakAudioManager.Instance.Volume);
            if (_volumeValueText != null && DurakAudioManager.Instance != null)
                _volumeValueText.text = Mathf.RoundToInt(DurakAudioManager.Instance.Volume * 100f) + "%";
            UpdateMuteLabel();
        }

        private void HidePauseOverlay()
        {
            if (_pauseOverlay != null) _pauseOverlay.SetActive(false);
            if (_controller != null) _controller.IsPaused = false;
        }

        private void ShowPauseMainView()
        {
            if (_pauseMenuView    != null) _pauseMenuView.SetActive(true);
            if (_pauseOptionsView != null) _pauseOptionsView.SetActive(false);
        }

        private void ShowOptionsView()
        {
            if (_pauseMenuView    != null) _pauseMenuView.SetActive(false);
            if (_pauseOptionsView != null) _pauseOptionsView.SetActive(true);
        }

        private static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildMenuPanel(Transform parent)
        {
            ConfigureVerticalContainer(parent, 9f, 22, 20);

            CreateSectionTitle("MenuTitle", parent, "Play");
            CreateDivider(parent);
            CreateSpacer(parent, 4f);

            CreateSectionLabel(parent, "OFFLINE");
            CreateButton(parent, "Start  —  1 Human + 1 Bot",  () => _controller.RequestStartOfflineMatch(2));
            CreateButton(parent, "Start  —  1 Human + 3 Bots", () => _controller.RequestStartOfflineMatch(4));
            CreateSpacer(parent, 6f);

            CreateSectionLabel(parent, "ONLINE");
            CreateButton(parent, "Host Session", () => _controller.RequestHostSession());

            var joinRow = new GameObject("JoinRow");
            joinRow.transform.SetParent(parent, false);
            var jr = joinRow.AddComponent<HorizontalLayoutGroup>();
            jr.spacing = 10f;
            jr.childControlWidth = true; jr.childControlHeight = true;
            jr.childForceExpandWidth = false; jr.childForceExpandHeight = false;
            SetPreferredHeight(joinRow, 46f);

            _joinCodeInput = CreateInputField(joinRow.transform, "JoinCodeInput");
            SetPreferredWidth(_joinCodeInput.gameObject, 340f);
            var joinBtn = CreateButton(joinRow.transform, "Join Session",
                () => _controller.RequestJoinSession(_joinCodeInput.text));
            SetPreferredWidth(joinBtn.gameObject, 220f);

            var quickJoin = CreateButton(parent, "Join Localhost  (DIRECT)",
                () => _controller.RequestJoinSession("DIRECT:127.0.0.1:7777"));
            SetPreferredHeight(quickJoin.gameObject, 36f);
        }

        private void BuildLobbyPanel(Transform parent)
        {
            ConfigureVerticalContainer(parent, 10f, 22, 20);

            _lobbyHeaderText = CreateSectionTitle("LobbyTitle", parent, "Lobby");
            CreateDivider(parent);
            CreateSpacer(parent, 4f);

            var infoPanel = CreateBorderedPanel("LobbyInfoPanel", parent, SurfaceColor, DividerColor);
            ConfigureVerticalContainer(infoPanel.transform, 6f, 18, 14);
            SetPreferredHeight(infoPanel, 218f);
            SetMinHeight(infoPanel, 180f);

            _lobbyInfoText = CreateText("LobbyInfo", infoPanel.transform, "—", 18, FontStyle.Normal);
            _lobbyInfoText.alignment = TextAnchor.UpperLeft;
            _lobbyInfoText.color = PrimaryTextColor;
            _lobbyInfoText.supportRichText = true;
            _lobbyInfoText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lobbyInfoText.verticalOverflow = VerticalWrapMode.Truncate;
            SetFlexibleHeight(_lobbyInfoText.gameObject, 1f);

            CreateSpacer(parent, 4f);
            _copyJoinCodeButton     = CreateButton(parent, "Copy Join Code",     CopyJoinCodeToClipboard);
            _startOnlineMatchButton = CreateButton(parent, "Start Online Match",  () => _controller.RequestStartOnlineMatch());
            _toggleReadyButton      = CreateButton(parent, "Set READY",           () => _controller.RequestToggleLobbyReady());
            _leaveLobbyButton       = CreateButton(parent, "Leave Lobby",          () => _controller.RequestReturnToMenu());
            SetMinHeight(_copyJoinCodeButton.gameObject,     40f);
            SetMinHeight(_startOnlineMatchButton.gameObject, 40f);
            SetMinHeight(_toggleReadyButton.gameObject,      40f);
            SetMinHeight(_leaveLobbyButton.gameObject,       40f);
        }

        private void BuildMatchPanel(Transform parent)
        {
            ConfigureVerticalContainer(parent, 8f, 18, 14);

            CreateSectionTitle("MatchTitle", parent, "Match");
            CreateDivider(parent);

            // ── Prominent turn / role / outcome banner ──────────────────
            _turnBannerPanel = new GameObject("TurnBanner");
            _turnBannerPanel.transform.SetParent(parent, false);
            _turnBannerImage = _turnBannerPanel.AddComponent<Image>();
            _turnBannerImage.color = SurfaceRaisedColor;
            ConfigureVerticalContainer(_turnBannerPanel.transform, 2f, 18, 8);
            SetPreferredHeight(_turnBannerPanel, 62f);
            SetMinHeight(_turnBannerPanel, 62f);

            _turnBannerTitleText = CreateText("TurnBannerTitle", _turnBannerPanel.transform,
                "—", 19, FontStyle.Bold);
            _turnBannerTitleText.alignment = TextAnchor.MiddleCenter;
            _turnBannerTitleText.color = PrimaryTextColor;
            _turnBannerTitleText.supportRichText = true;
            SetPreferredHeight(_turnBannerTitleText.gameObject, 24f);

            _turnBannerActionText = CreateText("TurnBannerAction", _turnBannerPanel.transform,
                "—", 14, FontStyle.Normal);
            _turnBannerActionText.alignment = TextAnchor.MiddleCenter;
            _turnBannerActionText.color = MutedTextColor;
            _turnBannerActionText.supportRichText = true;
            SetPreferredHeight(_turnBannerActionText.gameObject, 18f);

            // ── Opponents row (top) ─────────────────────────────────────
            _opponentsContainer = new GameObject("OpponentsRow");
            _opponentsContainer.transform.SetParent(parent, false);
            var oppLayout = _opponentsContainer.AddComponent<HorizontalLayoutGroup>();
            oppLayout.padding = new RectOffset(0, 0, 0, 0);
            oppLayout.spacing = 16f;
            oppLayout.childAlignment = TextAnchor.MiddleCenter;
            oppLayout.childControlWidth = false; oppLayout.childControlHeight = false;
            oppLayout.childForceExpandWidth = false; oppLayout.childForceExpandHeight = false;
            SetPreferredHeight(_opponentsContainer, 138f);
            SetMinHeight(_opponentsContainer, 130f);

            // ── Center row: deck/trump | table | status ─────────────────
            var centerRow = new GameObject("CenterRow");
            centerRow.transform.SetParent(parent, false);
            ConfigureHorizontalContainer(centerRow.transform, 12f, 0, 0);
            SetMinHeight(centerRow, CardHeight + 90f);
            SetFlexibleHeight(centerRow, 1f);

            // Left: deck + trump
            _deckTrumpContainer = CreateBorderedPanel("DeckTrump", centerRow.transform,
                SurfaceColor, DividerColor);
            ConfigureVerticalContainer(_deckTrumpContainer.transform, 4f, 10, 10);
            SetPreferredWidth(_deckTrumpContainer, 170f);
            SetFlexibleWidth(_deckTrumpContainer, 0f);

            // Center: table card area
            var tableArea = CreateBorderedPanel("TableArea", centerRow.transform,
                new Color(0.038f, 0.110f, 0.092f, 0.95f), DividerColor);
            ConfigureVerticalContainer(tableArea.transform, 4f, 10, 10);
            SetFlexibleWidth(tableArea, 1f);

            var tableTitle = CreateText("TableTitle", tableArea.transform, "Table", 16, FontStyle.Bold);
            tableTitle.alignment = TextAnchor.MiddleCenter;
            tableTitle.color = AccentTextColor;
            SetPreferredHeight(tableTitle.gameObject, 22f);

            _tableCardsContainer = BuildCardScrollRow("TableCards", tableArea.transform,
                CardHeight + 36f, CardHeight + 30f);
            SetFlexibleHeight(_tableCardsContainer.transform.parent.parent.gameObject, 1f);

            // Right: status info
            _statusInfoContainer = CreateBorderedPanel("StatusInfo", centerRow.transform,
                SurfaceColor, DividerColor);
            ConfigureVerticalContainer(_statusInfoContainer.transform, 6f, 12, 10);
            SetPreferredWidth(_statusInfoContainer, 280f);
            SetFlexibleWidth(_statusInfoContainer, 0f);

            _summaryText = CreateText("Summary", _statusInfoContainer.transform, "—", 14, FontStyle.Normal);
            _summaryText.alignment = TextAnchor.UpperLeft;
            _summaryText.color = PrimaryTextColor;
            _summaryText.supportRichText = true;
            _summaryText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _summaryText.verticalOverflow = VerticalWrapMode.Truncate;
            SetFlexibleHeight(_summaryText.gameObject, 1f);

            CreateDivider(_statusInfoContainer.transform);

            _playersText = CreateText("Players", _statusInfoContainer.transform, "—", 13, FontStyle.Normal);
            _playersText.alignment = TextAnchor.UpperLeft;
            _playersText.color = PrimaryTextColor;
            _playersText.supportRichText = true;
            _playersText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _playersText.verticalOverflow = VerticalWrapMode.Truncate;
            SetFlexibleHeight(_playersText.gameObject, 1f);

            // ── Result text (between center and hand) ───────────────────
            _resultText = CreateText("Result", parent, string.Empty, 18, FontStyle.Bold);
            _resultText.alignment = TextAnchor.MiddleCenter;
            _resultText.color = PrimaryTextColor;
            _resultText.supportRichText = true;
            _resultText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _resultText.verticalOverflow = VerticalWrapMode.Overflow;
            SetPreferredHeight(_resultText.gameObject, 72f);

            // ── Hand row (bottom) ───────────────────────────────────────
            _handSection = new GameObject("HandSection");
            _handSection.transform.SetParent(parent, false);
            ConfigureVerticalContainer(_handSection.transform, 2f, 0, 0);
            SetPreferredHeight(_handSection, CardHeight + 32f);

            var handTitle = CreateText("HandTitle", _handSection.transform, "Your Hand",
                14, FontStyle.Bold);
            handTitle.alignment = TextAnchor.MiddleCenter;
            handTitle.color = AccentTextColor;
            SetPreferredHeight(handTitle.gameObject, 18f);

            _handCardsContainer = BuildCardScrollRow("HandCards", _handSection.transform,
                CardHeight + 8f, CardHeight);

            // ── Quick action bar: Take / End / Back ─────────────────────
            _quickActionBar = new GameObject("QuickActions");
            _quickActionBar.transform.SetParent(parent, false);
            var qabLayout = _quickActionBar.AddComponent<HorizontalLayoutGroup>();
            qabLayout.spacing = 10f;
            qabLayout.childControlWidth = true; qabLayout.childControlHeight = true;
            qabLayout.childForceExpandWidth = true; qabLayout.childForceExpandHeight = false;
            SetPreferredHeight(_quickActionBar, 48f);
            SetMinHeight(_quickActionBar, 48f);

            _takeCardsButton = CreateButton(_quickActionBar.transform, "Aufnehmen", SubmitPrimaryQuickAction);
            _endAttackButton = CreateButton(_quickActionBar.transform, "Angriff beenden", SubmitSecondaryQuickAction);

            // ── Actions title + scroll (for card disambiguation) ────────
            _actionsTitleText = CreateText("ActionsTitle", parent, "Actions", 15, FontStyle.Bold);
            _actionsTitleText.alignment = TextAnchor.MiddleLeft;
            _actionsTitleText.color = AccentTextColor;
            SetPreferredHeight(_actionsTitleText.gameObject, 22f);

            _actionsScrollRoot = CreatePanel("ActionsScrollRoot", parent, SurfaceColor);
            SetMinHeight(_actionsScrollRoot, 60f);
            SetPreferredHeight(_actionsScrollRoot, 80f);
            _actionsScrollRect = _actionsScrollRoot.AddComponent<ScrollRect>();
            _actionsScrollRect.horizontal = false;
            _actionsScrollRect.vertical = true;
            _actionsScrollRect.movementType = ScrollRect.MovementType.Clamped;

            var actionsViewport = CreatePanel("ActionsViewport", _actionsScrollRoot.transform,
                new Color(0f, 0f, 0f, 0.01f));
            StretchRect(actionsViewport.GetComponent<RectTransform>());
            actionsViewport.AddComponent<Mask>().showMaskGraphic = false;

            _actionsContainer = new GameObject("ActionsContent");
            _actionsContainer.transform.SetParent(actionsViewport.transform, false);
            var acRect = _actionsContainer.AddComponent<RectTransform>();
            acRect.anchorMin = new Vector2(0f, 1f);
            acRect.anchorMax = new Vector2(1f, 1f);
            acRect.pivot = new Vector2(0.5f, 1f);
            acRect.anchoredPosition = Vector2.zero;
            acRect.sizeDelta = Vector2.zero;
            var acLayout = _actionsContainer.AddComponent<VerticalLayoutGroup>();
            acLayout.spacing = 6f;
            acLayout.padding = new RectOffset(8, 8, 8, 8);
            acLayout.childControlWidth = true; acLayout.childControlHeight = true;
            acLayout.childForceExpandWidth = true; acLayout.childForceExpandHeight = false;
            var acFitter = _actionsContainer.AddComponent<ContentSizeFitter>();
            acFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            acFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _actionsScrollRect.viewport = actionsViewport.GetComponent<RectTransform>();
            _actionsScrollRect.content = acRect;
        }

        private void BuildDeckTrumpContent()
        {
            ClearChildren(_deckTrumpContainer.transform);

            _trumpLabelText = CreateText("TrumpLabel", _deckTrumpContainer.transform,
                "Trumpf: —", 14, FontStyle.Bold);
            _trumpLabelText.alignment = TextAnchor.MiddleCenter;
            _trumpLabelText.color = AccentTextColor;
            _trumpLabelText.supportRichText = true;
            SetPreferredHeight(_trumpLabelText.gameObject, 22f);

            // Deck visual area (deck stack + trump card)
            var deckArea = new GameObject("DeckArea");
            deckArea.transform.SetParent(_deckTrumpContainer.transform, false);
            deckArea.AddComponent<RectTransform>();
            SetPreferredHeight(deckArea, CardHeight + 30f);
            SetMinHeight(deckArea, CardHeight + 20f);

            _deckCountText = CreateText("DeckCount", _deckTrumpContainer.transform,
                "Deck: 0", 13, FontStyle.Normal);
            _deckCountText.alignment = TextAnchor.MiddleCenter;
            _deckCountText.color = MutedTextColor;
            SetPreferredHeight(_deckCountText.gameObject, 18f);
        }

        // Builds a horizontal card scroll area and returns the content container.
        private GameObject BuildCardScrollRow(string name, Transform parent, float preferredH, float minH)
        {
            var scrollRoot = CreatePanel(name + "Scroll", parent, new Color(0.04f, 0.10f, 0.08f, 0.45f));
            SetPreferredHeight(scrollRoot, preferredH);
            SetMinHeight(scrollRoot, minH);

            var sr = scrollRoot.AddComponent<ScrollRect>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.elasticity = 0.1f;

            var viewport = CreatePanel(name + "Viewport", scrollRoot.transform,
                new Color(0f, 0f, 0f, 0.01f));
            StretchRect(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject(name + "Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            var layout = content.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = contentRect;

            return content;
        }

        // ── Refresh ────────────────────────────────────────────────────────────
        private void RefreshStaticBindings()
        {
            _copyJoinCodeButton.gameObject.SetActive(true);
            _leaveLobbyButton.gameObject.SetActive(true);
            _toggleReadyButton.gameObject.SetActive(true);
        }

        private void RefreshDynamicContent()
        {
            _statusText.text = "Status:  " + _controller.Status;
            if (_versionText != null) _versionText.text = BuildVersionLabel();

            var screen = _controller.CurrentScreen;
            _menuPanel.SetActive(screen == DurakAppController.AppScreen.Menu);
            _lobbyPanel.SetActive(screen == DurakAppController.AppScreen.Lobby);
            _matchPanel.SetActive(screen == DurakAppController.AppScreen.Match);

            if (screen == DurakAppController.AppScreen.Lobby) RefreshLobby();
            if (screen == DurakAppController.AppScreen.Match)  RefreshMatch();
        }

        private void RefreshLobby()
        {
            if (_lobbyHeaderText != null)
                _lobbyHeaderText.text = _controller.IsHost ? "Host Lobby" : "Client Lobby";

            _lobbyInfoText.text = _controller.GetLobbyInfoText();
            SetButtonLabel(_toggleReadyButton, _controller.LocalLobbyReady ? "Set  NOT READY" : "Set  READY");
            _toggleReadyButton.interactable = _controller.IsOnline;

            if (_copyJoinCodeButton != null)
            {
                _copyJoinCodeButton.gameObject.SetActive(_controller.IsHost);
                _copyJoinCodeButton.interactable =
                    _controller.IsHost && !string.IsNullOrWhiteSpace(_controller.JoinCode);
            }

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
            if (state == null) return;

            // Lazy-build deck/trump children on first refresh
            if (_deckCountText == null) BuildDeckTrumpContent();

            DetectAndPlayTransitionSounds(state);

            var accentHex = ColorToHex(AccentTextColor);
            var successHex = ColorToHex(SuccessTextColor);
            var dangerHex = ColorToHex(DangerTextColor);
            var attackCount = state.Round != null && state.Round.Table != null ? state.Round.Table.Count : 0;
            var attackLimit = state.Round != null ? state.Round.AttackLimit : 0;

            _summaryText.text =
                Labeled("Phase",       accentHex) + state.Phase                                        + "\n" +
                Labeled("Runde",       accentHex) + state.Round.RoundNumber                            + "\n" +
                Labeled("Angriffe",    accentHex) + attackCount + "/" + attackLimit                    + "\n" +
                Labeled("Angreifer",   successHex) + _controller.GetPlayerLabel(state.Round.AttackerId) + "\n" +
                Labeled("Verteidiger", dangerHex) + _controller.GetPlayerLabel(state.Round.DefenderId) + "\n" +
                Labeled("Am Zug",      accentHex) + _controller.GetPlayerLabel(state.CurrentTurnPlayerId);

            if (state.Round.DefenderIsTaking)
            {
                _summaryText.text += "\n" + Labeled("Status", accentHex) + "Verteidiger nimmt; Nachwerfen möglich";
            }

            if (_controller.IsRoundRevealActive)
            {
                _summaryText.text += "\n" +
                    Labeled("Status", accentHex) + "Showing completed round " +
                    _controller.RoundRevealRoundNumber;
            }

            _playersText.text = BuildPlayersText(state);

            RefreshTurnBanner(state);
            RefreshOpponents(state);
            RefreshDeckTrump(state);
            RefreshTableCards(state);
            RefreshHandCards(state);
            RefreshQuickActions(state);

            if (state.Phase == GamePhase.Completed)
            {
                _resultText.text = BuildResultText(state.MatchResult);
                if (_actionsTitleText != null) _actionsTitleText.gameObject.SetActive(false);
                if (_actionsScrollRoot != null) _actionsScrollRoot.SetActive(false);
                if (_actionsSignature != "COMPLETED")
                {
                    ClearChildren(_actionsContainer.transform);
                    _actionsSignature = "COMPLETED";
                }
                return;
            }

            _resultText.text = string.Empty;
            RefreshActionButtons();
        }

        // Prominent banner showing whose turn it is, in what role, and what they
        // are expected to do — or, during round-reveal, how the round just ended.
        private void RefreshTurnBanner(GameState state)
        {
            if (_turnBannerPanel == null) return;

            // ── Round reveal: show what just happened ──
            if (_controller.IsRoundRevealActive
                && state.LastResolvedRoundOutcome != RoundOutcome.None
                && state.LastResolvedRoundNumber == _controller.RoundRevealRoundNumber)
            {
                var defenderLabel = _controller.GetPlayerLabel(state.LastResolvedRoundDefenderId);
                var defenderIsLocal = state.LastResolvedRoundDefenderId == _controller.LocalPlayerId;

                if (state.LastResolvedRoundOutcome == RoundOutcome.DefenderWon)
                {
                    SetTurnBanner(
                        title:  (defenderIsLocal ? "Du hast" : defenderLabel + " hat") +
                                " alle Angriffe abgewehrt",
                        action: "Die Karten wandern auf den Abwurfstapel. Verteidiger wird neuer Angreifer.",
                        background: new Color(0.16f, 0.30f, 0.22f, 1f),
                        accent:     SuccessTextColor);
                    return;
                }

                if (state.LastResolvedRoundOutcome == RoundOutcome.DefenderTook)
                {
                    var n = state.LastResolvedRoundTakenCardCount;
                    SetTurnBanner(
                        title:  (defenderIsLocal ? "Du musstest " : defenderLabel + " musste ") +
                                n + " Karte" + (n == 1 ? "" : "n") + " nehmen",
                        action: "Verteidiger bleibt Verteidiger — der nächste Angreifer kommt dran.",
                        background: new Color(0.32f, 0.16f, 0.14f, 1f),
                        accent:     DangerTextColor);
                    return;
                }
            }

            // ── Match completed banner ──
            if (state.Phase == GamePhase.Completed)
            {
                var winnerHas = state.MatchResult.Winners != null
                             && state.MatchResult.Winners.Contains(_controller.LocalPlayerId);
                SetTurnBanner(
                    title:  winnerHas ? "Du hast gewonnen!" : "Match beendet",
                    action: "Drücke ESC, um zurück ins Menü zu kehren.",
                    background: winnerHas ? new Color(0.16f, 0.30f, 0.22f, 1f) : SurfaceRaisedColor,
                    accent:     winnerHas ? SuccessTextColor : PrimaryTextColor);
                return;
            }

            // ── Live turn banner ──
            var turnId    = state.CurrentTurnPlayerId;
            var localId   = _controller.LocalPlayerId;
            var isMyTurn  = turnId == localId;
            var role      = ResolveRoleForPlayer(state, turnId);
            var turnName  = _controller.GetPlayerLabel(turnId);

            var roleLabelMine = role switch
            {
                TurnRole.Defender     => "Du bist <color=#" + ColorToHex(DangerTextColor)  + ">Verteidiger</color>",
                TurnRole.MainAttacker => "Du bist <color=#" + ColorToHex(AccentTextColor)  + ">Angreifer</color>",
                TurnRole.CoAttacker   => "Du bist <color=#" + ColorToHex(AccentTextColor)  + ">Co-Angreifer</color>",
                _                     => "Du bist am Zug",
            };
            var roleLabelOther = role switch
            {
                TurnRole.Defender     => turnName + " <color=#" + ColorToHex(DangerTextColor) + ">verteidigt</color>",
                TurnRole.MainAttacker => turnName + " <color=#" + ColorToHex(AccentTextColor) + ">greift an</color>",
                TurnRole.CoAttacker   => turnName + " <color=#" + ColorToHex(AccentTextColor) + ">spielt nach</color>",
                _                     => turnName + " ist am Zug",
            };

            var action = ResolveActionHint(state, role, isMyTurn);
            SetTurnBanner(
                title:      isMyTurn ? roleLabelMine : roleLabelOther,
                action:     action,
                background: isMyTurn ? new Color(0.22f, 0.30f, 0.18f, 1f) : SurfaceRaisedColor,
                accent:     isMyTurn ? AccentTextColor : MutedTextColor);
        }

        private enum TurnRole { Other, Defender, MainAttacker, CoAttacker }

        private static TurnRole ResolveRoleForPlayer(GameState state, int playerId)
        {
            if (state.Round == null || playerId < 0) return TurnRole.Other;
            if (playerId == state.Round.DefenderId) return TurnRole.Defender;
            if (playerId == state.Round.AttackerId) return TurnRole.MainAttacker;
            if (state.Round.AttackerOrder != null && state.Round.AttackerOrder.Contains(playerId))
                return TurnRole.CoAttacker;
            return TurnRole.Other;
        }

        private string ResolveActionHint(GameState state, TurnRole role, bool isMyTurn)
        {
            var tableCount  = state.Round != null ? state.Round.Table.Count : 0;
            var attackLimit = state.Round != null ? state.Round.AttackLimit : 0;
            var taking      = state.Round != null && state.Round.DefenderIsTaking;
            var sufx        = "  (" + tableCount + "/" + attackLimit + ")";

            switch (role)
            {
                case TurnRole.Defender:
                    if (taking)
                        return isMyTurn
                            ? "Du nimmst die Karten — warte auf weitere Nachlegungen oder bis die Angreifer beenden"
                            : "Verteidiger nimmt die Karten auf";
                    return isMyTurn
                        ? "Schlag jede Angriffskarte mit einer höheren der gleichen Farbe oder mit Trumpf — sonst Karten aufnehmen" + sufx
                        : "Muss die Angriffskarten abwehren oder aufnehmen" + sufx;

                case TurnRole.MainAttacker:
                    if (tableCount == 0)
                        return isMyTurn
                            ? "Leg eine Angriffskarte aus deiner Hand"
                            : "Wählt eine Angriffskarte";
                    if (taking)
                        return isMyTurn
                            ? "Leg passende Karten nach (gleicher Rang) oder beende den Angriff" + sufx
                            : "Kann nachlegen oder beenden" + sufx;
                    return isMyTurn
                        ? "Leg eine passende Karte nach (gleicher Rang) oder beende den Angriff" + sufx
                        : "Legt nach oder beendet den Angriff" + sufx;

                case TurnRole.CoAttacker:
                    return isMyTurn
                        ? "Leg eine Karte mit einem Rang nach, der bereits auf dem Tisch liegt — oder passe" + sufx
                        : "Spielt nach oder passt" + sufx;

                default:
                    return "Warte auf den nächsten Zug";
            }
        }

        private void SetTurnBanner(string title, string action, Color background, Color accent)
        {
            if (_turnBannerImage != null) _turnBannerImage.color = background;
            if (_turnBannerTitleText != null)
            {
                _turnBannerTitleText.text  = title;
                _turnBannerTitleText.color = accent;
            }
            if (_turnBannerActionText != null) _turnBannerActionText.text = action;
        }

        private void RefreshOpponents(GameState state)
        {
            var localId = _controller.LocalPlayerId;
            var sb = new System.Text.StringBuilder();
            sb.Append(state.CurrentTurnPlayerId).Append('|');
            sb.Append(state.Round.AttackerId).Append('|').Append(state.Round.DefenderId).Append('|');
            for (var i = 0; i < state.Players.Count; i++)
            {
                var p = state.Players[i];
                if (p.PlayerId == localId) continue;
                sb.Append(p.PlayerId).Append(':').Append(p.Hand.Count)
                  .Append(':').Append(p.IsBot ? '1' : '0').Append('|');
            }
            var sig = sb.ToString();
            if (sig == _opponentsSignature) return;

            ClearChildren(_opponentsContainer.transform);
            for (var i = 0; i < state.Players.Count; i++)
            {
                var p = state.Players[i];
                if (p.PlayerId == localId) continue;
                CreateOpponentSlot(_opponentsContainer.transform, p,
                    p.PlayerId == state.CurrentTurnPlayerId,
                    p.PlayerId == state.Round.AttackerId,
                    p.PlayerId == state.Round.DefenderId);
            }
            _opponentsSignature = sig;
        }

        private void RefreshDeckTrump(GameState state)
        {
            var sig = state.DeckCount + "|" + (int)state.TrumpSuit;
            if (sig == _deckSignature) return;

            _trumpLabelText.text = "Trumpf:  " + SuitRich(state.TrumpSuit);
            _deckCountText.text  = "Deck: " + state.DeckCount + "  cards";

            // Rebuild the visual deck stack + trump card
            var deckArea = _deckTrumpContainer.transform.Find("DeckArea");
            if (deckArea != null) BuildDeckArtwork(deckArea, state);

            _deckSignature = sig;
        }

        private void BuildDeckArtwork(Transform deckArea, GameState state)
        {
            ClearChildren(deckArea);

            var hasDeck    = state.DeckCount > 0;
            var stackDepth = Mathf.Clamp(state.DeckCount, 0, 4);

            // Trump card peeks out from under the deck — placed first so deck is on top.
            // Use a placeholder Ace of trump suit for the visual.
            var trumpVisual = CreateCardWidget(deckArea, new Card { Rank = Rank.Ace, Suit = state.TrumpSuit });
            var trumpRect = trumpVisual.GetComponent<RectTransform>();
            // Lift LayoutElement off so we can position absolutely
            var trumpLE = trumpVisual.GetComponent<LayoutElement>();
            if (trumpLE != null) trumpLE.ignoreLayout = true;
            trumpRect.anchorMin = new Vector2(0.5f, 0.5f);
            trumpRect.anchorMax = new Vector2(0.5f, 0.5f);
            trumpRect.pivot     = new Vector2(0.5f, 0.5f);
            trumpRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            trumpRect.anchoredPosition = new Vector2(20f, -10f);
            trumpRect.localRotation = Quaternion.Euler(0f, 0f, -90f);

            if (hasDeck)
            {
                for (var d = 0; d < stackDepth; d++)
                {
                    var back = CreateCardBack(deckArea);
                    var backRect = back.GetComponent<RectTransform>();
                    var backLE = back.GetComponent<LayoutElement>();
                    if (backLE != null) backLE.ignoreLayout = true;
                    backRect.anchorMin = new Vector2(0.5f, 0.5f);
                    backRect.anchorMax = new Vector2(0.5f, 0.5f);
                    backRect.pivot     = new Vector2(0.5f, 0.5f);
                    backRect.sizeDelta = new Vector2(CardWidth, CardHeight);
                    backRect.anchoredPosition = new Vector2(-12f - d * 1.5f, 6f - d * 1.5f);
                }
            }
        }

        private void RefreshQuickActions(GameState state)
        {
            if (state.Phase == GamePhase.Completed)
            {
                _takeCardsButton.gameObject.SetActive(true);
                _endAttackButton.gameObject.SetActive(true);
                SetButtonLabel(_takeCardsButton, _controller.GetPlayAgainButtonLabel());
                SetButtonLabel(_endAttackButton, _controller.IsOnline ? "Session verlassen" : "Zum Menü");
                _takeCardsButton.interactable = true;
                _endAttackButton.interactable = true;
                return;
            }

            var isMyTurn = _controller.IsLocalHumanTurn();
            var legal    = isMyTurn ? _controller.GetLocalLegalIntents() : null;
            var canTake  = HasNonCardIntent(legal, PlayerIntentType.TakeCards);
            var canEnd   = HasNonCardIntent(legal, PlayerIntentType.EndAttack);

            _takeCardsButton.gameObject.SetActive(true);
            _endAttackButton.gameObject.SetActive(true);
            SetButtonLabel(_takeCardsButton, "Aufnehmen");
            SetButtonLabel(_endAttackButton, "Angriff beenden");
            _takeCardsButton.interactable = canTake;
            _endAttackButton.interactable = canEnd;
        }

        private void SubmitPrimaryQuickAction()
        {
            var state = _controller.State;
            if (state != null && state.Phase == GamePhase.Completed)
            {
                _controller.RequestPlayAgain();
                return;
            }

            SubmitNonCardIntentByType(PlayerIntentType.TakeCards);
        }

        private void SubmitSecondaryQuickAction()
        {
            var state = _controller.State;
            if (state != null && state.Phase == GamePhase.Completed)
            {
                _controller.RequestReturnToMenu();
                return;
            }

            SubmitNonCardIntentByType(PlayerIntentType.EndAttack);
        }

        private static bool HasNonCardIntent(IReadOnlyList<PlayerIntent> intents, PlayerIntentType type)
        {
            if (intents == null) return false;
            for (var i = 0; i < intents.Count; i++)
            {
                var x = intents[i];
                if (!x.HasCard && x.Type == type) return true;
            }
            return false;
        }

        private void SubmitNonCardIntentByType(PlayerIntentType type)
        {
            var intents = _controller.GetLocalLegalIntents();
            for (var i = 0; i < intents.Count; i++)
            {
                var x = intents[i];
                if (!x.HasCard && x.Type == type)
                {
                    DurakAudioManager.PlaySfx(type == PlayerIntentType.TakeCards
                        ? SfxKind.CardTake : SfxKind.CardPlay);
                    _hasSelectedHandCard = false;
                    _controller.RequestSubmitIntent(x);
                    return;
                }
            }
        }

        private void DetectAndPlayTransitionSounds(GameState state)
        {
            // Match completion → win/lose chord
            if (state.Phase == GamePhase.Completed && !_prevMatchCompleteAnnounced)
            {
                _prevMatchCompleteAnnounced = true;
                var winners = state.MatchResult.Winners;
                var localWon = winners != null && winners.Contains(_controller.LocalPlayerId);
                DurakAudioManager.PlaySfx(localWon ? SfxKind.MatchWin : SfxKind.MatchLose);
            }
            else if (state.Phase != GamePhase.Completed)
            {
                _prevMatchCompleteAnnounced = false;
            }

            // Turn became local — turn jingle
            var isMyTurn = _controller.IsLocalHumanTurn();
            if (isMyTurn && !_prevWasMyTurn) DurakAudioManager.PlaySfx(SfxKind.TurnStart);
            _prevWasMyTurn       = isMyTurn;
            _prevTurnPlayerId    = state.CurrentTurnPlayerId;
            _prevPhase           = state.Phase;
        }

        private void RefreshTableCards(GameState state)
        {
            var table = _controller.IsRoundRevealActive ? _controller.RoundRevealTable : state.Round.Table;
            var sig = BuildTableSignature(state, table, _controller.IsRoundRevealActive);
            if (sig == _tableSignature) return;

            ClearChildren(_tableCardsContainer.transform);

            if (table.Count == 0)
            {
                var empty = CreateText("EmptyLabel", _tableCardsContainer.transform,
                    "(empty)", 16, FontStyle.Italic);
                empty.color = MutedTextColor;
                empty.alignment = TextAnchor.MiddleCenter;
                LE(_tableCardsContainer).preferredWidth = 120f;
            }
            else
            {
                // Compact pair scaling: shrink cards as pair count grows so all pairs fit.
                var maxPairs    = Mathf.Max(table.Count, 1);
                var scale       = maxPairs <= 4 ? 1.00f
                                : maxPairs == 5 ? 0.85f
                                                : 0.72f;
                var pairCardW   = CardWidth  * scale;
                var pairCardH   = CardHeight * scale;
                var defOffsetX  = 22f * scale;
                var defOffsetY  = 24f * scale;

                for (var i = 0; i < table.Count; i++)
                {
                    var pair = table[i];
                    CreatePairWidget(_tableCardsContainer.transform, pair,
                        pairCardW, pairCardH, defOffsetX, defOffsetY);
                }
            }

            _tableSignature = sig;
        }

        // Renders an attack/defense pair as one widget with the defense card overlapping
        // the attack card (offset right + up), like cards stacked on a real table.
        private void CreatePairWidget(Transform parent, TablePair pair,
            float cardW, float cardH, float defOffsetX, float defOffsetY)
        {
            var container = new GameObject("Pair");
            container.transform.SetParent(parent, false);
            container.AddComponent<RectTransform>();
            var le = container.AddComponent<LayoutElement>();
            le.preferredWidth  = cardW + defOffsetX;
            le.preferredHeight = cardH + defOffsetY;
            le.minWidth        = cardW + defOffsetX;
            le.minHeight       = cardH + defOffsetY;

            // Attack card — anchored to bottom-left
            var attack = CreateCardWidget(container.transform, pair.AttackCard);
            DetachLayoutAndPlace(attack, cardW, cardH, new Vector2(0f, 0f));

            // Defense card (or placeholder slot) — overlapped at top-right
            var defObj = pair.IsDefended
                ? CreateCardWidget(container.transform, pair.DefenseCard)
                : CreateEmptySlotForPair(container.transform);
            DetachLayoutAndPlace(defObj, cardW, cardH, new Vector2(defOffsetX, defOffsetY));
        }

        private GameObject CreateEmptySlotForPair(Transform parent)
        {
            var slot = new GameObject("EmptySlot");
            slot.transform.SetParent(parent, false);
            slot.AddComponent<Image>().color = CardSlotColor;
            slot.AddComponent<LayoutElement>();
            var txt = CreateText("Q", slot.transform, "?", 28, FontStyle.Bold);
            txt.color = new Color(0.4f, 0.55f, 0.48f, 0.55f);
            txt.alignment = TextAnchor.MiddleCenter;
            StretchRect(txt.rectTransform);
            return slot;
        }

        private static void DetachLayoutAndPlace(GameObject card, float w, float h, Vector2 offset)
        {
            var le = card.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;
            var rect = card.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot     = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = offset;
        }

        private void RefreshHandCards(GameState state)
        {
            var localPlayer = state.GetPlayer(_controller.LocalPlayerId);
            var showHand    = localPlayer != null && !localPlayer.IsBot;

            if (_handSection != null) _handSection.SetActive(showHand);

            if (!showHand)
            {
                if (_handSignature != "HIDDEN")
                {
                    _hasSelectedHandCard = false;
                    ClearChildren(_handCardsContainer.transform);
                    _handSignature = "HIDDEN";
                }
                return;
            }

            var isMyTurn    = _controller.IsLocalHumanTurn();
            var legalIntents = isMyTurn ? _controller.GetLocalLegalIntents() : null;

            // If the selected card was played / left hand, clear selection
            if (_hasSelectedHandCard && !HandContains(localPlayer.Hand, _selectedHandCard))
                _hasSelectedHandCard = false;

            var sig = BuildHandSignature(localPlayer.Hand, isMyTurn, state.TurnSequence, _hasSelectedHandCard,
                _hasSelectedHandCard ? _selectedHandCard : default);
            if (sig == _handSignature) return;

            ClearChildren(_handCardsContainer.transform);

            if (localPlayer.Hand == null || localPlayer.Hand.Count == 0)
            {
                var empty = CreateText("EmptyLabel", _handCardsContainer.transform,
                    "(no cards)", 16, FontStyle.Italic);
                empty.color   = MutedTextColor;
                empty.alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                for (var i = 0; i < localPlayer.Hand.Count; i++)
                {
                    var card        = localPlayer.Hand[i];
                    var cardIntents = FindIntentsForCard(legalIntents, card);
                    var isPlayable  = cardIntents.Count > 0;
                    var isSelected  = _hasSelectedHandCard && CardEquals(card, _selectedHandCard);

                    if (isPlayable)
                    {
                        var capturedCard    = card;
                        var capturedIntents = cardIntents;
                        CreateCardWidget(_handCardsContainer.transform, card,
                            onClick: () => OnHandCardClicked(capturedCard, capturedIntents),
                            isSelected: isSelected,
                            dimmed: false);
                    }
                    else
                    {
                        CreateCardWidget(_handCardsContainer.transform, card,
                            onClick: null, isSelected: false, dimmed: true);
                    }
                }
            }

            _handSignature = sig;
        }

        private void OnHandCardClicked(Card card, List<PlayerIntent> intents)
        {
            if (intents.Count == 0) return;

            if (intents.Count == 1 && intents[0].Type != PlayerIntentType.Transfer)
            {
                DurakAudioManager.PlaySfx(SfxForIntent(intents[0].Type));
                _hasSelectedHandCard = false;
                _actionsSignature    = string.Empty;
                _handSignature       = string.Empty;
                _controller.RequestSubmitIntent(intents[0]);
                return;
            }

            // Select transfer cards too, because same-rank transfer is visually easy to mistake for defense.
            DurakAudioManager.PlaySfx(SfxKind.CardSelect);
            _hasSelectedHandCard = true;
            _selectedHandCard    = card;
            _actionsSignature    = string.Empty; // force actions rebuild
            _handSignature       = string.Empty; // force hand rebuild (highlight update)
        }

        private static SfxKind SfxForIntent(PlayerIntentType type)
        {
            switch (type)
            {
                case PlayerIntentType.Defend:    return SfxKind.CardDefend;
                case PlayerIntentType.TakeCards: return SfxKind.CardTake;
                default:                         return SfxKind.CardPlay;
            }
        }

        private static List<PlayerIntent> FindIntentsForCard(
            IReadOnlyList<PlayerIntent> intents, Card card)
        {
            var result = new List<PlayerIntent>();
            if (intents == null) return result;
            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                if (intent.HasCard && CardEquals(intent.Card, card))
                    result.Add(intent);
            }
            return result;
        }

        private static bool HandContains(List<Card> hand, Card card)
        {
            if (hand == null) return false;
            for (var i = 0; i < hand.Count; i++)
                if (CardEquals(hand[i], card)) return true;
            return false;
        }

        private static bool CardEquals(Card a, Card b)
            => a.Rank == b.Rank && a.Suit == b.Suit;

        private void RefreshActionButtons()
        {
            var state = _controller.State;
            if (state == null)
            {
                if (_actionsSignature.Length > 0)
                {
                    ClearChildren(_actionsContainer.transform);
                    _actionsSignature = string.Empty;
                }
                return;
            }

            if (!_controller.IsLocalHumanTurn())
            {
                _hasSelectedHandCard = false;
                if (_actionsSignature != "NO_TURN")
                {
                    ClearChildren(_actionsContainer.transform);
                    _actionsSignature = "NO_TURN";
                }
                if (_actionsScrollRoot != null) _actionsScrollRoot.SetActive(false);
                if (_actionsTitleText  != null) _actionsTitleText.gameObject.SetActive(false);
                return;
            }

            var legal    = _controller.GetLocalLegalIntents();
            var displayed = GetDisplayedIntents(legal);
            var signature = (_hasSelectedHandCard ? "SEL:" + CardKey(_selectedHandCard) + "|" : "")
                          + BuildActionsSignature(state.TurnSequence, displayed);

            if (signature == _actionsSignature) return;

            ClearChildren(_actionsContainer.transform);

            if (_hasSelectedHandCard)
            {
                SetActionsTitle("Aktion für " + RankToShortString(_selectedHandCard.Rank) +
                    SuitSymbol(_selectedHandCard.Suit));

                CreateButton(_actionsContainer.transform, "Abbrechen", () =>
                {
                    _hasSelectedHandCard = false;
                    _actionsSignature    = string.Empty;
                    _handSignature       = string.Empty;
                }, 34f);
            }
            else
            {
                SetActionsTitle("Actions");
            }

            for (var i = 0; i < displayed.Count; i++)
            {
                var intent        = displayed[i];
                var capturedIntent = intent;
                CreateButton(_actionsContainer.transform,
                    _controller.GetIntentLabel(intent),
                    () =>
                    {
                        DurakAudioManager.PlaySfx(SfxForIntent(capturedIntent.Type));
                        _hasSelectedHandCard = false;
                        _controller.RequestSubmitIntent(capturedIntent);
                    }, 36f);
            }

            // Hide the actions area when it would be empty and no card is selected
            var hasContent = displayed.Count > 0 || _hasSelectedHandCard;
            if (_actionsScrollRoot != null) _actionsScrollRoot.SetActive(hasContent);
            if (_actionsTitleText  != null) _actionsTitleText.gameObject.SetActive(hasContent);

            if (_actionsScrollRect != null) _actionsScrollRect.verticalNormalizedPosition = 1f;
            _actionsSignature = signature;
        }

        // Returns intents to display in the actions scroll.
        // Only shows card-specific intents for the currently selected hand card (disambiguation).
        // Non-card intents (TakeCards, EndAttack) are handled by the Quick Action Bar and
        // therefore intentionally excluded here — otherwise the actions row duplicates them.
        private IReadOnlyList<PlayerIntent> GetDisplayedIntents(IReadOnlyList<PlayerIntent> legal)
        {
            var result = new List<PlayerIntent>();
            if (!_hasSelectedHandCard) return result;
            for (var i = 0; i < legal.Count; i++)
            {
                var intent = legal[i];
                if (intent.HasCard && CardEquals(intent.Card, _selectedHandCard))
                    result.Add(intent);
            }
            return result;
        }

        private void SetActionsTitle(string title)
        {
            if (_actionsTitleText != null) _actionsTitleText.text = title;
        }

        // ── Text Builders ──────────────────────────────────────────────────────
        private string BuildPlayersText(GameState state)
        {
            var accentHex  = ColorToHex(AccentTextColor);
            var mutedHex   = ColorToHex(MutedTextColor);
            var successHex = ColorToHex(SuccessTextColor);
            var dangerHex  = ColorToHex(DangerTextColor);
            var sb = new StringBuilder();
            sb.Append("<color=#").Append(accentHex).Append(">Players</color>\n");

            for (var i = 0; i < state.Players.Count; i++)
            {
                var player  = state.Players[i];
                var isLocal = player.PlayerId == _controller.LocalPlayerId;
                var isTurn  = player.PlayerId == state.CurrentTurnPlayerId;
                var isAttacker = player.PlayerId == state.Round.AttackerId;
                var isDefender = player.PlayerId == state.Round.DefenderId;
                var nameHex = isDefender ? dangerHex :
                    isAttacker ? successHex :
                    isTurn ? successHex : (isLocal ? accentHex : ColorToHex(PrimaryTextColor));

                sb.Append("  <color=#").Append(nameHex).Append(">")
                  .Append(_controller.GetPlayerLabel(player.PlayerId))
                  .Append("</color>")
                  .Append("  ").Append(player.Hand.Count).Append(" cards");

                if (player.IsBot)  sb.Append("  <color=#").Append(mutedHex).Append(">[BOT]</color>");
                if (isLocal)       sb.Append("  <color=#").Append(accentHex).Append(">[YOU]</color>");
                if (isAttacker)    sb.Append("  <color=#").Append(successHex).Append(">[ANGREIFER]</color>");
                if (isDefender)    sb.Append("  <color=#").Append(dangerHex).Append(">[VERTEIDIGER]</color>");
                if (isTurn)        sb.Append("  <color=#").Append(successHex).Append(">[TURN]</color>");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string BuildResultText(MatchResult result)
        {
            var winners = result.Winners ?? new List<int>();
            var names   = new StringBuilder();
            for (var i = 0; i < winners.Count; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append(_controller.GetPlayerLabel(winners[i]));
            }
            return
                "<color=#" + ColorToHex(DangerTextColor)  + ">Durak (loser):</color>  " +
                    _controller.GetPlayerLabel(result.DurakPlayerId) + "\n" +
                "<color=#" + ColorToHex(SuccessTextColor) + ">Winners:</color>  " + names + "\n" +
                "<color=#" + ColorToHex(AccentTextColor) + ">Nochmal:</color>  " +
                    _controller.GetPlayAgainStatusText();
        }

        // ── Signature Helpers ──────────────────────────────────────────────────
        private static string BuildActionsSignature(int turnSeq, IReadOnlyList<PlayerIntent> intents)
        {
            var sb = new StringBuilder();
            sb.Append("SEQ:").Append(turnSeq).Append('|');
            for (var i = 0; i < intents.Count; i++)
            {
                var x = intents[i];
                sb.Append((int)x.Type).Append(':').Append(x.PlayerId).Append(':')
                  .Append(x.HasCard ? CardKey(x.Card) : "-").Append(':')
                  .Append(x.TargetPairIndex).Append('|');
            }
            return sb.ToString();
        }

        private static string BuildTableSignature(GameState state, IReadOnlyList<TablePair> table, bool isReveal)
        {
            var sb = new StringBuilder();
            sb.Append(isReveal ? "REVEAL:" : "LIVE:")
              .Append(state.Round.RoundNumber).Append('|');
            for (var i = 0; i < table.Count; i++)
            {
                var p = table[i];
                sb.Append(CardKey(p.AttackCard)).Append(':')
                  .Append(p.IsDefended ? CardKey(p.DefenseCard) : "-").Append('|');
            }
            return sb.ToString();
        }

        private static string BuildHandSignature(List<Card> hand, bool isMyTurn, int turnSequence,
            bool hasSelection, Card selectedCard)
        {
            if (hand == null || hand.Count == 0) return "EMPTY";
            var sb = new StringBuilder();
            sb.Append("SEQ:").Append(turnSequence).Append('|');
            if (isMyTurn)  sb.Append("TURN|");
            if (hasSelection) sb.Append("SEL:").Append(CardKey(selectedCard)).Append('|');
            for (var i = 0; i < hand.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(CardKey(hand[i]));
            }
            return sb.ToString();
        }

        private static string CardKey(Card card)
            => RankToShortString(card.Rank) + (int)card.Suit;

        // ── Card Widget Factories ──────────────────────────────────────────────
        private GameObject CreateCardWidget(Transform parent, Card card,
            Action onClick = null, bool isSelected = false, bool dimmed = false)
        {
            var isRed     = card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds;
            var suitColor = isRed ? CardRedColor : CardBlackColor;
            var rankStr   = RankToShortString(card.Rank);
            var suitStr   = SuitSymbol(card.Suit);

            // Outer: dark border, acts as the Button target graphic
            var outer    = new GameObject("Card_" + rankStr + suitStr);
            outer.transform.SetParent(parent, false);
            var outerImg = outer.AddComponent<Image>();
            outerImg.color = isSelected
                ? new Color(AccentTextColor.r, AccentTextColor.g, AccentTextColor.b, 1f)
                : CardBorderColor;

            var le = outer.AddComponent<LayoutElement>();
            le.preferredWidth  = CardWidth;
            le.preferredHeight = CardHeight;
            le.minWidth  = CardWidth  * 0.75f;
            le.minHeight = CardHeight * 0.75f;

            if (onClick != null)
            {
                var btn = outer.AddComponent<Button>();
                btn.targetGraphic = outerImg;
                btn.colors = new ColorBlock
                {
                    normalColor      = Color.white,
                    highlightedColor = new Color(1.30f, 1.30f, 1.10f, 1f),
                    pressedColor     = new Color(0.75f, 0.75f, 0.75f, 1f),
                    selectedColor    = new Color(1.15f, 1.15f, 0.90f, 1f),
                    disabledColor    = new Color(0.5f,  0.5f,  0.5f,  0.6f),
                    colorMultiplier  = 1f,
                    fadeDuration     = 0.08f,
                };
                btn.onClick.AddListener(() => onClick());
            }

            // Face: cream fill inside border
            var face     = new GameObject("Face");
            face.transform.SetParent(outer.transform, false);
            face.AddComponent<Image>().color = CardFaceColor;
            var faceRect = face.GetComponent<RectTransform>();
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = isSelected ? new Vector2(3f, 3f) : new Vector2(2f, 2f);
            faceRect.offsetMax = isSelected ? new Vector2(-3f, -3f) : new Vector2(-2f, -2f);

            // Rank — top left
            var rankTL     = CreateText("RankTL", face.transform, rankStr, 17, FontStyle.Bold);
            rankTL.color   = suitColor;
            rankTL.alignment = TextAnchor.UpperLeft;
            var rankTLRect = rankTL.rectTransform;
            rankTLRect.anchorMin = new Vector2(0f, 0.55f);
            rankTLRect.anchorMax = new Vector2(1f, 1f);
            rankTLRect.offsetMin = new Vector2(5f, 0f);
            rankTLRect.offsetMax = new Vector2(-5f, -4f);

            // Suit — center
            var suitC     = CreateText("Suit", face.transform, suitStr, 32, FontStyle.Normal);
            suitC.color   = suitColor;
            suitC.alignment = TextAnchor.MiddleCenter;
            var suitCRect = suitC.rectTransform;
            suitCRect.anchorMin = new Vector2(0f, 0.10f);
            suitCRect.anchorMax = new Vector2(1f, 0.65f);
            suitCRect.offsetMin = Vector2.zero;
            suitCRect.offsetMax = Vector2.zero;

            // Dim overlay for non-playable cards
            if (dimmed)
            {
                var dim    = new GameObject("Dim");
                dim.transform.SetParent(outer.transform, false);
                dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.48f);
                var dimRect = dim.GetComponent<RectTransform>();
                dimRect.anchorMin = Vector2.zero;
                dimRect.anchorMax = Vector2.one;
                dimRect.offsetMin = Vector2.zero;
                dimRect.offsetMax = Vector2.zero;
            }

            return outer;
        }

        private void CreateEmptyCardSlot(Transform parent)
        {
            var slot = new GameObject("EmptySlot");
            slot.transform.SetParent(parent, false);
            slot.AddComponent<Image>().color = CardSlotColor;
            var le = slot.AddComponent<LayoutElement>();
            le.preferredWidth  = CardWidth;
            le.preferredHeight = CardHeight;
            le.minWidth  = CardWidth  * 0.75f;
            le.minHeight = CardHeight * 0.75f;

            var txt = CreateText("Q", slot.transform, "?", 28, FontStyle.Bold);
            txt.color = new Color(0.4f, 0.55f, 0.48f, 0.55f);
            txt.alignment = TextAnchor.MiddleCenter;
            StretchRect(txt.rectTransform);
        }

        private void CreateCardGap(Transform parent, float width)
        {
            var gap = new GameObject("Gap");
            gap.transform.SetParent(parent, false);
            var le = gap.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
        }

        // Face-down card (deck stack / opponent hand visualization).
        private GameObject CreateCardBack(Transform parent, float width = -1f, float height = -1f)
        {
            if (width  <= 0f) width  = CardWidth;
            if (height <= 0f) height = CardHeight;

            var outer = new GameObject("CardBack");
            outer.transform.SetParent(parent, false);
            outer.AddComponent<Image>().color = CardBorderColor;
            var le = outer.AddComponent<LayoutElement>();
            le.preferredWidth  = width;
            le.preferredHeight = height;
            le.minWidth  = width  * 0.6f;
            le.minHeight = height * 0.6f;

            // Inner solid back
            var inner = new GameObject("BackBg");
            inner.transform.SetParent(outer.transform, false);
            inner.AddComponent<Image>().color = CardBackColor;
            var innerRect = inner.GetComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero; innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(2f, 2f); innerRect.offsetMax = new Vector2(-2f, -2f);

            // Pattern panel (lighter inset rectangle)
            var pattern = new GameObject("Pattern");
            pattern.transform.SetParent(outer.transform, false);
            pattern.AddComponent<Image>().color = CardBackPatternColor;
            var pr = pattern.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(0.18f, 0.10f);
            pr.anchorMax = new Vector2(0.82f, 0.90f);
            pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;

            // Inner edge strip (thin frame inside the pattern)
            var edge = new GameObject("Edge");
            edge.transform.SetParent(outer.transform, false);
            edge.AddComponent<Image>().color = CardBackEdgeColor;
            var er = edge.GetComponent<RectTransform>();
            er.anchorMin = new Vector2(0.30f, 0.20f);
            er.anchorMax = new Vector2(0.70f, 0.80f);
            er.offsetMin = Vector2.zero; er.offsetMax = Vector2.zero;

            // Centered emblem
            var emblem = CreateText("Sym", outer.transform, "D", Mathf.RoundToInt(height * 0.32f),
                FontStyle.Bold);
            emblem.color = CardBackEmblemColor;
            emblem.alignment = TextAnchor.MiddleCenter;
            StretchRect(emblem.rectTransform);

            return outer;
        }

        // Opponent panel: name, card-back stack, hand count.
        private GameObject CreateOpponentSlot(Transform parent, PlayerState player, bool isCurrentTurn,
            bool isAttacker, bool isDefender)
        {
            var bg = isDefender
                ? new Color(0.180f, 0.065f, 0.060f, 0.92f)
                : isAttacker
                    ? new Color(0.060f, 0.165f, 0.090f, 0.92f)
                    : (isCurrentTurn ? OpponentActiveColor : SurfaceColor);
            var border = isDefender
                ? DangerTextColor
                : isAttacker
                    ? SuccessTextColor
                    : (isCurrentTurn ? AccentTextColor : DividerColor);
            var slot = CreateBorderedPanel("OppSlot_" + player.PlayerId, parent, bg, border);
            ConfigureVerticalContainer(slot.transform, 4f, 12, 8);
            SetPreferredWidth(slot, 230f);
            SetPreferredHeight(slot, 130f);
            SetMinWidth(slot, 200f);
            SetMinHeight(slot, 130f);

            // Name + role
            var nameText = CreateText("Name", slot.transform,
                _controller.GetPlayerLabel(player.PlayerId), 15, FontStyle.Bold);
            nameText.color = isDefender ? DangerTextColor :
                isAttacker ? SuccessTextColor :
                isCurrentTurn ? AccentTextColor : PrimaryTextColor;
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.supportRichText = true;
            SetPreferredHeight(nameText.gameObject, 20f);

            // Mini card-back stack (centered area)
            var stack = new GameObject("CardStack");
            stack.transform.SetParent(slot.transform, false);
            stack.AddComponent<RectTransform>();
            SetPreferredHeight(stack, MiniCardH + 6f);
            SetFlexibleHeight(stack, 0f);
            BuildOpponentStackArt(stack, player.Hand != null ? player.Hand.Count : 0);

            // Status row (count + tags)
            var status = new System.Text.StringBuilder();
            status.Append(player.Hand != null ? player.Hand.Count : 0).Append(" cards");
            if (isAttacker) status.Append("  · ANGREIFER");
            if (isDefender) status.Append("  · VERTEIDIGER");
            if (player.IsBot) status.Append("  · BOT");
            if (isCurrentTurn) status.Append("  · TURN");
            var statusText = CreateText("Status", slot.transform, status.ToString(), 12, FontStyle.Normal);
            statusText.color = isDefender ? DangerTextColor :
                isAttacker ? SuccessTextColor :
                isCurrentTurn ? AccentTextColor : MutedTextColor;
            statusText.alignment = TextAnchor.MiddleCenter;
            SetPreferredHeight(statusText.gameObject, 16f);

            return slot;
        }

        private void BuildOpponentStackArt(GameObject stack, int handCount)
        {
            ClearChildren(stack.transform);
            if (handCount <= 0) return;

            var visible = Mathf.Min(handCount, 5);
            var spread  = (visible - 1) * 14f;
            for (var i = 0; i < visible; i++)
            {
                var back = CreateCardBack(stack.transform, MiniCardW, MiniCardH);
                var le   = back.GetComponent<LayoutElement>();
                if (le != null) le.ignoreLayout = true;
                var rect = back.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot     = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(MiniCardW, MiniCardH);
                rect.anchoredPosition = new Vector2(-spread / 2f + i * 14f, 0f);
                rect.localRotation = Quaternion.Euler(0f, 0f, (i - (visible - 1) / 2f) * -6f);
            }
        }

        // ── Card / Suit Helpers ────────────────────────────────────────────────
        private static string SuitRich(Suit suit)
        {
            var isRed = suit == Suit.Hearts || suit == Suit.Diamonds;
            var hex   = isRed ? ColorToHex(CardRedColor) : ColorToHex(PrimaryTextColor);
            return "<color=#" + hex + ">" + SuitSymbol(suit) + " " + SuitToLongString(suit) + "</color>";
        }

        private static string SuitSymbol(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs:    return "♣";
                case Suit.Diamonds: return "♦";
                case Suit.Hearts:   return "♥";
                case Suit.Spades:   return "♠";
                default:            return "?";
            }
        }

        private static string SuitToLongString(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs:    return "Kreuz";
                case Suit.Diamonds: return "Karo";
                case Suit.Hearts:   return "Herz";
                case Suit.Spades:   return "Pik";
                default:            return suit.ToString();
            }
        }

        private static string CardToShortString(Card card)
            => RankToShortString(card.Rank) + SuitSymbol(card.Suit);

        private static string RankToShortString(Rank rank)
        {
            switch (rank)
            {
                case Rank.Six:   return "6";
                case Rank.Seven: return "7";
                case Rank.Eight: return "8";
                case Rank.Nine:  return "9";
                case Rank.Ten:   return "10";
                case Rank.Jack:  return "J";
                case Rank.Queen: return "Q";
                case Rank.King:  return "K";
                case Rank.Ace:   return "A";
                default:         return ((int)rank).ToString();
            }
        }

        // ── Factory Helpers ────────────────────────────────────────────────────
        private static string ColorToHex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        private static string Labeled(string label, string hex)
            => "<color=#" + hex + ">" + label + "</color>  ";

        private Text CreateSectionTitle(string name, Transform parent, string text)
        {
            var txt = CreateText(name, parent, text, 26, FontStyle.Bold);
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = AccentTextColor;
            SetPreferredHeight(txt.gameObject, 36f);
            SetMinHeight(txt.gameObject, 36f);
            return txt;
        }

        private void CreateSectionLabel(Transform parent, string text)
        {
            var txt = CreateText("SLabel_" + text, parent, text, 13, FontStyle.Normal);
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = MutedTextColor;
            SetPreferredHeight(txt.gameObject, 20f);
        }

        private void CreateDivider(Transform parent)
        {
            var d = CreatePanel("Divider", parent, DividerColor);
            SetPreferredHeight(d, 1f);
            SetMinHeight(d, 1f);
        }

        private void CreateSpacer(Transform parent, float height)
        {
            var s = new GameObject("Spacer");
            s.transform.SetParent(parent, false);
            var le = s.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
        }

        // Border panel: outer (border color) contains a background child (inner color, ignoreLayout).
        // Content is added to the outer; the LayoutGroup on outer skips the background child.
        private GameObject CreateBorderedPanel(string name, Transform parent,
            Color innerColor, Color borderColor)
        {
            var outer = new GameObject(name + "_Frame");
            outer.transform.SetParent(parent, false);
            outer.AddComponent<Image>().color = borderColor;

            var bg = new GameObject(name + "_Bg");
            bg.transform.SetParent(outer.transform, false);
            bg.AddComponent<Image>().color = innerColor;
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = new Vector2(1f, 1f);
            bgRect.offsetMax = new Vector2(-1f, -1f);
            bg.AddComponent<LayoutElement>().ignoreLayout = true;

            return outer;
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var p = new GameObject(name);
            p.transform.SetParent(parent, false);
            p.AddComponent<Image>().color = color;
            return p;
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

        private Button CreateButton(Transform parent, string label, Action onClick, float height = 44f)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = ButtonColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = BuildButtonColors();
            btn.onClick.AddListener(() =>
            {
                DurakAudioManager.PlaySfx(SfxKind.ButtonClick);
                onClick?.Invoke();
            });

            var txt = CreateText("Label", go.transform, label, 17, FontStyle.Normal);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = PrimaryTextColor;
            StretchRect(txt.rectTransform);

            SetPreferredHeight(go, height);
            return btn;
        }

        private static ColorBlock BuildButtonColors() => new ColorBlock
        {
            normalColor      = ButtonColor,
            highlightedColor = ButtonHighlightColor,
            pressedColor     = ButtonPressedColor,
            selectedColor    = ButtonHighlightColor,
            disabledColor    = ButtonDisabledColor,
            colorMultiplier  = 1f,
            fadeDuration     = 0.10f,
        };

        private static void SetButtonLabel(Button btn, string label)
        {
            if (btn == null) return;
            var txt = btn.GetComponentInChildren<Text>();
            if (txt != null) txt.text = label;
        }

        private void CopyJoinCodeToClipboard()
        {
            var code = _controller != null ? _controller.JoinCode : string.Empty;
            if (!string.IsNullOrWhiteSpace(code)) GUIUtility.systemCopyBuffer = code;
        }

        private InputField CreateInputField(Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.AddComponent<Image>().color = SurfaceRaisedColor;

            var input = root.AddComponent<InputField>();
            input.interactable = true;
            input.readOnly = false;
            input.lineType = InputField.LineType.SingleLine;

            var txt = CreateText("Text", root.transform, string.Empty, 17, FontStyle.Normal);
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = PrimaryTextColor;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            StretchRect(txt.rectTransform);
            txt.rectTransform.offsetMin = new Vector2(12f, 6f);
            txt.rectTransform.offsetMax = new Vector2(-12f, -6f);

            var ph = CreateText("Placeholder", root.transform, "JOIN CODE", 15, FontStyle.Italic);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.color = new Color(PrimaryTextColor.r, PrimaryTextColor.g, PrimaryTextColor.b, 0.38f);
            StretchRect(ph.rectTransform);
            ph.rectTransform.offsetMin = new Vector2(12f, 6f);
            ph.rectTransform.offsetMax = new Vector2(-12f, -6f);

            input.textComponent = txt;
            input.placeholder = ph;
            input.characterValidation = InputField.CharacterValidation.None;
            input.characterLimit = 64;
            SetPreferredHeight(root, 46f);
            return input;
        }

        // ── Layout Helpers ─────────────────────────────────────────────────────
        private static void ConfigureVerticalContainer(Transform t, float spacing, int padH, int padV)
        {
            var l = t.gameObject.AddComponent<VerticalLayoutGroup>();
            l.padding = new RectOffset(padH, padH, padV, padV);
            l.spacing = spacing;
            l.childControlWidth = true; l.childControlHeight = true;
            l.childForceExpandWidth = true; l.childForceExpandHeight = false;
        }

        private static void ConfigureHorizontalContainer(Transform t, float spacing, int padH, int padV)
        {
            var l = t.gameObject.AddComponent<HorizontalLayoutGroup>();
            l.padding = new RectOffset(padH, padH, padV, padV);
            l.spacing = spacing;
            l.childControlWidth = true; l.childControlHeight = true;
            l.childForceExpandWidth = true; l.childForceExpandHeight = true;
        }

        private static LayoutElement LE(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>();
            return le != null ? le : go.AddComponent<LayoutElement>();
        }

        private static void SetPreferredHeight(GameObject go, float v) => LE(go).preferredHeight = v;
        private static void SetMinHeight(GameObject go, float v)       => LE(go).minHeight = v;
        private static void SetPreferredWidth(GameObject go, float v)  => LE(go).preferredWidth = v;
        private static void SetMinWidth(GameObject go, float v)        => LE(go).minWidth = v;
        private static void SetFlexibleHeight(GameObject go, float v)  => LE(go).flexibleHeight = v;
        private static void SetFlexibleWidth(GameObject go, float v)   => LE(go).flexibleWidth = v;

        private static void StretchRect(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        private static void ClearChildren(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        private static string BuildVersionLabel() => BuildVersionProvider.GetDisplayVersion();
    }
}
