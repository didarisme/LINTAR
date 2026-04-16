using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class ScenarioSelector : Pageable
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
    [SerializeField] private Color normalColor = new(0.12f, 0.12f, 0.16f);
    [SerializeField] private Color selectedColor = new(0.25f, 0.35f, 0.7f);

    [Header("Animation")]
    [SerializeField] private float entryDuration = 0.3f;
    [SerializeField] private float entryStagger = 0.04f;
    [SerializeField] private float colorLerpSpeed = 8f;
    [SerializeField] private float exitDuration = 0.2f;

    private string _folderPath;
    private string _selectedScenarioPath;
    private GameObject _selectedButton;

    private readonly Dictionary<string, GameObject> _buttons = new();

    private void Awake()
    {
        _folderPath = Path.Combine(Application.streamingAssetsPath, "FireScenarios");

        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);
    }

    public override void OnOpen()
    {
        chooseSimPanel.SetActive(true);

        LoadSimulations();
        AnimateAllButtons();
    }

    public override void OnClose()
    {
        chooseSimPanel.SetActive(false);
        ResetSelection();
        ResetScrollPosition();

        StopAllCoroutines();
    }

    // =========================
    // LOAD (объединённый метод)
    // =========================
    private void LoadSimulations()
    {
        if (!Directory.Exists(_folderPath))
        {
            Debug.LogError("Folder not found: " + _folderPath);
            return;
        }

        string[] files = Directory.GetFiles(_folderPath, "*.txt");
        HashSet<string> currentFiles = new(files);

        // Добавляем новые
        foreach (var file in files)
        {
            if (!_buttons.ContainsKey(file))
            {
                CreateButton(file);
            }
        }

        // Удаляем отсутствующие
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

    private void CreateButton(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        GameObject btn = Instantiate(simButtonPrefab, contentParent);

        btn.GetComponentInChildren<TextMeshProUGUI>().text = fileName;
        btn.GetComponent<Image>().color = normalColor;

        btn.GetComponent<Button>()
            .onClick.AddListener(() => SelectSimulation(filePath, btn));

        SetupDeleteButton(btn, filePath, fileName);

        _buttons[filePath] = btn;
    }

    private void SetupDeleteButton(GameObject btn, string filePath, string fileName)
    {
        Transform deleteTransform = btn.transform.Find("DeleteBtn");
        if (deleteTransform == null) return;

        if (!deleteTransform.TryGetComponent(out Button deleteBtn)) return;

        deleteBtn.onClick.AddListener(() =>
            confirmationDialog.Show(fileName, () =>
                DeleteSimulation(filePath, btn)));
    }

    // =========================
    // SELECTION
    // =========================
    private void SelectSimulation(string path, GameObject btn)
    {
        if (_selectedButton != null)
        {
            StartCoroutine(AnimateColor(
                _selectedButton.GetComponent<Image>(),
                normalColor));
        }

        _selectedScenarioPath = path;
        _selectedButton = btn;

        StartCoroutine(AnimateColor(
            btn.GetComponent<Image>(),
            selectedColor));

        continueButton.interactable = true;
    }

    // =========================
    // DELETE
    // =========================
    private void DeleteSimulation(string filePath, GameObject btn)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);

        if (_selectedScenarioPath == filePath)
            ResetSelection();

        _buttons.Remove(filePath);

        StartCoroutine(AnimateExit(btn));
    }

    // =========================
    // UI HELPERS
    // =========================
    private void ResetSelection()
    {
        if (_selectedButton != null)
        {
            _selectedButton.GetComponent<Image>().color = normalColor;
        }

        _selectedScenarioPath = null;
        _selectedButton = null;
        continueButton.interactable = false;
    }

    private void ResetScrollPosition()
    {
        scrollRect.verticalNormalizedPosition = 1f;
        scrollRect.velocity = Vector2.zero;
    }

    private void AnimateAllButtons()
    {
        int index = 0;
        foreach (Transform child in contentParent)
        {
            StartCoroutine(AnimateEntry(child.gameObject, index++));
        }
    }

    // =========================
    // ANIMATIONS
    // =========================
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

    private IEnumerator AnimateEntry(GameObject btn, int index)
    {
        if (!btn.TryGetComponent(out CanvasGroup cg))
            cg = btn.AddComponent<CanvasGroup>();

        RectTransform rect = btn.GetComponent<RectTransform>();

        cg.alpha = 0f;
        rect.localScale = Vector3.one * 0.85f;

        yield return new WaitForSeconds(index * entryStagger);

        float elapsed = 0f;

        while (elapsed < entryDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / entryDuration);

            cg.alpha = t;
            rect.localScale = Vector3.Lerp(Vector3.one * 0.85f, Vector3.one, t);

            yield return null;
        }

        cg.alpha = 1f;
        rect.localScale = Vector3.one;
    }

    private IEnumerator AnimateExit(GameObject btn)
    {
        if (!btn.TryGetComponent(out CanvasGroup cg))
            cg = btn.AddComponent<CanvasGroup>();

        btn.GetComponent<Button>().interactable = false;

        float elapsed = 0f;

        while (elapsed < exitDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / exitDuration;

            cg.alpha = 1f - t;
            btn.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.85f, t);

            yield return null;
        }

        Destroy(btn);
    }

    // =========================
    // CONTINUE
    // =========================
    private void OnContinuePressed()
    {
        if (_selectedScenarioPath == null) return;

        fireScenario.SelectedScenarioPath = _selectedScenarioPath;
        SceneManager.LoadScene("CesiumShowcase");
    }
}