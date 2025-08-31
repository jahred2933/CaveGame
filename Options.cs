using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

public class SettingsMenu : MonoBehaviour
{
    [Header("Audio Settings")]
    public AudioMixer audioMixer;
    public Slider volumeSlider;

    [Header("Graphics Settings")]
    public TMP_Dropdown resolutionDropdown;
    public TMP_Dropdown qualityDropdown;
    public Toggle fullscreenToggle;

    float currentVolume;
    Resolution[] resolutions;

    void Start()
    {
        // Setup resolution
        resolutionDropdown.ClearOptions();
        List<string> options = new List<string>();
        resolutions = Screen.resolutions;
        HashSet<string> uniqueRes = new HashSet<string>();
        int highestResolutionIndex = 0;

        // Find the highest resolution available
        Resolution highestResolution = resolutions[0];
        for (int i = 0; i < resolutions.Length; i++)
        {
            string opt = resolutions[i].width + " x " + resolutions[i].height;
            if (uniqueRes.Add(opt))
            {
                options.Add(opt);

                // Check if the current resolution is higher than the previously found one
                if (resolutions[i].width * resolutions[i].height > highestResolution.width * highestResolution.height)
                {
                    highestResolution = resolutions[i];
                    highestResolutionIndex = options.Count - 1; // Update index to the highest resolution
                }
            }
        }

        resolutionDropdown.AddOptions(options);

        // Set the default to the highest resolution
        resolutionDropdown.value = highestResolutionIndex;
        resolutionDropdown.RefreshShownValue();

        // Apply the highest resolution to the current monitor
        int currentMonitorIndex = Display.displays[0].active ? 0 : 1;  // Assuming two displays for simplicity
        Screen.SetResolution(highestResolution.width, highestResolution.height, Screen.fullScreen, currentMonitorIndex);

        // Volume
        currentVolume = PlayerPrefs.GetFloat("Volume", 0.75f);
        volumeSlider.value = currentVolume;
        SetVolume(currentVolume);

        // Quality
        int savedQuality = PlayerPrefs.GetInt("QualityLevel", QualitySettings.GetQualityLevel());
        qualityDropdown.value = savedQuality;
        QualitySettings.SetQualityLevel(savedQuality);

        // Fullscreen
        bool isFullscreen = PlayerPrefs.GetInt("Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        fullscreenToggle.isOn = isFullscreen;
        Screen.fullScreen = isFullscreen;
    }

    public void SetVolume(float sliderValue)
    {
        if (sliderValue < 0.0001f)
            sliderValue = 0.0001f;

        float dB = Mathf.Log10(sliderValue) * 20f;
        audioMixer.SetFloat("Volume", dB);

        currentVolume = sliderValue;
        PlayerPrefs.SetFloat("Volume", sliderValue);
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
        PlayerPrefs.SetInt("Fullscreen", isFullscreen ? 1 : 0);
    }

    public void SetResolution(int resolutionIndex)
    {
        string[] resParts = resolutionDropdown.options[resolutionIndex].text.Split('x');
        int width = int.Parse(resParts[0].Trim());
        int height = int.Parse(resParts[1].Trim());

        // Apply resolution to the active display (e.g., Display 0, Display 1, etc.)
        Screen.SetResolution(width, height, Screen.fullScreen, 0); // Change the index if using more than one display
        PlayerPrefs.SetInt("ResolutionIndex", resolutionIndex);
    }

    public void SetQuality(int qualityIndex)
    {
        QualitySettings.SetQualityLevel(qualityIndex);
        PlayerPrefs.SetInt("QualityLevel", qualityIndex);
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}
