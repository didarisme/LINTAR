using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.VFX;

public class FireSimulation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FireSimLoader loader;
    [SerializeField] private VisualEffect fireVFX;
    [SerializeField] private CesiumGlobeAnchor fireAnchorObject;

    [Header("Simulation")]
    [SerializeField] private float fireSpeed = 1f;
    [SerializeField] private int fireLifetime = 20;
    [SerializeField] private float raycastHeight = 500f;
    [SerializeField] private float yOffset = 1f;

    [Header("VFX")]
    [SerializeField] private string spawnEventName = "UpdateFire";

    private FireSimConfig config;

    private GraphicsBuffer positionBuffer;
    private GraphicsBuffer fireDataBuffer;
    private int maxFires = 10000;
    private float tickDuration = 0.05f;

    private HashSet<Vector2Int> burntSet = new();
    private List<FireInstance> fireInstances = new();

    private List<FireInstance> pendingVfxSpawns = new();

    private int currentTick;

    private bool started = false;

    public float FireSpeed => fireSpeed;
    public float YOffset => yOffset;
    public int Lifetime => fireLifetime;

    private void OnEnable()
    {
        if (loader == null)
        {
            Debug.LogError("FireSimLoader reference missing!");
            return;
        }

        positionBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxFires,
            sizeof(float) * 3
        );

        fireDataBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            maxFires,
            sizeof(float)
        );

        loader.OnDataLoaded += OnConfigLoaded;
    }

    private void OnDisable()
    {
        if (loader != null)
            loader.OnDataLoaded -= OnConfigLoaded;

        positionBuffer?.Release();
        fireDataBuffer?.Release();
    }

    private void OnConfigLoaded(FireSimConfig newConfig)
    {
        config = newConfig;
        fireAnchorObject.longitudeLatitudeHeight = config.MapCenter;
        UpdateTickDuration();
    }

    private void UpdateTickDuration()
    {
        float patchSize = config.PatchWidthMeters;
        tickDuration = patchSize / fireSpeed;
    }

    public void StartSimulation()
    {
        if (started != true)
        {
            StartCoroutine(PlayFire());
            started = true;
        }
    }

    public float SetSpeed(float speedValue)
    {
        fireSpeed = Mathf.Clamp(speedValue, 1f, 30f);
        UpdateTickDuration();
        return fireSpeed;
    }

    public float SetLifetime(float lifetimeValue)
    {
        int lifetime = (int)Mathf.Clamp(lifetimeValue, 1f, 50f);
        fireLifetime = lifetime;
        return lifetime;
    }

    public float SetYOffset(float offsetValue)
    {
        yOffset = offsetValue;
        return yOffset;
    }

    private IEnumerator PlayFire()
    {
        for (currentTick = 0; currentTick <= config.MaxTick; currentTick++)
        {
            bool spawned = false;

            if (config.FireData.TryGetValue(currentTick, out var positions))
            {
                foreach (var pos in positions)
                {
                    SpawnFire(pos.x, pos.y);
                    spawned = true;
                }
            }

            if (spawned)
                FlushVFXSpawns();

            float timer = 0f;
            while (timer < tickDuration)
            {
                timer += Time.deltaTime;
                yield return null;
            }
        }

        Debug.Log("Simulation finished");
    }

    private void SpawnFire(int px, int py)
    {
        Vector2Int key = new(px, py);

        if (!burntSet.Add(key))
            return;

        Vector3 groundPos = GetGroundPosition(px, py);

        var fire = new FireInstance
        {
            startTick = currentTick,
            position = groundPos
        };

        fireInstances.Add(fire);
        pendingVfxSpawns.Add(fire);
    }

    private Vector3 GetGroundPosition(int px, int py)
    {
        float localX = (px - config.CenterPx) * config.PatchWidthMeters;
        float localZ = (py - config.CenterPy) * config.PatchHeightMeters;

        Vector3 worldPos = fireAnchorObject.transform.TransformPoint(
            new Vector3(localX, raycastHeight, localZ)
        );

        if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, raycastHeight * 2))
        {
            return hit.point + Vector3.up * yOffset;
        }

        return worldPos + Vector3.up * yOffset;
    }

    private void FlushVFXSpawns()
    {
        int count = pendingVfxSpawns.Count;

        if (count == 0)
            return;

        Vector3[] positions = new Vector3[count];
        float[] lifetimes = new float[count];

        for (int i = 0; i < count; i++)
        {
            positions[i] = pendingVfxSpawns[i].position;
            lifetimes[i] = fireLifetime;
        }

        positionBuffer.SetData(positions, 0, 0, count);
        fireDataBuffer.SetData(lifetimes, 0, 0, count);

        fireVFX.SetGraphicsBuffer("PositionBuffer", positionBuffer);
        fireVFX.SetGraphicsBuffer("FireDataBuffer", fireDataBuffer);
        fireVFX.SetUInt("SpawnCount", (uint)count);
        fireVFX.SendEvent(spawnEventName);

        pendingVfxSpawns.Clear();
    }

    private class FireInstance
    {
        public int startTick;
        public Vector3 position;
    }
}