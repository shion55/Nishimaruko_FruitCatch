using System.Collections;
using UnityEngine;

public sealed class FruitCatchAudioManager : MonoBehaviour
{
    private const string SettingsResourceName = "FruitCatchAudioSettings";
    private static FruitCatchAudioManager instance;

    private FruitCatchAudioSettings settings;
    private AudioSource musicSource;
    private AudioSource transformationSource;
    private AudioSource soundEffectSource;
    private Coroutine musicFade;
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
        transformationSource = CreateSource("Transformation");
        soundEffectSource = CreateSource("SoundEffects");

        if (settings == null)
            Debug.LogWarning("FruitCatchAudioSettings が Resources に見つかりません。");
    }

    public void PlayMainMenuBgm() => PlayMusic(settings?.MainMenuBgm, settings?.MainMenuBgmVolume ?? 0f, true);
    public void PlayCountdown() => PlayOneShot(settings?.CountdownSound, settings?.CountdownSoundVolume ?? 0f);
    public void PlayStart() => PlayOneShot(settings?.StartSound, settings?.StartSoundVolume ?? 0f);
    public void PlayFinish() => PlayOneShot(settings?.FinishSound, settings?.FinishSoundVolume ?? 0f);
    public void PlayFruitBounce() => PlayOneShot(settings?.FruitBounceSound, settings?.FruitBounceSoundVolume ?? 0f);
    public void PlayFruitEat() => PlayOneShot(settings?.FruitEatSound, settings?.FruitEatSoundVolume ?? 0f);
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
        musicFade = StartCoroutine(FadeInMusicRoutine(settings.MainMenuBgmVolume * volume, duration));
    }

    public void FadeOutPreparationBgm(float duration)
    {
        if (!quietPreparationBgm) return;
        quietPreparationBgm = false;
        FadeOutMusic(duration);
    }

    private IEnumerator FadeInMusicRoutine(float volume, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(0f, volume, elapsed / duration);
            yield return null;
        }
        musicSource.volume = volume;
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

    public void PlayGameBgm() => PlayMusic(settings?.GameBgm, settings?.GameBgmVolume ?? 0f, false);

    public void FadeOutMusic(float duration)
    {
        StopMusicFade();
        musicFade = StartCoroutine(FadeOutMusicRoutine(duration));
    }

    public void FadeOutGameBgm()
    {
        if (settings != null)
            FadeOutMusic(settings.GameBgmFadeOutDuration);
    }

    public void StopGameAudio()
    {
        if (settings != null && musicSource.clip == settings.GameBgm)
        {
            StopMusicFade();
            musicSource.Stop();
        }
        soundEffectSource.Stop();
        transformationSource.Stop();
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
        StopMusicFade();
        musicSource.Stop();
        musicSource.clip = clip;
        musicSource.volume = volume;
        musicSource.loop = loop;
        musicSource.Play();
    }

    private IEnumerator FadeOutMusicRoutine(float duration)
    {
        if (!musicSource.isPlaying)
            yield break;

        float initialVolume = musicSource.volume;
        float actualDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;
        while (elapsed < actualDuration && musicSource.isPlaying)
        {
            elapsed += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(initialVolume, 0f, Mathf.Clamp01(elapsed / actualDuration));
            yield return null;
        }
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
