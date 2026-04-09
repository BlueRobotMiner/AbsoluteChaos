using UnityEngine;

/// <summary>
/// Singleton audio manager — persists across all scene loads.
/// Handles background music and one-shot SFX.
/// Wire AudioClips in the Inspector; AudioSource component required on this GameObject.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────

    public static AudioManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _musicSource = GetComponent<AudioSource>();
        _musicSource.loop = true;
    }

    // ── Inspector ────────────────────────────────────────────────────────

    [Header("Music")]
    [SerializeField] AudioClip _menuMusic;
    [SerializeField] AudioClip _gameMusic;
    [SerializeField] AudioClip _draftMusic;

    [Header("SFX")]
    [SerializeField] AudioClip _shootSFX;
    [SerializeField] AudioClip _hitSFX;
    [SerializeField] AudioClip _deathSFX;
    [SerializeField] AudioClip _jumpSFX;
    [SerializeField] AudioClip _roundEndSFX;
    [SerializeField] AudioClip _cardPickSFX;

    // Internal 0-1 values — sliders send 0-100, divided on receipt
    float _musicVolume = 0.8f;
    float _sfxVolume   = 0.8f;

    AudioSource _musicSource;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Start()
    {
        // Subscribe to game events as soon as GameManager is available
        if (GameManager.Instance != null)
            SubscribeToGameEvents();
    }

    void SubscribeToGameEvents()
    {
        GameManager.Instance.OnPlayerEliminated += _ => PlayDeathSFX();
        GameManager.Instance.OnRoundEnd         += _ => PlayRoundEndSFX();
    }

    // ── Music ─────────────────────────────────────────────────────────────

    public void PlayMenuMusic()  => SwitchMusic(_menuMusic);
    public void PlayGameMusic()  => SwitchMusic(_gameMusic);
    public void PlayDraftMusic() => SwitchMusic(_draftMusic);

    void SwitchMusic(AudioClip clip)
    {
        if (clip == null || _musicSource.clip == clip) return;
        _musicSource.clip   = clip;
        _musicSource.volume = _musicVolume;
        _musicSource.Play();
    }

    // ── SFX ───────────────────────────────────────────────────────────────

    public void PlayShootSFX()    => PlaySFX(_shootSFX);
    public void PlayHitSFX()      => PlaySFX(_hitSFX);
    public void PlayDeathSFX()    => PlaySFX(_deathSFX);
    public void PlayJumpSFX()     => PlaySFX(_jumpSFX);
    public void PlayRoundEndSFX() => PlaySFX(_roundEndSFX);
    public void PlayCardPickSFX() => PlaySFX(_cardPickSFX);

    void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, Camera.main ? Camera.main.transform.position : Vector3.zero, _sfxVolume);
    }

    // ── Volume controls (wired to Settings sliders in UIManager) ─────────

    /// <summary>Accepts 0-100 from the slider and converts to 0-1 for Unity audio.</summary>
    public void SetMusicVolume(float volume0to100)
    {
        _musicVolume        = Mathf.Clamp01(volume0to100 / 100f);
        _musicSource.volume = _musicVolume;
    }

    /// <summary>Accepts 0-100 from the slider and converts to 0-1 for Unity audio.</summary>
    public void SetSFXVolume(float volume0to100)
    {
        _sfxVolume = Mathf.Clamp01(volume0to100 / 100f);
    }
}
