using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

public class FireSimulation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FireSimLoader loader;
    [SerializeField] private CesiumGeoreference geoRef;

    [Header("Rendering")]
    [SerializeField] private Mesh cubeMesh;
    [SerializeField] private Material instancedMaterial;
    [SerializeField] private float cubeSize = 8f;
    [SerializeField] private float tickDuration = 0.05f;
    [SerializeField] private float raycastHeight = 500f;

    private FireSimConfig config;
    private GameObject fireAnchorObject;

    private readonly List<Matrix4x4> matrices = new();
    private readonly List<Matrix4x4> batch = new(1023);

    private void Awake()
    {
        if (loader == null)
        {
            Debug.LogError("FireSimLoader reference missing!");
            return;
        }

        loader.OnDataLoaded += HandleDataLoaded;
    }

    private void Update()
    {
        RenderInstances();
    }

    private void OnDestroy()
    {
        if (loader != null)
            loader.OnDataLoaded -= HandleDataLoaded;
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

        for (int tick = 0; tick <= maxTick; tick++)
        {
            if (config.FireData.TryGetValue(tick, out var positions))
            {
                foreach (var pos in positions)
                    AddInstance(pos.x, pos.y);
            }

            yield return new WaitForSeconds(tickDuration);
        }

        Debug.Log("Simulation finished");
    }

    private void AddInstance(int px, int py)
    {
        float localX = (px - config.CenterPx) * config.PatchWidthMeters;
        float localZ = (py - config.CenterPy) * config.PatchHeightMeters;

        Vector3 worldPos = fireAnchorObject.transform.TransformPoint(
            new Vector3(localX, raycastHeight, localZ)
        );

        if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, raycastHeight * 2))
        {
            Matrix4x4 matrix = Matrix4x4.TRS(
                hit.point,
                Quaternion.identity,
                Vector3.one * cubeSize
            );

            matrices.Add(matrix);
        }
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
}