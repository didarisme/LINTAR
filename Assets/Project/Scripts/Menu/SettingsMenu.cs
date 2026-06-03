using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class SettingsMenu : Pageable
{
    [SerializeField] private TMP_Dropdown qualityDropdown;

    protected override void Awake()
    {
        base.Awake();
        InitQuality();
    }

    protected override void OnOpen()
    {
        
    }
    protected override void OnClose()
    {
        
    }

    private void InitQuality()
    {
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
        qualityDropdown.value = QualitySettings.GetQualityLevel();
        qualityDropdown.RefreshShownValue();
        qualityDropdown.onValueChanged.AddListener(SetQuality);
    }

    public void SetQuality(int index)
    {
        QualitySettings.SetQualityLevel(index, true);
    }
}