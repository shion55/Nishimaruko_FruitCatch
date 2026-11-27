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
    private AudioSource previewSource;
    private System.Func<float> previewVolume;
    private float musicGain;
    private float interruptedGain;
    private bool mainMenuMusic;
    private bool fixedFeverMusic;
    private MicrophoneVoiceFeedback voiceFeedback;
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

    internal static void ClearVoiceFeedbackInputIfAvailable(MicrophoneAnalysis input)
    {
        if (instance != null)
            instance.voiceFeedback?.ClearInput(input);
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
        var asset = Resources.Load<FruitCatchAudioSettings>(SettingsResourceName);
        settings = asset == null ? null : Instantiate(asset);
        FruitCatchOperatorSettings.BindAudio(settings);
        musicSource = CreateSource("Music");
        timedMusicSource = CreateSource("TimedMusic");
        transformationSource = CreateSource("Transformation");
        soundEffectSource = CreateSource("SoundEffects");
        chiliSwaySource = CreateSource("ChiliSway");
        chiliSwaySource.loop = true;
        bananaSlideSource = CreateSource("BananaSlide");
        previewSource = CreateSource("OperatorPreview");
        voiceFeedback = MicrophoneVoiceFeedback.Attach(CreateSource("VoiceFeedback"));

        if (settings == null)
            Debug.LogWarning("FruitCatchAudioSettings が Resources に見つかりません。");
        else
        {
            PreloadMusic(settings.FeverBgm);
            PreloadMusic(settings.ResultsBgm);
        }
    }

    public void PlayMainMenuBgm() => PlayMusic(settings?.MainMenuBgm, 1f, true);
    public void PlayCountdown() => PlayOneShot(settings?.CountdownSound, settings?.CountdownSoundVolume ?? 0f);
    public void PlayStart() => PlayOneShot(settings?.StartSound, settings?.StartSoundVolume ?? 0f);
    public void PlayFinish() => PlayOneShot(settings?.FinishSound, settings?.FinishSoundVolume ?? 0f);
    public void PlayFruitBounce() => PlayOneShot(settings?.FruitBounceSound, settings?.FruitBounceSoundVolume ?? 0f);
    public void PlayFruitEat() => PlayOneShot(settings?.FruitEatSound, settings?.FruitEatSoundVolume ?? 0f);
    public void PlayWallImpact() => PlayOneShot(settings?.WallImpactSound, settings?.WallImpactSoundVolume ?? 0f);
    public void PlayBugEat() => PlayOneShot(settings?.BugEatSound, settings?.BugEatSoundVolume ?? 0f);

    internal void SetVoiceFeedbackInput(MicrophoneAnalysis input) => voiceFeedback.SetInput(input);

    public void SetChiliSwayPlaying(bool swaying)
    {
        AudioClip clip = settings?.ChiliSwaySound;
        if (!swaying || clip == null)
        {
            if (chiliSwaySource.isPlaying)
                chiliSwaySource.Stop();
            return;
        }

        chiliSwaySource.volume = settings.ChiliSwaySoundVolume * SeGain;
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
        bananaSlideSource.volume = settings.BananaSlideSoundVolume * SeGain;
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
            fruit ? FruitCatchOperatorSettings.Get("audio.fruitPracticeSuccessVolume") : settings?.PracticeSuccessVolume ?? 1f);
    }

    public void PlayQuietPreparationBgm(float volume, float duration)
    {
        if (settings == null || settings.MainMenuBgm == null) return;
        PlayMusic(settings.MainMenuBgm, 0f, true);
        quietPreparationBgm = true;
        FadeMusicTo(volume, duration, stopAtEnd: false);
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
        transformationSource.volume = settings.TransformationSoundVolume * SeGain;
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
        PlayMusic(settings?.GameBgm, 1f, false);
    }

    public void PlayFeverBgm(float fixedDuration = 0f)
    {
        if (timedMusic == TimedMusic.EndingFever) StopTimedMusic();
        if (timedMusic != TimedMusic.None || settings == null) return;
        if (PlayTimedMusic(TimedMusic.Fever, settings.FeverBgm, settings.FeverBgmVolume,
                settings.FeverBgmFadeOutDuration) > 0f)
        {
            fixedFeverMusic = fixedDuration > 0f;
            if (fixedFeverMusic && timedMusicStart + fixedDuration < timedMusicEnd)
            {
                timedMusicEnd = timedMusicStart + fixedDuration;
                timedMusicSource.SetScheduledEndTime(timedMusicEnd);
            }
            FadeMusicTo(0f, settings.GameBgmFadeOutDuration, stopAtEnd: false);
        }
    }

    public bool TryCompleteFeverBgm()
    {
        if (timedMusic != TimedMusic.Fever || fixedFeverMusic || AudioSettings.dspTime < timedMusicEnd)
            return false;

        StopTimedMusic();
        if (musicSource.isPlaying)
            FadeMusicTo(1f, settings.GameBgmResumeFadeInDuration, stopAtEnd: false);
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
        InterruptFever(resumeGameMusic: false);
    }

    internal void InterruptFever(bool resumeGameMusic)
    {
        if (resumeGameMusic && musicSource.isPlaying)
            FadeMusicTo(1f, settings?.GameBgmResumeFadeInDuration ?? 0f, stopAtEnd: false);
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
        interruptedGain = TimedFeverGain(now);
        timedMusicFadeDuration = duration;
        timedMusicEnd = now + duration;
    }

    public void StopGameAudio()
    {
        voiceFeedback.SetInput(null);
        StopMusic();
        StopTimedMusic();
        StopItemEffectAudio();
        soundEffectSource.Stop();
        transformationSource.Stop();
        StopPreview();
    }

    private static float BgmGain => FruitCatchOperatorSettings.Get("audio.bgm");
    private static float SeGain => FruitCatchOperatorSettings.Get("audio.se");

    internal void Preview(AudioClip clip, System.Func<float> volume, float pitch, bool bgm)
    {
        StopPreview();
        if (clip == null) return;
        previewVolume = () => volume() * (bgm ? BgmGain : SeGain);
        previewSource.clip = clip;
        previewSource.volume = previewVolume();
        previewSource.pitch = pitch;
        previewSource.Play();
    }

    internal void StopPreview()
    {
        previewVolume = null;
        previewSource.Stop();
        previewSource.clip = null;
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
        timedMusicSource.volume = volume * BgmGain;
        timedMusicSource.PlayScheduled(timedMusicStart);
        return (float)duration;
    }

    private void Update()
    {
        voiceFeedback?.Refresh(settings);
        ApplyMusicGain();
        if (previewVolume != null) previewSource.volume = previewVolume();
        soundEffectSource.volume = SeGain;
        if (settings != null)
        {
            chiliSwaySource.volume = settings.ChiliSwaySoundVolume * SeGain;
            transformationSource.volume = settings.TransformationSoundVolume * SeGain;
            bananaSlideSource.volume = settings.BananaSlideSoundVolume * SeGain;
        }
        if (timedMusic == TimedMusic.Results)
            timedMusicSource.volume = (settings?.ResultsBgmVolume ?? timedMusicVolume) * BgmGain;
        if (timedMusic != TimedMusic.Fever && timedMusic != TimedMusic.EndingFever)
            return;

        float remaining = (float)(timedMusicEnd - AudioSettings.dspTime);
        if (remaining <= 0f && timedMusic == TimedMusic.EndingFever)
        {
            StopTimedMusic();
            return;
        }

        timedMusicSource.volume = (settings?.FeverBgmVolume ?? timedMusicVolume) *
            BgmGain * TimedFeverGain(AudioSettings.dspTime) *
            (timedMusic == TimedMusic.EndingFever ? interruptedGain : 1f);
    }

    private float TimedFeverGain(double now) =>
        timedMusicFadeDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((float)(timedMusicEnd - now) / timedMusicFadeDuration));

    private void StopTimedMusic()
    {
        timedMusic = TimedMusic.None;
        fixedFeverMusic = false;
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
        {
            soundEffectSource.volume = SeGain;
            soundEffectSource.PlayOneShot(clip, volume);
        }
    }

    private void PlayMusic(AudioClip clip, float volume, bool loop)
    {
        quietPreparationBgm = false;
        if (clip == null)
            return;
        StopMusic();
        musicSource.clip = clip;
        mainMenuMusic = loop;
        musicGain = volume;
        ApplyMusicGain();
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
        if (!musicSource.isPlaying || duration <= 0f || Mathf.Approximately(musicGain, volume))
        {
            musicGain = volume;
            ApplyMusicGain();
            if (stopAtEnd)
                musicSource.Stop();
            return;
        }
        musicFade = StartCoroutine(FadeMusicRoutine(volume, duration, stopAtEnd));
    }

    private IEnumerator FadeMusicRoutine(float volume, float duration, bool stopAtEnd)
    {
        float initialVolume = musicGain;
        float elapsed = 0f;
        while (elapsed < duration && musicSource.isPlaying)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            musicGain = Mathf.Lerp(initialVolume, volume, Mathf.Clamp01(elapsed / duration));
            ApplyMusicGain();
        }
        if (stopAtEnd)
            musicSource.Stop();
        musicFade = null;
    }

    private void ApplyMusicGain()
    {
        float baseVolume = mainMenuMusic ? settings?.MainMenuBgmVolume ?? 0f : settings?.GameBgmVolume ?? 0f;
        musicSource.volume = baseVolume * BgmGain * musicGain;
    }

    private void StopMusicFade()
    {
        if (musicFade == null)
            return;
        StopCoroutine(musicFade);
        musicFade = null;
    }
}
