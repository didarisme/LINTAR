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

    private int _uploadedCount = 0;
    private int _skippedCount  = 0;

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
            SetStatus("Error:", "Failed to parse file", warningColor);
            return;
        }

        string fileName    = parts[0];
        string fileContent = parts[1];

        if (SimulationMemoryManager.Instance.GetSimulationContent(fileName) != null)
        {
            _skippedCount++;
        }
        else
        {
            SimulationMemoryManager.Instance.StoreSimulation(fileName, fileContent);
            _uploadedCount++;
        }

        UpdateStatusLabel();
        postUploadButtons.SetActive(true);
    }

    private void UpdateStatusLabel()
    {
        if (_skippedCount > 0 && _uploadedCount == 0)
            SetStatus("Already exists:", $"{_skippedCount} skipped", warningColor);
        else if (_skippedCount > 0)
            SetStatus("Uploaded:", $"{_uploadedCount} files ({_skippedCount} skipped)", successColor);
        else
            SetStatus("Uploaded:", $"{_uploadedCount} file(s)", successColor);
    }

    private void OnUploadAnother() => ResetUI();

    private void ResetUI()
    {
        _uploadedCount = 0;
        _skippedCount  = 0;
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