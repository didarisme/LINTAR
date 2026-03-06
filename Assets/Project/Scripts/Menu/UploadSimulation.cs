using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;

public class UploadSimulation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Button            uploadPanel;
    [SerializeField] private Button            continueButton;
    [SerializeField] private TextMeshProUGUI   labelText;
    [SerializeField] private TextMeshProUGUI   fileNameText;

    [Header("Colors")]
    [SerializeField] private Color defaultColor  = new Color(0.5f, 0.5f, 0.5f);
    [SerializeField] private Color selectedColor = new Color(0.4f, 0.6f, 1f);
    [SerializeField] private Color successColor  = new Color(0.3f, 0.8f, 0.4f);
    [SerializeField] private Color warningColor  = new Color(0.9f, 0.7f, 0.2f);

    private string _targetFolder;
    private string _pendingFilePath;

    private void Start()
    {
        _targetFolder = Path.Combine(Application.streamingAssetsPath, "FireScenarios");

        if (!Directory.Exists(_targetFolder))
            Directory.CreateDirectory(_targetFolder);

        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);
        uploadPanel.onClick.AddListener(OpenFileDialog);

        SetDefault();
    }

    private void OpenFileDialog()
    {
        var extensions = new[] { new ExtensionFilter("Text Files", "txt") };

        string[] paths = StandaloneFileBrowser.OpenFilePanel(
            "Select Simulation File", "", extensions, false);

        if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

        _pendingFilePath = paths[0];
        SetStatus("Selected:", Path.GetFileName(_pendingFilePath), selectedColor);
        continueButton.interactable = true;
    }

    private void OnContinuePressed()
    {
        if (_pendingFilePath == null) return;

        string fileName = Path.GetFileName(_pendingFilePath);
        string destPath = Path.Combine(_targetFolder, fileName);

        if (File.Exists(destPath))
        {
            SetStatus("Already exists:", fileName, warningColor);
            continueButton.interactable = false;
            _pendingFilePath = null;
            return;
        }

        File.Copy(_pendingFilePath, destPath);
        _pendingFilePath = null;

        SetStatus("Uploaded:", fileName, successColor);
        continueButton.interactable = false;

        Debug.Log("File uploaded: " + destPath);
    }

    private void SetDefault()
    {
        if (labelText != null)
        {
            labelText.text  = "Click to select simulation file";
            labelText.color = defaultColor;
        }

        if (fileNameText != null)
        {
            fileNameText.text  = "Supported format: .txt — max 10 MB";
            fileNameText.color = defaultColor;
        }
    }

    private void SetStatus(string label, string file, Color color)
    {
        if (labelText != null)
        {
            labelText.text  = label;
            labelText.color = color;
        }

        if (fileNameText != null)
        {
            fileNameText.text  = file;
            fileNameText.color = color;
        }
    }
}