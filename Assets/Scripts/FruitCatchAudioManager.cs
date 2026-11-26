using System.Collections;
using UnityEngine;

public sealed class FruitCatchAudioManager : MonoBehaviour
{
    private const string SettingsResourceName = "FruitCatchAudioSettings";
    private const double MusicScheduleLead = 0.05;
    private static FruitCatchAudioManager instance;

    private enum TimedMusic
    {
        None,
        Fever,
        EndingFever,
        Results
    }

    private FruitCatchAudioSettings settings;
    private AudioSource musicSource;
    private AudioSource timedMusicSource;
    private AudioSource transformationSource;
    private AudioSource soundEffectSource;
    private AudioSource chiliSwaySource;
    private AudioSource bananaSlideSource;
    private Coroutine musicFade;
    private TimedMusic timedMusic;
    private double timedMusicStart;
    private double timedMusicEnd;
    private float timedMusicVolume;
    private float timedMusicFadeDuration;
    private bool quietPreparationBgm;

    public static FruitCatchAudioManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<FruitCatchAudioManager>();
                if (instance == null)
                    instance = new GameObject("FruitCatchAudioManager").AddComponent<FruitCatchAudioManager>();
            }
            return instance;
        }
    }

    public static void StopGameAudioIfAvailable()
    {
        if (instance != null)
            instance.StopGameAudio();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateBeforeSceneLoad() => _ = Instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        settings = Resources.Load<FruitCatchAudioSettings>(SettingsResourceName);
        musicSource = CreateSource("Music");
        timedMusicSource = CreateSource("TimedMusic");
        transformationSource = CreateSource("Transformation");
        soundEffectSource = CreateSource("SoundEffects");
        chiliSwaySource = CreateSource("ChiliSway");
        chiliSwaySource.loop = true;
        bananaSlideSource = CreateSource("BananaSlide");

        if (settings == null)
            Debug.LogWarning("FruitCatchAudioSettings が Resources に見つかりません。");
        else
        {
            PreloadMusic(settings.FeverBgm);
            PreloadMusic(settings.ResultsBgm);
        }
    }

    public void PlayMainMenuBgm() => PlayMusic(settings?.MainMenuBgm, settings?.MainMenuBgmVolume ?? 0f, true);
    public void PlayCountdown() => PlayOneShot(settings?.CountdownSound, settings?.CountdownSoundVolume ?? 0f);
    public void PlayStart() => PlayOneShot(settings?.StartSound, settings?.StartSoundVolume ?? 0f);
    public void PlayFinish() => PlayOneShot(settings?.FinishSound, settings?.FinishSoundVolume ?? 0f);
    public void PlayFruitBounce() => PlayOneShot(settings?.FruitBounceSound, settings?.FruitBounceSoundVolume ?? 0f);
    public void PlayFruitEat() => PlayOneShot(settings?.FruitEatSound, settings?.FruitEatSoundVolume ?? 0f);
    public void PlayWallImpact() => PlayOneShot(settings?.WallImpactSound, settings?.WallImpactSoundVolume ?? 0f);
    public void PlayBugEat() => PlayOneShot(settings?.BugEatSound, settings?.BugEatSoundVolume ?? 0f);

    public void SetChiliSwayPlaying(bool swaying)
    {
        AudioClip clip = settings?.ChiliSwaySound;
        if (!swaying || clip == null)
        {
            if (chiliSwaySource.isPlaying)
                chiliSwaySource.Stop();
            return;
        }

        chiliSwaySource.volume = settings.ChiliSwaySoundVolume;
        chiliSwaySource.pitch = settings.ChiliSwaySoundPitch;
        if (chiliSwaySource.isPlaying && chiliSwaySource.clip == clip)
            return;
        chiliSwaySource.clip = clip;
        chiliSwaySource.Play();
    }

    public void PlayBananaSlide()
    {
        AudioClip clip = settings?.BananaSlideSound;
        if (clip == null)
            return;
        bananaSlideSource.clip = clip;
        bananaSlideSource.volume = settings.BananaSlideSoundVolume;
        bananaSlideSource.Play();
    }

    public void StopBananaSlide() => bananaSlideSource.Stop();

    public void StopItemEffectAudio()
    {
        chiliSwaySource.Stop();
        StopBananaSlide();
    }

    public void PlayPracticeSuccess(bool fruit)
    {
        if (fruit) PlayFruitEat();
        PlayOneShot(fruit ? settings?.FruitPracticeSuccess : settings?.MovementPracticeSuccess,
            settings?.PracticeSuccessVolume ?? 1f);
    }

    public void PlayQuietPreparationBgm(float volume, float duration)
    {
        if (settings == null || settings.MainMenuBgm == null) return;
        PlayMusic(settings.MainMenuBgm, 0f, true);
        quietPreparationBgm = true;
        FadeMusicTo(settings.MainMenuBgmVolume * volume, duration, stopAtEnd: false);
    }

    public void FadeOutPreparationBgm(float duration)
    {
        if (!quietPreparationBgm) return;
        quietPreparationBgm = false;
        FadeOutMusic(duration);
    }

    public void PlayTransformation()
    {
        if (settings?.TransformationSound == null)
            return;
        transformationSource.Stop();
        transformationSource.clip = settings.TransformationSound;
        transformationSource.volume = settings.TransformationSoundVolume;
        transformationSource.loop = false;
        transformationSource.Play();
    }

    public void PlayTransformationComplete()
    {
        transformationSource.Stop();
        PlayOneShot(settings?.TransformationCompleteSound, settings?.TransformationCompleteSoundVolume ?? 0f);
    }

    public void StopTransformation() => transformationSource.Stop();

    public void PlayGameBgm()
    {
        StopMusic();
        StopTimedMusic();
        PlayMusic(settings?.GameBgm, settings?.GameBgmVolume ?? 0f, false);
    }

    public void PlayFeverBgm()
    {
        if (timedMusic != TimedMusic.None || settings == null)
            return;
        if (PlayTimedMusic(TimedMusic.Fever, settings.FeverBgm, settings.FeverBgmVolume,
                settings.FeverBgmFadeOutDuration) > 0f)
            FadeMusicTo(0f, settings.GameBgmFadeOutDuration, stopAtEnd: false);
    }

    public bool TryCompleteFeverBgm()
    {
        if (timedMusic != TimedMusic.Fever || AudioSettings.dspTime < timedMusicEnd)
            return false;

        StopTimedMusic();
        if (musicSource.isPlaying)
            FadeMusicTo(settings.GameBgmVolume, settings.GameBgmResumeFadeInDuration, stopAtEnd: false);
        return true;
    }

    public float PlayResultsBgm()
    {
        StopMusic();
        StopTimedMusic();
        return PlayTimedMusic(TimedMusic.Results, settings?.ResultsBgm, settings?.ResultsBgmVolume ?? 0f, 0f);
    }

    public bool IsResultsBgmActive => timedMusic == TimedMusic.Results;
    public bool HasResultsBgmStarted => IsResultsBgmActive && AudioSettings.dspTime >= timedMusicStart;
    public bool HasResultsBgmEndedNaturally => IsResultsBgmActive && AudioSettings.dspTime >= timedMusicEnd;

    public void FadeOutMusic(float duration) => FadeMusicTo(0f, duration, stopAtEnd: true);

    public void FadeOutGameBgm()
    {
        FadeOutMusic(settings?.GameBgmFadeOutDuration ?? 0f);
        if (timedMusic != TimedMusic.Fever)
            return;

        double now = AudioSettings.dspTime;
        float duration = Mathf.Min(settings.FeverBgmInterruptedFadeOutDuration, (float)(timedMusicEnd - now));
        if (now < timedMusicStart || duration <= 0f)
        {
            StopTimedMusic();
            return;
        }

        // Changing the state invalidates natural completion before fading the remaining audio.
        timedMusic = TimedMusic.EndingFever;
        timedMusicVolume = timedMusicSource.volume;
        timedMusicFadeDuration = duration;
        timedMusicEnd = now + duration;
    }

    public void StopGameAudio()
    {
        StopMusic();
        StopTimedMusic();
        StopItemEffectAudio();
        soundEffectSource.Stop();
        transformationSource.Stop();
    }

    private static void PreloadMusic(AudioClip clip)
    {
        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
    }

    private float PlayTimedMusic(TimedMusic kind, AudioClip clip, float volume, float fadeOutDuration)
    {
        if (clip == null || clip.samples <= 0 || clip.frequency <= 0 ||
            clip.loadState == AudioDataLoadState.Failed ||
            (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData()))
            return 0f;

        double duration = clip.samples / (double)clip.frequency;
        timedMusic = kind;
        timedMusicStart = AudioSettings.dspTime + MusicScheduleLead;
        timedMusicEnd = timedMusicStart + duration;
        timedMusicVolume = volume;
        timedMusicFadeDuration = Mathf.Clamp(fadeOutDuration, 0f, (float)duration);

        timedMusicSource.clip = clip;
        timedMusicSource.loop = false;
        timedMusicSource.volume = volume;
        timedMusicSource.PlayScheduled(timedMusicStart);
        return (float)duration;
    }

    private void Update()
    {
        if (timedMusic != TimedMusic.Fever && timedMusic != TimedMusic.EndingFever)
            return;

        float remaining = (float)(timedMusicEnd - AudioSettings.dspTime);
        if (remaining <= 0f && timedMusic == TimedMusic.EndingFever)
        {
            StopTimedMusic();
            return;
        }

        if (timedMusicFadeDuration > 0f)
        {
            float gain = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remaining / timedMusicFadeDuration));
            timedMusicSource.volume = timedMusicVolume * gain;
        }
    }

    private void StopTimedMusic()
    {
        timedMusic = TimedMusic.None;
        timedMusicSource.Stop();
        timedMusicSource.clip = null;
    }

    private AudioSource CreateSource(string sourceName)
    {
        var child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);
        AudioSource source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip != null)
            soundEffectSource.PlayOneShot(clip, volume);
    }

    private void PlayMusic(AudioClip clip, float volume, bool loop)
    {
        quietPreparationBgm = false;
        if (clip == null)
            return;
        StopMusic();
        musicSource.clip = clip;
        musicSource.volume = volume;
        musicSource.loop = loop;
        musicSource.Play();
    }

    private void StopMusic()
    {
        StopMusicFade();
        musicSource.Stop();
        quietPreparationBgm = false;
    }

    private void FadeMusicTo(float volume, float duration, bool stopAtEnd)
    {
        StopMusicFade();
        if (!musicSource.isPlaying || duration <= 0f || Mathf.Approximately(musicSource.volume, volume))
        {
            musicSource.volume = volume;
            if (stopAtEnd)
                musicSource.Stop();
            return;
        }
        musicFade = StartCoroutine(FadeMusicRoutine(volume, duration, stopAtEnd));
    }

    private IEnumerator FadeMusicRoutine(float volume, float duration, bool stopAtEnd)
    {
        float initialVolume = musicSource.volume;
        float elapsed = 0f;
        while (elapsed < duration && musicSource.isPlaying)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(initialVolume, volume, Mathf.Clamp01(elapsed / duration));
        }
        if (stopAtEnd)
            musicSource.Stop();
        musicFade = null;
    }

    private void StopMusicFade()
    {
        if (musicFade == null)
            return;
        StopCoroutine(musicFade);
        musicFade = null;
    }
}
