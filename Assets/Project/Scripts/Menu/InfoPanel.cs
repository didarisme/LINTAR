using System.Collections;
using UnityEngine;

public class InfoPanel : Pageable
{
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private GameObject[] pages;
    [SerializeField] private InfoNavButton[] navButtons;

    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.2f;

    private int _activePage = 0;
    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        foreach (GameObject page in pages)
        {
            page.SetActive(false);
            GetCanvasGroup(page).alpha = 1f;
        }
    }

    public override void OnOpen()
    {
        infoPanel.SetActive(true);
        _activePage = -1;

        OpenPageByIndex(0);
    }

    public override void OnClose()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        infoPanel.SetActive(false);
    }

    public void OpenPageByIndex(int pageIndex)
    {
        if (pages == null || pages.Length == 0) return;

        for (int i = 0; i < navButtons.Length; i++)
            navButtons[i].SetActiveColor(i == pageIndex);

        if (_activePage == -1)
        {
            _activePage = pageIndex;
            pages[pageIndex].SetActive(true);
            return;
        }

        if (pageIndex == _activePage && pages[_activePage].activeSelf) return;

        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadePage(_activePage, pageIndex));
    }

    private IEnumerator FadePage(int fromIndex, int toIndex)
    {
        CanvasGroup from = GetCanvasGroup(pages[fromIndex]);
        yield return StartCoroutine(Fade(from, 1f, 0f));
        pages[fromIndex].SetActive(false);

        _activePage = toIndex;
        pages[toIndex].SetActive(true);

        CanvasGroup to = GetCanvasGroup(pages[toIndex]);
        to.alpha = 0f;
        yield return StartCoroutine(Fade(to, 0f, 1f));
    }

    private IEnumerator Fade(CanvasGroup cg, float from, float to)
    {
        float elapsed = 0f;
        cg.alpha = from;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / fadeDuration);
            yield return null;
        }

        cg.alpha = to;
    }

    private CanvasGroup GetCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = go.AddComponent<CanvasGroup>();
        return cg;
    }
}