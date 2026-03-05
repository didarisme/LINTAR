using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

public class FireSimulation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FireSimLoader loader;
    [SerializeField] private CesiumGeoreference geoRef;
    [SerializeField] private FirePool firePool;

    [Header("Rendering")]
    [SerializeField] private Mesh cubeMesh;
    [SerializeField] private Material instancedMaterial;
    [SerializeField] private float cubeSize = 8f;

    [Header("Simulation")]
    [SerializeField] private float tickDuration = 0.05f;
    [SerializeField] private int fireLifetimeTicks = 20;
    [SerializeField] private float raycastHeight = 500f;

    private FireSimConfig config;
    private GameObject fireAnchorObject;

    private readonly List<Matrix4x4> matrices = new();
    private readonly List<Matrix4x4> batch = new(1023);

    private readonly Dictionary<Vector2Int, FireInstance> activeFires = new();
    private readonly Dictionary<Vector2Int, Vector3> cachedGroundPositions = new();

    private int currentTick;

    private void OnEnable()
    {
        if (loader == null)
        {
            Debug.LogError("FireSimLoader reference missing!");
            return;
        }

        loader.OnDataLoaded += HandleDataLoaded;
    }

    private void OnDisable()
    {
        if (loader != null)
            loader.OnDataLoaded -= HandleDataLoaded;
    }

    private void Update()
    {
        RenderInstances();
    }

    private void HandleDataLoaded(FireSimConfig newConfig)
    {
        config = newConfig;
        CreateAnchor();
    }

    private void CreateAnchor()
    {
        fireAnchorObject = new GameObject("FireAnchor");
        fireAnchorObject.transform.SetParent(geoRef.transform, false);

        var anchor = fireAnchorObject.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = config.MapCenter;
    }

    public void StartSimulation()
    {
        StartCoroutine(PlayFire());
    }

    private IEnumerator PlayFire()
    {
        int maxTick = 0;

        foreach (var t in config.FireData.Keys)
            if (t > maxTick) maxTick = t;

        for (currentTick = 0; currentTick <= maxTick; currentTick++)
        {
            if (config.FireData.TryGetValue(currentTick, out var positions))
            {
                foreach (var pos in positions)
                    SpawnFire(pos.x, pos.y);
            }

            UpdateFireLifecycle();

            yield return new WaitForSeconds(tickDuration);
        }

        Debug.Log("Simulation finished");
    }

    private void SpawnFire(int px, int py)
    {
        Vector2Int key = new(px, py);

        if (activeFires.ContainsKey(key))
            return;

        Vector3 groundPos = GetGroundPosition(px, py);

        GameObject fire = firePool.Get();
        fire.transform.position = groundPos;
        fire.transform.SetParent(fireAnchorObject.transform);
        fire.SetActive(true);

        activeFires[key] = new FireInstance
        {
            vfx = fire,
            startTick = currentTick,
            position = groundPos
        };
    }

    private void UpdateFireLifecycle()
    {
        List<Vector2Int> toRemove = new();

        foreach (var pair in activeFires)
        {
            FireInstance fire = pair.Value;

            if (currentTick - fire.startTick >= fireLifetimeTicks)
            {
                firePool.Release(fire.vfx);

                Matrix4x4 matrix = Matrix4x4.TRS(
                    fire.position,
                    Quaternion.identity,
                    Vector3.one * cubeSize
                );

                matrices.Add(matrix);

                toRemove.Add(pair.Key);
            }
        }

        foreach (var key in toRemove)
            activeFires.Remove(key);
    }

    private Vector3 GetGroundPosition(int px, int py)
    {
        Vector2Int key = new(px, py);

        if (cachedGroundPositions.TryGetValue(key, out var pos))
            return pos;

        float localX = (px - config.CenterPx) * config.PatchWidthMeters;
        float localZ = (py - config.CenterPy) * config.PatchHeightMeters;

        Vector3 worldPos = fireAnchorObject.transform.TransformPoint(
            new Vector3(localX, raycastHeight, localZ)
        );

        if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, raycastHeight * 2))
        {
            cachedGroundPositions[key] = hit.point;
            return hit.point;
        }

        cachedGroundPositions[key] = worldPos;
        return worldPos;
    }

    private void RenderInstances()
    {
        if (cubeMesh == null || instancedMaterial == null) return;

        const int batchSize = 1023;

        for (int i = 0; i < matrices.Count; i += batchSize)
        {
            int count = Mathf.Min(batchSize, matrices.Count - i);

            batch.Clear();

            for (int j = 0; j < count; j++)
                batch.Add(matrices[i + j]);

            Graphics.DrawMeshInstanced(
                cubeMesh,
                0,
                instancedMaterial,
                batch
            );
        }
    }

    private class FireInstance
    {
        public GameObject vfx;
        public int startTick;
        public Vector3 position;
    }
}