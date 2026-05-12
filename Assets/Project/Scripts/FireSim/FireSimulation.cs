using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

public class FireSimulation : MonoBehaviour
{
    [SerializeField] private FireSimLoader     loader;
    [SerializeField] private CesiumGlobeAnchor fireAnchorObject;

    [SerializeField] private Mesh     fireMesh;
    [SerializeField] private Material fireMaterial;
    [SerializeField] private float    fireMeshScale = 5f;

    [SerializeField] private float fireSpeed     = 1f;
    [SerializeField] private int   fireLifetime  = 20;
    [SerializeField] private float raycastHeight = 500f;
    [SerializeField] private float yOffset       = 1f;

    private FireSimConfig       config;
    private float               tickDuration = 0.05f;
    private HashSet<Vector2Int> burntSet     = new();
    private int  currentTick;
    private bool started = false;

    private const int            BatchSize = 1023;
    private readonly List<Matrix4x4> _batch = new(BatchSize);

    public List<FireInstance> FireInstances { get; private set; } = new();

    public float FireSpeed => fireSpeed;
    public float YOffset   => yOffset;
    public int   Lifetime  => fireLifetime;

    private void OnEnable()
    {
        if (loader == null)
        {
            Debug.LogError("[FireSimulation] FireSimLoader reference missing!");
            return;
        }

        loader.OnDataLoaded += OnConfigLoaded;
    }

    private void OnDisable()
    {
        if (loader != null)
            loader.OnDataLoaded -= OnConfigLoaded;
    }

    private void Update()
    {
        if (started)
            RenderFire();
    }

    private void OnConfigLoaded(FireSimConfig newConfig)
    {
        config = newConfig;
        fireAnchorObject.longitudeLatitudeHeight = config.MapCenter;
        UpdateTickDuration();
    }

    private void UpdateTickDuration()
    {
        tickDuration = config.PatchWidthMeters / fireSpeed;
    }

    public void StartSimulation()
    {
        if (started) return;
        started = true;
        StartCoroutine(PlayFire());
    }

    public float SetSpeed(float value)
    {
        fireSpeed = Mathf.Clamp(value, 1f, 30f);
        UpdateTickDuration();
        return fireSpeed;
    }

    public float SetLifetime(float value)
    {
        fireLifetime = (int)Mathf.Clamp(value, 1f, 50f);
        return fireLifetime;
    }

    public float SetYOffset(float value)
    {
        yOffset = value;
        return yOffset;
    }

    private IEnumerator PlayFire()
    {
        for (currentTick = 0; currentTick <= config.MaxTick; currentTick++)
        {
            if (config.FireData.TryGetValue(currentTick, out var positions))
                foreach (var pos in positions)
                    SpawnFire(pos.x, pos.y);

            float timer = 0f;
            while (timer < tickDuration)
            {
                timer += Time.deltaTime;
                yield return null;
            }
        }

        Debug.Log("[FireSimulation] Simulation finished.");
    }

    private void SpawnFire(int px, int py)
    {
        Vector2Int key = new(px, py);
        if (!burntSet.Add(key)) return;

        FireInstances.Add(new FireInstance
        {
            startTick = currentTick,
            position  = GetGroundPosition(px, py)
        });
    }

    private Vector3 GetGroundPosition(int px, int py)
    {
        float localX = (px - config.CenterPx) * config.PatchWidthMeters;
        float localZ = (py - config.CenterPy) * config.PatchHeightMeters;

        Vector3 worldPos = fireAnchorObject.transform.TransformPoint(
            new Vector3(localX, raycastHeight, localZ));

        if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, raycastHeight * 2f))
            return hit.point + Vector3.up * yOffset;

        return new Vector3(worldPos.x, fireAnchorObject.transform.position.y + yOffset, worldPos.z);
    }

    private void RenderFire()
    {
        if (fireMesh == null || fireMaterial == null)
        {
            Debug.LogWarning("[FireSimulation] fireMesh or fireMaterial not assigned!");
            return;
        }

        if (FireInstances.Count == 0) return;

        _batch.Clear();

        for (int i = 0; i < FireInstances.Count; i++)
        {
            _batch.Add(Matrix4x4.TRS(
                FireInstances[i].position,
                Quaternion.identity,
                Vector3.one * fireMeshScale));

            if (_batch.Count == BatchSize)
            {
                Graphics.DrawMeshInstanced(fireMesh, 0, fireMaterial, _batch);
                _batch.Clear();
            }
        }

        if (_batch.Count > 0)
            Graphics.DrawMeshInstanced(fireMesh, 0, fireMaterial, _batch);
    }

    public class FireInstance
    {
        public int     startTick;
        public Vector3 position;
    }
}