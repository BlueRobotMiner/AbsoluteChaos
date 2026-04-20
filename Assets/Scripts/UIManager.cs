using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the MainMenu scene UI. Shows/hides panels and wires buttons to NetworkInitializer.
/// Join code and local IP are displayed in the Lobby scene by NetworkLobbyManager —
/// this script is destroyed when the scene changes so it never shows that info.
/// </summary>
public class UIManager : MonoBehaviour
{
    // ── Panels ───────────────────────────────────────────────────────────

    [Header("Panels")]
    [SerializeField] GameObject _mainMenuPanel;
    [SerializeField] GameObject _hostPanel;
    [SerializeField] GameObject _joinPanel;
    [SerializeField] GameObject _settingsPanel;
    [SerializeField] GameObject _characterPanel;

    // ── Main Menu Buttons ────────────────────────────────────────────────

    [Header("Main Menu Buttons")]
    [SerializeField] Button _hostButton;
    [SerializeField] Button _joinButton;
    [SerializeField] Button _settingsButton;
    [SerializeField] Button _characterButton;
    [SerializeField] Button _quitButton;

    // ── Host Panel ───────────────────────────────────────────────────────

    [Header("Host Panel")]
    [SerializeField] Button _createPublicButton;  // "Create public" — Unity Relay
    [SerializeField] Button _createLocalButton;   // "Create Local"  — Direct IP
    [SerializeField] Button _hostBackButton;

    // ── Join Panel ───────────────────────────────────────────────────────

    [Header("Join Panel")]
    [SerializeField] TMP_InputField _codeInputField;  // "Code text Field" — relay join code
    [SerializeField] TMP_InputField _ipInputField;    // IP text field — auto-filled with local IP
    [SerializeField] Button         _submitButton;    // "Submit"     → join by relay code
    [SerializeField] Button         _joinLocalButton; // "Join Local" → join by IP
    [SerializeField] Button         _joinBackButton;

    // ── Settings Panel ───────────────────────────────────────────────────

    [Header("Settings Panel")]
    [SerializeField] SettingsPanel _settingsPanelComponent;

    // ── Character Panel ───────────────────────────────────────────────────

    [Header("Character Panel")]
    [SerializeField] Button         _characterBackButton;
    [SerializeField] TMP_InputField _nameInputField;

    [Header("Head Cycle Selector")]
    [SerializeField] Button  _headLeftButton;    // ← arrow
    [SerializeField] Button  _headRightButton;   // → arrow
    [SerializeField] Image   _headPreviewImage;  // center — sprite changes per head type
    [SerializeField] Sprite[] _headTypeSprites;  // one sprite per head type (0=circle etc.)

    [Header("Color Cycle Selector")]
    [SerializeField] Button  _colorLeftButton;   // ← arrow
    [SerializeField] Button  _colorRightButton;  // → arrow
    [SerializeField] Image   _colorPreviewImage; // center — Image color tinted to current preset
    [SerializeField] Color[] _presetColors;      // list of colors to cycle through

    // Runtime indices — tracked separately from save data so arrows feel instant
    int _currentHeadIndex;
    int _currentColorIndex;

    // ── Error display ─────────────────────────────────────────────────────

    [Header("Error")]
    [SerializeField] TMP_Text _errorText;

    // ── Dependency ────────────────────────────────────────────────────────

    [Header("Network")]
    [SerializeField] NetworkInitializer _networkInitializer;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Start()
    {
        // Main menu navigation
        _hostButton.onClick.AddListener(()     => ShowPanel(_hostPanel));
        _joinButton.onClick.AddListener(OnJoinPanelOpened);
        _settingsButton.onClick.AddListener(OpenSettings);
        if (_characterButton != null)
            _characterButton.onClick.AddListener(OnCharacterPanelOpened);
        _quitButton.onClick.AddListener(Application.Quit);

        // Host panel
        _createPublicButton.onClick.AddListener(OnCreatePublicClicked);
        _createLocalButton.onClick.AddListener(OnCreateLocalClicked);
        if (_hostBackButton != null)
            _hostBackButton.onClick.AddListener(() => ShowPanel(_mainMenuPanel));

        // Join panel
        _submitButton.onClick.AddListener(OnSubmitClicked);
        _joinLocalButton.onClick.AddListener(OnJoinLocalClicked);
        if (_joinBackButton != null)
            _joinBackButton.onClick.AddListener(() => ShowPanel(_mainMenuPanel));

        // Settings panel — logic lives in SettingsPanel component; just wire the OnClose callback
        if (_settingsPanelComponent != null)
            _settingsPanelComponent.OnClose = () => ShowPanel(_mainMenuPanel);

        // Prefer the persistent singleton over the scene-serialized reference —
        // the scene's NetworkManager GO may be destroyed by NGO if one already exists.
        if (_networkInitializer == null || _networkInitializer.gameObject == null)
            _networkInitializer = NetworkInitializer.Instance;

        // NetworkInitializer error event only — join code is handled in Lobby by NetworkLobbyManager
        if (_networkInitializer != null)
        {
            _networkInitializer.OnNetworkError += OnNetworkError;
            _networkInitializer.OnConnecting   += OnConnecting;
        }

        ShowPanel(_mainMenuPanel);
        HideError();

        AudioManager.Instance?.PlayMenuMusic();
    }

    void OnDestroy()
    {
        if (_networkInitializer == null) return;
        _networkInitializer.OnNetworkError -= OnNetworkError;
        _networkInitializer.OnConnecting   -= OnConnecting;
    }

    // ── Panel navigation ──────────────────────────────────────────────────

    void ShowPanel(GameObject target)
    {
        _mainMenuPanel.SetActive(_mainMenuPanel == target);
        _hostPanel.SetActive(_hostPanel         == target);
        _joinPanel.SetActive(_joinPanel         == target);
        if (_settingsPanel  != null) _settingsPanel.SetActive(_settingsPanel   == target);
        if (_characterPanel != null) _characterPanel.SetActive(_characterPanel == target);
    }

    void OpenSettings()
    {
        // Hide all regular panels, then let SettingsPanel show itself
        ShowPanel(null);
        if (_settingsPanelComponent != null)
            _settingsPanelComponent.gameObject.SetActive(true);
    }

    // ── Host handlers ─────────────────────────────────────────────────────

    void OnCreatePublicClicked()
    {
        if (_networkInitializer == null) { ShowError("Network not ready."); return; }
        SetHostButtonsInteractable(false);
        HideError();
        _networkInitializer.HostOnlineAsync();
    }

    void OnCreateLocalClicked()
    {
        if (_networkInitializer == null) { ShowError("Network not ready."); return; }
        SetHostButtonsInteractable(false);
        HideError();
        _networkInitializer.HostLocalAsync();
    }

    void SetHostButtonsInteractable(bool interactable)
    {
        _createPublicButton.interactable = interactable;
        _createLocalButton.interactable  = interactable;
    }

    // ── Join handlers ─────────────────────────────────────────────────────

    void OnSubmitClicked()
    {
        if (_networkInitializer == null) { ShowError("Network not ready."); return; }
        string code = _codeInputField.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(code)) { ShowError("Please enter a join code."); return; }
        HideError();
        _networkInitializer.JoinOnlineAsync(code);
    }

    void OnJoinPanelOpened()
    {
        // Pre-fill the IP field with this machine's local IP.
        // For same-machine testing it works as-is; for cross-machine LAN the player edits it.
        if (_ipInputField != null)
            _ipInputField.text = NetworkInitializer.GetLocalIP();
        ShowPanel(_joinPanel);
    }

    void OnJoinLocalClicked()
    {
        if (_networkInitializer == null) { ShowError("Network not ready."); return; }
        string ip = _ipInputField != null ? _ipInputField.text.Trim() : string.Empty;
        if (!System.Net.IPAddress.TryParse(ip, out _)) { ShowError("Enter a valid IP address."); return; }
        HideError();
        _networkInitializer.JoinLocalAsync(ip);
    }

    // ── Character panel handlers ──────────────────────────────────────────

    void OnCharacterPanelOpened()
    {
        var data = SaveLoadManager.Instance?.Data ?? new PlayerSaveData();

        // Restore saved indices
        _currentHeadIndex  = data.headTypeIndex;
        _currentColorIndex = ColorIndexFromSave(data);

        // Populate name field
        if (_nameInputField != null)
        {
            _nameInputField.text = data.playerName;
            _nameInputField.onEndEdit.RemoveListener(OnNameEdited);
            _nameInputField.onEndEdit.AddListener(OnNameEdited);
        }

        // Wire arrows — RemoveAllListeners prevents double-registration on repeated opens
        if (_headLeftButton  != null) { _headLeftButton.onClick.RemoveAllListeners();  _headLeftButton.onClick.AddListener(OnHeadLeft);   }
        if (_headRightButton != null) { _headRightButton.onClick.RemoveAllListeners(); _headRightButton.onClick.AddListener(OnHeadRight);  }
        if (_colorLeftButton  != null) { _colorLeftButton.onClick.RemoveAllListeners();  _colorLeftButton.onClick.AddListener(OnColorLeft);  }
        if (_colorRightButton != null) { _colorRightButton.onClick.RemoveAllListeners(); _colorRightButton.onClick.AddListener(OnColorRight); }

        if (_characterBackButton != null)
        {
            _characterBackButton.onClick.RemoveListener(OnCharacterPanelClosed);
            _characterBackButton.onClick.AddListener(OnCharacterPanelClosed);
        }

        // Refresh preview displays
        RefreshHeadPreview();
        RefreshColorPreview();

        ShowPanel(_characterPanel);
    }

    void OnCharacterPanelClosed()
    {
        // Flush all current selections to disk in one save
        if (SaveLoadManager.Instance != null)
        {
            var data = SaveLoadManager.Instance.Data;
            if (_nameInputField != null)
                data.playerName = _nameInputField.text;
            data.headTypeIndex = _currentHeadIndex;
            if (_presetColors != null && _currentColorIndex >= 0 && _currentColorIndex < _presetColors.Length)
                data.FromColor(_presetColors[_currentColorIndex]);
            SaveLoadManager.Instance.Save();
        }
        ShowPanel(_mainMenuPanel);
    }

    // ── Head cycle ────────────────────────────────────────────────────────

    void OnHeadLeft()
    {
        int count = _headTypeSprites != null ? _headTypeSprites.Length : 1;
        _currentHeadIndex = (_currentHeadIndex - 1 + count) % count;
        SaveHeadIndex();
        RefreshHeadPreview();
    }

    void OnHeadRight()
    {
        int count = _headTypeSprites != null ? _headTypeSprites.Length : 1;
        _currentHeadIndex = (_currentHeadIndex + 1) % count;
        SaveHeadIndex();
        RefreshHeadPreview();
    }

    void RefreshHeadPreview()
    {
        if (_headPreviewImage == null || _headTypeSprites == null) return;
        if (_currentHeadIndex >= 0 && _currentHeadIndex < _headTypeSprites.Length)
        {
            _headPreviewImage.sprite = _headTypeSprites[_currentHeadIndex];
            _headPreviewImage.color  = Color.white;   // always show the sprite unaltered
        }
    }

    void SaveHeadIndex()
    {
        if (SaveLoadManager.Instance == null) return;
        SaveLoadManager.Instance.Data.headTypeIndex = _currentHeadIndex;
        SaveLoadManager.Instance.Save();
    }

    // ── Color cycle ───────────────────────────────────────────────────────

    void OnColorLeft()
    {
        int count = _presetColors != null ? _presetColors.Length : 1;
        _currentColorIndex = (_currentColorIndex - 1 + count) % count;
        SaveColorIndex();
        RefreshColorPreview();
    }

    void OnColorRight()
    {
        int count = _presetColors != null ? _presetColors.Length : 1;
        _currentColorIndex = (_currentColorIndex + 1) % count;
        SaveColorIndex();
        RefreshColorPreview();
    }

    void RefreshColorPreview()
    {
        if (_colorPreviewImage == null || _presetColors == null) return;
        if (_currentColorIndex < 0 || _currentColorIndex >= _presetColors.Length) return;
        Color c = _presetColors[_currentColorIndex];
        c.a = 1f;   // force fully opaque — Unity defaults new Color array entries to alpha 0
        _colorPreviewImage.color = c;
    }

    void SaveColorIndex()
    {
        if (SaveLoadManager.Instance == null || _presetColors == null) return;
        SaveLoadManager.Instance.Data.FromColor(_presetColors[_currentColorIndex]);
        SaveLoadManager.Instance.Save();
    }

    /// <summary>Finds which preset color index best matches the saved color (exact match first, fallback to 0).</summary>
    int ColorIndexFromSave(PlayerSaveData data)
    {
        if (_presetColors == null) return 0;
        Color saved = data.ToColor();
        for (int i = 0; i < _presetColors.Length; i++)
            if (Mathf.Approximately(_presetColors[i].r, saved.r) &&
                Mathf.Approximately(_presetColors[i].g, saved.g) &&
                Mathf.Approximately(_presetColors[i].b, saved.b))
                return i;
        return 0;
    }

    // ── Name ──────────────────────────────────────────────────────────────

    void OnNameEdited(string value)
    {
        if (SaveLoadManager.Instance == null) return;
        SaveLoadManager.Instance.Data.playerName = value;
        SaveLoadManager.Instance.Save();
    }

    // ── NetworkInitializer event handlers ─────────────────────────────────

    void OnConnecting()
    {
        HideError();
    }

    void OnNetworkError(string message)
    {
        ShowPanel(_mainMenuPanel);
        SetHostButtonsInteractable(true);
        ShowError(message);
    }

    // ── Error helpers ─────────────────────────────────────────────────────

    void ShowError(string message)
    {
        if (_errorText == null) { Debug.LogWarning($"[UIManager] {message}"); return; }
        _errorText.text = message;
        _errorText.gameObject.SetActive(true);
    }

    void HideError()
    {
        if (_errorText != null)
            _errorText.gameObject.SetActive(false);
    }
}
