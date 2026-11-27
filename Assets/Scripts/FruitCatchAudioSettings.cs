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
    [SerializeField] private AudioClip movementPracticeSuccess;
    [SerializeField] private AudioClip fruitPracticeSuccess;
    [SerializeField, Range(0f, 1f)] private float practiceSuccessVolume = 1f;

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

    [Header("Fever BGM")]
    [SerializeField] private AudioClip feverBgm;
    [SerializeField, Range(0f, 1f)] private float feverBgmVolume = 0.7f;
    [SerializeField, Min(0f)] private float feverBgmFadeOutDuration = 0.65f;
    [SerializeField, Min(0f)] private float feverBgmInterruptedFadeOutDuration = 0.25f;
    [SerializeField, Min(0f)] private float gameBgmResumeFadeInDuration = 0.5f;

    [Header("Results BGM")]
    [SerializeField] private AudioClip resultsBgm;
    [SerializeField, Range(0f, 1f)] private float resultsBgmVolume = 0.7f;

    [Header("Item Effects")]
    [SerializeField] private AudioClip wallImpactSound;
    [SerializeField, Range(0f, 1f)] private float wallImpactSoundVolume = 1f;
    [SerializeField] private AudioClip chiliSwaySound;
    [SerializeField, Range(0f, 1f)] private float chiliSwaySoundVolume = 1f;
    [SerializeField, Range(0.1f, 3f), Tooltip("揺れの周期に合わせるための再生速度。1は音源の元の速度。")]
    private float chiliSwaySoundPitch = 1f;
    [SerializeField] private AudioClip bananaSlideSound;
    [SerializeField, Range(0f, 1f)] private float bananaSlideSoundVolume = 1f;
    [SerializeField] private AudioClip bugEatSound;
    [SerializeField, Range(0f, 1f)] private float bugEatSoundVolume = 1f;

    [Header("Voice Feedback")]
    [SerializeField] private bool voiceFeedbackEnabled = true;
    [SerializeField, Range(0f, 1f)] private float voiceFeedbackVolume = 0.22f;
    [SerializeField, Range(0f, 1f)] private float voiceFeedbackDry = 0f;
    [SerializeField, Range(0f, 1f)] private float voiceFeedbackWet = 0.7f;
    [SerializeField, Range(0.1f, 2f)] private float voiceFeedbackReverbTime = 0.65f;

    public AudioClip MainMenuBgm => mainMenuBgm;
    public float MainMenuBgmVolume => mainMenuBgmVolume;
    public AudioClip CountdownSound => countdownSound;
    public float CountdownSoundVolume => countdownSoundVolume;
    public AudioClip TransformationSound => transformationSound;
    public float TransformationSoundVolume => transformationSoundVolume;
    public AudioClip TransformationCompleteSound => transformationCompleteSound;
    public float TransformationCompleteSoundVolume => transformationCompleteSoundVolume;
    public AudioClip MovementPracticeSuccess => movementPracticeSuccess != null ? movementPracticeSuccess : countdownSound;
    public AudioClip FruitPracticeSuccess => fruitPracticeSuccess;
    public float PracticeSuccessVolume => practiceSuccessVolume;
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
    public AudioClip FeverBgm => feverBgm;
    public float FeverBgmVolume => feverBgmVolume;
    public float FeverBgmFadeOutDuration => feverBgmFadeOutDuration;
    public float FeverBgmInterruptedFadeOutDuration => feverBgmInterruptedFadeOutDuration;
    public float GameBgmResumeFadeInDuration => gameBgmResumeFadeInDuration;
    public AudioClip ResultsBgm => resultsBgm;
    public float ResultsBgmVolume => resultsBgmVolume;
    public AudioClip WallImpactSound => wallImpactSound;
    public float WallImpactSoundVolume => wallImpactSoundVolume;
    public AudioClip ChiliSwaySound => chiliSwaySound;
    public float ChiliSwaySoundVolume => chiliSwaySoundVolume;
    public float ChiliSwaySoundPitch => chiliSwaySoundPitch;
    public AudioClip BananaSlideSound => bananaSlideSound;
    public float BananaSlideSoundVolume => bananaSlideSoundVolume;
    public AudioClip BugEatSound => bugEatSound;
    public float BugEatSoundVolume => bugEatSoundVolume;
    public bool VoiceFeedbackEnabled => voiceFeedbackEnabled;
    public float VoiceFeedbackVolume => voiceFeedbackVolume;
    public float VoiceFeedbackDry => voiceFeedbackDry;
    public float VoiceFeedbackWet => voiceFeedbackWet;
    public float VoiceFeedbackReverbTime => voiceFeedbackReverbTime;
}
