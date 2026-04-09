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

    // ── Main Menu Buttons ────────────────────────────────────────────────

    [Header("Main Menu Buttons")]
    [SerializeField] Button _hostButton;
    [SerializeField] Button _joinButton;
    [SerializeField] Button _settingsButton;
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
    [SerializeField] Slider _musicVolumeSlider;
    [SerializeField] Slider _sfxVolumeSlider;
    [SerializeField] Button _settingsApplyButton;
    [SerializeField] Button _settingsBackButton;

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
        _settingsButton.onClick.AddListener(() => ShowPanel(_settingsPanel));
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

        // Settings panel
        if (_settingsBackButton  != null) _settingsBackButton.onClick.AddListener(() => ShowPanel(_mainMenuPanel));
        if (_settingsApplyButton != null) _settingsApplyButton.onClick.AddListener(() => ShowPanel(_mainMenuPanel));
        if (_musicVolumeSlider != null)
        {
            _musicVolumeSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetMusicVolume(v));
            AudioManager.Instance?.SetMusicVolume(_musicVolumeSlider.value);  // apply default (80) immediately
        }
        if (_sfxVolumeSlider != null)
        {
            _sfxVolumeSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetSFXVolume(v));
            AudioManager.Instance?.SetSFXVolume(_sfxVolumeSlider.value);      // apply default (80) immediately
        }

        // NetworkInitializer error event only — join code is handled in Lobby by NetworkLobbyManager
        _networkInitializer.OnNetworkError += OnNetworkError;
        _networkInitializer.OnConnecting   += OnConnecting;

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
        _settingsPanel.SetActive(_settingsPanel == target);
    }

    // ── Host handlers ─────────────────────────────────────────────────────

    void OnCreatePublicClicked()
    {
        SetHostButtonsInteractable(false);
        HideError();
        _networkInitializer.HostOnlineAsync();
        // Scene will change to Lobby — NetworkLobbyManager shows the relay code there
    }

    void OnCreateLocalClicked()
    {
        SetHostButtonsInteractable(false);
        HideError();
        _networkInitializer.HostLocalAsync();
        // Scene will change to Lobby — NetworkLobbyManager hides the code text for local games
    }

    void SetHostButtonsInteractable(bool interactable)
    {
        _createPublicButton.interactable = interactable;
        _createLocalButton.interactable  = interactable;
    }

    // ── Join handlers ─────────────────────────────────────────────────────

    void OnSubmitClicked()
    {
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
        string ip = _ipInputField != null ? _ipInputField.text.Trim() : string.Empty;
        if (!System.Net.IPAddress.TryParse(ip, out _)) { ShowError("Enter a valid IP address."); return; }
        HideError();
        _networkInitializer.JoinLocalAsync(ip);
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
