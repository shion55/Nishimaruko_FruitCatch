using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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

    private const string PreferenceKey = "FruitCatch.SelectedMicrophone";
    private readonly List<string> deviceNames = new List<string>();

    private void Start()
    {
        if (microphone == null || panelRoot == null || microphoneDropdown == null)
        {
            Debug.LogError("MicrophoneDebugUI: Assign Microphone, Panel Root and Microphone Dropdown in Inspector.");
            enabled = false;
            return;
        }

        // Device selection is stored locally on this computer, not in Git.
        string savedDevice = PlayerPrefs.GetString(PreferenceKey, "");
        if (savedDevice.Length > 0 && !microphone.SelectMicrophone(savedDevice))
        {
            microphone.SelectMicrophone("");
            PlayerPrefs.DeleteKey(PreferenceKey);
        }

        RefreshDevices();
        microphoneDropdown.onValueChanged.AddListener(OnDeviceSelected);
        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshDevices);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            panelRoot.SetActive(!panelRoot.activeSelf);

        if (!panelRoot.activeInHierarchy || microphone == null) return;

        if (measurementText != null)
        {
            string state = microphone.IsCapturing ? "Active" : "No input";
            string pitch = microphone.PitchHz > 0f
                ? microphone.PitchHz.ToString("F1") + " Hz"
                : "---";
            measurementText.text =
                "Microphone: " + state + "\n" +
                "Gain: " + microphone.Gain.ToString("F3") +
                " (" + microphone.GainDb.ToString("F1") + " dBFS)\n" +
                "Pitch: " + pitch;
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
            microphone.SelectMicrophone("");
            PlayerPrefs.DeleteKey(PreferenceKey);
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
        if (microphone.SelectMicrophone(requested))
        {
            PlayerPrefs.SetString(PreferenceKey, requested);
            PlayerPrefs.Save();
        }
        else
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
