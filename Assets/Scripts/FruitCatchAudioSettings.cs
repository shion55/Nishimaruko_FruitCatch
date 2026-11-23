using UnityEngine;

[CreateAssetMenu(fileName = "FruitCatchAudioSettings", menuName = "FruitCatch/Audio Settings")]
public sealed class FruitCatchAudioSettings : ScriptableObject
{
    [Header("Preparation")]
    [SerializeField] private AudioClip mainMenuBgm;
    [SerializeField, Range(0f, 1f)] private float mainMenuBgmVolume = 0.7f;
    [SerializeField] private AudioClip countdownSound;
    [SerializeField, Range(0f, 1f)] private float countdownSoundVolume = 1f;

    [Header("Transformation")]
    [SerializeField] private AudioClip transformationSound;
    [SerializeField, Range(0f, 1f)] private float transformationSoundVolume = 0.8f;
    [SerializeField] private AudioClip transformationCompleteSound;
    [SerializeField, Range(0f, 1f)] private float transformationCompleteSoundVolume = 1f;

    [Header("Gameplay")]
    [SerializeField] private AudioClip startSound;
    [SerializeField, Range(0f, 1f)] private float startSoundVolume = 1f;
    [SerializeField] private AudioClip finishSound;
    [SerializeField, Range(0f, 1f)] private float finishSoundVolume = 1f;
    [SerializeField] private AudioClip gameBgm;
    [SerializeField, Range(0f, 1f)] private float gameBgmVolume = 0.7f;
    [SerializeField, Min(0f)] private float gameBgmFadeOutDuration = 0.5f;
    [SerializeField] private AudioClip fruitBounceSound;
    [SerializeField, Range(0f, 1f)] private float fruitBounceSoundVolume = 1f;
    [SerializeField] private AudioClip fruitEatSound;
    [SerializeField, Range(0f, 1f)] private float fruitEatSoundVolume = 1f;

    public AudioClip MainMenuBgm => mainMenuBgm;
    public float MainMenuBgmVolume => mainMenuBgmVolume;
    public AudioClip CountdownSound => countdownSound;
    public float CountdownSoundVolume => countdownSoundVolume;
    public AudioClip TransformationSound => transformationSound;
    public float TransformationSoundVolume => transformationSoundVolume;
    public AudioClip TransformationCompleteSound => transformationCompleteSound;
    public float TransformationCompleteSoundVolume => transformationCompleteSoundVolume;
    public AudioClip StartSound => startSound;
    public float StartSoundVolume => startSoundVolume;
    public AudioClip FinishSound => finishSound;
    public float FinishSoundVolume => finishSoundVolume;
    public AudioClip GameBgm => gameBgm;
    public float GameBgmVolume => gameBgmVolume;
    public float GameBgmFadeOutDuration => gameBgmFadeOutDuration;
    public AudioClip FruitBounceSound => fruitBounceSound;
    public float FruitBounceSoundVolume => fruitBounceSoundVolume;
    public AudioClip FruitEatSound => fruitEatSound;
    public float FruitEatSoundVolume => fruitEatSoundVolume;
}
