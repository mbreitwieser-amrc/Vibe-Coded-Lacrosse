using UnityEngine;

/// <summary>
/// Simple AudioManager singleton. Uses a pool of AudioSources for 3D SFX.
/// Attach to a persistent GameObject in the scene. Unity 6000.x.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public enum SFXType
    {
        BallImpact,
        Pass,
        Shoot,
        Goal,
        FinalWhistle,
        Whistle,
        CrowdCheer,
        StickCheck
    }

    public static AudioManager Instance { get; private set; }

    [Header("Music")]
    public AudioClip backgroundMusic;
    [Range(0f, 1f)] public float musicVolume = 0.4f;

    [Header("SFX Clips")]
    public AudioClip sfxBallImpact;
    public AudioClip sfxPass;
    public AudioClip sfxShoot;
    public AudioClip sfxGoal;
    public AudioClip sfxFinalWhistle;
    public AudioClip sfxWhistle;
    public AudioClip sfxCrowdCheer;
    public AudioClip sfxStickCheck;

    [Header("Pool")]
    [Tooltip("Number of simultaneous SFX sources.")]
    public int sfxPoolSize = 8;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    private AudioSource   _musicSource;
    private AudioSource[] _sfxPool;
    private int           _poolIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPool();
    }

    private void Start()
    {
        if (backgroundMusic != null)
        {
            _musicSource.clip   = backgroundMusic;
            _musicSource.volume = musicVolume;
            _musicSource.Play();
        }
    }

    public void PlaySFX(SFXType type, Vector3 worldPos)
    {
        AudioClip clip = GetClip(type);
        if (clip == null) return;

        AudioSource src = _sfxPool[_poolIndex % sfxPoolSize];
        _poolIndex++;

        src.transform.position = worldPos;
        src.clip               = clip;
        src.volume             = sfxVolume;
        src.Play();
    }

    public void SetMusicVolume(float v)
    {
        musicVolume         = Mathf.Clamp01(v);
        _musicSource.volume = musicVolume;
    }

    public void SetSFXVolume(float v) => sfxVolume = Mathf.Clamp01(v);

    private void BuildPool()
    {
        _musicSource              = gameObject.AddComponent<AudioSource>();
        _musicSource.loop         = true;
        _musicSource.spatialBlend = 0f;

        _sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var go                = new GameObject($"SFXSource_{i}");
            go.transform.SetParent(transform);
            var src               = go.AddComponent<AudioSource>();
            src.spatialBlend      = 1f;
            src.rolloffMode       = AudioRolloffMode.Logarithmic;
            src.maxDistance       = 50f;
            _sfxPool[i]           = src;
        }
    }

    private AudioClip GetClip(SFXType type) => type switch
    {
        SFXType.BallImpact   => sfxBallImpact,
        SFXType.Pass         => sfxPass,
        SFXType.Shoot        => sfxShoot,
        SFXType.Goal         => sfxGoal,
        SFXType.FinalWhistle => sfxFinalWhistle,
        SFXType.Whistle      => sfxWhistle,
        SFXType.CrowdCheer   => sfxCrowdCheer,
        SFXType.StickCheck   => sfxStickCheck,
        _                    => null
    };
}
