using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;

public class UploadSimulation : Pageable
{
   
    [SerializeField] private Button uploadPanelBtn;
    [SerializeField] private Button uploadAnotherBtn;
    [SerializeField] private Button continueButton;
    [SerializeField] private GameObject postUploadButtons;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI fileNameText;

    [Header("Colors")]
    private Color defaultColor = new Color(0.5f, 0.5f, 0.5f);
    private Color selectedColor = new Color(0.4f, 0.6f, 1f);
    private Color successColor = new Color(0.3f, 0.8f, 0.4f);
    private Color warningColor = new Color(0.9f, 0.7f, 0.2f);

    private string[] _pendingFilePaths;
    private bool _initialized = false;

#if UNITY_WEBGL &&!UNITY_EDITOR
   [DllImport("__Internal")]
    private static extern void TriggerBrowserFileUpload(string objectName, string methodName);
#endif

    protected override void Awake()
    {
        base.Awake();
        Initialize();
        ResetUI();
    }

    protected override void OnOpen() { }
    protected override void OnClose()
    {
        _pendingFilePaths = null;
        ResetUI();
    }

    private void Initialize()
    {
        if (_initialized) return;

        uploadPanelBtn.onClick.AddListener(OnUploadClicked);
        continueButton.onClick.AddListener(OnContinuePressed);
        uploadAnotherBtn.onClick.AddListener(OnUploadAnother);

        _initialized = true;
    }

    private void ResetUI()
    {
        continueButton.gameObject.SetActive(true);
        continueButton.interactable = false;
        postUploadButtons.SetActive(false);

        SetDefault();
    }

    private void OnUploadClicked()
    {
    #if UNITY_WEBGL &&!UNITY_EDITOR
        TriggerBrowserFileUpload(gameObject.name, nameof(OnFileReceivedFromBrowser));
    #else
        OpenDesktopFileDialog();
    #endif
    }
    #if !UNITY_WEBGL || UNITY_EDITOR
    private void OpenDesktopFileDialog()
    {
        var extensions = new[] { new ExtensionFilter("Text Files", "txt") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select Simulation Files", "", extensions, true);

        if (paths == null || paths.Length == 0) return;

        string label = paths.Length == 1? Path.GetFileName(paths[0]) : $"{paths.Length} files selected";
        SetStatus("Selected:", label, selectedColor);

        _pendingFilePaths = paths;
        continueButton.interactable = true;
    }
    #endif

    private void OnContinuePressed()
    {
        if (_pendingFilePaths == null || _pendingFilePaths.Length == 0) return;

    #if !UNITY_WEBGL || UNITY_EDITOR
        int uploaded = 0;
        int skipped = 0;

        foreach (string path in _pendingFilePaths)
        {
            string fileName = Path.GetFileName(path);
            string destPath = Path.Combine(Application.persistentDataPath, fileName);

            if (SimulationMemoryManager.Instance.GetSimulationContent(fileName) != null)
            {
                skipped++;
                continue;
            }

            string fileContent = File.ReadAllText(path);
            SimulationMemoryManager.Instance.StoreSimulation(fileName, fileContent);
            File.WriteAllText(destPath, fileContent);
            uploaded++;
        }

        _pendingFilePaths = null;

        if (skipped > 0 && uploaded == 0)
            SetStatus("File(s) already exist:", $"{skipped} skipped", warningColor);
        else if (skipped > 0)
            SetStatus("Uploaded:", $"{uploaded} files ({skipped} skipped)", successColor);
        else
            SetStatus("Uploaded:", $"{uploaded} file(s)", successColor);

        continueButton.gameObject.SetActive(false);
        postUploadButtons.SetActive(true);
    #endif
    }

    public void OnFileReceivedFromBrowser(string payload)
    {
        string[] parts = payload.Split(new string[] { "|::|" }, 2, StringSplitOptions.None);
        
        if (parts.Length == 2)
        {
            string fileName = parts[0];
            string fileContent = parts[1];

            SimulationMemoryManager.Instance.StoreSimulation(fileName, fileContent);

            SetStatus("Uploaded:", fileName, successColor);
            continueButton.gameObject.SetActive(false);
            postUploadButtons.SetActive(true);
        }
        else
        {
            SetStatus("Error:", "Failed to parse file from browser", warningColor);
        }
    }

    private void OnUploadAnother() => ResetUI();

    private void SetDefault()
    {
        if (labelText!= null) { labelText.text = "Click to select simulation file"; labelText.color = defaultColor; }
        if (fileNameText!= null) { fileNameText.text = "Supported format:.txt"; fileNameText.color = defaultColor; }
    }

    private void SetStatus(string label, string file, Color color)
    {
        if (labelText!= null) { labelText.text = label; labelText.color = color; }
        if (fileNameText!= null) { fileNameText.text = file; fileNameText.color = color; }
    }
}