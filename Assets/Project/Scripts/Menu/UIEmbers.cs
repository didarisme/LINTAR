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
    [SerializeField] private int   count    = 60;
    [SerializeField] private float minSize  = 3f;
    [SerializeField] private float maxSize  = 9f;

    [Header("Movement")]
    [SerializeField] private float minSpeed     = 40f;
    [SerializeField] private float maxSpeed     = 160f;
    [SerializeField] private float minDrift     = 15f;
    [SerializeField] private float maxDrift     = 50f;
    [SerializeField] private float minDriftFreq = 0.3f;
    [SerializeField] private float maxDriftFreq = 1.8f;
    [SerializeField] private float turbulence   = 25f;

    [Header("Appearance")]
    [SerializeField] private float flickerSpeed   = 8f;
    [SerializeField] private float flickerAmount  = 0.3f;
    [SerializeField] private float rotationSpeed  = 90f;
    [SerializeField] private float shrinkAmount   = 0.4f;

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
        public float         DriftPhase;
        public float         StartX;
        public float         Elapsed;
        public float         BaseSize;
        public float         RotSpeed;
        public float         FlickerOffset;
        public float         TurbSeedX;
        public float         TurbSeedY;
        public Color         BaseColor;
        public float         DepthScale; // 0.5 = far, 1.0 = near
    }

    private readonly List<Ember> _embers = new List<Ember>();
    private Sprite _glowSprite;
    private float  _w, _h;

    // Color gradient: deep red → orange → yellow → white-hot
    private static readonly Color[] EmberColors = {
        new Color(0.85f, 0.15f, 0.02f),  // deep red
        new Color(1.0f,  0.30f, 0.05f),  // red-orange
        new Color(1.0f,  0.45f, 0.08f),  // orange
        new Color(1.0f,  0.55f, 0.10f),  // warm orange
        new Color(1.0f,  0.65f, 0.15f),  // yellow-orange
        new Color(1.0f,  0.75f, 0.25f),  // yellow
        new Color(1.0f,  0.85f, 0.50f),  // bright yellow
        new Color(1.0f,  0.92f, 0.70f),  // white-hot
    };

    // ─────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────
    private void Start()
    {
        _glowSprite = CreateGlowSprite();

        _w = embersLayer.rect.width;
        _h = embersLayer.rect.height;

        for (int i = 0; i < count; i++)
        {
            Ember e = SpawnEmber();
            e.Elapsed = Random.Range(0f, TravelTime(e));
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

        float travel = TravelTime(e);
        if (e.Elapsed >= travel)
        {
            ResetEmber(e);
            return;
        }

        float t = e.Elapsed / travel;

        // ── Position with turbulence ──
        float sineX = Mathf.Sin(e.Elapsed * e.DriftFreq + e.DriftPhase) * e.Drift;
        float turbX = (Mathf.PerlinNoise(e.TurbSeedX + e.Elapsed * 0.7f, 0f) - 0.5f) * 2f * turbulence;
        float turbY = (Mathf.PerlinNoise(0f, e.TurbSeedY + e.Elapsed * 0.5f) - 0.5f) * 2f * turbulence * 0.3f;

        float x = e.StartX + (sineX + turbX) * e.DepthScale;
        // Slight acceleration upward (ease-in)
        float tCurved = t * t * (3f - 2f * t); // smoothstep-ish, accelerates then decelerates
        float y = Mathf.Lerp(-_h / 2f - 30f, _h / 2f + 80f, tCurved) + turbY;
        e.Rect.anchoredPosition = new Vector2(x, y);

        // ── Size: shrink as it rises ──
        float sizeT = 1f - t * shrinkAmount;
        float sz = e.BaseSize * sizeT * e.DepthScale;
        e.Rect.sizeDelta = new Vector2(sz, sz);

        // ── Rotation ──
        float rot = e.Elapsed * e.RotSpeed;
        e.Rect.localRotation = Quaternion.Euler(0f, 0f, rot);

        // ── Flicker ──
        float flicker = 1f - flickerAmount
            + Mathf.Sin(e.Elapsed * flickerSpeed + e.FlickerOffset) * flickerAmount * 0.5f
            + Mathf.Sin(e.Elapsed * flickerSpeed * 2.7f + e.FlickerOffset * 1.3f) * flickerAmount * 0.3f
            + (Mathf.PerlinNoise(e.FlickerOffset, e.Elapsed * 3f) - 0.5f) * flickerAmount * 0.4f;

        // ── Alpha: fade in → hold → fade out (softer curve) ──
        float alpha;
        if (t < 0.08f)
            alpha = Mathf.SmoothStep(0f, 1f, t / 0.08f);
        else if (t > 0.6f)
            alpha = Mathf.SmoothStep(1f, 0f, (t - 0.6f) / 0.4f);
        else
            alpha = 1f;

        // ── Color shift: warm up slightly as it rises ──
        Color c = e.BaseColor;
        // Desaturate and dim toward the end of life
        if (t > 0.5f)
        {
            float fadeT = (t - 0.5f) / 0.5f;
            c = Color.Lerp(c, new Color(0.6f, 0.3f, 0.15f), fadeT * 0.4f);
        }

        c.a = alpha * flicker * 0.9f;
        e.Image.color = c;
    }

    private Ember SpawnEmber()
    {
        var go = new GameObject("Ember", typeof(RectTransform));
        go.transform.SetParent(embersLayer, false);

        float depth   = Random.Range(0.5f, 1.0f);
        float sz      = Random.Range(minSize, maxSize) * depth;

        var rect       = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(sz, sz);

        var img           = go.AddComponent<Image>();
        img.sprite        = _glowSprite;
        img.raycastTarget = false;

        Color baseColor = PickEmberColor();
        img.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);

        return new Ember
        {
            Rect          = rect,
            Image         = img,
            Speed         = Random.Range(minSpeed, maxSpeed) * depth,
            Drift         = Random.Range(minDrift, maxDrift),
            DriftFreq     = Random.Range(minDriftFreq, maxDriftFreq),
            DriftPhase    = Random.Range(0f, Mathf.PI * 2f),
            StartX        = Random.Range(-_w / 2f, _w / 2f),
            Elapsed       = 0f,
            BaseSize      = sz,
            RotSpeed      = Random.Range(-rotationSpeed, rotationSpeed),
            FlickerOffset = Random.Range(0f, 100f),
            TurbSeedX     = Random.Range(0f, 1000f),
            TurbSeedY     = Random.Range(0f, 1000f),
            BaseColor     = baseColor,
            DepthScale    = depth
        };
    }

    private void ResetEmber(Ember e)
    {
        float depth     = Random.Range(0.5f, 1.0f);
        e.Speed         = Random.Range(minSpeed, maxSpeed) * depth;
        e.Drift         = Random.Range(minDrift, maxDrift);
        e.DriftFreq     = Random.Range(minDriftFreq, maxDriftFreq);
        e.DriftPhase    = Random.Range(0f, Mathf.PI * 2f);
        e.StartX        = Random.Range(-_w / 2f, _w / 2f);
        e.Elapsed       = 0f;
        e.RotSpeed      = Random.Range(-rotationSpeed, rotationSpeed);
        e.FlickerOffset = Random.Range(0f, 100f);
        e.TurbSeedX     = Random.Range(0f, 1000f);
        e.TurbSeedY     = Random.Range(0f, 1000f);
        e.DepthScale    = depth;

        float sz         = Random.Range(minSize, maxSize) * depth;
        e.BaseSize       = sz;
        e.Rect.sizeDelta = new Vector2(sz, sz);

        e.BaseColor   = PickEmberColor();
        e.Image.color = new Color(e.BaseColor.r, e.BaseColor.g, e.BaseColor.b, 0f);
    }

    private float TravelTime(Ember e) => (_h + 110f) / e.Speed;

    private static Color PickEmberColor()
    {
        // Weighted: more orange/red, fewer white-hot
        float r = Random.value;
        int idx;
        if (r < 0.15f)      idx = 0; // deep red
        else if (r < 0.30f) idx = 1; // red-orange
        else if (r < 0.50f) idx = 2; // orange
        else if (r < 0.65f) idx = 3; // warm orange
        else if (r < 0.78f) idx = 4; // yellow-orange
        else if (r < 0.88f) idx = 5; // yellow
        else if (r < 0.95f) idx = 6; // bright yellow
        else                idx = 7; // white-hot (rare)

        // Slight random variation
        Color c = EmberColors[idx];
        c.r = Mathf.Clamp01(c.r + Random.Range(-0.03f, 0.03f));
        c.g = Mathf.Clamp01(c.g + Random.Range(-0.05f, 0.05f));
        c.b = Mathf.Clamp01(c.b + Random.Range(-0.03f, 0.03f));
        return c;
    }

    /// <summary>
    /// Generates a glow sprite with bright core and soft falloff.
    /// </summary>
    private static Sprite CreateGlowSprite()
    {
        const int res    = 64;
        const float half = res / 2f;

        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);

        for (int x = 0; x < res; x++)
        for (int y = 0; y < res; y++)
        {
            float dist = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;

            // Bright core (< 0.2) + soft glow falloff
            float core = Mathf.Clamp01(1f - dist / 0.3f);               // sharp bright center
            float glow = Mathf.Clamp01(1f - dist) * Mathf.Clamp01(1f - dist); // quadratic falloff
            float alpha = Mathf.Clamp01(core * 0.7f + glow * 0.5f);

            // Core is slightly brighter/whiter
            float whiteness = core * 0.3f;
            float r = Mathf.Clamp01(1f);
            float g = Mathf.Clamp01(1f * whiteness + (1f - whiteness));
            float b = Mathf.Clamp01(1f * whiteness + (1f - whiteness));

            tex.SetPixel(x, y, new Color(r, g, b, alpha));
        }

        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
    }
}
