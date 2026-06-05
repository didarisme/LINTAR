using System.Collections;
using UnityEngine;

public class PopUpWindow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool isShown = false;

    [Header("Scale Settings")]
    [SerializeField] private Vector3 hiddenScale = Vector3.zero;
    [SerializeField] private Vector3 shownScale = Vector3.one;
    [SerializeField] private float duration = 0.2f;

    private Coroutine animCoroutine;

    private void Awake()
    {
        if (target == null)
            target = transform;

        target.localScale = isShown ? shownScale : hiddenScale;

        if (canvasGroup != null)
            canvasGroup.interactable = isShown;
    }

    public void Show()
    {
        if (isShown)
            return;

        isShown = true;
        StartAnimation(shownScale);
    }

    public void Hide()
    {
        if (!isShown)
            return;

        isShown = false;
        StartAnimation(hiddenScale);
    }

    public void Toggle()
    {
        if (isShown)
            Hide();
        else
            Show();
    }

    private void StartAnimation(Vector3 targetScale)
    {
        if (animCoroutine != null)
            StopCoroutine(animCoroutine);

        animCoroutine = StartCoroutine(AnimateScale(targetScale));
    }

    private IEnumerator AnimateScale(Vector3 targetScale)
    {
        if (canvasGroup != null)
            canvasGroup.interactable = false;

        Vector3 startScale = target.localScale;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;

            target.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }

        target.localScale = targetScale;
        animCoroutine = null;

        if (canvasGroup != null)
            canvasGroup.interactable = isShown;
    }
}