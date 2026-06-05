using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class SettingsMenu : Pageable
{
    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private TMP_Dropdown fpsDropdown;

    [Header("Toggles")]
    [SerializeField] private Toggle vsyncToggle;

    private readonly int[] _fpsOptions = { 30, 60, 120, 144, -1 };

    protected override void Awake()
    {
        base.Awake();

        InitQuality();
        InitVSync();
        InitFPS();
    }

    protected override void OnOpen()
    {
        // page open
    }

    protected override void OnClose()
    {
        // page close
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

    public void SetFPS(int index)
    {
        Application.targetFrameRate = _fpsOptions[index];
    }
}