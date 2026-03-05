using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIEmbers : MonoBehaviour
{
    // ─────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────
    [Header("References")]
    [SerializeField] private RectTransform embersLayer;

    [Header("Spawn Settings")]
    [SerializeField] private int   count    = 40;
    [SerializeField] private float minSize  = 2f;
    [SerializeField] private float maxSize  = 6f;

    [Header("Movement")]
    [SerializeField] private float minSpeed    = 60f;
    [SerializeField] private float maxSpeed    = 180f;
    [SerializeField] private float minDrift    = 10f;
    [SerializeField] private float maxDrift    = 40f;
    [SerializeField] private float minDriftFreq = 0.5f;
    [SerializeField] private float maxDriftFreq = 2f;

    // ─────────────────────────────────────────
    //  Internal
    // ─────────────────────────────────────────
    private class Ember
    {
        public RectTransform Rect;
        public Image         Image;
        public float         Speed;
        public float         Drift;
        public float         DriftFreq;
        public float         StartX;
        public float         Elapsed;
    }

    private readonly List<Ember> _embers = new List<Ember>();
    private Sprite _circleSprite;
    private float  _w, _h;

    // ─────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────
    private void Start()
    {
        _circleSprite = CreateCircleSprite();

        _w = embersLayer.rect.width;
        _h = embersLayer.rect.height;

        for (int i = 0; i < count; i++)
        {
            Ember e = SpawnEmber();
            e.Elapsed = Random.Range(0f, TravelTime(e.Speed));
            _embers.Add(e);
        }
    }

    private void Update()
    {
        _w = embersLayer.rect.width;
        _h = embersLayer.rect.height;

        foreach (Ember e in _embers)
            UpdateEmber(e);
    }

    // ─────────────────────────────────────────
    //  Private Methods
    // ─────────────────────────────────────────
    private void UpdateEmber(Ember e)
    {
        e.Elapsed += Time.deltaTime;

        if (e.Elapsed >= TravelTime(e.Speed))
        {
            ResetEmber(e);
            return;
        }

        float t = e.Elapsed / TravelTime(e.Speed);

        // Position
        float x = e.StartX + Mathf.Sin(e.Elapsed * e.DriftFreq) * e.Drift;
        float y = Mathf.Lerp(-_h / 2f, _h / 2f + 60f, t);
        e.Rect.anchoredPosition = new Vector2(x, y);

        // Fade in → hold → fade out
        float alpha = t < 0.1f ? t / 0.1f
                    : t > 0.7f ? 1f - (t - 0.7f) / 0.3f
                    : 1f;

        Color c = e.Image.color;
        c.a = alpha * 0.85f;
        e.Image.color = c;
    }

    private Ember SpawnEmber()
    {
        var go = new GameObject("Ember", typeof(RectTransform));
        go.transform.SetParent(embersLayer, false);

        var rect      = go.GetComponent<RectTransform>();
        float sz      = Random.Range(minSize, maxSize);
        rect.sizeDelta = new Vector2(sz, sz);

        var img             = go.AddComponent<Image>();
        img.sprite          = _circleSprite;
        img.raycastTarget   = false;
        img.color           = new Color(1f, Random.Range(0.4f, 0.7f), 0.1f, 0f);

        return new Ember
        {
            Rect      = rect,
            Image     = img,
            Speed     = Random.Range(minSpeed, maxSpeed),
            Drift     = Random.Range(minDrift, maxDrift),
            DriftFreq = Random.Range(minDriftFreq, maxDriftFreq),
            StartX    = Random.Range(-_w / 2f, _w / 2f),
            Elapsed   = 0f
        };
    }

    private void ResetEmber(Ember e)
    {
        e.Speed     = Random.Range(minSpeed, maxSpeed);
        e.Drift     = Random.Range(minDrift, maxDrift);
        e.DriftFreq = Random.Range(minDriftFreq, maxDriftFreq);
        e.StartX    = Random.Range(-_w / 2f, _w / 2f);
        e.Elapsed   = 0f;

        float sz        = Random.Range(minSize, maxSize);
        e.Rect.sizeDelta = new Vector2(sz, sz);
        e.Image.color   = new Color(1f, Random.Range(0.4f, 0.7f), 0.1f, 0f);
    }

    private float TravelTime(float speed) => _h / speed;

    /// <summary>
    /// Generates a soft circle sprite via texture — no external assets needed.
    /// </summary>
    private static Sprite CreateCircleSprite()
    {
        const int res    = 32;
        const float half = res / 2f;

        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);

        for (int x = 0; x < res; x++)
        for (int y = 0; y < res; y++)
        {
            float dist  = Vector2.Distance(new Vector2(x, y), new Vector2(half, half));
            float alpha = Mathf.Clamp01(1f - dist / (half - 1f));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
    }
}