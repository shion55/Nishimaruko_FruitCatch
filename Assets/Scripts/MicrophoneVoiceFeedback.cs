using System;
using UnityEngine;

[DisallowMultipleComponent]
internal sealed class MicrophoneVoiceFeedback : MonoBehaviour
{
    private const float MinimumLagSeconds = 0.055f;
    private const float OutputKnee = 0.10f;
    private const float OutputCeiling = 0.18f;

    private AudioSource source;
    private AudioReverbFilter reverb;
    private MicrophoneAnalysis input;
    private float targetLagSeconds;
    private double nextCursorCheck;

    // Only these parameters cross to the DSP thread. The limiter never calls Unity APIs there.
    private volatile float targetGain;
    private volatile float gainSmoothing;
    private volatile int playbackVersion;
    private float dspGain;
    private int dspPlaybackVersion;

    internal static MicrophoneVoiceFeedback Attach(AudioSource source)
    {
        source.loop = true;
        source.mute = true;
        source.reverbZoneMix = 0f;

        // Filter component order: remove low rumble, add reverb, then limit the feedback output.
        var highPass = source.gameObject.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = 150f;
        var reverb = source.gameObject.AddComponent<AudioReverbFilter>();
        reverb.reverbPreset = AudioReverbPreset.User;
        reverb.roomHF = -350f;
        reverb.roomLF = -550f;
        reverb.decayHFRatio = 0.75f;
        reverb.reflectionsLevel = -800f;
        reverb.reflectionsDelay = 0.007f;
        reverb.reverbLevel = -180f;
        reverb.reverbDelay = 0.012f;
        reverb.enabled = false;

        var feedback = source.gameObject.AddComponent<MicrophoneVoiceFeedback>();
        feedback.source = source;
        feedback.reverb = reverb;
        return feedback;
    }

    internal void SetInput(MicrophoneAnalysis microphone)
    {
        if (ReferenceEquals(input, microphone))
            return;
        StopPlayback();
        input = microphone;
    }

    internal void ClearInput(MicrophoneAnalysis microphone)
    {
        if (ReferenceEquals(input, microphone))
            SetInput(null);
    }

    internal void Refresh(FruitCatchAudioSettings settings)
    {
        if (settings == null || !settings.VoiceFeedbackEnabled || input == null ||
            (settings.VoiceFeedbackDry <= 0f && settings.VoiceFeedbackWet <= 0f))
        {
            StopPlayback();
            return;
        }

        AudioClip clip = input.CaptureClip;
        int writePosition = clip != null ? input.GetCapturePosition() : -1;
        if (writePosition < 0)
        {
            StopPlayback();
            return;
        }

        if (source.clip != clip || !source.isPlaying)
        {
            StopPlayback();
            StartPlayback(clip, writePosition, settings);
            return;
        }

        ApplySettings(settings);
        double now = AudioSettings.dspTime;
        if (now < nextCursorCheck)
            return;
        nextCursorCheck = now + 0.25;

        // The input and output devices can drift. Seek back from the capture head if necessary.
        int lag = (writePosition - source.timeSamples + clip.samples) % clip.samples;
        if (lag < clip.frequency * targetLagSeconds * 0.25f || lag > clip.frequency * targetLagSeconds * 2.8f)
        {
            StopPlayback();
            StartPlayback(clip, writePosition, settings);
        }
    }

    private void StartPlayback(AudioClip clip, int writePosition, FruitCatchAudioSettings settings)
    {
        int outputRate = Mathf.Max(1, AudioSettings.outputSampleRate);
        AudioSettings.GetDSPBufferSize(out int framesPerBuffer, out _);
        targetLagSeconds = Mathf.Max(MinimumLagSeconds, 2f * framesPerBuffer / outputRate);
        int lag = Mathf.Clamp(Mathf.CeilToInt(targetLagSeconds * clip.frequency), 1, clip.samples - 1);
        if (writePosition < lag)
            return;

        gainSmoothing = 1f - Mathf.Exp(-1f / (0.025f * outputRate));
        playbackVersion++;
        ApplySettings(settings);
        reverb.enabled = true;
        source.clip = clip;
        source.Play();
        source.timeSamples = writePosition - lag;
        nextCursorCheck = AudioSettings.dspTime + 0.25;
        source.mute = false;
    }

    private void ApplySettings(FruitCatchAudioSettings settings)
    {
        targetGain = Mathf.Clamp01(settings.VoiceFeedbackVolume);
        float dry = ToMillibels(settings.VoiceFeedbackDry);
        float room = ToMillibels(settings.VoiceFeedbackWet);
        float decay = Mathf.Clamp(settings.VoiceFeedbackReverbTime, 0.1f, 2f);
        if (!Mathf.Approximately(reverb.dryLevel, dry)) reverb.dryLevel = dry;
        if (!Mathf.Approximately(reverb.room, room)) reverb.room = room;
        if (!Mathf.Approximately(reverb.decayTime, decay)) reverb.decayTime = decay;
    }

    private static float ToMillibels(float gain) => 2000f * Mathf.Log10(Mathf.Clamp(gain, 0.00001f, 1f));

    private void StopPlayback()
    {
        targetGain = 0f;
        if (source == null || (source.mute && source.clip == null))
            return;

        source.mute = true;
        source.Stop();
        source.clip = null;
        reverb.enabled = false;
    }

    private void OnDisable() => StopPlayback();

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (channels <= 0)
            return;

        int version = playbackVersion;
        if (dspPlaybackVersion != version)
        {
            dspPlaybackVersion = version;
            dspGain = 0f;
        }
        float gain = dspGain;
        float target = targetGain;
        float smoothing = gainSmoothing;
        for (int frame = 0; frame < data.Length; frame += channels)
        {
            gain += (target - gain) * smoothing;
            for (int channel = 0; channel < channels; channel++)
            {
                int index = frame + channel;
                float sample = data[index] * gain;
                float excess = Math.Abs(sample) - OutputKnee;
                if (excess > 0f)
                {
                    float range = OutputCeiling - OutputKnee;
                    float limited = OutputKnee + range * (excess / (range + excess));
                    sample = sample < 0f ? -limited : limited;
                }
                data[index] = sample;
            }
        }
        dspGain = gain;
    }
}
