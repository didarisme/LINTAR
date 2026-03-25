using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InfoPanel : MonoBehaviour
{
    [System.Serializable]
    public class InfoSection
    {
        public Button       navButton;
        public GameObject   content;
    }

    [Header("Sections")]
    [SerializeField] private InfoSection[] sections;

    [Header("Colors")]
    [SerializeField] private Color activeColor   = new Color(0.91f, 0.79f, 0.48f, 1f);
    [SerializeField] private Color inactiveColor = new Color(0.35f, 0.35f, 0.42f, 1f);

    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.25f;
    [SerializeField] private float slideOffset  = 30f;

    private int          _currentIndex = -1;
    private Coroutine    _animCoroutine;
    private CanvasGroup[] _groups;

    private void Start()
    {
        _groups = new CanvasGroup[sections.Length];

        for (int i = 0; i < sections.Length; i++)
        {
            int index = i;
            sections[i].navButton.onClick.AddListener(() => ShowSection(index));

            // Добавляем CanvasGroup если нет
            var cg = sections[i].content.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = sections[i].content.AddComponent<CanvasGroup>();
            _groups[i] = cg;
        }

        ShowSection(0);
    }

    private void ShowSection(int index)
    {
        if (index == _currentIndex) return;

        if (_animCoroutine != null)
            StopCoroutine(_animCoroutine);

        _animCoroutine = StartCoroutine(TransitionTo(index));
    }

    private IEnumerator TransitionTo(int index)
    {
        if (_currentIndex >= 0 && _currentIndex < sections.Length)
        {
            yield return FadeOut(_currentIndex);
            sections[_currentIndex].content.SetActive(false);
        }

        _currentIndex = index;

        UpdateButtons(index);

        sections[index].content.SetActive(true);
        yield return FadeIn(index);

        _animCoroutine = null;
    }

    private IEnumerator FadeOut(int index)
    {
        CanvasGroup cg = _groups[index];
        float elapsed = 0f;
        float halfDuration = fadeDuration * 0.4f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / halfDuration;
            cg.alpha = 1f - t;
            yield return null;
        }

        cg.alpha = 0f;
    }

    private IEnumerator FadeIn(int index)
    {
        CanvasGroup cg = _groups[index];
        RectTransform rt = sections[index].content.GetComponent<RectTransform>();

        Vector2 startPos = rt.anchoredPosition;
        Vector2 offset = new Vector2(0f, -slideOffset);

        cg.alpha = 0f;
        rt.anchoredPosition = startPos + offset;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            cg.alpha = ease;
            rt.anchoredPosition = Vector2.LerpUnclamped(startPos + offset, startPos, ease);
            yield return null;
        }

        cg.alpha = 1f;
        rt.anchoredPosition = startPos;
    }

    private void UpdateButtons(int activeIndex)
    {
        for (int i = 0; i < sections.Length; i++)
        {
            bool isActive = i == activeIndex;

            var tmp = sections[i].navButton.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
                tmp.color = isActive ? activeColor : inactiveColor;

            var img = sections[i].navButton.GetComponent<Image>();
            if (img != null)
                img.color = isActive
                    ? new Color(0.91f, 0.79f, 0.48f, 0.07f)
                    : new Color(1f, 1f, 1f, 0f);
        }
    }
}
