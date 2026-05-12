using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UploadSimulation : Pageable
{
    [SerializeField] private Button uploadPanelBtn;
    [SerializeField] private Button uploadAnotherBtn;
    [SerializeField] private GameObject postUploadButtons;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI fileNameText;

    [Header("Colors")]
    private Color defaultColor = new Color(0.5f, 0.5f, 0.5f);
    private Color successColor = new Color(0.3f, 0.8f, 0.4f);
    private Color warningColor = new Color(0.9f, 0.7f, 0.2f);

    [DllImport("__Internal")]
    private static extern void TriggerBrowserFileUpload(string objectName, string methodName);


    protected override void Awake()
    {
        base.Awake();
        uploadPanelBtn.onClick.AddListener(OnUploadClicked);
        uploadAnotherBtn.onClick.AddListener(OnUploadAnother);
        ResetUI();
    }

    protected override void OnOpen() { }

    protected override void OnClose() => ResetUI();

    private void OnUploadClicked()
    {
        TriggerBrowserFileUpload(gameObject.name, nameof(OnFileReceivedFromBrowser));
    }


    public void OnFileReceivedFromBrowser(string payload)
    {
        string[] parts = payload.Split(new[] { "|::|" }, 2, StringSplitOptions.None);

        if (parts.Length != 2)
        {
            SetStatus("Error:", "Failed to parse file from browser", warningColor);
            return;
        }

        string fileName    = parts[0];
        string fileContent = parts[1];

        if (SimulationMemoryManager.Instance.GetSimulationContent(fileName) != null)
        {
            SetStatus("Already exists:", fileName, warningColor);
            postUploadButtons.SetActive(true);
            return;
        }

        SimulationMemoryManager.Instance.StoreSimulation(fileName, fileContent);
        SetStatus("Uploaded:", fileName, successColor);
        postUploadButtons.SetActive(true);
    }

    private void OnUploadAnother() => ResetUI();


    private void ResetUI()
    {
        postUploadButtons.SetActive(false);
        SetDefault();
    }

    private void SetDefault()
    {
        if (labelText    != null) { labelText.text    = "Click to select simulation file"; labelText.color    = defaultColor; }
        if (fileNameText != null) { fileNameText.text  = "Supported format: .txt";          fileNameText.color = defaultColor; }
    }

    private void SetStatus(string label, string file, Color color)
    {
        if (labelText    != null) { labelText.text    = label; labelText.color    = color; }
        if (fileNameText != null) { fileNameText.text  = file;  fileNameText.color = color; }
    }
}