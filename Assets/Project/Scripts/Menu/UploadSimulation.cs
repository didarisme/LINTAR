using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;

public class UploadSimulation : Pageable
{
    [Header("Buttons")]
    [SerializeField] private Button uploadPanelBtn;
    [SerializeField] private Button uploadAnotherBtn;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button btnGoToSims;

    [Space]
    [SerializeField] private GameObject uploadPanel;
    [SerializeField] private GameObject postUploadButtons;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI fileNameText;

    [Header("Colors")]
    [SerializeField] private Color defaultColor = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField] private Color selectedColor = new Color(0.4f, 0.6f, 1f);
    [SerializeField] private Color successColor = new Color(0.3f, 0.8f, 0.4f);
    [SerializeField] private Color warningColor = new Color(0.9f, 0.7f, 0.2f);

    private string _targetFolder;
    private string[] _pendingFilePaths;

    private bool _initialized = false;

    private void Start()
    {
        Initialize();
    }

    protected override void OnOpen()
    {
        uploadPanel.SetActive(true);
        ResetUI();
    }

    protected override void OnClose()
    {
        _pendingFilePaths = null;
        uploadPanel.SetActive(false);
    }

    private void Initialize()
    {
        if (_initialized) return;

        _targetFolder = Path.Combine(Application.streamingAssetsPath, "FireScenarios");

        if (!Directory.Exists(_targetFolder))
            Directory.CreateDirectory(_targetFolder);

        uploadPanelBtn.onClick.AddListener(OpenFileDialog);
        continueButton.onClick.AddListener(OnContinuePressed);
        uploadAnotherBtn.onClick.AddListener(OnUploadAnother);
        btnGoToSims.onClick.AddListener(OnGoToSims);

        _initialized = true;
    }

    private void ResetUI()
    {
        continueButton.gameObject.SetActive(true);
        continueButton.interactable = false;
        postUploadButtons.SetActive(false);

        SetDefault();
    }

    private void OpenFileDialog()
    {
        var extensions = new[] { new ExtensionFilter("Text Files", "txt") };

        string[] paths = StandaloneFileBrowser.OpenFilePanel(
            "Select Simulation Files", "", extensions, true);

        if (paths == null || paths.Length == 0) return;

        string label = paths.Length == 1
            ? Path.GetFileName(paths[0])
            : $"{paths.Length} files selected";

        SetStatus("Selected:", label, selectedColor);

        _pendingFilePaths = paths;
        continueButton.interactable = true;
    }

    private void OnContinuePressed()
    {
        if (_pendingFilePaths == null || _pendingFilePaths.Length == 0) return;

        int uploaded = 0;
        int skipped = 0;

        foreach (string path in _pendingFilePaths)
        {
            string fileName = Path.GetFileName(path);
            string destPath = Path.Combine(_targetFolder, fileName);

            if (File.Exists(destPath))
            {
                skipped++;
                continue;
            }

            File.Copy(path, destPath);
            uploaded++;
        }

        _pendingFilePaths = null;

        if (skipped > 0 && uploaded == 0)
            SetStatus("All files already exist:", $"{skipped} skipped", warningColor);
        else if (skipped > 0)
            SetStatus("Uploaded:", $"{uploaded} files ({skipped} skipped)", successColor);
        else
            SetStatus("Uploaded:", $"{uploaded} file(s)", successColor);

        continueButton.gameObject.SetActive(false);
        postUploadButtons.SetActive(true);
    }

    private void OnUploadAnother()
    {
        ResetUI();
    }

    private void OnGoToSims()
    {

        Debug.Log("Go to simulations page");
    }

    private void SetDefault()
    {
        if (labelText != null)
        {
            labelText.text = "Click to select simulation file";
            labelText.color = defaultColor;
        }

        if (fileNameText != null)
        {
            fileNameText.text = "Supported format: .txt";
            fileNameText.color = defaultColor;
        }
    }

    private void SetStatus(string label, string file, Color color)
    {
        if (labelText != null)
        {
            labelText.text = label;
            labelText.color = color;
        }

        if (fileNameText != null)
        {
            fileNameText.text = file;
            fileNameText.color = color;
        }
    }
}