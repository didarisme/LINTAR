using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class SimulationLoader : MonoBehaviour
{
    [SerializeField] private FireScenarioSelectionSO fireScenario;

    [Header("References")]
    [SerializeField] private Transform  contentParent;
    [SerializeField] private GameObject simButtonPrefab;
    [SerializeField] private Button     continueButton;

    [Header("Colors")]
    [SerializeField] private Color normalColor   = new Color(0.12f, 0.12f, 0.16f);
    [SerializeField] private Color selectedColor = new Color(0.25f, 0.35f, 0.7f);

    private string     _folderPath;
    private string     _selectedFilePath;
    private GameObject _selectedButton;

    private void Start()
    {
        _folderPath = Path.Combine(Application.streamingAssetsPath, "FireScenarios");

        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);

        LoadSimulations();
    }

    private void LoadSimulations()
    {
        if (!Directory.Exists(_folderPath))
        {
            Debug.LogError("Folder not found: " + _folderPath);
            return;
        }

        string[] files = Directory.GetFiles(_folderPath, "*.txt");

        if (files.Length == 0)
        {
            Debug.LogWarning("No .txt files found in: " + _folderPath);
            return;
        }

        foreach (string file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);

            GameObject btn = Instantiate(simButtonPrefab, contentParent);
            btn.GetComponentInChildren<TextMeshProUGUI>().text = fileName;
            btn.GetComponent<Image>().color = normalColor;

            string filePath = file;
            btn.GetComponent<Button>().onClick.AddListener(() =>
            {
                SelectSimulation(filePath, btn);
            });
        }
    }

    private void SelectSimulation(string path, GameObject btn)
    {
        if (_selectedButton != null)
            _selectedButton.GetComponent<Image>().color = normalColor;

        _selectedFilePath = path;
        _selectedButton   = btn;

        btn.GetComponent<Image>().color = selectedColor;
        continueButton.interactable = true;
    }

    private void OnContinuePressed()
    {
        if (_selectedFilePath == null) return;

        string content = File.ReadAllText(_selectedFilePath);
        fireScenario.SelectedScenario = new TextAsset(content);

        SceneManager.LoadScene("CesuimShowcase");
    }
}