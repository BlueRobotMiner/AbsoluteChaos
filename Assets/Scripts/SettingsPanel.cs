using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Self-contained settings panel. Drop on any settings panel GO (main menu, pause).
/// Loads saved values on open, applies audio live as sliders move, saves on Apply/Back.
/// Set this GO inactive by default — caller shows/hides it.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [SerializeField] Slider _musicVolumeSlider;
    [SerializeField] Slider _sfxVolumeSlider;
    [SerializeField] Button _backButton;    // hides panel and saves
    [SerializeField] Button _applyButton;  // optional — same behaviour as back

    /// <summary>Called after the panel saves and hides itself. Set by the caller for navigation.</summary>
    public System.Action OnClose;

    void Start()
    {
        if (_musicVolumeSlider != null)
            _musicVolumeSlider.onValueChanged.AddListener(OnMusicChanged);

        if (_sfxVolumeSlider != null)
            _sfxVolumeSlider.onValueChanged.AddListener(OnSFXChanged);

        if (_backButton  != null) _backButton.onClick.AddListener(Close);
        if (_applyButton != null) _applyButton.onClick.AddListener(Close);
    }

    void OnEnable()
    {
        // Use saved data if available, otherwise fall back to defaults
        // (fallback matters when entering a map scene directly in the editor)
        var data = SettingsSaveManager.Instance?.Data ?? new SettingsSaveData();

        if (_musicVolumeSlider != null) _musicVolumeSlider.value = data.musicVolume;
        if (_sfxVolumeSlider   != null) _sfxVolumeSlider.value   = data.sfxVolume;
    }

    /// <summary>Save settings, hide the panel, and notify the caller.</summary>
    public void Close()
    {
        SaveSettings();
        gameObject.SetActive(false);
        OnClose?.Invoke();
    }

    // ── Slider callbacks — apply audio live, save on Close ───────────────

    void OnMusicChanged(float value)
    {
        AudioManager.Instance?.SetMusicVolume(value);
    }

    void OnSFXChanged(float value)
    {
        AudioManager.Instance?.SetSFXVolume(value);
    }

    void SaveSettings()
    {
        if (SettingsSaveManager.Instance == null) return;
        var data = SettingsSaveManager.Instance.Data;
        if (_musicVolumeSlider != null) data.musicVolume = _musicVolumeSlider.value;
        if (_sfxVolumeSlider   != null) data.sfxVolume   = _sfxVolumeSlider.value;
        SettingsSaveManager.Instance.Save();
    }
}
