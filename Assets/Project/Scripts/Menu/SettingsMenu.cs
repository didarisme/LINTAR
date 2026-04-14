using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class SettingsMenu : Pageable
{
    [Header("Panel")]
    [SerializeField] private GameObject settingsPanel;

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private TMP_Dropdown fpsDropdown;

    [Header("Toggles")]
    [SerializeField] private Toggle vsyncToggle;
    [SerializeField] private Toggle fullscreenToggle;

    private List<Resolution> _uniqueResolutions = new List<Resolution>();
    private readonly int[] _fpsOptions = { 30, 60, 120, 144, -1 };
    private bool _initialized       = false;

    public override void OnOpen()
    {
        settingsPanel.SetActive(true);

        if (!_initialized)
        {
            InitResolutions();
            InitQuality();
            InitVSync();
            InitFullscreen();
            InitFPS();
            _initialized = true;
        }
    }

    public override void OnClose()
    {
        settingsPanel.SetActive(false);
    }

    private void InitResolutions()
    {
        Resolution[] all = Screen.resolutions;

        resolutionDropdown.ClearOptions();

        List<string>    options = new List<string>();
        HashSet<string> seen    = new HashSet<string>();
        int currentIndex        = 0;

        foreach (Resolution r in all)
        {
            string key = r.width + "x" + r.height;
            if (seen.Contains(key)) continue;
            seen.Add(key);

            _uniqueResolutions.Add(r);
            options.Add(r.width + " × " + r.height);

            if (r.width  == Screen.currentResolution.width &&
                r.height == Screen.currentResolution.height)
                currentIndex = _uniqueResolutions.Count - 1;
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentIndex;
        resolutionDropdown.RefreshShownValue();
        resolutionDropdown.onValueChanged.AddListener(SetResolution);
    }

    private void InitQuality()
    {
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
        qualityDropdown.value = QualitySettings.GetQualityLevel();
        qualityDropdown.RefreshShownValue();
        qualityDropdown.onValueChanged.AddListener(SetQuality);
    }

    private void InitVSync()
    {
    vsyncToggle.isOn = QualitySettings.vSyncCount > 0;
    vsyncToggle.onValueChanged.AddListener(SetVSync);
    fpsDropdown.interactable = QualitySettings.vSyncCount == 0;
    }

    private void InitFullscreen()
    {
        fullscreenToggle.isOn = Screen.fullScreen;
        fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
    }

    private void InitFPS()
    {
        fpsDropdown.ClearOptions();
        fpsDropdown.AddOptions(new List<string> { "30", "60", "120", "144", "Unlimited" });

        int current      = Application.targetFrameRate;
        int currentIndex = 1;
        for (int i = 0; i < _fpsOptions.Length; i++)
        {
            if (_fpsOptions[i] == current)
            {
                currentIndex = i;
                break;
            }
        }

        fpsDropdown.value = currentIndex;
        fpsDropdown.RefreshShownValue();
        fpsDropdown.onValueChanged.AddListener(SetFPS);
    }

    public void SetResolution(int index)
    {
        Resolution r = _uniqueResolutions[index];
        Screen.SetResolution(r.width, r.height, Screen.fullScreen);
    }

    public void SetQuality(int index)
    {
    int userVSync = QualitySettings.vSyncCount;

    QualitySettings.SetQualityLevel(index, true);
    QualitySettings.vSyncCount = userVSync;
    vsyncToggle.isOn = userVSync > 0;
    }

    public void SetVSync(bool enabled)
    {
    QualitySettings.vSyncCount = enabled ? 1 : 0;
    
    fpsDropdown.interactable = !enabled;
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
    }

    public void SetFPS(int index)
    {
        Application.targetFrameRate = _fpsOptions[index];
    }
}