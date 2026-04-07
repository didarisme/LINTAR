using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class FireScenarioSelector : Pageable
{
    [SerializeField] private FireScenarioSelectorSO fireScenario;

    [Header("References")]
    [SerializeField] private GameObject chooseSimPanel;
    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject simButtonPrefab;
    [SerializeField] private Button continueButton;
    [SerializeField] private ConfirmationDialog confirmationDialog;
    [SerializeField] private ScrollRect scrollRect;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new Color(0.12f, 0.12f, 0.16f);
    [SerializeField] private Color selectedColor = new Color(0.25f, 0.35f, 0.7f);

    [Header("Animation")]
    [SerializeField] private float entryDuration = 0.3f;
    [SerializeField] private float entryStagger = 0.04f;
    [SerializeField] private float colorLerpSpeed = 8f;
    [SerializeField] private float exitDuration = 0.2f;

    private string _folderPath;

    private string _selectedFilePath;
    private GameObject _selectedButton;

    private Dictionary<string, GameObject> _buttons = new();
    private bool _initialized = false;

    private void Awake()
    {
        _folderPath = Path.Combine(Application.streamingAssetsPath, "FireScenarios");

        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);
    }

    public override void OnOpen()
    {
        chooseSimPanel.SetActive(true);

        if (!_initialized)
        {
            LoadSimulationsInitial();
            _initialized = true;
        }
        else
        {
            RefreshSimulations();
        }

        ResetSelection();
        ResetScrollPosition();
    }

    public override void OnClose()
    {
        chooseSimPanel.SetActive(false);
    }

    private void ResetSelection()
    {
        _selectedFilePath = null;
        _selectedButton = null;
        continueButton.interactable = false;
    }

    private void ResetScrollPosition()
    {
        scrollRect.verticalNormalizedPosition = 1f;
        scrollRect.velocity = Vector2.zero;
    }

    private void LoadSimulationsInitial()
    {
        if (!Directory.Exists(_folderPath))
        {
            Debug.LogError("Folder not found: " + _folderPath);
            return;
        }

        string[] files = Directory.GetFiles(_folderPath, "*.txt");

        for (int i = 0; i < files.Length; i++)
        {
            CreateButton(files[i], i);
        }
    }

    private void RefreshSimulations()
    {
        if (!Directory.Exists(_folderPath)) return;

        string[] files = Directory.GetFiles(_folderPath, "*.txt");
        HashSet<string> currentFiles = new(files);

        int index = _buttons.Count;

        foreach (var file in files)
        {
            if (!_buttons.ContainsKey(file))
            {
                CreateButton(file, index++);
            }
        }

        var toRemove = new List<string>();

        foreach (var kvp in _buttons)
        {
            if (!currentFiles.Contains(kvp.Key))
            {
                Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var key in toRemove)
        {
            _buttons.Remove(key);
        }
    }

    private void CreateButton(string filePath, int index)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        GameObject btn = Instantiate(simButtonPrefab, contentParent);

        btn.GetComponentInChildren<TextMeshProUGUI>().text = fileName;

        Image img = btn.GetComponent<Image>();
        img.color = normalColor;

        btn.GetComponent<Button>().onClick.AddListener(() => SelectSimulation(filePath, btn));

        SetupDeleteButton(btn, filePath, fileName);

        _buttons[filePath] = btn;

        StartCoroutine(AnimateEntry(btn, index));
    }

    private void SetupDeleteButton(GameObject btn, string filePath, string fileName)
    {
        Transform deleteTransform = btn.transform.Find("DeleteBtn");

        if (deleteTransform == null) return;

        if (!deleteTransform.TryGetComponent<Button>(out Button deleteBtn)) return;

        deleteBtn.onClick.AddListener(() =>
            confirmationDialog.Show(fileName, () =>
                DeleteSimulation(filePath, btn)));
    }

    private void SelectSimulation(string path, GameObject btn)
    {
        if (_selectedButton != null)
        {
            Image prevImg = _selectedButton.GetComponent<Image>();
            StartCoroutine(AnimateColor(prevImg, normalColor));
        }

        _selectedFilePath = path;
        _selectedButton = btn;

        Image img = btn.GetComponent<Image>();
        StartCoroutine(AnimateColor(img, selectedColor));

        continueButton.interactable = true;
    }

    private IEnumerator AnimateColor(Image img, Color target)
    {
        Color start = img.color;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * colorLerpSpeed;
            img.color = Color.Lerp(start, target, t);
            yield return null;
        }

        img.color = target;
    }

    private void DeleteSimulation(string filePath, GameObject btn)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);

        if (_selectedFilePath == filePath)
        {
            ResetSelection();
        }

        _buttons.Remove(filePath);

        StartCoroutine(AnimateExit(btn));
    }

    private IEnumerator AnimateExit(GameObject btn)
    {
        CanvasGroup cg = btn.GetComponent<CanvasGroup>();
        if (cg == null) cg = btn.AddComponent<CanvasGroup>();

        btn.GetComponent<Button>().interactable = false;

        float elapsed = 0f;

        while (elapsed < exitDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);

            cg.alpha = 1f - t;
            btn.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.85f, t);

            yield return null;
        }

        Destroy(btn);
    }

    // ========================
    // ENTRY ANIMATION
    // ========================

    private IEnumerator AnimateEntry(GameObject btn, int index)
    {
        CanvasGroup cg = btn.AddComponent<CanvasGroup>();
        RectTransform rect = btn.GetComponent<RectTransform>();

        cg.alpha = 0f;
        rect.localScale = Vector3.one * 0.85f;

        yield return new WaitForSeconds(index * entryStagger);

        float elapsed = 0f;

        while (elapsed < entryDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / entryDuration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            cg.alpha = smooth;
            rect.localScale = Vector3.Lerp(Vector3.one * 0.85f, Vector3.one, smooth);

            yield return null;
        }

        cg.alpha = 1f;
        rect.localScale = Vector3.one;
    }

    // ========================
    // CONTINUE
    // ========================

    private void OnContinuePressed()
    {
        if (_selectedFilePath == null) return;

        fireScenario.SelectedScenarioPath = _selectedFilePath;

        SceneManager.LoadScene("CesiumShowcase");
    }
}