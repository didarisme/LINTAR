using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PageTransitioner : MonoBehaviour
{
    public static PageTransitioner Instance { get; private set; }

    private readonly Dictionary<CanvasGroup, Coroutine> _activeAnimations = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void AnimateOpen(CanvasGroup group, float duration, Action onComplete = null)
    {
        Play(group, FadeIn(group, duration, onComplete));
    }

    public void AnimateClose(CanvasGroup group, float duration, Action onComplete = null)
    {
        Play(group, FadeOut(group, duration, onComplete));
    }

    private void Play(CanvasGroup group, IEnumerator routine)
    {
        if (_activeAnimations.TryGetValue(group, out var existing))
        {
            StopCoroutine(existing);
            _activeAnimations.Remove(group);
        }

        var coroutine = StartCoroutine(Run(group, routine));
        _activeAnimations[group] = coroutine;
    }

    private IEnumerator Run(CanvasGroup group, IEnumerator routine)
    {
        yield return routine;
        _activeAnimations.Remove(group);
    }

    private IEnumerator FadeIn(CanvasGroup group, float duration, Action onComplete)
    {
        group.gameObject.SetActive(true);

        group.interactable = true;
        group.blocksRaycasts = true;

        yield return FadeRoutine(group, 1f, duration, onComplete);
    }

    private IEnumerator FadeOut(CanvasGroup group, float duration, Action onComplete)
    {
        group.interactable = false;
        group.blocksRaycasts = false;

        yield return FadeRoutine(group, 0f, duration, onComplete);

        group.gameObject.SetActive(false);
    }

    private IEnumerator FadeRoutine(CanvasGroup group, float targetAlpha, float duration, Action onComplete)
    {
        float startAlpha = group.alpha;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / duration);
            yield return null;
        }

        group.alpha = targetAlpha;
        onComplete?.Invoke();
    }
}