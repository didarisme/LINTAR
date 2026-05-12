using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.SceneManagement;

public class ScenarioSelector : Pageable
{
    [SerializeField] private FireScenarioSelectorSO fireScenario;

    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject simButtonPrefab;
    [SerializeField] private Button continueButton;
    [SerializeField] private ConfirmationDialog confirmationDialog;
    [SerializeField] private ScrollRect scrollRect;

    [SerializeField] private string[] preloadedScenarioNames;

    private Color normalColor   = new Color(0.12f, 0.12f, 0.16f);
    private Color selectedColor = new Color(0.25f, 0.35f, 0.7f);

    [SerializeField] private float entryDuration  = 0.3f;
    [SerializeField] private float entryStagger   = 0.04f;
    [SerializeField] private float colorLerpSpeed = 8f;
    [SerializeField] private float exitDuration   = 0.2f;

    private string     _selectedScenarioName;
    private GameObject _selectedButton;

    private readonly Dictionary<string, GameObject> _buttons          = new();
    private readonly List<Coroutine>                _activeAnimations = new();

    private bool animateEntry   = true;
    private bool preloadDone    = false;


    protected override void Awake()
    {
        base.Awake();
        continueButton.interactable = false;
        continueButton.onClick.AddListener(OnContinuePressed);

        StartCoroutine(PreloadStreamingAssets());
    }

    protected override void OnOpen()
    {
        StartCoroutine(WaitForPreloadThenLoad());
    }

    protected override void OnClose()
    {
        ResetSelection();
        ResetScrollPosition();
        StopAllAnimations();
        SetAllButtonsAlpha(0f);
    }


    private IEnumerator PreloadStreamingAssets()
    {
        if (preloadedScenarioNames == null || preloadedScenarioNames.Length == 0)
        {
            preloadDone = true;
            yield break;
        }

        foreach (string fileName in preloadedScenarioNames)
        {
            if (SimulationMemoryManager.Instance.GetSimulationContent(fileName) != null)
                continue;

            // StreamingAssets на WebGL — это URL вида:
            // http://localhost:8080/StreamingAssets/FireScenarios/filename.txt
            string url = System.IO.Path.Combine(
                Application.streamingAssetsPath, "FireScenarios", fileName);

            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                SimulationMemoryManager.Instance.StoreSimulation(fileName, req.downloadHandler.text);
                Debug.Log($"[ScenarioSelector] Preloaded: {fileName}");
            }
            else
            {
                Debug.LogWarning($"[ScenarioSelector] Failed to preload '{fileName}': {req.error}");
            }
        }

        preloadDone = true;
    }


    private IEnumerator WaitForPreloadThenLoad()
    {
        while (!preloadDone)
            yield return null;

        LoadSimulations();
        AnimateAllButtons();
    }


    private void RunAnimation(IEnumerator routine)
    {
        _activeAnimations.Add(StartCoroutine(routine));
    }

    private void StopAllAnimations()
    {
        foreach (var c in _activeAnimations)
            if (c != null) StopCoroutine(c);
        _activeAnimations.Clear();
    }


    private IEnumerator AnimateFloat(System.Action<float> setter, float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            setter(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        setter(to);
    }

    private IEnumerator AnimateAlpha(CanvasGroup cg, float from, float to, float duration)
    {
        yield return AnimateFloat(v => cg.alpha = v, from, to, duration);
    }

    private IEnumerator AnimateScale(Transform target, Vector3 from, Vector3 to, float duration)
    {
        yield return AnimateFloat(
            t => target.localScale = Vector3.Lerp(from, to, t),
            0f, 1f, duration);
    }

    private IEnumerator AnimateColor(Image img, Color target)
    {
        Color start = img.color;
        yield return AnimateFloat(t => img.color = Color.Lerp(start, target, t), 0f, 1f, 1f / colorLerpSpeed);
    }

    private IEnumerator AnimateEntry(GameObject btn, int index)
    {
        if (!btn.TryGetComponent(out CanvasGroup cg))
            cg = btn.AddComponent<CanvasGroup>();

        RectTransform rect = btn.GetComponent<RectTransform>();
        cg.alpha        = 0f;
        rect.localScale = Vector3.one * 0.85f;

        yield return new WaitForSeconds(index * entryStagger);

        RunAnimation(AnimateAlpha(cg, 0f, 1f, entryDuration));
        yield return AnimateScale(rect, Vector3.one * 0.85f, Vector3.one, entryDuration);
    }

    private IEnumerator AnimateExit(string fileName, GameObject btn)
    {
        if (!btn.TryGetComponent(out CanvasGroup cg))
            cg = btn.AddComponent<CanvasGroup>();

        btn.GetComponent<Button>().interactable = false;

        RunAnimation(AnimateAlpha(cg, 1f, 0f, exitDuration));
        yield return AnimateScale(btn.transform, Vector3.one, Vector3.one * 0.85f, exitDuration);

        Destroy(btn);
        _buttons.Remove(fileName);
    }

    private void AnimateAllButtons()
    {
        if (!animateEntry) return;

        int index = 0;
        foreach (Transform child in contentParent)
            RunAnimation(AnimateEntry(child.gameObject, index++));

        animateEntry = false;
    }

    private void LoadSimulations()
    {
        HashSet<string> currentFiles = new(SimulationMemoryManager.Instance.GetAllSimulationNames());

        foreach (string fileName in currentFiles)
            if (!_buttons.ContainsKey(fileName))
                CreateButton(fileName);

        var toRemove = new List<string>();
        foreach (var kvp in _buttons)
            if (!currentFiles.Contains(kvp.Key))
            {
                Destroy(kvp.Value);
                toRemove.Add(kvp.Key);
            }

        foreach (var key in toRemove)
            _buttons.Remove(key);
    }

    private void CreateButton(string fileName)
    {
        GameObject btn = Instantiate(simButtonPrefab, contentParent);

        btn.GetComponentInChildren<TextMeshProUGUI>().text = fileName;
        btn.GetComponent<Image>().color = normalColor;
        btn.GetComponent<Button>().onClick.AddListener(() => SelectSimulation(fileName, btn));

        Transform deleteTransform = btn.transform.Find("DeleteBtn");
        if (deleteTransform != null && deleteTransform.TryGetComponent(out Button deleteBtn))
            deleteBtn.onClick.AddListener(() =>
                confirmationDialog.Show(fileName, () => DeleteSimulation(fileName, btn)));

        _buttons[fileName] = btn;
    }


    private void SelectSimulation(string fileName, GameObject btn)
    {
        if (_selectedButton != null)
            RunAnimation(AnimateColor(_selectedButton.GetComponent<Image>(), normalColor));

        _selectedScenarioName = fileName;
        _selectedButton       = btn;

        RunAnimation(AnimateColor(btn.GetComponent<Image>(), selectedColor));
        continueButton.interactable = true;
    }

    private void DeleteSimulation(string fileName, GameObject btn)
    {
        SimulationMemoryManager.Instance.RemoveSimulation(fileName);

        if (_selectedScenarioName == fileName)
            ResetSelection();

        RunAnimation(AnimateExit(fileName, btn));
    }

    private void ResetSelection()
    {
        if (_selectedButton != null)
            _selectedButton.GetComponent<Image>().color = normalColor;

        _selectedScenarioName       = null;
        _selectedButton             = null;
        continueButton.interactable = false;
    }

    private void ResetScrollPosition()
    {
        scrollRect.verticalNormalizedPosition = 1f;
        scrollRect.velocity = Vector2.zero;
    }

    private void SetAllButtonsAlpha(float alpha)
    {
        foreach (Transform child in contentParent)
        {
            if (!child.TryGetComponent(out CanvasGroup cg))
                cg = child.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = alpha;
        }

        animateEntry = true;
    }

    private void OnContinuePressed()
    {
        if (_selectedScenarioName == null) return;

        fireScenario.SelectedScenarioPath = _selectedScenarioName;
        SceneManager.LoadScene("CesiumShowcase");
    }
}