using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class SimulationLoader : MonoBehaviour
{
    [SerializeField] private FireScenarioSelectionSO fireScenario;

    [Header("References")]
    [SerializeField] private Transform          contentParent;
    [SerializeField] private GameObject         simButtonPrefab;
    [SerializeField] private Button             continueButton;
    [SerializeField] private ConfirmationDialog confirmationDialog;
    [SerializeField] private ScrollRect         scrollRect;

    [Header("Colors")]
    [SerializeField] private Color normalColor   = new Color(0.12f, 0.12f, 0.16f);
    [SerializeField] private Color selectedColor = new Color(0.25f, 0.35f, 0.7f);

    [Header("Animation")]
    [SerializeField] private float entryDuration  = 0.3f;
    [SerializeField] private float entryStagger   = 0.04f;
    [SerializeField] private float colorLerpSpeed  = 10f;
    [SerializeField] private float exitDuration    = 0.2f;

    private string     _folderPath;
    private string     _selectedFilePath;
    private GameObject _selectedButton;

    private struct ColorTarget
    {
        public Image Image;
        public Color Target;
    }
    private readonly List<ColorTarget> _colorTargets = new List<ColorTarget>();

    private void Start()
    {
        _folderPath = Path.Combine(Application.streamingAssetsPath, "FireScenarios");

        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(_folderPath))
            _folderPath = Path.Combine(Application.streamingAssetsPath, "FireScenarios");

        // Очищаем старый список
        foreach (Transform child in contentParent)
            Destroy(child.gameObject);

        _selectedFilePath = null;
        _selectedButton   = null;
        _colorTargets.Clear();
        continueButton.interactable = false;

        LoadSimulations();

        // Сбросить скролл наверх после загрузки
        if (scrollRect != null)
            StartCoroutine(ResetScrollPosition());
    }

    private IEnumerator ResetScrollPosition()
    {
        yield return new WaitForEndOfFrame();

        RectTransform contentRect = contentParent as RectTransform;
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        Canvas.ForceUpdateCanvases();
        contentRect.anchoredPosition = Vector2.zero;

        // Если используется SmoothScrollRect — сбросить через его метод
        var smooth = scrollRect as SmoothScrollRect;
        if (smooth != null)
            smooth.SetPositionImmediate(new Vector2(0f, 1f));
        else
        {
            scrollRect.verticalNormalizedPosition = 1f;
            scrollRect.velocity = Vector2.zero;
        }
    }

    private void Update()
    {
        // Smooth color transitions for selection
        for (int i = 0; i < _colorTargets.Count; i++)
        {
            var ct = _colorTargets[i];
            if (ct.Image == null) continue;

            Color current = ct.Image.color;
            // Only lerp RGB, leave alpha alone (entry animation controls it)
            current.r = Mathf.Lerp(current.r, ct.Target.r, Time.deltaTime * colorLerpSpeed);
            current.g = Mathf.Lerp(current.g, ct.Target.g, Time.deltaTime * colorLerpSpeed);
            current.b = Mathf.Lerp(current.b, ct.Target.b, Time.deltaTime * colorLerpSpeed);
            ct.Image.color = current;
        }
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

        for (int i = 0; i < files.Length; i++)
        {
            string file     = files[i];
            string fileName = Path.GetFileNameWithoutExtension(file);

            GameObject btn = Instantiate(simButtonPrefab, contentParent);
            btn.GetComponentInChildren<TextMeshProUGUI>().text = fileName;

            Image img = btn.GetComponent<Image>();
            img.color = normalColor;
            _colorTargets.Add(new ColorTarget { Image = img, Target = normalColor });

            // Add CanvasGroup for fade animation (doesn't break layout)
            CanvasGroup cg = btn.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            string filePath = file;

            btn.GetComponent<Button>().onClick.AddListener(() =>
                SelectSimulation(filePath, btn));

            Transform deleteTransform = btn.transform.Find("DeleteBtn");
            if (deleteTransform != null)
            {
                Button deleteBtn = deleteTransform.GetComponent<Button>();
                if (deleteBtn != null)
                {
                    deleteBtn.onClick.AddListener(() =>
                        confirmationDialog.Show(fileName, () =>
                            DeleteSimulation(filePath, btn)));
                }
            }

            // Animate entry with stagger
            StartCoroutine(AnimateEntry(btn, i));
        }
    }

    private IEnumerator AnimateEntry(GameObject btn, int index)
    {
        CanvasGroup cg = btn.GetComponent<CanvasGroup>();
        RectTransform rect = btn.GetComponent<RectTransform>();

        // Small stagger delay based on index
        float delay = index * entryStagger;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        // Scale + fade animation (не трогаем anchoredPosition — LayoutGroup управляет позицией)
        float elapsed = 0f;
        while (elapsed < entryDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / entryDuration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            cg.alpha = smooth;
            rect.localScale = new Vector3(
                Mathf.Lerp(0.85f, 1f, smooth),
                Mathf.Lerp(0.85f, 1f, smooth),
                1f);

            yield return null;
        }

        cg.alpha = 1f;
        rect.localScale = Vector3.one;
    }

    private void SelectSimulation(string path, GameObject btn)
    {
        if (_selectedButton != null)
            SetColorTarget(_selectedButton.GetComponent<Image>(), normalColor);

        _selectedFilePath = path;
        _selectedButton   = btn;

        SetColorTarget(btn.GetComponent<Image>(), selectedColor);
        continueButton.interactable = true;
    }

    private void SetColorTarget(Image img, Color target)
    {
        for (int i = 0; i < _colorTargets.Count; i++)
        {
            if (_colorTargets[i].Image == img)
            {
                _colorTargets[i] = new ColorTarget { Image = img, Target = target };
                return;
            }
        }
    }

    private void DeleteSimulation(string filePath, GameObject btn)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);

        if (_selectedFilePath == filePath)
        {
            _selectedFilePath = null;
            _selectedButton   = null;
            continueButton.interactable = false;
        }

        // Remove from color targets
        for (int i = _colorTargets.Count - 1; i >= 0; i--)
        {
            if (_colorTargets[i].Image == btn.GetComponent<Image>())
            {
                _colorTargets.RemoveAt(i);
                break;
            }
        }

        StartCoroutine(AnimateExit(btn));
    }

    private IEnumerator AnimateExit(GameObject btn)
    {
        CanvasGroup cg = btn.GetComponent<CanvasGroup>();
        if (cg == null) cg = btn.AddComponent<CanvasGroup>();

        // Disable button interaction during exit
        btn.GetComponent<Button>().interactable = false;

        float elapsed = 0f;
        while (elapsed < exitDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);

            cg.alpha = 1f - smooth;
            btn.transform.localScale = new Vector3(
                Mathf.Lerp(1f, 0.85f, smooth),
                Mathf.Lerp(1f, 0.85f, smooth),
                1f);

            yield return null;
        }

        Destroy(btn);
    }

    private void OnContinuePressed()
    {
        if (_selectedFilePath == null) return;

        string content = File.ReadAllText(_selectedFilePath);
        fireScenario.SelectedScenario = new TextAsset(content);

        SceneManager.LoadScene("CesuimShowcase");
    }
}
