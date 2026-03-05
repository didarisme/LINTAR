using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimulationLoader : MonoBehaviour
{
    [Header("References")]
    public Transform contentParent;
    public GameObject simButtonPrefab;
    public Button continueButton;

    [Header("Colors")]
    public Color normalColor   = new Color(0.12f, 0.12f, 0.16f); 
    public Color selectedColor = new Color(0.25f, 0.35f, 0.7f); 

    private string selectedFilePath = null;
    private GameObject selectedButton = null;

    void Start()
    {
        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);
        LoadSimulations();
    }

    void LoadSimulations()
    {
        string folderPath = Path.Combine(Application.dataPath, "Project/Source/FireScenarios");

        if (!Directory.Exists(folderPath))
        {
            Debug.LogError("Folder not found: " + folderPath);
            return;
        }

        string[] files = Directory.GetFiles(folderPath, "*.txt");

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

    void SelectSimulation(string path, GameObject btn)
    {
        // Reset color of previous button
        if (selectedButton != null)
            selectedButton.GetComponent<Image>().color = normalColor;

        selectedFilePath = path;
        selectedButton = btn;

        // Change the color of selected button
        btn.GetComponent<Image>().color = selectedColor;

        continueButton.interactable = true;
    }
/*
    void OnContinuePressed()
    {
        if (selectedFilePath == null) return;

        SimulationData.filePath = selectedFilePath;
        SimulationData.content  = File.ReadAllText(selectedFilePath);

        UnityEngine.SceneManagement.SceneManager.LoadScene("SimulationScene");
    }  */

    //Delete after tests and uncomment upper func
    void OnContinuePressed()
{
    if (selectedFilePath == null) return;

    SimulationData.filePath = selectedFilePath;
    SimulationData.content  = File.ReadAllText(selectedFilePath);

    
    Debug.Log("Выбрана симуляция: " + selectedFilePath);
}

}
