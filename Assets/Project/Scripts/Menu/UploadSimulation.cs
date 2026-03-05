using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;

public class UploadSimulation : MonoBehaviour
{
    [Header("References")]
    public Button uploadPanel;
    public Button continueButton;
    public TextMeshProUGUI labelText;
    public TextMeshProUGUI fileNameText;

    [Header("Colors")]
    public Color defaultColor = new Color(0.5f, 0.5f, 0.5f);
    public Color selectedColor = new Color(0.4f, 0.6f, 1f);   // синий — файл выбран
    public Color successColor  = new Color(0.3f, 0.8f, 0.4f); // зелёный — загружен
    public Color warningColor  = new Color(0.9f, 0.7f, 0.2f); // жёлтый — уже существует

    private string targetFolder;
    private string pendingFilePath = null; // путь к выбранному файлу

    void Start()
    {
        targetFolder = Path.Combine(Application.dataPath, "Project/Source/FireScenarios");

        // Continue неактивен пока файл не выбран
        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);

        uploadPanel.onClick.AddListener(OpenFileDialog);

        SetDefault();
    }

    void OpenFileDialog()
    {
        var extensions = new[] { new ExtensionFilter("Text Files", "txt") };

        string[] paths = StandaloneFileBrowser.OpenFilePanel(
            "Select Simulation File",
            "",
            extensions,
            false
        );

        if (paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

        // Просто запоминаем путь, ничего не копируем
        pendingFilePath = paths[0];
        string fileName = Path.GetFileName(pendingFilePath);

        // Показываем что файл выбран
        SetStatus("Selected:", fileName, selectedColor);

        // Разблокируем Continue
        continueButton.interactable = true;
    }

    void OnContinuePressed()
    {
        if (pendingFilePath == null) return;

        string fileName = Path.GetFileName(pendingFilePath);
        string destPath = Path.Combine(targetFolder, fileName);

        // Файл уже существует
        if (File.Exists(destPath))
        {
            SetStatus("Already exists:", fileName, warningColor);
            continueButton.interactable = false;
            pendingFilePath = null;
            return;
        }

        // Копируем файл
        File.Copy(pendingFilePath, destPath);
        pendingFilePath = null;

        SetStatus("Uploaded:", fileName, successColor);
        continueButton.interactable = false;

        Debug.Log("Файл загружен: " + destPath);
    }

    void SetDefault()
    {
        if (labelText != null)
        {
            labelText.text  = "Click to select simulation file";
            labelText.color = defaultColor;
        }
        if (fileNameText != null)
            fileNameText.text = "Supported format: .txt — max 10 MB";
            fileNameText.color = defaultColor;
    }

    void SetStatus(string label, string file, Color color)
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