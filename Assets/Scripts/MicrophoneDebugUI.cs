using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MicrophoneDebugUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MicrophoneAnalysis microphone;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Dropdown microphoneDropdown;
    [SerializeField] private Button refreshButton;
    [SerializeField] private TMP_Text measurementText;
    [SerializeField] private Slider gainMeter;
    [SerializeField] private bool showPitch = true;

    private readonly List<string> deviceNames = new List<string>();

    private void Start()
    {
        if (microphone == null || panelRoot == null || microphoneDropdown == null)
        {
            Debug.LogError("MicrophoneDebugUI: Assign Microphone, Panel Root and Microphone Dropdown in Inspector.");
            enabled = false;
            return;
        }

        RefreshDevices();
        microphoneDropdown.onValueChanged.AddListener(OnDeviceSelected);
        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshDevices);
    }

    private void Update()
    {
        if (!panelRoot.activeInHierarchy || microphone == null) return;

        if (measurementText != null)
        {
            string state = microphone.IsCapturing ? "Active" : "No input";
            string measurement =
                "Microphone: " + state + "\n" +
                "Gain: " + microphone.Gain.ToString("F3") +
                " (" + microphone.GainDb.ToString("F1") + " dBFS)";
            if (showPitch)
            {
                string pitch = microphone.PitchHz > 0f ? microphone.PitchHz.ToString("F1") + " Hz" : "---";
                measurement += "\nPitch: " + pitch;
            }
            measurementText.text = measurement;
        }

        if (gainMeter != null)
            gainMeter.value = Mathf.Clamp01(microphone.Gain / 0.1f);
    }

    public void RefreshDevices()
    {
        if (microphoneDropdown == null || microphone == null) return;

        deviceNames.Clear();
        deviceNames.Add("");
        var options = new List<string> { "OS Default" };

        foreach (string device in Microphone.devices)
        {
            deviceNames.Add(device);
            options.Add(device);
        }

        // If the previously used device was unplugged, fall back to default.
        if (microphone.ActiveDeviceName.Length > 0 &&
            !deviceNames.Contains(microphone.ActiveDeviceName))
        {
            FruitCatchOperatorSettings.SelectMicrophone("");
        }

        microphoneDropdown.ClearOptions();
        microphoneDropdown.AddOptions(options);
        int index = deviceNames.IndexOf(microphone.ActiveDeviceName);
        microphoneDropdown.SetValueWithoutNotify(Mathf.Max(0, index));
        microphoneDropdown.RefreshShownValue();
    }

    private void OnDeviceSelected(int index)
    {
        if (index < 0 || index >= deviceNames.Count) return;

        string requested = deviceNames[index];
        if (!FruitCatchOperatorSettings.SelectMicrophone(requested))
        {
            Debug.LogWarning("MicrophoneDebugUI: Could not select microphone: " + requested);
        }

        RefreshDevices();
    }

    private void OnDestroy()
    {
        if (microphoneDropdown != null)
            microphoneDropdown.onValueChanged.RemoveListener(OnDeviceSelected);
        if (refreshButton != null)
            refreshButton.onClick.RemoveListener(RefreshDevices);
    }
}
