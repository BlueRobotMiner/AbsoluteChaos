using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    [SerializeField] AudioClip   _menuMusic;
    [SerializeField] AudioClip   _draftMusic;
    [SerializeField] AudioClip[] _mapMusicTracks;   // randomly cycled during map rounds

    [Header("SFX — Combat")]
    [SerializeField] AudioClip _shootSFX;
    [SerializeField] AudioClip _hitSFX;
    [SerializeField] AudioClip _deathSFX;
    [SerializeField] AudioClip _punchSFX;
    [SerializeField] AudioClip _ricochetSFX;

    [Header("SFX — Movement")]
    [SerializeField] AudioClip _jumpSFX;
    [SerializeField] AudioClip _landSFX;

    [Header("SFX — Items")]
    [SerializeField] AudioClip _gunPickupSFX;
    [SerializeField] AudioClip _gunThrowSFX;
    [SerializeField] AudioClip _healthPickupSFX;

    [Header("SFX — UI / Round")]
    [SerializeField] AudioClip _roundEndSFX;
    [SerializeField] AudioClip _victorySFX;
    [SerializeField] AudioClip _countdownSFX;
    [SerializeField] AudioClip _cardPickSFX;

    // Internal 0-1 values — sliders send 0-100, divided on receipt
    float _musicVolume = 0.8f;
    float _sfxVolume   = 0.8f;

    AudioSource _musicSource;
    Coroutine   _musicCycleCoroutine;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Start()
    {
        // Apply saved volumes — AudioManager owns this so timing is always correct
        var settings = SettingsSaveManager.Instance.Data;
        SetMusicVolume(settings.musicVolume);
        SetSFXVolume(settings.sfxVolume);

        if (GameManager.Instance != null)
            SubscribeToGameEvents();

        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        switch (scene.name)
        {
            case "MainMenu":
            case "Lobby":
                PlayMenuMusic();
                break;
            case "CardDraft":
                PlayDraftMusic();
                break;
            default:
                // Any map scene — starts a random cycling coroutine
                PlayMapMusic();
                break;
        }
    }

    void SubscribeToGameEvents()
    {
        GameManager.Instance.OnPlayerEliminated += _ => PlayDeathSFX();
        GameManager.Instance.OnRoundEnd         += _ => PlayRoundEndSFX();
    }

    // ── Music ─────────────────────────────────────────────────────────────

    public void PlayMenuMusic()  { StopMusicCycle(); SwitchMusic(_menuMusic); }
    public void PlayDraftMusic() { StopMusicCycle(); SwitchMusic(_draftMusic); }
    public void PlayMapMusic()
    {
        StopMusicCycle();
        if (_mapMusicTracks == null || _mapMusicTracks.Length == 0) return;
        _musicCycleCoroutine = StartCoroutine(CycleMapMusic());
    }

    IEnumerator CycleMapMusic()
    {
        int lastIndex = -1;
        while (true)
        {
            int index;
            do { index = UnityEngine.Random.Range(0, _mapMusicTracks.Length); }
            while (_mapMusicTracks.Length > 1 && index == lastIndex);
            lastIndex = index;

            var clip = _mapMusicTracks[index];
            SwitchMusic(clip);

            yield return new WaitForSeconds(clip != null ? clip.length : 60f);
        }
    }

    void StopMusicCycle()
    {
        if (_musicCycleCoroutine != null) { StopCoroutine(_musicCycleCoroutine); _musicCycleCoroutine = null; }
    }

    void SwitchMusic(AudioClip clip)
    {
        if (clip == null || _musicSource.clip == clip) return;
        _musicSource.clip   = clip;
        _musicSource.volume = _musicVolume;
        _musicSource.Play();
    }

    // ── SFX ───────────────────────────────────────────────────────────────

    public void PlayShootSFX()        => PlaySFX(_shootSFX);
    public void PlayHitSFX()          => PlaySFX(_hitSFX);
    public void PlayDeathSFX()        => PlaySFX(_deathSFX);
    public void PlayPunchSFX()        => PlaySFX(_punchSFX);
    public void PlayRicochetSFX()     => PlaySFX(_ricochetSFX);
    public void PlayJumpSFX()         => PlaySFX(_jumpSFX);
    public void PlayLandSFX()         => PlaySFX(_landSFX);
    public void PlayGunPickupSFX()    => PlaySFX(_gunPickupSFX);
    public void PlayGunThrowSFX()     => PlaySFX(_gunThrowSFX);
    public void PlayHealthPickupSFX() => PlaySFX(_healthPickupSFX);
    public void PlayRoundEndSFX()     => PlaySFX(_roundEndSFX);
    public void PlayVictorySFX()      => PlaySFX(_victorySFX);
    public void PlayCountdownSFX()    => PlaySFX(_countdownSFX);
    public void PlayCardPickSFX()     => PlaySFX(_cardPickSFX);

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
