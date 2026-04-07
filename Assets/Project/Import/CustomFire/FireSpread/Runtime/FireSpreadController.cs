using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Rendering;
using Unity.Collections;
using System.Collections.Generic;
using System.Collections;

namespace LemiGame
{
    /// <summary>
    /// GPU-accelerated fire spread manager (pure GPU + VFX Graph)
    /// Package version: does not depend on Odin Inspector, comments and tooltips are in English.
    /// Class name is suffixed with  to avoid conflicts with existing project code.
    /// This script works independently with direct Terrain/Texture assignments.
    /// </summary>
    public class FireSpreadController : MonoBehaviour
    {
        [System.Serializable]
        public class FireSpreadSettings
        {
            [Header("Timing")]
            public float burnDuration = 2f;
            public float waitDuration = 2f;
            public float recoverPrepDuration = 10f;
            public float recoverGrowDuration = 2f;

            [Header("Spread")]
            public float spreadInterval = 1f;
            [Tooltip("Read GPU data every N spread iterations to reduce GPU->CPU transfers (1=every time, 2=every two times, etc.)")]
            [Range(1, 5)] public int gpuReadSkipFrames = 1;
            [Tooltip("Detection coroutine update interval (seconds). Lower values reduce GPU->CPU transfers.")]
            [Range(0.1f, 2f)] public float detectionUpdateInterval = 0.5f;
            [Range(0f, 1f)] public float initialSpreadChance = 1f;
            [Range(0f, 1f)] public float spreadChanceDecay = 0.15f;
            [Range(0f, 1f)] public float minSpreadChance = 0.05f;
            public int maxFireCount = 50;
            public int maxSpreadAttempts = 8; // Also used for perFireMaxChecks
            public int perFireMaxSpawns = 4;
            [Tooltip("Randomization range multiplier (x=min, y=max). Applied to duration, scale, time offset, and cooldown.")]
            public Vector2 randomRange = new Vector2(0.85f, 1.15f); // Unified random range for all variations
            public Vector2 spreadDelayRange = new Vector2(0.05f, 0.6f);
        }

        [SerializeField] private FireSpreadSettings settings = new FireSpreadSettings();
        [Tooltip("Burn state map (R=burn, G=wait, B=recover). If null, will be created automatically.")]
        [SerializeField] private RenderTexture burnMap;
        [SerializeField] private ComputeShader fireComputeShader;
        [SerializeField] private VisualEffect vfx;
        
        [Tooltip("Terrain reference. If not assigned, will try to find in scene if available.")]
        [SerializeField] private Terrain terrain;
        [Tooltip("Grass mask texture (white=grass, black=no grass). Optional.")]
        [SerializeField] private Texture2D grassMask;
        [SerializeField] private Transform camOrTarget;
        [SerializeField] private float radius = 30f;
        [SerializeField] private int updateEveryNFrames = 1;
        [Tooltip("Enable camera distance culling to only update fires and grass within camera range. Significantly improves performance.")]
        [SerializeField] private bool enableCameraCulling = true;
        [Tooltip("Maximum distance from camera to update fires. Fires beyond this distance will pause updates but retain state.")]
        [SerializeField] private float maxUpdateDistance = 100f;
        [SerializeField] private LayerMask groundLayer = ~0; // default to all layers
        private Texture heightTex;
        private float heightScale = 100f;
        private float heightOffset = 0f;
        private Vector3 terrainPosition;
        private Vector3 terrainSize;

        [Header("Trigger/Detection")]
        [SerializeField] private float burnThreshold = 0.1f; // burnMap.r threshold to consider "on fire"
        private readonly HashSet<Collider> monitoredColliders = new HashSet<Collider>();
        private readonly Dictionary<Collider, bool> colliderInFire = new Dictionary<Collider, bool>();

        [Header("Compute Shader Kernels")]
        private string kernelUpdateName = "CSUpdateFires";
        private string kernelSpreadName = "CSSpreadFires";
        private string kernelWriteBurnMapName = "CSWriteBurnMap";

        [Header("Spread area limitation")]
        [SerializeField] private bool onlySpreadOnGrass = false;
#if LEMIGAME_INTERACTIVE_GRASS
        [Tooltip("Limit spread area by interactive grass shrink strength. If true, will not spread if the shrink strength is greater than maxShrinkStrengthForSpread.")]
        [SerializeField] private bool limitByShrinkStrength = true;
        [Range(0f, 1f)][SerializeField] private float maxShrinkStrengthForSpread = 0.6f;
#endif

        [Header("VFX Graph Properties")]
        private string positionBufferProp = "PositionBuffer";
        private string positionCountProp = "PositionCount";
        private string fireDataBufferProp = "FireDataBuffer";
        private string spawnCountProp = "SpawnCount";
        private string spawnEventName = "UpdateFire";
        [SerializeField] private float baseLifeTime = 3f;

        // GPU state structure
        private struct FireStateGPU
        {
            public Vector2Int pixelPos;
            public float startTime;
            public float durationMul;
            public float spawnScale;
            public float currentSpreadChance;
            public float nextSpreadTime;
            public int totalSpawns;
            public int totalChecks;
            public int isActive;
            public int fireID;
        }

        // VFX single burst data
        private struct FireVFXData
        {
            public Vector3 position;
            public float lifeTime;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NewFireDataCPU
        {
            public int px;
            public int py;
            public float startTime;
            public float durationMul;
            public float chance;
            public float cooldownUntil;
        }

        // GPU Buffers
        private GraphicsBuffer fireStateBuffer;
        private GraphicsBuffer visiblePositionsBuffer; // Append: float3 (Single Burst temp)
        private GraphicsBuffer fireDataBuffer;         // Append: float (lifeTime)
        private GraphicsBuffer countBuffer;
        private GraphicsBuffer spreadResultBuffer;     // Append: x,y,spreadChance,delay
        private GraphicsBuffer fireStateListCPU;
        private RenderTexture stateMap;                // CA state map: R=startTime, G=durationMul, B=currentChance, A=cooldown
        private GraphicsBuffer newFiresBuffer;         // CA: Append/Consume new fire list
        private Material burnMaterial;                 // Material for drawing burn pixels
        
        // Double buffered cache system (completely non-blocking)
        private Texture2D burnMapCacheA;               // Cache A: current cache for reading (stable)
        private Texture2D burnMapCacheB;               // Cache B: background cache for updating
        private bool usingCacheA = true;              // Which cache is currently used for reading
        
        // Async GPU readback requests
        private AsyncGPUReadbackRequest newFiresCountRequest;
        private AsyncGPUReadbackRequest newFiresDataRequest;
        private AsyncGPUReadbackRequest fireStatesRequest;
        private AsyncGPUReadbackRequest spreadResultsRequest;
        private AsyncGPUReadbackRequest burnMapCacheRequest;
        
        // Async readback pending flags
        private bool isNewFiresCountPending = false;
        private bool isNewFiresDataPending = false;
        private bool isFireStatesPending = false;
        private bool isSpreadResultsPending = false;
        private bool isBurnMapCachePending = false;
        
        // Temporary storage for async readback data
        private int lastNewFiresCount = 0; // Store count between async readback stages
        
        // Performance optimization: Batch GL draw operations
        private List<System.Tuple<Vector2Int, int, Color>> pendingPixelDraws = new List<System.Tuple<Vector2Int, int, Color>>();
        private bool isBatchingDraws = false;
        
        // Performance optimization: Cache for interactive grass strength
#if LEMIGAME_INTERACTIVE_GRASS
        private Texture2D interactiveMapCache;
        private float lastInteractiveMapCacheUpdate = -1f;
        private const float INTERACTIVE_MAP_CACHE_UPDATE_INTERVAL = 0.5f; // Update cache every 0.5 seconds
#endif

        private readonly List<FireVFXData> pendingVfxSpawns = new List<FireVFXData>();
        
        // Performance optimization: Cache world positions to avoid expensive Raycast calls
        private Dictionary<Vector2Int, Vector3> cachedWorldPositions = new Dictionary<Vector2Int, Vector3>();
        
        // Performance optimization: Cache height values to avoid repeated terrain.SampleHeight calls
        private Dictionary<Vector2Int, float> cachedHeights = new Dictionary<Vector2Int, float>();
        
        private float lastVFXUpdateTime = -1f;
        private const float VFX_UPDATE_INTERVAL = 0.25f; // Update VFX every 0.25 seconds to reduce expensive operations

        // Lightweight CPU management data
        private List<FireStateGPU> activeFiresList = new List<FireStateGPU>();
        private HashSet<Vector2Int> activeFirePixels = new HashSet<Vector2Int>();
        private int nextFireID = 0;

        // Kernel IDs
        private int kernelUpdateID;
        private int kernelSpreadID;
        private int kernelWriteBurnMapID;
        private int kernelGridUpdateID;
        private int kernelGridSpreadID;
        private int kernelGridApplyID;
        private int kernelSeedID;
        private int kernelClearStateMapRegionID;

        // VFX property IDs
        private int positionBufferPropID;
        private int positionCountPropID;
        private int fireDataBufferPropID;
        private int spawnCountPropID;
        
        // Trigger-like events for objects entering/exiting fire
        public System.Action<Collider> OnFireEnter;
        public System.Action<Collider> OnFireExit;
        public System.Action<Collider> OnFireStay;

        void Start()
        {
            if (!vfx) vfx = GetComponent<VisualEffect>();
            if (!camOrTarget && Camera.main) camOrTarget = Camera.main.transform;
            if (vfx && !vfx.isActiveAndEnabled) vfx.Play();

            // Initialize terrain and burn map
            InitializeTerrainData();

            InitializeBurnMap();
            InitializeGPU();
            InitializeWorldParams();
            
            // Initialize first cache synchronously (one-time blocking for initialization only)
            InitializeBurnMapCacheSync();
            
            // Only start coroutines if initialization was successful
            if (burnMap != null && fireComputeShader != null)
            {
                StartCoroutine(FireUpdateCoroutine());
                StartCoroutine(FireSpreadCoroutine());
                StartCoroutine(FireDetectionCoroutine());
                Debug.Log("FireSpreadController: Initialization complete. Fire system is ready.");
            }
            else
            {
                Debug.LogError("FireSpreadController: Initialization failed. Check Inspector settings for burnMap and fireComputeShader.");
            }
        }
        
        /// <summary>
        /// Initialize double buffer caches (completely async, no blocking).
        /// Both caches start as black, then async readback fills them.
        /// </summary>
        private void InitializeBurnMapCacheSync()
        {
            if (burnMap == null) return;
            
            int width = burnMap.width;
            int height = burnMap.height;
            
            // Create first cache (non-blocking initialization)
            if (burnMapCacheA == null)
            {
                burnMapCacheA = new Texture2D(width, height, TextureFormat.RGBA32, false);
                
                // Initialize to black (non-blocking)
                Color32[] blackPixels = new Color32[width * height];
                for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
                burnMapCacheA.SetPixelData(blackPixels, 0);
                burnMapCacheA.Apply();
            }
            
            // Create second cache (non-blocking initialization)
            if (burnMapCacheB == null)
            {
                burnMapCacheB = new Texture2D(width, height, TextureFormat.RGBA32, false);
                
                // Initialize to black (non-blocking)
                Color32[] blackPixels = new Color32[width * height];
                for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
                burnMapCacheB.SetPixelData(blackPixels, 0);
                burnMapCacheB.Apply();
            }
            
            usingCacheA = true;
            lastBurnMapCacheUpdate = Time.time;
            // IsBurnMapCacheReady is auto-computed from cache existence, no need to set
            
            // Request async readback immediately (non-blocking)
            RequestBurnMapCacheAsync(forceUpdate: true);
        }

        /// <summary>
        /// Initialize terrain data from direct parameters or scene search.
        /// </summary>
        private void InitializeTerrainData()
        {
            // If no terrain assigned, try to find in scene
            if (terrain == null)
            {
                terrain = FindFirstObjectByType<Terrain>();
                if (terrain == null)
                {
                    Debug.LogWarning("FireSpreadController: No Terrain found. Please assign a Terrain in Inspector.");
                }
            }

            // Initialize terrain info
            if (terrain != null)
            {
                terrainSize = terrain.terrainData.size;
                terrainPosition = terrain.transform.position;
            }
        }

        private void InitializeBurnMap()
        {
            if (burnMap == null)
            {
                Debug.LogWarning("FireSpreadController: burnMap is null. Cannot initialize burn map. Please assign burnMap in Inspector.");
                return;
            }

            // Create burn material
            Shader drawShader = Shader.Find("Hidden/Internal-Colored");
            if (drawShader == null)
            {
                Debug.LogError("FireSpreadController: Cannot find 'Hidden/Internal-Colored' shader. Burn map drawing will not work.");
                return;
            }
            burnMaterial = new Material(drawShader);
            burnMaterial.hideFlags = HideFlags.HideAndDontSave;

            // Validate burn map (burnMap is already checked to be non-null above)
            if (!burnMap.IsCreated())
            {
                Debug.LogWarning("FireSpreadController: burnMap is not created. Attempting to create it.");
                burnMap.Create();
            }
            
            // Ensure burn map has random write enabled
            if (!burnMap.enableRandomWrite)
            {
                Debug.LogWarning("FireSpreadController: burnMap.enableRandomWrite is false. This may cause issues with compute shader writes.");
            }
        }

        private void InitializeGPU()
        {
            if (fireComputeShader == null)
            {
                Debug.LogError("FireSpreadController: fireComputeShader is null. Please assign a ComputeShader in Inspector.");
                return;
            }

            int fireStateSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(FireStateGPU));
            int capacity = settings.maxFireCount;
            fireStateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, fireStateSize);

            visiblePositionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 3);
            fireDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 1);

            countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint));
            spreadResultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, capacity, sizeof(float) * 4);
            fireStateListCPU = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, fireStateSize);

            // Kernels
            kernelUpdateID = fireComputeShader.FindKernel(kernelUpdateName);
            kernelSpreadID = fireComputeShader.FindKernel(kernelSpreadName);
            if (fireComputeShader.HasKernel(kernelWriteBurnMapName))
            {
                kernelWriteBurnMapID = fireComputeShader.FindKernel(kernelWriteBurnMapName);
            }
            if (fireComputeShader.HasKernel("CSGridUpdate")) kernelGridUpdateID = fireComputeShader.FindKernel("CSGridUpdate");
            if (fireComputeShader.HasKernel("CSGridSpread")) kernelGridSpreadID = fireComputeShader.FindKernel("CSGridSpread");
            if (fireComputeShader.HasKernel("CSGridApplyNewFires")) kernelGridApplyID = fireComputeShader.FindKernel("CSGridApplyNewFires");
            if (fireComputeShader.HasKernel("CSSeedFire")) kernelSeedID = fireComputeShader.FindKernel("CSSeedFire");
            if (fireComputeShader.HasKernel("CSClearStateMapRegion")) kernelClearStateMapRegionID = fireComputeShader.FindKernel("CSClearStateMapRegion");

            // Bind buffers
            fireComputeShader.SetBuffer(kernelUpdateID, "_FireStates", fireStateBuffer);
            fireComputeShader.SetBuffer(kernelUpdateID, "_VisiblePositions", visiblePositionsBuffer);
            fireComputeShader.SetBuffer(kernelUpdateID, "_FireData", fireDataBuffer);

            fireComputeShader.SetBuffer(kernelSpreadID, "_FireStates", fireStateBuffer);
            fireComputeShader.SetBuffer(kernelSpreadID, "_SpreadResults", spreadResultBuffer);

            if (fireComputeShader.HasKernel(kernelWriteBurnMapName))
            {
                fireComputeShader.SetBuffer(kernelWriteBurnMapID, "_FireStates", fireStateBuffer);
            }

            // CA assets
            if (burnMap != null)
            {
                if (stateMap != null)
                {
                    stateMap.Release();
                    stateMap = null;
                }
                int width = burnMap.width;
                int height = burnMap.height;
                stateMap = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
                stateMap.enableRandomWrite = true;
                stateMap.name = "FireStateMap";
                stateMap.Create();

                int newFireStride = sizeof(int) * 2 + sizeof(float) * 4; // int2 + float4
                newFiresBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, width * height, newFireStride);

                if (kernelGridUpdateID != 0)
                {
                    fireComputeShader.SetTexture(kernelGridUpdateID, "_StateMap", stateMap);
                    fireComputeShader.SetTexture(kernelGridUpdateID, "_BurnMapRW", burnMap);
                }
                if (kernelGridSpreadID != 0)
                {
                    fireComputeShader.SetTexture(kernelGridSpreadID, "_StateMap", stateMap);
                    fireComputeShader.SetTexture(kernelGridSpreadID, "_BurnMap", burnMap);
                    fireComputeShader.SetBuffer(kernelGridSpreadID, "_NewFiresAppend", newFiresBuffer);
                    if (grassMask != null)
                        fireComputeShader.SetTexture(kernelGridSpreadID, "_GrassMask", grassMask);
                }
                if (kernelGridApplyID != 0)
                {
                    fireComputeShader.SetTexture(kernelGridApplyID, "_StateMap", stateMap);
                    fireComputeShader.SetBuffer(kernelGridApplyID, "_NewFiresConsume", newFiresBuffer);
                }
                if (kernelSeedID != 0)
                {
                    fireComputeShader.SetTexture(kernelSeedID, "_StateMap", stateMap);
                }
                if (kernelClearStateMapRegionID != 0)
                {
                    fireComputeShader.SetTexture(kernelClearStateMapRegionID, "_StateMap", stateMap);
                }
            }

            // VFX props
            positionBufferPropID = Shader.PropertyToID(positionBufferProp);
            positionCountPropID = Shader.PropertyToID(positionCountProp);
            fireDataBufferPropID = Shader.PropertyToID(fireDataBufferProp);
            spawnCountPropID = Shader.PropertyToID(spawnCountProp);
        }

        private void InitializeWorldParams()
        {
            if (terrain == null || burnMap == null) return;

            // Auto derive height scale/offset from terrain
            heightScale = terrain.terrainData.size.y;
            heightOffset = terrain.transform.position.y;
            
            // Performance optimization: Auto-generate heightmap texture from terrain if not assigned
            // This allows direct texture sampling instead of using terrain.SampleHeight() which is slower
            if (heightTex == null && terrain != null)
            {
                // Try to get heightmap from terrain data
                // Note: Unity doesn't directly expose heightmap texture, but we can use terrain.SampleHeight
                // For better performance, consider baking heightmap texture using TerrainTextureBaker
                // For now, we'll use terrain.SampleHeight but cache results
            }

            // Set terrain origin and size to compute shader (XZ plane only)
            fireComputeShader.SetVector("_OriginXZ", new Vector2(terrainPosition.x, terrainPosition.z));
            fireComputeShader.SetVector("_SizeXZ", new Vector2(terrainSize.x, terrainSize.z));
            fireComputeShader.SetInt("_TextureWidth", burnMap.width);
            fireComputeShader.SetInt("_TextureHeight", burnMap.height);

            // Camera culling params (initialized with default values)
            var camPos = camOrTarget ? camOrTarget.position : (Camera.main ? Camera.main.transform.position : Vector3.zero);
            fireComputeShader.SetVector("_CamPos", camPos);
            fireComputeShader.SetFloat("_Radius2", radius * radius);
            fireComputeShader.SetFloat("_MaxUpdateDistance2", maxUpdateDistance * maxUpdateDistance);
            fireComputeShader.SetInt("_EnableCameraCulling", enableCameraCulling ? 1 : 0);

            // Height texture initial params
            fireComputeShader.SetInt("_UseHeightTex", heightTex ? 1 : 0);
            fireComputeShader.SetFloat("_HeightScale", heightScale);
            fireComputeShader.SetFloat("_HeightOffset", heightOffset);
            if (heightTex) fireComputeShader.SetTexture(kernelUpdateID, "_HeightTex", heightTex);
        }

        /// <summary>
        /// Ignite at a world position using default initial chance.
        /// </summary>
        public void StartFireAt(Vector3 worldPos)
        {
            StartFireAt(worldPos, -1f);
        }

        /// <summary>
        /// Ignite at world position with optional initial chance (<0 uses default settings.initialSpreadChance).
        /// </summary>
        public void StartFireAt(Vector3 worldPos, float initialChance)
        {
            if (burnMap == null)
            {
                Debug.LogWarning("FireSpreadController.StartFireAt: burnMap is null. Cannot start fire.");
                return;
            }

            if (terrain == null)
            {
                Debug.LogWarning("FireSpreadController.StartFireAt: terrain is null. Cannot convert world position to pixel.");
                return;
            }

            if (activeFirePixels.Count >= settings.maxFireCount)
            {
                Debug.LogWarning($"FireSpreadController.StartFireAt: Maximum fire count ({settings.maxFireCount}) reached.");
                return;
            }

            Vector2Int pixel = WorldPosToPixel(worldPos);
            if (activeFirePixels.Contains(pixel))
            {
                Debug.Log($"FireSpreadController.StartFireAt: Fire already exists at pixel ({pixel.x}, {pixel.y}).");
                return;
            }

            if (!IsAreaAllowedForSpread(pixel.x, pixel.y))
            {
                Debug.Log($"FireSpreadController.StartFireAt: Area at pixel ({pixel.x}, {pixel.y}) is not allowed for spread.");
                return;
            }

            // Snap to ground using heightmap (no Raycast)
            worldPos.y = GetHeightAtWorldPos(worldPos.x, worldPos.z);

            int fireID = nextFireID++;
            float durationMul = Random.Range(settings.randomRange.x, settings.randomRange.y);
            float spawnScale = Random.Range(settings.randomRange.x, settings.randomRange.y);
            float startTimeOffset = 0f; // Initial fire delay is 0

            // CA path if available; otherwise, fallback to buffer path
            if (stateMap == null || kernelSeedID == 0)
            {
                FireStateGPU gpuState = new FireStateGPU
                {
                    pixelPos = pixel,
                    startTime = Time.time - startTimeOffset,
                    durationMul = durationMul,
                    spawnScale = spawnScale,
                    currentSpreadChance = (initialChance >= 0f ? initialChance : settings.initialSpreadChance),
                    nextSpreadTime = Time.time + Random.Range(0.1f, 0.7f),
                    totalSpawns = 0,
                    totalChecks = 0,
                    isActive = 1,
                    fireID = fireID
                };

                activeFiresList.Add(gpuState);
                activeFirePixels.Add(pixel);
                
                // Cache world position to avoid expensive computation later
                cachedWorldPositions[pixel] = worldPos;

                // Initial mark on burn map (immediate flush for critical initialization)
                SetPixelBurnValue(pixel.x, pixel.y, 0.1f, 0f, 0f, 1);
                FlushPendingPixelDraws(); // Immediate flush to ensure fire starts correctly

                // Queue a Single Burst spawn
                float totalDuration = (settings.burnDuration + settings.waitDuration + settings.recoverPrepDuration + settings.recoverGrowDuration) * durationMul;
                float lifeTime = Mathf.Max(0.1f, (baseLifeTime > 0f ? baseLifeTime : totalDuration) + Random.Range(-1f, 1f));
                pendingVfxSpawns.Add(new FireVFXData { position = worldPos, lifeTime = lifeTime });
                
                // Reset timer - new fire was created
                lastFireUpdateTime = Time.time;
                
                Debug.Log($"FireSpreadController.StartFireAt: Successfully created fire at pixel ({pixel.x}, {pixel.y}) in buffer mode. World pos: {worldPos}");
                return;
            }

            // CA mode: seed a pixel directly on GPU state map
            if (fireComputeShader == null)
            {
                Debug.LogError("FireSpreadController.StartFireAt: fireComputeShader is null. Cannot seed fire in CA mode.");
                return;
            }
            float initChance = (initialChance >= 0f ? initialChance : settings.initialSpreadChance);
            float cooldown = Time.time + Random.Range(settings.randomRange.x * 0.5f, settings.randomRange.y * 1.5f); // Scaled from randomRange
            fireComputeShader.SetInt("_TextureWidth", burnMap.width);
            fireComputeShader.SetInt("_TextureHeight", burnMap.height);
            fireComputeShader.SetInt("_SeedPixelX", pixel.x);
            fireComputeShader.SetInt("_SeedPixelY", pixel.y);
            fireComputeShader.SetFloat("_SeedStartTime", Time.time - startTimeOffset);
            fireComputeShader.SetFloat("_SeedDurationMul", durationMul);
            fireComputeShader.SetFloat("_SeedInitialChance", initChance);
            fireComputeShader.SetFloat("_SeedCooldownUntil", cooldown);
            fireComputeShader.Dispatch(kernelSeedID, 1, 1, 1);
            
            // Reset timer - new fire was created
            lastFireUpdateTime = Time.time;
            
            Debug.Log($"FireSpreadController.StartFireAt: Successfully seeded fire at pixel ({pixel.x}, {pixel.y}) in CA mode.");

            // Initial mark on burn map (immediate flush for critical initialization)
            SetPixelBurnValue(pixel.x, pixel.y, 0.1f, 0f, 0f, 1);
            FlushPendingPixelDraws(); // Immediate flush to ensure fire starts correctly
            
            // CA mode also needs VFX data - queue a Single Burst spawn
            // Note: No duplicate check here - GPU-side stateMap should handle duplicates
            // Spread fires are handled through FireSpreadCoroutine, not through StartFireAt
            float caTotalDuration = (settings.burnDuration + settings.waitDuration + settings.recoverPrepDuration + settings.recoverGrowDuration) * durationMul;
            float caLifeTime = Mathf.Max(0.1f, (baseLifeTime > 0f ? baseLifeTime : caTotalDuration) + Random.Range(-1f, 1f));
            pendingVfxSpawns.Add(new FireVFXData { position = worldPos, lifeTime = caLifeTime });
            
            // Note: In CA mode, we don't track activeFirePixels here to avoid duplicate VFX spawns
            // VFX is handled through pendingVfxSpawns only, matching the old version behavior
        }

        // Periodic GPU update for VFX feeding and CA updates
        private IEnumerator FireUpdateCoroutine()
        {
            while (true)
            {
                if (updateEveryNFrames > 1 && (Time.frameCount % updateEveryNFrames != 0)) { yield return null; continue; }
                // Performance optimization: Increase update interval slightly to reduce CPU overhead
                yield return new WaitForSeconds(0.15f);
                
                // Request async cache update (non-blocking)
                RequestBurnMapCacheAsync();

                // Single burst: send pending spawns to VFX
                if (vfx != null && pendingVfxSpawns.Count > 0)
                {
                    int spawnCount = Mathf.Min(pendingVfxSpawns.Count, settings.maxFireCount);
                    var positions = new Vector3[spawnCount];
                    var lifetimes = new float[spawnCount];
                    for (int i = 0; i < spawnCount; i++)
                    {
                        positions[i] = pendingVfxSpawns[i].position;
                        lifetimes[i] = pendingVfxSpawns[i].lifeTime;
                    }
                    visiblePositionsBuffer.SetData(positions, 0, 0, spawnCount);
                    fireDataBuffer.SetData(lifetimes, 0, 0, spawnCount);
                    vfx.SetGraphicsBuffer(positionBufferPropID, visiblePositionsBuffer);
                    vfx.SetGraphicsBuffer(fireDataBufferPropID, fireDataBuffer);
                    vfx.SetUInt(positionCountPropID, (uint)spawnCount);
                    vfx.SetUInt(spawnCountPropID, (uint)spawnCount);
                    vfx.SendEvent(spawnEventName);
                    if (pendingVfxSpawns.Count == spawnCount) pendingVfxSpawns.Clear(); else pendingVfxSpawns.RemoveRange(0, spawnCount);
                }

                // Flush any pending pixel draws before GPU operations
                FlushPendingPixelDraws();
                
                if (burnMap == null) { UpdateBurnMap(); continue; }
                int width = burnMap.width;
                int height = burnMap.height;
                
                // CA grid update writes full burn map
                if (kernelGridUpdateID != 0 && stateMap != null)
                {
                    // Performance optimization: Skip expensive operations if no active fires
                    // CSGridUpdate will early-return for empty pixels (line 373), so dispatch is still needed
                    // but we can skip VFX updates and other expensive operations
                    bool hasActiveFires = activeFirePixels.Count > 0 || pendingVfxSpawns.Count > 0;
                    
                    if (!hasActiveFires)
                    {
                        // Still dispatch CSGridUpdate to handle recovery phase pixels, but skip VFX updates
                        fireComputeShader.SetFloat("_CurrentTime", Time.time);
                        fireComputeShader.SetFloat("_BurnDuration", settings.burnDuration);
                        fireComputeShader.SetFloat("_WaitDuration", settings.waitDuration);
                        fireComputeShader.SetFloat("_RecoverPrepDuration", settings.recoverPrepDuration);
                        fireComputeShader.SetFloat("_RecoverGrowDuration", settings.recoverGrowDuration);
                        fireComputeShader.SetInt("_TextureWidth", width);
                        fireComputeShader.SetInt("_TextureHeight", height);
                        int dispatchGx = Mathf.CeilToInt(width / 8.0f);
                        int dispatchGy = Mathf.CeilToInt(height / 8.0f);
                        fireComputeShader.Dispatch(kernelGridUpdateID, Mathf.Max(1, dispatchGx), Mathf.Max(1, dispatchGy), 1);
                        continue; // Skip VFX updates and other expensive operations
                    }

                    // Performance optimization: Update camera position for distance culling in compute shader
                    var cam = camOrTarget != null ? camOrTarget : (Camera.main != null ? Camera.main.transform : null);
                    if (cam != null)
                    {
                        fireComputeShader.SetVector("_CamPos", cam.position);
                        fireComputeShader.SetFloat("_Radius2", radius * radius);
                        fireComputeShader.SetFloat("_MaxUpdateDistance2", maxUpdateDistance * maxUpdateDistance);
                        fireComputeShader.SetInt("_EnableCameraCulling", enableCameraCulling ? 1 : 0);
                    }

                    fireComputeShader.SetFloat("_CurrentTime", Time.time);
                    fireComputeShader.SetFloat("_BurnDuration", settings.burnDuration);
                    fireComputeShader.SetFloat("_WaitDuration", settings.waitDuration);
                    fireComputeShader.SetFloat("_RecoverPrepDuration", settings.recoverPrepDuration);
                    fireComputeShader.SetFloat("_RecoverGrowDuration", settings.recoverGrowDuration);
                    fireComputeShader.SetInt("_TextureWidth", width);
                    fireComputeShader.SetInt("_TextureHeight", height);
                    int gx = Mathf.CeilToInt(width / 8.0f);
                    int gy = Mathf.CeilToInt(height / 8.0f);
                    fireComputeShader.Dispatch(kernelGridUpdateID, Mathf.Max(1, gx), Mathf.Max(1, gy), 1);
                    
                    // CA mode: VFX is handled through pendingVfxSpawns in FireSpreadCoroutine
                    // Do not call UpdateVFXFromCAState here to avoid duplicate VFX spawns
                    // This matches the old version behavior where CA mode only uses pendingVfxSpawns
                    continue;
                }

                // Flush any pending pixel draws before updating
                FlushPendingPixelDraws();
                
                if (activeFiresList.Count == 0) { UpdateBurnMap(); continue; }

                // Sync CPU list to GPU
                SyncFireStatesToGPU();

                // Update common params
                fireComputeShader.SetFloat("_CurrentTime", Time.time);
                fireComputeShader.SetFloat("_BurnDuration", settings.burnDuration);
                fireComputeShader.SetFloat("_WaitDuration", settings.waitDuration);
                fireComputeShader.SetFloat("_RecoverPrepDuration", settings.recoverPrepDuration);
                fireComputeShader.SetFloat("_RecoverGrowDuration", settings.recoverGrowDuration);
                fireComputeShader.SetInt("_TextureWidth", width);
                fireComputeShader.SetInt("_TextureHeight", height);

                if (heightTex != null)
                {
                    fireComputeShader.SetInt("_UseHeightTex", 1);
                    fireComputeShader.SetFloat("_HeightScale", heightScale);
                    fireComputeShader.SetFloat("_HeightOffset", heightOffset);
                    fireComputeShader.SetTexture(kernelUpdateID, "_HeightTex", heightTex);
                }
                else
                {
                    fireComputeShader.SetInt("_UseHeightTex", 0);
                }

                // Camera culling params
                var camPos = camOrTarget ? camOrTarget.position : (Camera.main ? Camera.main.transform.position : Vector3.zero);
                fireComputeShader.SetVector("_CamPos", camPos);
                fireComputeShader.SetFloat("_Radius2", radius * radius);

                // Single burst (redundant safety if pending remained)
                if (vfx != null && pendingVfxSpawns.Count > 0)
                {
                    int spawnCount = Mathf.Min(pendingVfxSpawns.Count, settings.maxFireCount);
                    var positions = new Vector3[spawnCount];
                    var lifetimes = new float[spawnCount];
                    for (int i = 0; i < spawnCount; i++)
                    {
                        positions[i] = pendingVfxSpawns[i].position;
                        lifetimes[i] = pendingVfxSpawns[i].lifeTime;
                    }
                    visiblePositionsBuffer.SetData(positions, 0, 0, spawnCount);
                    fireDataBuffer.SetData(lifetimes, 0, 0, spawnCount);
                    vfx.SetGraphicsBuffer(positionBufferPropID, visiblePositionsBuffer);
                    vfx.SetGraphicsBuffer(fireDataBufferPropID, fireDataBuffer);
                    vfx.SetUInt(positionCountPropID, (uint)spawnCount);
                    vfx.SetUInt(spawnCountPropID, (uint)spawnCount);
                    vfx.SendEvent(spawnEventName);
                    if (pendingVfxSpawns.Count == spawnCount) pendingVfxSpawns.Clear(); else pendingVfxSpawns.RemoveRange(0, spawnCount);
                }

                // Flush any pending pixel draws before updating burn map
                FlushPendingPixelDraws();
                
                UpdateBurnMap();
            }
        }

        // Performance optimization: Track last fire update time to skip coroutine when all fires are done
        private float lastFireUpdateTime = -1f;
        private float maxFireDuration = 0f; // Cached max fire duration

        // Periodic GPU spread
        // Optimization: Use async GPU readback to avoid blocking main thread
        private IEnumerator FireSpreadCoroutine()
        {
            int spreadCounter = 0;

            // Calculate max fire duration once (longest possible fire lifecycle)
            maxFireDuration = (settings.burnDuration + settings.waitDuration + settings.recoverPrepDuration + settings.recoverGrowDuration) * settings.randomRange.y;

            while (true)
            {
                yield return new WaitForSeconds(settings.spreadInterval);

                // Performance optimization: Skip if no fires have been updated for max duration
                // This means all fires have completed their lifecycle
                if (lastFireUpdateTime >= 0f && pendingVfxSpawns.Count == 0)
                {
                    float timeSinceLastUpdate = Time.time - lastFireUpdateTime;
                    if (timeSinceLastUpdate >= maxFireDuration)
                    {
                        continue; // Skip this iteration - all fires are done
                    }
                }

                    // CA grid spread path
                if (kernelGridSpreadID != 0 && kernelGridApplyID != 0 && stateMap != null && burnMap != null)
                {
                    // Note: In CA mode, we don't track activeFirePixels, so we can't check it here
                    // The GPU-side stateMap will handle fire state, so we always dispatch spread logic
                    // This matches the old version behavior

                    // Performance optimization: Update camera position for distance culling
                    var cam = camOrTarget != null ? camOrTarget : (Camera.main != null ? Camera.main.transform : null);
                    if (cam != null)
                    {
                        fireComputeShader.SetVector("_CamPos", cam.position);
                        fireComputeShader.SetFloat("_MaxUpdateDistance2", maxUpdateDistance * maxUpdateDistance);
                        fireComputeShader.SetInt("_EnableCameraCulling", enableCameraCulling ? 1 : 0);
                    }

                    int width = burnMap.width;
                    int height = burnMap.height;
                    fireComputeShader.SetFloat("_CurrentTime", Time.time);
                    fireComputeShader.SetFloat("_BurnDuration", settings.burnDuration);
                    fireComputeShader.SetFloat("_WaitDuration", settings.waitDuration);
                    fireComputeShader.SetFloat("_RecoverPrepDuration", settings.recoverPrepDuration);
                    fireComputeShader.SetFloat("_RecoverGrowDuration", settings.recoverGrowDuration);
                    fireComputeShader.SetFloat("_SpreadChanceDecay", settings.spreadChanceDecay);
                    fireComputeShader.SetFloat("_MinSpreadChance", settings.minSpreadChance);
                    fireComputeShader.SetInt("_MaxSpreadAttempts", settings.maxSpreadAttempts);
                    fireComputeShader.SetFloat("_SpreadDelayMin", settings.spreadDelayRange.x);
                    fireComputeShader.SetFloat("_SpreadDelayMax", settings.spreadDelayRange.y);
                    fireComputeShader.SetFloat("_DurationMulMin", settings.randomRange.x);
                    fireComputeShader.SetFloat("_DurationMulMax", settings.randomRange.y);
                    fireComputeShader.SetInt("_TextureWidth", width);
                    fireComputeShader.SetInt("_TextureHeight", height);
                    fireComputeShader.SetInt("_UseGrassMask", (onlySpreadOnGrass && grassMask != null) ? 1 : 0);

                    newFiresBuffer.SetCounterValue(0);

                    int gx = Mathf.CeilToInt(width / 8.0f);
                    int gy = Mathf.CeilToInt(height / 8.0f);
                    fireComputeShader.Dispatch(kernelGridSpreadID, Mathf.Max(1, gx), Mathf.Max(1, gy), 1);

                    spreadCounter++;

                    // Skip some GPU->CPU reads based on settings to reduce blocking
                    bool shouldRead = (spreadCounter % settings.gpuReadSkipFrames == 0);

                    if (shouldRead)
                    {
                        // Async read count (non-blocking GPU->CPU transfer)
                        RequestNewFiresCountAsync();
                    }
                    else
                    {
                        // Even without reading data, apply new fires to state map
                        // Use conservative but sufficient group count to process all possible fires
                        // Consume() returns invalid data when buffer is empty, shader will skip automatically
                        int maxPossibleFires = Mathf.Min(settings.maxFireCount * 2, width * height / 4); // Conservative estimate
                        int groups = Mathf.CeilToInt(maxPossibleFires / 64.0f);
                        fireComputeShader.Dispatch(kernelGridApplyID, Mathf.Max(1, groups), 1, 1);
                    }
                    continue;
                }
            }
        }





        private void SyncFireStatesToGPU()
        {
            if (activeFiresList.Count == 0) return;

            float currentTime = Time.time;
            for (int i = activeFiresList.Count - 1; i >= 0; i--)
            {
                var fire = activeFiresList[i];
                float elapsedTime = currentTime - fire.startTime;
                float totalDuration = (settings.burnDuration + settings.waitDuration + settings.recoverPrepDuration + settings.recoverGrowDuration) * fire.durationMul;

                if (elapsedTime >= totalDuration)
                {
                    activeFirePixels.Remove(fire.pixelPos);
                    cachedWorldPositions.Remove(fire.pixelPos); // Remove from cache
                    SetPixelBurnValue(fire.pixelPos.x, fire.pixelPos.y, 0f, 0f, 0f, 1);
                    activeFiresList.RemoveAt(i);
                }
            }
            
            // Flush pending pixel draws after cleanup
            if (pendingPixelDraws.Count > 0)
            {
                FlushPendingPixelDraws();
            }

            if (activeFiresList.Count > 0)
            {
                FireStateGPU[] states = activeFiresList.ToArray();
                fireStateBuffer.SetData(states, 0, 0, activeFiresList.Count);
            }
        }

        /// <summary>
        /// Request async readback of new fires count from GPU.
        /// </summary>
        private void RequestNewFiresCountAsync()
        {
            if (isNewFiresCountPending || countBuffer == null) return;
            
            // Copy count to countBuffer first
            GraphicsBuffer.CopyCount(newFiresBuffer, countBuffer, 0);
            
            // Request async readback
            isNewFiresCountPending = true;
            newFiresCountRequest = AsyncGPUReadback.Request(countBuffer, OnNewFiresCountReadback);
        }
        
        /// <summary>
        /// Callback for async readback of new fires count.
        /// </summary>
        private void OnNewFiresCountReadback(AsyncGPUReadbackRequest request)
        {
            isNewFiresCountPending = false;
            
            if (request.hasError)
            {
                Debug.LogError("FireSpreadController: Failed to async read new fires count");
                return;
            }
            
            NativeArray<uint> countData = request.GetData<uint>();
            if (countData.Length < 1)
            {
                Debug.LogError("FireSpreadController: Invalid count data length");
                return;
            }
            
            uint count = countData[0];
            
            // Validate count
            if (count > (uint)settings.maxFireCount * 2)
            {
                Debug.LogWarning($"FireSpreadController: Count {count} exceeds reasonable limit, clamping to {settings.maxFireCount * 2}");
                count = (uint)settings.maxFireCount * 2;
            }
            
            if (count > 0)
            {
                // Store count for next stage
                lastNewFiresCount = (int)count;
                
                // Reset timer - new fires were created
                lastFireUpdateTime = Time.time;
                
                // Continue to async read data
                RequestNewFiresDataAsync((int)count);
            }
        }
        
        /// <summary>
        /// Request async readback of new fires data from GPU.
        /// </summary>
        private void RequestNewFiresDataAsync(int count)
        {
            if (isNewFiresDataPending || newFiresBuffer == null) return;
            
            // Validate count
            if (count <= 0 || count > settings.maxFireCount * 2)
            {
                Debug.LogWarning($"FireSpreadController: Invalid count for async readback: {count}");
                return;
            }
            
            // Request async readback
            isNewFiresDataPending = true;
            newFiresDataRequest = AsyncGPUReadback.Request(newFiresBuffer, OnNewFiresDataReadback);
        }
        
        /// <summary>
        /// Callback for async readback of new fires data.
        /// </summary>
        private void OnNewFiresDataReadback(AsyncGPUReadbackRequest request)
        {
            isNewFiresDataPending = false;
            
            if (request.hasError)
            {
                Debug.LogError("FireSpreadController: Failed to async read new fires data");
                return;
            }
            
            int expectedCount = lastNewFiresCount;
            if (expectedCount <= 0)
            {
                Debug.LogWarning("FireSpreadController: Expected count is invalid");
                return;
            }
            
            NativeArray<NewFireDataCPU> data = request.GetData<NewFireDataCPU>();
            
            // Use GetSubArray to get only the data we need (ensure length match)
            int actualCount = Mathf.Min(expectedCount, data.Length);
            if (actualCount < expectedCount)
            {
                Debug.LogWarning($"FireSpreadController: Data length mismatch. Expected {expectedCount}, got {data.Length}, using {actualCount}");
            }
            
            NativeArray<NewFireDataCPU> actualData = data.GetSubArray(0, actualCount);
            
            // Process data
            ProcessNewFiresData(actualData);
            
            // Apply new fires to state map
            int groups = Mathf.CeilToInt(actualCount / 64.0f);
            fireComputeShader.Dispatch(kernelGridApplyID, Mathf.Max(1, groups), 1, 1);
        }
        
        /// <summary>
        /// Process new fires data from async readback.
        /// </summary>
        private void ProcessNewFiresData(NativeArray<NewFireDataCPU> newFires)
        {
            for (int i = 0; i < newFires.Length; i++)
            {
                int x = newFires[i].px;
                int y = newFires[i].py;
                Vector2Int pixel = new Vector2Int(x, y);
                
                // Check if this pixel is already in pendingVfxSpawns to avoid duplicates
                bool alreadyPending = false;
                for (int j = 0; j < pendingVfxSpawns.Count; j++)
                {
                    Vector2Int pendingPixel = WorldPosToPixel(pendingVfxSpawns[j].position);
                    if (pendingPixel == pixel)
                    {
                        alreadyPending = true;
                        break;
                    }
                }
                
                if (alreadyPending)
                {
                    continue; // Skip duplicate - this fire was already added (likely from StartFireAt)
                }
                
                Vector3 wpos = PixelToWorldPos(x, y);
                float durMul = Mathf.Clamp(newFires[i].durationMul, settings.randomRange.x, settings.randomRange.y);
                float totalDuration = (settings.burnDuration + settings.waitDuration + settings.recoverPrepDuration + settings.recoverGrowDuration) * durMul;
                float life = Mathf.Max(0.1f, (baseLifeTime > 0f ? baseLifeTime : totalDuration) + Random.Range(-1f, 1f));
                pendingVfxSpawns.Add(new FireVFXData { position = wpos, lifeTime = life });
                
                // Note: In CA mode, we don't track activeFirePixels here to avoid duplicate VFX spawns
                // This matches the old version behavior where only pendingVfxSpawns is used
            }
        }
        
        private void SyncFireStatesFromGPU()
        {
            if (activeFiresList.Count == 0) return;
            
            // Use async readback instead of synchronous GetData
            RequestFireStatesAsync();
        }
        
        /// <summary>
        /// Request async readback of fire states from GPU.
        /// </summary>
        private void RequestFireStatesAsync()
        {
            if (isFireStatesPending || fireStateBuffer == null) return;
            if (activeFiresList.Count == 0) return;
            
            // Request async readback
            isFireStatesPending = true;
            fireStatesRequest = AsyncGPUReadback.Request(fireStateBuffer, OnFireStatesReadback);
        }
        
        /// <summary>
        /// Callback for async readback of fire states.
        /// </summary>
        private void OnFireStatesReadback(AsyncGPUReadbackRequest request)
        {
            isFireStatesPending = false;
            
            if (request.hasError)
            {
                Debug.LogError("FireSpreadController: Failed to async read fire states");
                return;
            }
            
            int expectedCount = activeFiresList.Count;
            if (expectedCount == 0) return;
            
            NativeArray<FireStateGPU> data = request.GetData<FireStateGPU>();
            
            // Use GetSubArray to get only the data we need (ensure length match)
            int actualCount = Mathf.Min(expectedCount, data.Length);
            if (actualCount < expectedCount)
            {
                Debug.LogWarning($"FireSpreadController: Fire states length mismatch. Expected {expectedCount}, got {data.Length}, using {actualCount}");
            }
            
            NativeArray<FireStateGPU> actualData = data.GetSubArray(0, actualCount);
            
            // Process states
            for (int i = 0; i < actualData.Length; i++)
            {
                int idx = activeFiresList.FindIndex(f => f.fireID == actualData[i].fireID);
                if (idx >= 0)
                {
                    activeFiresList[idx] = actualData[i];
                }
            }
        }

        private void ProcessSpreadResults(uint count)
        {
            // Use async readback instead of synchronous GetData
            RequestSpreadResultsAsync(count);
        }
        
        /// <summary>
        /// Request async readback of spread results from GPU.
        /// </summary>
        private void RequestSpreadResultsAsync(uint count)
        {
            if (isSpreadResultsPending || spreadResultBuffer == null) return;
            if (count == 0) return;
            
            // Request async readback
            isSpreadResultsPending = true;
            spreadResultsRequest = AsyncGPUReadback.Request(spreadResultBuffer, (request) => OnSpreadResultsReadback(request, count));
        }
        
        /// <summary>
        /// Callback for async readback of spread results.
        /// </summary>
        private void OnSpreadResultsReadback(AsyncGPUReadbackRequest request, uint expectedCount)
        {
            isSpreadResultsPending = false;
            
            if (request.hasError)
            {
                Debug.LogError("FireSpreadController: Failed to async read spread results");
                return;
            }
            
            NativeArray<Vector4> data = request.GetData<Vector4>();
            
            // Use GetSubArray to get only the data we need (ensure length match)
            int actualCount = Mathf.Min((int)expectedCount, data.Length);
            if (actualCount < expectedCount)
            {
                Debug.LogWarning($"FireSpreadController: Spread results length mismatch. Expected {expectedCount}, got {data.Length}, using {actualCount}");
            }
            
            NativeArray<Vector4> actualData = data.GetSubArray(0, actualCount);
            
            // Process results
            for (int i = 0; i < actualData.Length; i++)
            {
                Vector4 result = actualData[i];
                Vector2Int newPixel = new Vector2Int((int)result.x, (int)result.y);
                float spreadChance = result.z;
                float delay = result.w;

                if (!IsAreaAllowedForSpread(newPixel.x, newPixel.y))
                {
                    continue;
                }

                Vector3 worldPos = PixelToWorldPos(newPixel.x, newPixel.y);
                StartCoroutine(SpawnFireAfterDelay(worldPos, newPixel, delay, spreadChance));
            }
        }

        private IEnumerator SpawnFireAfterDelay(Vector3 worldPos, Vector2Int pixel, float delay, float inheritedChance)
        {
            yield return new WaitForSeconds(delay);

            if (activeFirePixels.Count >= settings.maxFireCount) yield break;
            if (activeFirePixels.Contains(pixel)) yield break;
            if (!IsAreaAllowedForSpread(pixel.x, pixel.y)) yield break;

            // Snap to ground using heightmap (no Raycast)
            worldPos.y = GetHeightAtWorldPos(worldPos.x, worldPos.z);

            StartFireAt(worldPos, inheritedChance);
        }

        private void UpdateBurnMap()
        {
            if (burnMap == null) return;
            if (activeFiresList == null || activeFiresList.Count == 0) return;

            fireComputeShader.SetFloat("_CurrentTime", Time.time);
            fireComputeShader.SetFloat("_BurnDuration", settings.burnDuration);
            fireComputeShader.SetFloat("_WaitDuration", settings.waitDuration);
            fireComputeShader.SetFloat("_RecoverPrepDuration", settings.recoverPrepDuration);
            fireComputeShader.SetFloat("_RecoverGrowDuration", settings.recoverGrowDuration);
            fireComputeShader.SetInt("_TextureWidth", burnMap.width);
            fireComputeShader.SetInt("_TextureHeight", burnMap.height);

            if (burnMap != null)
            {
                fireComputeShader.SetTexture(kernelWriteBurnMapID, "_BurnMapRW", burnMap);
            }

            fireComputeShader.SetInt("_FireCount", activeFiresList.Count);

            int groups = Mathf.CeilToInt(activeFiresList.Count / 64.0f);
            if (groups <= 0) groups = 1;
            fireComputeShader.Dispatch(kernelWriteBurnMapID, groups, 1, 1);
        }

        private bool IsAreaAllowedForSpread(int x, int y)
        {
            if (burnMap == null) return false;
            if (onlySpreadOnGrass && !HasGrassAtPixel(x, y)) return false;
#if LEMIGAME_INTERACTIVE_GRASS
            if (limitByShrinkStrength)
            {
                float strength = GetDirectionStrength(x, y);
                if (strength > maxShrinkStrengthForSpread) return false;
            }
#endif
            return true;
        }

        /// <summary>
        /// Clear the burn map (set to black - not burned).
        /// </summary>
        public void ClearBurnMap()
        {
            if (burnMap == null || burnMaterial == null) return;

            RenderTexture temp = RenderTexture.GetTemporary(burnMap.width, burnMap.height);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, burnMap.width, burnMap.height, 0);
            RenderTexture.active = temp;
            burnMaterial.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0, 0, 0, 0));
            GL.Vertex3(0, 0, 0);
            GL.Vertex3(burnMap.width, 0, 0);
            GL.Vertex3(burnMap.width, burnMap.height, 0);
            GL.Vertex3(0, burnMap.height, 0);
            GL.End();
            GL.PopMatrix();
            Graphics.Blit(temp, burnMap);
            RenderTexture.ReleaseTemporary(temp);
        }

        /// <summary>
        /// Set burn value at pixel.
        /// burnMap: R=burn, G=wait, B=recover.
        /// </summary>
        public void SetPixelBurnValue(int x, int y, float burnValue, float waitValue = 0f, float recoverValue = 0f)
        {
            SetPixelBurnValue(x, y, burnValue, waitValue, recoverValue, 1);
        }

        /// <summary>
        /// Set burn value with brush size parameter.
        /// Optimized: Batches operations for better performance.
        /// </summary>
        public void SetPixelBurnValue(int x, int y, float burnValue, float waitValue, float recoverValue, int brushSize)
        {
            if (burnMap == null || burnMaterial == null) return;

            Vector2Int size = new Vector2Int(burnMap.width, burnMap.height);
            x = Mathf.Clamp(x, 0, size.x - 1);
            y = Mathf.Clamp(y, 0, size.y - 1);
            Color color = new Color(burnValue, waitValue, recoverValue, 1f);
            
            // Add to batch queue instead of drawing immediately
            pendingPixelDraws.Add(new System.Tuple<Vector2Int, int, Color>(new Vector2Int(x, y), Mathf.Max(1, brushSize), color));
        }

        /// <summary>
        /// Draw a single pixel (immediate mode - for compatibility).
        /// For better performance, use SetPixelBurnValue which batches operations.
        /// </summary>
        private void DrawBurnPixel(int x, int y, int brushSize, Color color)
        {
            if (burnMap == null || burnMaterial == null) return;

            // If not currently batching, add to batch queue
            if (!isBatchingDraws)
            {
                pendingPixelDraws.Add(new System.Tuple<Vector2Int, int, Color>(new Vector2Int(x, y), brushSize, color));
                return;
            }

            // Immediate draw (for compatibility with existing code)
            Vector2Int size = new Vector2Int(burnMap.width, burnMap.height);
            int x2 = Mathf.Min(x + brushSize, size.x);
            int y2 = Mathf.Min(y + brushSize, size.y);
            int clampedX = Mathf.Clamp(x, 0, size.x - 1);
            int clampedY = Mathf.Clamp(y, 0, size.y - 1);
            if (x2 <= clampedX || y2 <= clampedY)
            {
                return;
            }

            GL.Color(color);
            GL.Vertex3(clampedX, clampedY, 0);
            GL.Vertex3(x2, clampedY, 0);
            GL.Vertex3(x2, y2, 0);
            GL.Vertex3(clampedX, y2, 0);
        }
        
        /// <summary>
        /// Batch draw all pending pixel operations in a single GL pass for better performance.
        /// </summary>
        private void FlushPendingPixelDraws()
        {
            if (pendingPixelDraws.Count == 0 || burnMap == null || burnMaterial == null) return;

            Vector2Int size = new Vector2Int(burnMap.width, burnMap.height);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, size.x, size.y, 0);
            RenderTexture.active = burnMap;
            burnMaterial.SetPass(0);
            
            isBatchingDraws = true;
            GL.Begin(GL.QUADS);
            
            foreach (var draw in pendingPixelDraws)
            {
                int x = draw.Item1.x;
                int y = draw.Item1.y;
                int brushSize = draw.Item2;
                Color color = draw.Item3;
                
                int x2 = Mathf.Min(x + brushSize, size.x);
                int y2 = Mathf.Min(y + brushSize, size.y);
                int clampedX = Mathf.Clamp(x, 0, size.x - 1);
                int clampedY = Mathf.Clamp(y, 0, size.y - 1);
                if (x2 <= clampedX || y2 <= clampedY) continue;

                GL.Color(color);
                GL.Vertex3(clampedX, clampedY, 0);
                GL.Vertex3(x2, clampedY, 0);
                GL.Vertex3(x2, y2, 0);
                GL.Vertex3(clampedX, y2, 0);
            }
            
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = null;
            
            isBatchingDraws = false;
            pendingPixelDraws.Clear();
        }

        /// <summary>
        /// Periodically detect which monitored colliders are inside burning areas and fire trigger-like events.
        /// </summary>
        private IEnumerator FireDetectionCoroutine()
        {
            var wait = new WaitForSeconds(settings.detectionUpdateInterval);
            while (true)
            {
                yield return wait;
                if (burnMap == null) continue;
                if (monitoredColliders.Count == 0) continue;

                // Performance optimization: Skip expensive cache update if no active fires
                if (activeFirePixels.Count == 0)
                {
                    // Clear any previous fire states since there are no fires
                    foreach (var col in monitoredColliders)
                    {
                        if (col == null) continue;
                        if (colliderInFire.TryGetValue(col, out var wasOnFire) && wasOnFire)
                        {
                            colliderInFire[col] = false;
                            OnFireExit?.Invoke(col);
                        }
                    }
                    continue;
                }

                // Update CPU cache (expensive GPU->CPU transfer, frequency reduced via detectionUpdateInterval)
                UpdateBurnMapCache();

                foreach (var col in monitoredColliders)
                {
                    if (col == null) continue;
                    Vector3 p = col.bounds.center;
                    bool isOnFire = IsWorldPositionOnFire(p);
                    bool wasOnFire = colliderInFire.TryGetValue(col, out var prev) && prev;

                    if (isOnFire)
                    {
                        if (!wasOnFire)
                        {
                            colliderInFire[col] = true;
                            OnFireEnter?.Invoke(col);
                        }
                        else
                        {
                            OnFireStay?.Invoke(col);
                        }
                    }
                    else if (wasOnFire)
                    {
                        colliderInFire[col] = false;
                        OnFireExit?.Invoke(col);
                    }
                }
            }
        }

        /// <summary>
        /// Register a collider for fire enter/exit detection.
        /// </summary>
        public void RegisterCollider(Collider col)
        {
            if (col == null) return;
            if (monitoredColliders.Add(col))
            {
                colliderInFire[col] = false;
            }
        }

        /// <summary>
        /// Unregister a collider from fire detection.
        /// </summary>
        public void UnregisterCollider(Collider col)
        {
            if (col == null) return;
            monitoredColliders.Remove(col);
            colliderInFire.Remove(col);
        }

        /// <summary>
        /// Convert world position to pixel coordinates.
        /// </summary>
        public Vector2Int WorldPosToPixel(Vector3 worldPos)
        {
            if (burnMap == null)
            {
                Debug.LogWarning("FireSpreadController.WorldPosToPixel: burnMap is null.");
                return Vector2Int.zero;
            }

            if (terrainSize.x == 0 || terrainSize.z == 0)
            {
                Debug.LogWarning("FireSpreadController.WorldPosToPixel: terrainSize is invalid. Make sure terrain is properly initialized.");
                return Vector2Int.zero;
            }

            float relativeX = (worldPos.x - terrainPosition.x) / terrainSize.x;
            float relativeZ = (worldPos.z - terrainPosition.z) / terrainSize.z;

            Vector2 uv = new Vector2(
                relativeX * burnMap.width,
                (1 - relativeZ) * burnMap.height
            );

            return new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(uv.x), 0, burnMap.width - 1),
                Mathf.Clamp(Mathf.FloorToInt(uv.y), 0, burnMap.height - 1)
            );
        }

        /// <summary>
        /// Check if a world position is within camera update range.
        /// </summary>
        private bool IsWithinUpdateRange(Vector3 worldPos)
        {
            if (!enableCameraCulling) return true;
            
            var cam = camOrTarget != null ? camOrTarget : (Camera.main != null ? Camera.main.transform : null);
            if (cam == null) return true; // If no camera, update everything
            
            float distanceSqr = (worldPos - cam.position).sqrMagnitude;
            return distanceSqr <= maxUpdateDistance * maxUpdateDistance;
        }
        
        /// <summary>
        /// Check if a pixel position is within camera update range.
        /// </summary>
        private bool IsPixelWithinUpdateRange(int x, int y)
        {
            if (!enableCameraCulling) return true;
            
            Vector3 worldPos = PixelToWorldPos(x, y);
            return IsWithinUpdateRange(worldPos);
        }

        /// <summary>
        /// Get height at world XZ position using heightmap (fastest) or terrain, without Raycast.
        /// </summary>
        private float GetHeightAtWorldPos(float worldX, float worldZ)
        {
            if (heightTex != null)
            {
                // Use heightmap texture (fastest method)
                if (heightTex is Texture2D)
                {
                    Texture2D heightTexture = heightTex as Texture2D;
                    float u = (worldX - terrainPosition.x) / terrainSize.x;
                    float v = (worldZ - terrainPosition.z) / terrainSize.z;
                    
                    int texX = Mathf.Clamp(Mathf.FloorToInt(u * heightTexture.width), 0, heightTexture.width - 1);
                    int texY = Mathf.Clamp(Mathf.FloorToInt((1f - v) * heightTexture.height), 0, heightTexture.height - 1);
                    
                    Color heightColor = heightTexture.GetPixel(texX, texY);
                    float normalizedHeight = heightColor.r; // R channel contains normalized height (0-1)
                    return heightOffset + normalizedHeight * heightScale;
                }
            }
            
            // Fallback to terrain.SampleHeight if heightTex not available
            if (terrain != null)
            {
                float terrainHeight = terrain.SampleHeight(new Vector3(worldX, 0, worldZ));
                return terrainPosition.y + terrainHeight;
            }
            
            return 0f;
        }

        /// <summary>
        /// Get world position from pixel coordinates.
        /// Performance optimized: Uses heightTex texture if available (fastest), then terrain.SampleHeight, finally Raycast as fallback.
        /// </summary>
        public Vector3 PixelToWorldPos(int x, int y)
        {
            if (burnMap == null || terrain == null) return Vector3.zero;

            float u = x / (float)burnMap.width;
            float v = y / (float)burnMap.height;

            float worldX = terrainPosition.x + u * terrainSize.x;
            float worldZ = terrainPosition.z + (1f - v) * terrainSize.z;

            // Use heightmap or terrain to get height (no Raycast)
            float worldY = GetHeightAtWorldPos(worldX, worldZ);

            return new Vector3(worldX, worldY, worldZ);
        }

        /// <summary>
        /// Check if a pixel position has grass (reads from grassMask Texture2D).
        /// </summary>
        public bool HasGrassAtPixel(int x, int y)
        {
            if (burnMap == null) return false;
            if (grassMask == null) return true;

            float u = (float)x / burnMap.width;
            float v = (float)y / burnMap.height;

            int texX = Mathf.FloorToInt(u * grassMask.width);
            int texY = Mathf.FloorToInt((1f - v) * grassMask.height);

            texX = Mathf.Clamp(texX, 0, grassMask.width - 1);
            texY = Mathf.Clamp(texY, 0, grassMask.height - 1);

            Color pixel = grassMask.GetPixel(texX, texY);
            return pixel.r > 0.5f;
        }

#if LEMIGAME_INTERACTIVE_GRASS
        /// <summary>
        /// Update cache for interactive map to avoid expensive ReadPixels calls.
        /// </summary>
        private void UpdateInteractiveMapCache()
        {
            if (InteractiveGrass.Instance == null || InteractiveGrass.Instance.interactiveMap == null) return;
            
            float currentTime = Time.time;
            // Only update cache at specified interval to reduce GPU->CPU transfers
            if (interactiveMapCache != null && currentTime - lastInteractiveMapCacheUpdate < INTERACTIVE_MAP_CACHE_UPDATE_INTERVAL)
            {
                return;
            }
            
            RenderTexture interactiveMap = InteractiveGrass.Instance.interactiveMap;
            
            // Create or resize cache if needed
            if (interactiveMapCache == null || 
                interactiveMapCache.width != interactiveMap.width || 
                interactiveMapCache.height != interactiveMap.height)
            {
                if (interactiveMapCache != null)
                {
                    Destroy(interactiveMapCache);
                }
                interactiveMapCache = new Texture2D(interactiveMap.width, interactiveMap.height, TextureFormat.RGBA32, false);
            }
            
            // Read from RenderTexture to CPU cache (expensive operation, but done at intervals)
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = interactiveMap;
            interactiveMapCache.ReadPixels(new Rect(0, 0, interactiveMap.width, interactiveMap.height), 0, 0);
            interactiveMapCache.Apply();
            RenderTexture.active = previous;
            
            lastInteractiveMapCacheUpdate = currentTime;
        }
        
        /// <summary>
        /// Read direction map strength (red channel) for grass scaling/force strength.
        /// Optimized: Uses cached texture instead of creating temporary Texture2D each time.
        /// </summary>
        public float GetDirectionStrength(int x, int y)
        {
            if (InteractiveGrass.Instance != null && InteractiveGrass.Instance.interactiveMap != null)
            {
                // Update cache if needed (only at intervals to reduce GPU->CPU transfers)
                UpdateInteractiveMapCache();
                
                if (interactiveMapCache != null)
                {
                    RenderTexture interactiveMap = InteractiveGrass.Instance.interactiveMap;
                    x = Mathf.Clamp(x, 0, interactiveMap.width - 1);
                    y = Mathf.Clamp(y, 0, interactiveMap.height - 1);
                    
                    // Read from cache (much faster than ReadPixels)
                    // Note: Texture2D y-coordinate is from bottom to top, need to flip
                    int cacheY = interactiveMap.height - 1 - y;
                    Color pixel = interactiveMapCache.GetPixel(x, cacheY);
                    return pixel.r;
                }
            }
            return 0f;
        }
#endif

        /// <summary>
        /// Returns true if the burn map at given world position is above burnThreshold and in burn phase.
        /// BurnMap channels: R=burn, G=wait, B=recover
        /// Only triggers when R > threshold and G/B are low (indicating active burn phase, not wait/recover).
        /// Requires cache to be ready - check IsBurnMapCacheReady before calling.
        /// </summary>
        public bool IsWorldPositionOnFire(Vector3 worldPos)
        {
            // Double buffered: always use current cache, never wait (completely non-blocking)
            Texture2D currentCache = usingCacheA ? burnMapCacheA : burnMapCacheB;
            if (burnMap == null || currentCache == null) return false;
            
            Vector2Int px = WorldPosToPixel(worldPos);
            float burnValue = SampleBurnChannel(px.x, px.y, 0); // R channel

            // Basic check: R channel must exceed threshold
            if (burnValue < burnThreshold) return false;

            // Check G and B channels to ensure trigger only in burn phase (not wait or recover phase)
            float waitValue = SampleBurnChannel(px.x, px.y, 1); // G channel (wait)
            float recoverValue = SampleBurnChannel(px.x, px.y, 2); // B channel (recover)

            // If G or B channel values are too high, indicating wait or recover phase, should not trigger
            const float maxWaitOrRecoverValue = 0.05f;
            if (waitValue > maxWaitOrRecoverValue || recoverValue > maxWaitOrRecoverValue)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Update VFX from CA state map. Reads active fires from burnMap and updates VFX positions.
        /// This is called periodically in CA mode to sync VFX with GPU state.
        /// Performance optimized: Reduced cache update frequency and cached world positions.
        /// </summary>
        private void UpdateVFXFromCAState()
        {
            if (burnMap == null || vfx == null || terrain == null) return;
            
            // Performance optimization: Skip if no active fire pixels
            if (activeFirePixels.Count == 0) return;
            
            float currentTime = Time.time;
            // Performance optimization: Only update VFX at specified interval to reduce expensive operations
            if (currentTime - lastVFXUpdateTime < VFX_UPDATE_INTERVAL)
            {
                return; // Skip this update
            }
            lastVFXUpdateTime = currentTime;
            
            // Update burn map cache (but not force update - use normal interval to reduce GPU->CPU transfers)
            UpdateBurnMapCache(forceUpdate: false);
            
            // Collect active fire positions from burnMap and clean up finished fires
            List<FireVFXData> activeFires = new List<FireVFXData>();
            List<Vector2Int> pixelsToRemove = new List<Vector2Int>(); // Track pixels that are no longer burning
            
            // Sample burnMap at tracked pixel positions
            foreach (var pixel in activeFirePixels)
            {
                float burnValue = SampleBurnChannel(pixel.x, pixel.y, 0); // R channel
                float waitValue = SampleBurnChannel(pixel.x, pixel.y, 1); // G channel
                float recoverValue = SampleBurnChannel(pixel.x, pixel.y, 2); // B channel
                
                // Check if fire has fully recovered (all channels near zero)
                // This check is done even for out-of-range pixels to clean up activeFirePixels
                if (burnValue < 0.01f && waitValue < 0.01f && recoverValue < 0.01f)
                {
                    // Fire has fully recovered, remove from tracking to improve performance
                    pixelsToRemove.Add(pixel);
                    cachedWorldPositions.Remove(pixel);
                    continue;
                }
                
                // Performance optimization: Skip fires outside camera range for VFX updates
                // But we still check recovery status above to clean up activeFirePixels
                if (enableCameraCulling && !IsPixelWithinUpdateRange(pixel.x, pixel.y))
                {
                    continue; // Skip VFX update, but keep in activeFirePixels if still recovering
                }
                
                // Check if fire is still active (in burn phase)
                if (burnValue > burnThreshold && waitValue < 0.05f && recoverValue < 0.05f)
                {
                    // Performance optimization: Use cached world position to avoid expensive Raycast
                    Vector3 worldPos;
                    if (!cachedWorldPositions.TryGetValue(pixel, out worldPos))
                    {
                        // Cache miss - compute and cache the position
                        worldPos = PixelToWorldPos(pixel.x, pixel.y);
                        cachedWorldPositions[pixel] = worldPos;
                    }
                    
                    float vfxLifeTime = Mathf.Max(0.1f, baseLifeTime > 0f ? baseLifeTime : 3f);
                    activeFires.Add(new FireVFXData { position = worldPos, lifeTime = vfxLifeTime });
                }
                else
                {
                    // Fire has finished burning (entered wait or recover phase, or burn value too low)
                    // Mark for removal to avoid checking it again in future updates
                    pixelsToRemove.Add(pixel);
                    // Also remove from cache to free memory
                    cachedWorldPositions.Remove(pixel);
                }
            }
            
            // Remove finished fire pixels to improve performance
            foreach (var pixel in pixelsToRemove)
            {
                activeFirePixels.Remove(pixel);
            }
            
            // Also check for new fires that might have been spawned by CA spread
            // This is a simplified approach - for better performance, consider using a compute shader to output active fire positions
            if (activeFires.Count > 0 && activeFires.Count <= settings.maxFireCount)
            {
                int spawnCount = activeFires.Count;
                var positions = new Vector3[spawnCount];
                var lifetimes = new float[spawnCount];
                for (int i = 0; i < spawnCount; i++)
                {
                    positions[i] = activeFires[i].position;
                    lifetimes[i] = activeFires[i].lifeTime;
                }
                visiblePositionsBuffer.SetData(positions, 0, 0, spawnCount);
                fireDataBuffer.SetData(lifetimes, 0, 0, spawnCount);
                vfx.SetGraphicsBuffer(positionBufferPropID, visiblePositionsBuffer);
                vfx.SetGraphicsBuffer(fireDataBufferPropID, fireDataBuffer);
                vfx.SetUInt(positionCountPropID, (uint)spawnCount);
                vfx.SetUInt(spawnCountPropID, (uint)spawnCount);
                vfx.SendEvent(spawnEventName);
            }
        }

        private float lastBurnMapCacheUpdate = -1f;
        private const float BURN_MAP_CACHE_UPDATE_INTERVAL = 0.5f; // Update cache every 0.5 seconds (reduced frequency for better performance)
        private const float BURN_MAP_CACHE_UPDATE_INTERVAL_VFX = 0.2f; // Faster update for VFX (0.2 seconds, but still reduced)
        
        /// <summary>
        /// Public property to check if burn map cache is ready for reading.
        /// With double buffering, this always returns true (cache is always available).
        /// </summary>
        public bool IsBurnMapCacheReady => (usingCacheA ? burnMapCacheA : burnMapCacheB) != null;
        
        /// <summary>
        /// Request async readback of burn map cache from GPU (double buffered, completely non-blocking).
        /// Updates the background cache while reading uses the current cache.
        /// </summary>
        private void RequestBurnMapCacheAsync(bool forceUpdate = false)
        {
            if (isBurnMapCachePending || burnMap == null) return;
            
            float currentTime = Time.time;
            float updateInterval = forceUpdate ? BURN_MAP_CACHE_UPDATE_INTERVAL_VFX : BURN_MAP_CACHE_UPDATE_INTERVAL;
            
            // Check if update is needed
            Texture2D currentCache = usingCacheA ? burnMapCacheA : burnMapCacheB;
            if (!forceUpdate && currentCache != null && currentTime - lastBurnMapCacheUpdate < updateInterval)
            {
                return; // Use existing cache, no update needed
            }
            
            // Select target cache (the one NOT currently used for reading)
            Texture2D targetCache = usingCacheA ? burnMapCacheB : burnMapCacheA;
            
            // Create or validate target cache size
            if (targetCache == null || targetCache.width != burnMap.width || targetCache.height != burnMap.height)
            {
                if (targetCache != null)
                {
                    Destroy(targetCache);
                }
                targetCache = new Texture2D(burnMap.width, burnMap.height, TextureFormat.RGBA32, false);
                if (usingCacheA)
                {
                    burnMapCacheB = targetCache;
                }
                else
                {
                    burnMapCacheA = targetCache;
                }
            }
            
            // Request async readback to update background cache (non-blocking)
            isBurnMapCachePending = true;
            burnMapCacheRequest = AsyncGPUReadback.Request(burnMap, 0, TextureFormat.RGBA32, OnBurnMapCacheReadback);
        }
        
        /// <summary>
        /// Callback for async readback of burn map cache (double buffered).
        /// Updates background cache and swaps when ready.
        /// </summary>
        private void OnBurnMapCacheReadback(AsyncGPUReadbackRequest request)
        {
            isBurnMapCachePending = false;
            
            if (request.hasError)
            {
                Debug.LogError("FireSpreadController: Failed to async read burn map cache");
                return;
            }
            
            // Get target cache (the one NOT currently used for reading)
            Texture2D targetCache = usingCacheA ? burnMapCacheB : burnMapCacheA;
            if (targetCache == null) return;
            
            // Copy async readback data to background cache
            NativeArray<Color32> data = request.GetData<Color32>();
            targetCache.SetPixelData(data, 0);
            targetCache.Apply();
            
            // Swap caches atomically (main thread only, thread-safe)
            usingCacheA = !usingCacheA;
            
            lastBurnMapCacheUpdate = Time.time;
        }
        
        /// <summary>
        /// Update CPU-side cache of burn map for efficient reading (legacy method, now uses async).
        /// Optimized: Only updates at specified intervals to reduce expensive GPU->CPU transfers.
        /// </summary>
        /// <param name="forceUpdate">If true, forces immediate update (for VFX synchronization)</param>
        private void UpdateBurnMapCache(bool forceUpdate = false)
        {
            // Use async readback instead of synchronous ReadPixels
            RequestBurnMapCacheAsync(forceUpdate);
        }

        /// <summary>
        /// Sample burn value from CPU cache (much more efficient than reading from RenderTexture each time).
        /// Returns R channel (burn value) by default.
        /// </summary>
        private float SampleBurn(int x, int y)
        {
            return SampleBurnChannel(x, y, 0); // 0 = R channel
        }

        /// <summary>
        /// Sample specific channel from burn map cache (double buffered, always uses current cache).
        /// channel: 0 = R (burn), 1 = G (wait), 2 = B (recover), 3 = A (alpha)
        /// </summary>
        private float SampleBurnChannel(int x, int y, int channel)
        {
            // Always use current cache (double buffered, completely non-blocking)
            Texture2D currentCache = usingCacheA ? burnMapCacheA : burnMapCacheB;
            if (currentCache == null || burnMap == null) return 0f;
            
            x = Mathf.Clamp(x, 0, burnMap.width - 1);
            y = Mathf.Clamp(y, 0, burnMap.height - 1);
            // Note: Texture2D y-coordinate is from bottom to top, while our UV is from top to bottom, need to flip
            int cacheY = burnMap.height - 1 - y;
            Color c = currentCache.GetPixel(x, cacheY);

            switch (channel)
            {
                case 0: return c.r; // burn
                case 1: return c.g; // wait
                case 2: return c.b; // recover
                case 3: return c.a; // alpha
                default: return c.r;
            }
        }

        /// <summary>
        /// Create explosion fire at world position with multiple fire points and immediate burn area painting.
        /// </summary>
        public void CreateExplosionFire(Vector3 explosionPos, float explosionRadius, int fireCountMultiplier = 3)
        {
            if (burnMap == null || terrain == null) return;

            // Calculate number of fires to spawn based on explosion radius
            int fireCount = Mathf.RoundToInt(explosionRadius * fireCountMultiplier);
            fireCount = Mathf.Clamp(fireCount, fireCountMultiplier, 5 * fireCountMultiplier); // Min 3, max 15

            // 3. Spawn multiple fire points with better distribution to avoid overlap
            // Use a simple grid-based distribution with jitter to avoid perfect alignment
            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(fireCount));
            float cellSize = (explosionRadius * 1.8f) / gridSize; // Slightly larger than radius to spread fires
            float minDistance = cellSize * 0.3f; // Minimum distance between fires
            
            List<Vector2> firePositions = new List<Vector2>();
            HashSet<Vector2Int> firePixels = new HashSet<Vector2Int>(); // Track pixels that will have fire points
            
            // Generate fire positions with minimum distance constraint
            int attempts = 0;
            int maxAttempts = fireCount * 10; // Limit attempts to avoid infinite loop
            
            while (firePositions.Count < fireCount && attempts < maxAttempts)
            {
                attempts++;
                
                // Try grid-based position first, then fallback to random
                Vector2 candidatePos;
                if (firePositions.Count < gridSize * gridSize)
                {
                    // Grid-based distribution
                    int gridX = firePositions.Count % gridSize;
                    int gridY = firePositions.Count / gridSize;
                    float baseX = (gridX - gridSize * 0.5f + 0.5f) * cellSize;
                    float baseY = (gridY - gridSize * 0.5f + 0.5f) * cellSize;
                    // Add jitter
                    candidatePos = new Vector2(
                        baseX + Random.Range(-cellSize * 0.3f, cellSize * 0.3f),
                        baseY + Random.Range(-cellSize * 0.3f, cellSize * 0.3f)
                    );
                }
                else
                {
                    // Random fallback
                    candidatePos = Random.insideUnitCircle * explosionRadius;
                }
                
                // Check if position is within radius
                if (candidatePos.magnitude > explosionRadius)
                {
                    candidatePos = candidatePos.normalized * Random.Range(explosionRadius * 0.3f, explosionRadius);
                }
                
                // Check minimum distance from existing fires
                bool tooClose = false;
                foreach (var existingPos in firePositions)
                {
                    if (Vector2.Distance(candidatePos, existingPos) < minDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }
                
                if (!tooClose)
                {
                    firePositions.Add(candidatePos);
                    // Record the pixel that will have a fire point
                    Vector3 firePos = explosionPos + new Vector3(candidatePos.x, 0, candidatePos.y);
                    firePos.y = GetHeightAtWorldPos(firePos.x, firePos.z);
                    Vector2Int pixel = WorldPosToPixel(firePos);
                    firePixels.Add(pixel);
                }
            }

            // 1. Paint immediate burn area with reduced intensity to avoid overlap with fire points
            // Use lower intensity and smaller range to prevent brightness stacking
            float immediateRange = explosionRadius * 0.4f; // Further reduced to minimize overlap
            PaintImmediateBurnArea(explosionPos, immediateRange, 0.4f, firePixels); // Pass fire pixels to avoid duplicate seeding

            // 2. Paint immediate wait area with reduced intensity
            float waitRange = explosionRadius * 0.2f; // Further reduced
            PaintImmediateWaitArea(explosionPos, waitRange, 0.3f, firePixels); // Pass fire pixels to avoid duplicate seeding
            
            // Spawn fires with random burn progress to create more natural look
            for (int i = 0; i < firePositions.Count; i++)
            {
                Vector2 offset = firePositions[i];
                Vector3 firePos = explosionPos + new Vector3(offset.x, 0, offset.y);

                // Check ground height using heightmap (no Raycast)
                firePos.y = GetHeightAtWorldPos(firePos.x, firePos.z);

                // Randomize burn progress: some fires start fresh, some are already burning
                // This creates a more natural, less uniform appearance
                float burnProgress = Random.Range(0f, 0.7f); // 0 = just started, 0.7 = 70% through burn phase
                float timeOffset = -burnProgress * settings.burnDuration;
                
                // Spawn fire with time offset to simulate different burn stages
                StartFireAtWithTimeOffset(firePos, timeOffset);
            }
        }
        
        /// <summary>
        /// Start fire at world position with a time offset to simulate different burn stages.
        /// </summary>
        private void StartFireAtWithTimeOffset(Vector3 worldPos, float timeOffset)
        {
            if (burnMap == null) return;

            if (terrain == null)
            {
                Debug.LogWarning("FireSpreadController.StartFireAtWithTimeOffset: terrain is null.");
                return;
            }

            if (activeFirePixels.Count >= settings.maxFireCount)
            {
                return;
            }

            Vector2Int pixel = WorldPosToPixel(worldPos);
            if (activeFirePixels.Contains(pixel))
            {
                return;
            }

            if (!IsAreaAllowedForSpread(pixel.x, pixel.y))
            {
                return;
            }

            // Snap to ground using heightmap (no Raycast)
            worldPos.y = GetHeightAtWorldPos(worldPos.x, worldPos.z);

            int fireID = nextFireID++;
            float durationMul = Random.Range(settings.randomRange.x, settings.randomRange.y);
            float spawnScale = Random.Range(settings.randomRange.x, settings.randomRange.y);
            float startTimeOffset = timeOffset; // Use provided time offset

            // CA path if available; otherwise, fallback to buffer path
            if (stateMap == null || kernelSeedID == 0)
            {
                FireStateGPU gpuState = new FireStateGPU
                {
                    pixelPos = pixel,
                    startTime = Time.time - startTimeOffset,
                    durationMul = durationMul,
                    spawnScale = spawnScale,
                    currentSpreadChance = settings.initialSpreadChance,
                    nextSpreadTime = Time.time + Random.Range(0.1f, 0.7f),
                    totalSpawns = 0,
                    totalChecks = 0,
                    isActive = 1,
                    fireID = fireID
                };

                activeFiresList.Add(gpuState);
                activeFirePixels.Add(pixel);

                // Initial mark on burn map with progress-based value
                // Fire points use their own burn value, which should be brighter than immediate paint areas
                float burnValue = Mathf.Clamp01(0.1f + (Mathf.Abs(timeOffset) / settings.burnDuration) * 0.9f);
                // Since immediate paint uses lower intensity (0.4), fire points will naturally be brighter
                SetPixelBurnValue(pixel.x, pixel.y, burnValue, 0f, 0f, 1);
                FlushPendingPixelDraws(); // Immediate flush for critical initialization

                // Queue a Single Burst spawn (buffer mode)
                float totalDuration = (settings.burnDuration + settings.waitDuration + settings.recoverPrepDuration + settings.recoverGrowDuration) * durationMul;
                float lifeTime = Mathf.Max(0.1f, (baseLifeTime > 0f ? baseLifeTime : totalDuration) + Random.Range(-1f, 1f));
                pendingVfxSpawns.Add(new FireVFXData { position = worldPos, lifeTime = lifeTime });
                
                // Reset timer - new fire was created
                lastFireUpdateTime = Time.time;
                
                return;
            }

            // CA mode: seed a pixel directly on GPU state map
            if (fireComputeShader == null)
            {
                Debug.LogError("FireSpreadController.StartFireAtWithTimeOffset: fireComputeShader is null.");
                return;
            }
            float initChance = settings.initialSpreadChance;
            float cooldown = Time.time + Random.Range(settings.randomRange.x * 0.5f, settings.randomRange.y * 1.5f);
            fireComputeShader.SetInt("_TextureWidth", burnMap.width);
            fireComputeShader.SetInt("_TextureHeight", burnMap.height);
            fireComputeShader.SetInt("_SeedPixelX", pixel.x);
            fireComputeShader.SetInt("_SeedPixelY", pixel.y);
            fireComputeShader.SetFloat("_SeedStartTime", Time.time - startTimeOffset);
            fireComputeShader.SetFloat("_SeedDurationMul", durationMul);
            fireComputeShader.SetFloat("_SeedInitialChance", initChance);
            fireComputeShader.SetFloat("_SeedCooldownUntil", cooldown);
            fireComputeShader.Dispatch(kernelSeedID, 1, 1, 1);

            // Initial mark on burn map with progress-based value
            // Fire points use their own burn value, which should be brighter than immediate paint areas
            float caBurnValue = Mathf.Clamp01(0.1f + (Mathf.Abs(timeOffset) / settings.burnDuration) * 0.9f);
            // Since immediate paint uses lower intensity (0.4), fire points will naturally be brighter
            SetPixelBurnValue(pixel.x, pixel.y, caBurnValue, 0f, 0f, 1);
            FlushPendingPixelDraws(); // Immediate flush for critical initialization
            
            // CA mode also needs VFX data
            // Note: No duplicate check here - GPU-side stateMap should handle duplicates
            float caTotalDuration = (settings.burnDuration + settings.waitDuration + settings.recoverPrepDuration + settings.recoverGrowDuration) * durationMul;
            float caLifeTime = Mathf.Max(0.1f, (baseLifeTime > 0f ? baseLifeTime : caTotalDuration) + Random.Range(-1f, 1f));
            pendingVfxSpawns.Add(new FireVFXData { position = worldPos, lifeTime = caLifeTime });
            
            // Reset timer - new fire was created
            lastFireUpdateTime = Time.time;
            
            // Note: In CA mode, we don't track activeFirePixels here to avoid duplicate VFX spawns
            
            // Cache world position to avoid expensive computation later
            Vector3 cachedPos = PixelToWorldPos(pixel.x, pixel.y);
            cachedWorldPositions[pixel] = cachedPos;
        }

        /// <summary>
        /// Paint immediate burn area with specified intensity in a circular region.
        /// In CA mode, also seeds stateMap so the area can recover properly.
        /// </summary>
        /// <param name="excludeFirePixels">Pixels that will have fire points - these won't be seeded in stateMap to avoid duplicates</param>
        private void PaintImmediateBurnArea(Vector3 center, float radius, float intensity = 1f, HashSet<Vector2Int> excludeFirePixels = null)
        {
            if (burnMap == null || terrain == null) return;

            // Convert radius to pixels
            int radiusInPixels = Mathf.RoundToInt((radius / terrainSize.x) * burnMap.width);
            radiusInPixels = Mathf.Max(1, radiusInPixels); // At least 1 pixel

            Vector2Int centerPixel = WorldPosToPixel(center);
            float currentTime = Time.time;
            List<Vector2Int> paintedPixels = new List<Vector2Int>();

            // Draw a small circle with gradient effect (center brighter, edges dimmer)
            for (int x = -radiusInPixels; x <= radiusInPixels; x++)
            {
                for (int y = -radiusInPixels; y <= radiusInPixels; y++)
                {
                    float dist = Mathf.Sqrt(x * x + y * y);
                    if (dist <= radiusInPixels)
                    {
                        int px = centerPixel.x + x;
                        int py = centerPixel.y + y;

                        // Check bounds
                        if (px >= 0 && px < burnMap.width && py >= 0 && py < burnMap.height)
                        {
                            Vector2Int pixel = new Vector2Int(px, py);
                            
                            // Calculate gradient: center is full intensity, edges fade out
                            float normalizedDist = radiusInPixels > 0 ? dist / radiusInPixels : 0f;
                            float gradientFactor = 1f - (normalizedDist * 0.5f); // Fade from center to edge
                            float burnValue = intensity * Mathf.Clamp01(gradientFactor);
                            
                            // Use Max to avoid overwriting higher values from fire points
                            // But since we're using lower intensity, this should be fine
                            SetPixelBurnValue(px, py, burnValue, 0f, 0f, 1);
                            paintedPixels.Add(pixel);
                        }
                    }
                }
            }

            // In CA mode, seed stateMap for pixels that don't have fire points
            // This ensures painted areas can recover properly without duplicate seeding
            if (stateMap != null && kernelSeedID != 0 && fireComputeShader != null)
            {
                float durationMul = 1f; // Use default duration multiplier
                float initChance = settings.initialSpreadChance;
                
                foreach (var pixel in paintedPixels)
                {
                    // Skip pixels that will have fire points (they will be seeded by StartFireAtWithTimeOffset)
                    if (excludeFirePixels != null && excludeFirePixels.Contains(pixel))
                    {
                        continue;
                    }
                    
                    // Seed each pixel in stateMap with current time as start time
                    // This ensures CA update loop will process them and they can recover
                    fireComputeShader.SetInt("_TextureWidth", burnMap.width);
                    fireComputeShader.SetInt("_TextureHeight", burnMap.height);
                    fireComputeShader.SetInt("_SeedPixelX", pixel.x);
                    fireComputeShader.SetInt("_SeedPixelY", pixel.y);
                    fireComputeShader.SetFloat("_SeedStartTime", currentTime);
                    fireComputeShader.SetFloat("_SeedDurationMul", durationMul);
                    fireComputeShader.SetFloat("_SeedInitialChance", initChance);
                    fireComputeShader.SetFloat("_SeedCooldownUntil", currentTime + 0.1f); // Short cooldown
                    fireComputeShader.Dispatch(kernelSeedID, 1, 1, 1);
                }
            }
            
            // Flush pending pixel draws after painting
            FlushPendingPixelDraws();
        }

        /// <summary>
        /// Paint immediate wait area with specified intensity in a circular region.
        /// In CA mode, also seeds stateMap so the area can recover properly.
        /// </summary>
        /// <param name="excludeFirePixels">Pixels that will have fire points - these won't be seeded in stateMap to avoid duplicates</param>
        private void PaintImmediateWaitArea(Vector3 center, float radius, float intensity = 1f, HashSet<Vector2Int> excludeFirePixels = null)
        {
            if (burnMap == null || terrain == null) return;

            // Convert radius to pixels
            int radiusInPixels = Mathf.RoundToInt((radius / terrainSize.x) * burnMap.width);
            radiusInPixels = Mathf.Max(1, radiusInPixels); // At least 1 pixel

            Vector2Int centerPixel = WorldPosToPixel(center);
            float currentTime = Time.time;
            List<Vector2Int> paintedPixels = new List<Vector2Int>();

            // Draw a small circle with gradient effect
            for (int x = -radiusInPixels; x <= radiusInPixels; x++)
            {
                for (int y = -radiusInPixels; y <= radiusInPixels; y++)
                {
                    float dist = Mathf.Sqrt(x * x + y * y);
                    if (dist <= radiusInPixels)
                    {
                        int px = centerPixel.x + x;
                        int py = centerPixel.y + y;

                        // Check bounds
                        if (px >= 0 && px < burnMap.width && py >= 0 && py < burnMap.height)
                        {
                            Vector2Int pixel = new Vector2Int(px, py);
                            
                            // Calculate gradient: center is full intensity, edges fade out
                            float normalizedDist = radiusInPixels > 0 ? dist / radiusInPixels : 0f;
                            float gradientFactor = 1f - (normalizedDist * 0.5f);
                            float burnValue = intensity * Mathf.Clamp01(gradientFactor);
                            float waitValue = intensity * Mathf.Clamp01(gradientFactor);
                            
                            // Set to wait state with reduced intensity
                            SetPixelBurnValue(px, py, burnValue, waitValue, 0f, 1);
                            paintedPixels.Add(pixel);
                        }
                    }
                }
            }

            // In CA mode, seed stateMap for pixels that don't have fire points
            // For wait state, we set startTime to be in the wait phase already
            if (stateMap != null && kernelSeedID != 0 && fireComputeShader != null)
            {
                float durationMul = 1f;
                float initChance = settings.initialSpreadChance;
                // Set startTime to be in the wait phase (after burn duration)
                float waitStartTime = currentTime - settings.burnDuration;
                
                foreach (var pixel in paintedPixels)
                {
                    // Skip pixels that will have fire points (they will be seeded by StartFireAtWithTimeOffset)
                    if (excludeFirePixels != null && excludeFirePixels.Contains(pixel))
                    {
                        continue;
                    }
                    
                    fireComputeShader.SetInt("_TextureWidth", burnMap.width);
                    fireComputeShader.SetInt("_TextureHeight", burnMap.height);
                    fireComputeShader.SetInt("_SeedPixelX", pixel.x);
                    fireComputeShader.SetInt("_SeedPixelY", pixel.y);
                    fireComputeShader.SetFloat("_SeedStartTime", waitStartTime);
                    fireComputeShader.SetFloat("_SeedDurationMul", durationMul);
                    fireComputeShader.SetFloat("_SeedInitialChance", initChance);
                    fireComputeShader.SetFloat("_SeedCooldownUntil", currentTime + 0.1f);
                    fireComputeShader.Dispatch(kernelSeedID, 1, 1, 1);
                }
            }
            
            // Flush pending pixel draws after painting
            FlushPendingPixelDraws();
        }
        
        [Header("Extinguish Settings")]
        [SerializeField] private int extinguishBrushSizePixels = 3;

        /// <summary>
        /// Clear all fires and grass interaction map. Convenience method to reset both systems.
        /// </summary>
        public void ClearAll()
        {
            ExtinguishAll();
            ClearGrassInteractionMap();
        }

        /// <summary>
        /// Clear grass interaction map if InteractiveGrass is available.
        /// </summary>
        public void ClearGrassInteractionMap()
        {
#if LEMIGAME_INTERACTIVE_GRASS
            if (InteractiveGrass.Instance != null)
            {
                InteractiveGrass.Instance.ClearInteractiveMap();
            }
#endif
        }

        /// <summary>
        /// Extinguish all fires and clear burn/state maps.
        /// </summary>
        public void ExtinguishAll()
        {
            // Clear CPU-side data
            activeFiresList.Clear();
            activeFirePixels.Clear();
            cachedWorldPositions.Clear(); // Clear position cache
            nextFireID = 0;
            pendingVfxSpawns.Clear();
            
            // Reset fire update timer
            lastFireUpdateTime = -1f;
            
            // Clear GPU buffers
            if (newFiresBuffer != null)
            {
                newFiresBuffer.SetCounterValue(0);
            }
            if (fireStateBuffer != null && activeFiresList.Count == 0)
            {
                // Clear fire state buffer by setting all to zero
                int fireStateSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(FireStateGPU));
                int capacity = settings.maxFireCount;
                FireStateGPU[] emptyStates = new FireStateGPU[capacity];
                fireStateBuffer.SetData(emptyStates);
            }
            if (spreadResultBuffer != null)
            {
                spreadResultBuffer.SetCounterValue(0);
            }
            
            // Clear state map (CA mode)
            if (stateMap != null)
            {
                // Clear state map by recreating
                int w = stateMap.width;
                int h = stateMap.height;
                stateMap.Release();
                stateMap = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat);
                stateMap.enableRandomWrite = true;
                stateMap.name = "FireStateMap";
                stateMap.Create();
                
                // Rebind to compute shader
                if (kernelGridUpdateID != 0)
                {
                    fireComputeShader.SetTexture(kernelGridUpdateID, "_StateMap", stateMap);
                }
                if (kernelGridSpreadID != 0)
                {
                    fireComputeShader.SetTexture(kernelGridSpreadID, "_StateMap", stateMap);
                }
                if (kernelGridApplyID != 0)
                {
                    fireComputeShader.SetTexture(kernelGridApplyID, "_StateMap", stateMap);
                }
                if (kernelSeedID != 0)
                {
                    fireComputeShader.SetTexture(kernelSeedID, "_StateMap", stateMap);
                }
                if (kernelClearStateMapRegionID != 0)
                {
                    fireComputeShader.SetTexture(kernelClearStateMapRegionID, "_StateMap", stateMap);
                }
            }
            
            // Clear burn map
            ClearBurnMap();
            
            // Invalidate cache to force refresh (double buffered)
            if (burnMapCacheA != null) { Destroy(burnMapCacheA); burnMapCacheA = null; }
            if (burnMapCacheB != null) { Destroy(burnMapCacheB); burnMapCacheB = null; }
            usingCacheA = true; // Reset to cache A
        }

        /// <summary>
        /// Clear fire and grass interaction at a world position within a given radius.
        /// Convenience method to clear both fire and grass in an area.
        /// </summary>
        public void ClearAt(Vector3 worldPos, float worldRadius)
        {
            ExtinguishAt(worldPos, worldRadius);
            ClearGrassInteractionAt(worldPos, worldRadius);
        }

        /// <summary>
        /// Clear grass interaction map at a world position within a given radius.
        /// </summary>
        public void ClearGrassInteractionAt(Vector3 worldPos, float worldRadius)
        {
#if LEMIGAME_INTERACTIVE_GRASS
            if (InteractiveGrass.Instance == null || InteractiveGrass.Instance.interactiveMap == null) return;
            if (terrain == null) return;

            RenderTexture interactiveMap = InteractiveGrass.Instance.interactiveMap;
            
            // Convert world position to texture coordinates
            float relativeX = (worldPos.x - terrainPosition.x) / terrainSize.x;
            float relativeZ = (worldPos.z - terrainPosition.z) / terrainSize.z;
            float texX = relativeX * interactiveMap.width;
            float texY = (1f - relativeZ) * interactiveMap.height;
            
            // Convert radius to texture space
            float radiusInTex = (worldRadius / terrainSize.x) * interactiveMap.width;
            
            // Calculate pixel bounds
            int centerX = Mathf.FloorToInt(texX);
            int centerY = Mathf.FloorToInt(texY);
            int pixelRadius = Mathf.CeilToInt(radiusInTex);
            
            int minX = Mathf.Max(0, centerX - pixelRadius);
            int maxX = Mathf.Min(interactiveMap.width - 1, centerX + pixelRadius);
            int minY = Mathf.Max(0, centerY - pixelRadius);
            int maxY = Mathf.Min(interactiveMap.height - 1, centerY + pixelRadius);

            // Clear the region by drawing black using GL
            Shader drawShader = Shader.Find("Hidden/Internal-Colored");
            if (drawShader == null) return;
            
            Material clearMaterial = new Material(drawShader);
            clearMaterial.hideFlags = HideFlags.HideAndDontSave;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, interactiveMap.width, interactiveMap.height, 0);
            RenderTexture.active = interactiveMap;
            clearMaterial.SetPass(0);

            // Draw a circle of black pixels
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - texX) / radiusInTex;
                    float dy = (y - texY) / radiusInTex;
                    float distSqr = dx * dx + dy * dy;
                    
                    if (distSqr <= 1.0f)
                    {
                        GL.Begin(GL.QUADS);
                        GL.Color(new Color(0f, 0f, 0f, 1f));
                        GL.Vertex3(x, y, 0);
                        GL.Vertex3(x + 1, y, 0);
                        GL.Vertex3(x + 1, y + 1, 0);
                        GL.Vertex3(x, y + 1, 0);
                        GL.End();
                    }
                }
            }

            GL.PopMatrix();
            RenderTexture.active = null;
            Destroy(clearMaterial);
#endif
        }

        /// <summary>
        /// Extinguish fire near a world position within a given radius (world units).
        /// </summary>
        public void ExtinguishAt(Vector3 worldPos, float worldRadius)
        {
            if (burnMap == null || terrain == null) return;

            Vector2Int center = WorldPosToPixel(worldPos);
            int pixelRadius = Mathf.RoundToInt((worldRadius / terrainSize.x) * burnMap.width);
            pixelRadius = Mathf.Max(pixelRadius, extinguishBrushSizePixels);
            
            // Calculate pixel bounds for clearing
            int minX = Mathf.Max(0, center.x - pixelRadius);
            int maxX = Mathf.Min(burnMap.width - 1, center.x + pixelRadius);
            int minY = Mathf.Max(0, center.y - pixelRadius);
            int maxY = Mathf.Min(burnMap.height - 1, center.y + pixelRadius);

                    // Remove active fires within radius (CPU-side buffer mode)
            for (int i = activeFiresList.Count - 1; i >= 0; i--)
            {
                Vector2Int pixel = activeFiresList[i].pixelPos;
                if (pixel.x >= minX && pixel.x <= maxX && pixel.y >= minY && pixel.y <= maxY)
                {
                    // Use cached position if available, otherwise compute
                    Vector3 p;
                    if (!cachedWorldPositions.TryGetValue(pixel, out p))
                    {
                        p = PixelToWorldPos(pixel.x, pixel.y);
                    }
                    
                    if ((new Vector2(p.x, p.z) - new Vector2(worldPos.x, worldPos.z)).sqrMagnitude <= worldRadius * worldRadius)
                    {
                        activeFirePixels.Remove(pixel);
                        cachedWorldPositions.Remove(pixel); // Remove from cache
                        activeFiresList.RemoveAt(i);
                    }
                }
            }

            // Clear state map in the affected area (CA mode)
            if (stateMap != null && kernelGridUpdateID != 0)
            {
                ClearStateMapRegion(minX, maxX, minY, maxY);
            }

            // Flush any pending pixel draws before drawing zero circle
            FlushPendingPixelDraws();
            
            // Paint over burn map to zero in pixel radius
            DrawZeroCircle(center.x, center.y, pixelRadius);
            
            // Remove pending VFX spawns in the affected area
            for (int i = pendingVfxSpawns.Count - 1; i >= 0; i--)
            {
                Vector2Int pixel = WorldPosToPixel(pendingVfxSpawns[i].position);
                if (pixel.x >= minX && pixel.x <= maxX && pixel.y >= minY && pixel.y <= maxY)
                {
                    Vector3 p = pendingVfxSpawns[i].position;
                    if ((new Vector2(p.x, p.z) - new Vector2(worldPos.x, worldPos.z)).sqrMagnitude <= worldRadius * worldRadius)
                    {
                        pendingVfxSpawns.RemoveAt(i);
                    }
                }
            }
            
            // Invalidate cache to force refresh (double buffered)
            if (burnMapCacheA != null) { Destroy(burnMapCacheA); burnMapCacheA = null; }
            if (burnMapCacheB != null) { Destroy(burnMapCacheB); burnMapCacheB = null; }
            usingCacheA = true; // Reset to cache A
        }
        
        /// <summary>
        /// Clear a region of the state map (sets all pixels to zero).
        /// </summary>
        private void ClearStateMapRegion(int minX, int maxX, int minY, int maxY)
        {
            if (stateMap == null || fireComputeShader == null) return;
            if (kernelClearStateMapRegionID == 0) return;
            
            // Clamp bounds
            minX = Mathf.Max(0, minX);
            maxX = Mathf.Min(stateMap.width - 1, maxX);
            minY = Mathf.Max(0, minY);
            maxY = Mathf.Min(stateMap.height - 1, maxY);
            
            if (minX > maxX || minY > maxY) return;
            
            // Set parameters
            fireComputeShader.SetInt("_ClearMinX", minX);
            fireComputeShader.SetInt("_ClearMaxX", maxX);
            fireComputeShader.SetInt("_ClearMinY", minY);
            fireComputeShader.SetInt("_ClearMaxY", maxY);
            fireComputeShader.SetInt("_TextureWidth", stateMap.width);
            fireComputeShader.SetInt("_TextureHeight", stateMap.height);
            
            // Dispatch compute shader to clear the region
            int gx = Mathf.CeilToInt(stateMap.width / 8.0f);
            int gy = Mathf.CeilToInt(stateMap.height / 8.0f);
            fireComputeShader.Dispatch(kernelClearStateMapRegionID, Mathf.Max(1, gx), Mathf.Max(1, gy), 1);
        }

        private void DrawZeroCircle(int cx, int cy, int radius)
        {
            if (burnMap == null || burnMaterial == null) return;
            int width = burnMap.width;
            int height = burnMap.height;
            RenderTexture.active = burnMap;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, height, 0);
            burnMaterial.SetPass(0);
            for (int i = -radius; i <= radius; i++)
            {
                int y = cy + i;
                int half = Mathf.RoundToInt(Mathf.Sqrt(Mathf.Max(0, radius * radius - i * i)));
                int x1 = Mathf.Clamp(cx - half, 0, width - 1);
                int x2 = Mathf.Clamp(cx + half, 0, width - 1);
                GL.Begin(GL.QUADS);
                GL.Color(new Color(0f, 0f, 0f, 1f));
                GL.Vertex3(x1, y, 0);
                GL.Vertex3(x2, y, 0);
                GL.Vertex3(x2, y + 1, 0);
                GL.Vertex3(x1, y + 1, 0);
                GL.End();
            }
            GL.PopMatrix();
            RenderTexture.active = null;
        }

        void Update()
        {
            // Check async readback request status (optional: for timeout detection)
            // Note: AsyncGPUReadback callbacks are called automatically when ready
            // This Update method is mainly for monitoring/debugging purposes
        }
        
        void OnDestroy()
        {
            // Wait for any pending async readback requests to complete
            if (isNewFiresCountPending && newFiresCountRequest.done == false)
            {
                newFiresCountRequest.WaitForCompletion();
            }
            if (isNewFiresDataPending && newFiresDataRequest.done == false)
            {
                newFiresDataRequest.WaitForCompletion();
            }
            if (isFireStatesPending && fireStatesRequest.done == false)
            {
                fireStatesRequest.WaitForCompletion();
            }
            if (isSpreadResultsPending && spreadResultsRequest.done == false)
            {
                spreadResultsRequest.WaitForCompletion();
            }
            if (isBurnMapCachePending && burnMapCacheRequest.done == false)
            {
                burnMapCacheRequest.WaitForCompletion();
            }
            
            fireStateBuffer?.Dispose();
            visiblePositionsBuffer?.Dispose();
            fireDataBuffer?.Dispose();
            countBuffer?.Dispose();
            spreadResultBuffer?.Dispose();
            fireStateListCPU?.Dispose();
            if (stateMap != null) { stateMap.Release(); stateMap = null; }
            newFiresBuffer?.Dispose();
            if (burnMap != null) { burnMap.Release(); burnMap = null; }
            if (burnMaterial != null) { Destroy(burnMaterial); }
            if (burnMapCacheA != null) { Destroy(burnMapCacheA); burnMapCacheA = null; }
            if (burnMapCacheB != null) { Destroy(burnMapCacheB); burnMapCacheB = null; }
#if LEMIGAME_INTERACTIVE_GRASS
            if (interactiveMapCache != null) { Destroy(interactiveMapCache); interactiveMapCache = null; }
#endif
        }
    }
}


