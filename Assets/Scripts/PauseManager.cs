using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ESC toggles the pause panel. Buttons:
///   Resume       — closes panel, restores pre-pause input state
///   Restart      — HOST ONLY: clears all cards/scores, sends everyone to Lobby
///   Settings     — shows the settings sub-panel (drag the GO into the slot)
///   Back to Menu — disconnects this client and loads MainMenu
///   Quit         — Application.Quit()
///
/// Hierarchy expected:
///   Pause (parent GO — PauseManager lives here or anywhere in scene)
///   ├── PausePanel     → wire to _pausePanel    (set inactive in scene)
///   └── SettingsPanel  → wire to _settingsPanel (set inactive in scene)
///       └── (sliders, back button — SettingsPanel component handles those)
/// </summary>
public class PauseManager : MonoBehaviour
{
    [Header("Panels — set both inactive in scene")]
    [SerializeField] GameObject _pausePanel;
    [SerializeField] GameObject _settingsPanel;

    [Header("Buttons")]
    [SerializeField] UnityEngine.UI.Button _resumeButton;
    [SerializeField] UnityEngine.UI.Button _restartButton;
    [SerializeField] UnityEngine.UI.Button _settingsButton;
    [SerializeField] UnityEngine.UI.Button _backToMenuButton;
    [SerializeField] UnityEngine.UI.Button _quitButton;

    bool _paused;
    bool _inputWasEnabled;

    PlayerController _localPC;
    PlayerCombat     _localCombat;

    void Start()
    {
        if (_pausePanel    != null) _pausePanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);

        // Tell the SettingsPanel component that its back button should just close itself
        if (_settingsPanel != null)
        {
            var sp = _settingsPanel.GetComponent<SettingsPanel>();
            if (sp != null) sp.OnClose = () => _settingsPanel.SetActive(false);
        }

        if (_resumeButton     != null) _resumeButton.onClick.AddListener(Resume);
        if (_restartButton    != null) _restartButton.onClick.AddListener(Restart);
        if (_settingsButton   != null) _settingsButton.onClick.AddListener(OpenSettings);
        if (_backToMenuButton != null) _backToMenuButton.onClick.AddListener(BackToMenu);
        if (_quitButton       != null) _quitButton.onClick.AddListener(QuitGame);

        // Restart only visible to the host
        if (_restartButton != null)
            _restartButton.gameObject.SetActive(
                NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost);
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        // First ESC closes settings if open, second ESC closes pause menu
        if (_settingsPanel != null && _settingsPanel.activeSelf)
        {
            _settingsPanel.SetActive(false);
            return;
        }

        if (_paused) Resume();
        else         Pause();
    }

    // ── Pause / Resume ────────────────────────────────────────────────────

    public void Pause()
    {
        if (_paused) return;
        _paused = true;

        CacheLocalPlayer();
        _inputWasEnabled = _localPC != null && _localPC.IsInputEnabled;

        if (_pausePanel != null) _pausePanel.SetActive(true);

        // Gate input locally (immediate) AND on the server (stops physics for this player only)
        _localPC?.SetInputEnabled(false);
        _localPC?.SetInputEnabledServerRpc(false);
        _localCombat?.SetShootingEnabled(false);
    }

    public void Resume()
    {
        if (!_paused) return;
        _paused = false;

        if (_pausePanel    != null) _pausePanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);

        if (_inputWasEnabled)
        {
            _localPC?.SetInputEnabled(true);
            _localPC?.SetInputEnabledServerRpc(true);
            _localCombat?.SetShootingEnabled(true);
        }
    }

    // ── Restart (host only) ───────────────────────────────────────────────

    void Restart()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        GameManager.Instance?.ResetForNewMatch();
        NetworkManager.Singleton.SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
    }

    // ── Settings ──────────────────────────────────────────────────────────

    void OpenSettings()
    {
        if (_settingsPanel != null) _settingsPanel.SetActive(true);
    }

    // ── Back to Menu ──────────────────────────────────────────────────────

    void BackToMenu()
    {
        _paused = false;
        if (_pausePanel != null) _pausePanel.SetActive(false);

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        SceneManager.LoadScene("MainMenu");
    }

    // ── Quit ──────────────────────────────────────────────────────────────

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    void CacheLocalPlayer()
    {
        if (_localPC != null) return;
        var localObj = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localObj == null) return;
        _localPC     = localObj.GetComponent<PlayerController>();
        _localCombat = localObj.GetComponent<PlayerCombat>();
    }
}
