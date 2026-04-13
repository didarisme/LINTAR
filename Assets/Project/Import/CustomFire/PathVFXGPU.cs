using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Rendering;
using Unity.Collections;
using System.Collections.Generic;

namespace LemiGame
{
    /// <summary>
    /// GPU path culling and feeding to VFX Graph using regionMap RenderTexture.
    /// Supports circular regions via regionMap (similar to FireSpreadController's burnMap).
    /// </summary>
    public class PathVFXGPU : MonoBehaviour
    {
        [Header("Region Map")]
        [Tooltip("RenderTexture for storing circular region markers. Must be assigned by user.")]
        public RenderTexture regionMap;

        [Header("Terrain (Optional - auto-fills World Mapping)")]
        [Tooltip("If assigned, will automatically set originXZ, sizeXZ, heightScale, and heightOffset from terrain")]
        public Terrain terrain;

        [Header("Height")]
        [Tooltip("Height map texture (required for height sampling)")]
        public Texture heightTex;

        [Header("World Mapping")]
        [Tooltip("Lower-left world position (x,z). Auto-filled from Terrain if assigned.")]
        public Vector2 originXZ;              // Lower-left world position (x,z)
        [Tooltip("World size (width, depth). Auto-filled from Terrain if assigned.")]
        public Vector2 sizeXZ;                // World size (width, depth)
        [Tooltip("Height scale (world units). Auto-filled from Terrain if assigned.")]
        public float heightScale = 100f;      // Height scale (world units)
        [Tooltip("Height offset (world units). Auto-filled from Terrain if assigned.")]
        public float heightOffset = 0f;       // Height offset (world units)
        
        [Header("Height Map Transform")]
        [Tooltip("Mirror height map horizontally (X axis)")]
        public bool mirrorHeightX = false;
        [Tooltip("Mirror height map vertically (Z axis)")]
        public bool mirrorHeightZ = true; // Default true for correct Z axis mapping
        [Tooltip("Rotate height map: 0=0°, 1=90°, 2=180°, 3=270°")]
        [Range(0, 3)]
        public int rotateHeightSteps = 0;
        
        [Header("Particle Density")]
        [Tooltip("Pixel step for particle sampling. 1.0 = 1 pixel per particle, 2.0 = 1 particle per 2 pixels, etc. Higher values = fewer particles")]
        [Range(0.5f, 5f)]
        public float pixelStep = 1f;

        [Header("Update")]
        public int updateEveryNFrames = 1;    // 1 = every frame
        public float updateInterval = 0.1f;    // Update interval in seconds (0 = every frame)

        [Header("VFX Graph Binding")]
        public VisualEffect vfx;
        public string positionBufferProp = "PositionBuffer";
        public string positionCountProp  = "PositionCount";
        
        [Tooltip("Use Single Burst event to spawn particles (recommended). If false, uses continuous emission.")]
        public bool useSingleBurst = true;
        
        [Tooltip("Event name for Single Burst (if useSingleBurst is true). Leave empty to use default 'Spawn' event.")]
        public string burstEventName = "Spawn";
        
        [Tooltip("Send SingleBurst event even when data hasn't changed (for re-triggering particles). If false, only sends event when data changes.")]
        public bool alwaysSendBurstEvent = false;

        [Tooltip("Max visible points on screen (VFX Capacity must be >= this value)")]
        public uint capacity = 50000;

        [Header("Compute")]
        public ComputeShader cs;
        public string kernelName = "CSFilter";

        // Internal
        int kernel;
        int width, height;
        GraphicsBuffer visiblePoints;   // Append buffer (float3)
        GraphicsBuffer countBuffer;     // 1*uint, CopyCount target
        int positionBufferPropID;      // Cached VFX property ID
        int positionCountPropID;       // Cached VFX property ID for position count
        float lastUpdateTime = 0f;
        
        // Async GPU readback
        private AsyncGPUReadbackRequest visibleCountRequest;
        private bool isVisibleCountPending = false;
        
        // Temporary state for async callback
        private bool pendingNeedsRecompute = false;
        private bool pendingShouldSendEvent = false;
        
        // Async region map cache readback
        private AsyncGPUReadbackRequest regionMapCacheRequest;
        private bool isRegionMapCachePending = false;

        // Region drawing
        private Material regionDrawMaterial;  // Material for drawing regions
        private Dictionary<object, RegionData> registeredRegions = new Dictionary<object, RegionData>();

        // Double buffer for region map cache
        private Texture2D regionMapCacheA;
        private Texture2D regionMapCacheB;
        private bool usingCacheA = true; // Flag to indicate which cache is currently active for reading
        private NativeArray<Color32> cacheDataA;
        private NativeArray<Color32> cacheDataB;
        public bool IsRegionMapCacheReady { get; private set; } = false; // Public property for external scripts
        
        private bool regionMapCacheDirty = true; // True when regionMap has been updated and cache needs refresh
        private float lastRegionMapCacheUpdate = -1f;
        private const float REGION_MAP_CACHE_UPDATE_INTERVAL = 0.5f; // Minimum interval between cache updates
        
        // Compute Shader dirty tracking - avoid unnecessary recomputation
        private bool computeNeedsUpdate = true; // True when Compute Shader needs to be re-executed
        private int lastVisibleCount = 0; // Cache last visible count to avoid unnecessary updates

        private struct RegionData
        {
            public Vector3 center;
            public float radius;
        }

        void Start()
        {
            if (!vfx) vfx = GetComponent<VisualEffect>();
            if (vfx != null && !vfx.isActiveAndEnabled) vfx.Play();

            if (terrain != null)
            {
                InitializeFromTerrain();
            }

            if (regionMap == null)
            {
                Debug.LogError("PathVFXGPU: regionMap must be assigned!");
                return;
            }
            
            if (!regionMap.IsCreated())
            {
                regionMap.Create();
            }
            
            width = regionMap.width;
            height = regionMap.height;
            
            if (cs == null)
            {
                Debug.LogError("PathVFXGPU: ComputeShader must be assigned!");
                return;
            }

            // Initialize draw material BEFORE clearing regionMap (ClearRegionMap needs it)
            InitializeDrawMaterial();
            
            // Clear regionMap to remove any previous data (must be after InitializeDrawMaterial)
            ClearRegionMap();

            kernel = cs.FindKernel(kernelName);
            if (kernel == -1)
            {
                Debug.LogError($"PathVFXGPU: Kernel '{kernelName}' not found in ComputeShader!");
                return;
            }
            
            visiblePoints = new GraphicsBuffer(GraphicsBuffer.Target.Append, (int)capacity, sizeof(float)*3);
            visiblePoints.SetCounterValue(0);
            countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint));

            UpdateParameters();

            positionBufferPropID = Shader.PropertyToID(positionBufferProp);
            positionCountPropID = Shader.PropertyToID(positionCountProp);

            DispatchAndFeedVFX();
            
            // Initialize double buffer caches (synchronous read once at startup)
            InitializeRegionMapCaches();
        }

        void OnDestroy()
        {
            // Wait for any pending async readback requests to complete
            if (isVisibleCountPending && visibleCountRequest.done == false)
            {
                visibleCountRequest.WaitForCompletion();
            }
            if (isRegionMapCachePending && regionMapCacheRequest.done == false)
            {
                regionMapCacheRequest.WaitForCompletion();
            }
            
            visiblePoints?.Dispose();
            countBuffer?.Dispose();
            if (regionDrawMaterial != null)
            {
                Destroy(regionDrawMaterial);
            }
            if (regionMapCacheA != null)
            {
                Destroy(regionMapCacheA);
                regionMapCacheA = null;
            }
            if (regionMapCacheB != null)
            {
                Destroy(regionMapCacheB);
                regionMapCacheB = null;
            }
        }

        [ContextMenu("Update Compute Parameters")]
        private void UpdateParameters()
        {
            if (regionMap == null)
            {
                Debug.LogError("PathVFXGPU.UpdateParameters: regionMap is null!");
                return;
            }
            if (cs == null)
            {
                Debug.LogError("PathVFXGPU.UpdateParameters: ComputeShader is null!");
                return;
            }

            cs.SetInt("_Width", width);
            cs.SetInt("_Height", height);
            cs.SetVector("_OriginXZ", new Vector2(originXZ.x, originXZ.y));
            cs.SetVector("_SizeXZ", new Vector2(sizeXZ.x, sizeXZ.y));
            
            bool useHeightTex = (heightTex != null);
            cs.SetInt("_UseHeightTex", useHeightTex ? 1 : 0);
            cs.SetFloat("_HeightScale", heightScale);
            cs.SetFloat("_HeightOffset", heightOffset);
            cs.SetInt("_MirrorHeightX", mirrorHeightX ? 1 : 0);
            cs.SetInt("_MirrorHeightZ", mirrorHeightZ ? 1 : 0);
            cs.SetInt("_RotateHeightSteps", rotateHeightSteps);
            cs.SetFloat("_PixelStep", pixelStep);

            cs.SetTexture(kernel, "_RegionMap", regionMap);
            if (useHeightTex)
            {
                cs.SetTexture(kernel, "_HeightTex", heightTex);
            }
            cs.SetBuffer(kernel, "VisiblePoints", visiblePoints);
            
            // Parameters changed, need to recompute
            computeNeedsUpdate = true;
        }

        void Update()
        {
            if (updateEveryNFrames > 1 && (Time.frameCount % updateEveryNFrames != 0)) return;
            
            if (updateInterval > 0f && Time.time - lastUpdateTime < updateInterval) return;

            // Request async cache update if dirty (non-blocking)
            if (regionMapCacheDirty)
            {
                RequestRegionMapCacheAsync();
            }

            // Update if data changed OR if alwaysSendBurstEvent is enabled (to send events even when data unchanged)
            bool shouldUpdate = computeNeedsUpdate || (alwaysSendBurstEvent && useSingleBurst);
            if (shouldUpdate)
            {
                DispatchAndFeedVFX();
            }
            lastUpdateTime = Time.time;
        }
        
        /// <summary>
        /// Force an immediate update of the Compute Shader (bypasses dirty flag check)
        /// </summary>
        public void ForceUpdate()
        {
            computeNeedsUpdate = true;
            DispatchAndFeedVFX();
        }

        private void DispatchAndFeedVFX()
        {
            if (vfx == null || cs == null || regionMap == null) return;
            
            bool needsRecompute = computeNeedsUpdate;
            
            // Only recompute if data changed (optimization: avoid unnecessary GPU computation)
            if (needsRecompute)
            {
                if (!regionMap.IsCreated())
                {
                    regionMap.Create();
                }

                visiblePoints.SetCounterValue(0);

                cs.SetTexture(kernel, "_RegionMap", regionMap);
                cs.SetBuffer(kernel, "VisiblePoints", visiblePoints);
                
                bool useHeightTex = (heightTex != null);
                if (useHeightTex)
                {
                    cs.SetTexture(kernel, "_HeightTex", heightTex);
                }

                cs.SetInt("_Width", width);
                cs.SetInt("_Height", height);
                cs.SetVector("_OriginXZ", new Vector2(originXZ.x, originXZ.y));
                cs.SetVector("_SizeXZ", new Vector2(sizeXZ.x, sizeXZ.y));
                cs.SetInt("_UseHeightTex", useHeightTex ? 1 : 0);
                cs.SetFloat("_HeightScale", heightScale);
                cs.SetFloat("_HeightOffset", heightOffset);
                cs.SetInt("_MirrorHeightX", mirrorHeightX ? 1 : 0);
                cs.SetInt("_MirrorHeightZ", mirrorHeightZ ? 1 : 0);
                cs.SetInt("_RotateHeightSteps", rotateHeightSteps);
                cs.SetFloat("_PixelStep", pixelStep);

                // Adjust dispatch size based on pixel step to maintain coverage
                int gx = Mathf.CeilToInt(width  / (8.0f * pixelStep));
                int gy = Mathf.CeilToInt(height / (8.0f * pixelStep));
                cs.Dispatch(kernel, Mathf.Max(1, gx), Mathf.Max(1, gy), 1);

                // Async read count (non-blocking GPU->CPU transfer)
                RequestVisibleCountAsync();
                
                // Mark as updated (VFX will be updated in async callback)
                computeNeedsUpdate = false;
            }
            else
            {
                // Use cached count - no async read needed
                uint visibleCount = (uint)lastVisibleCount;
                UpdateVFXWithCount(visibleCount, alwaysSendBurstEvent && useSingleBurst);
            }
        }
        
        /// <summary>
        /// Request async readback of visible count from GPU.
        /// </summary>
        private void RequestVisibleCountAsync()
        {
            if (isVisibleCountPending || countBuffer == null) return;
            
            // Copy count to countBuffer first
            GraphicsBuffer.CopyCount(visiblePoints, countBuffer, 0);
            
            // Store state for callback
            pendingNeedsRecompute = true;
            pendingShouldSendEvent = alwaysSendBurstEvent || computeNeedsUpdate;
            
            // Request async readback
            isVisibleCountPending = true;
            visibleCountRequest = AsyncGPUReadback.Request(countBuffer, OnVisibleCountReadback);
        }
        
        /// <summary>
        /// Callback for async readback of visible count.
        /// </summary>
        private void OnVisibleCountReadback(AsyncGPUReadbackRequest request)
        {
            isVisibleCountPending = false;
            
            if (request.hasError)
            {
                Debug.LogError("PathVFXGPU: Failed to async read visible count");
                return;
            }
            
            NativeArray<uint> countData = request.GetData<uint>();
            if (countData.Length < 1)
            {
                Debug.LogError("PathVFXGPU: Invalid count data length");
                return;
            }
            
            uint visibleCount = countData[0];
            
            // Validate and clamp count
            visibleCount = (uint)Mathf.Min(visibleCount, capacity);
            
            // Update cached count
            lastVisibleCount = (int)visibleCount;
            
            // Update VFX with new count
            UpdateVFXWithCount(visibleCount, pendingShouldSendEvent);
        }
        
        /// <summary>
        /// Update VFX with visible count and optionally send event.
        /// </summary>
        private void UpdateVFXWithCount(uint visibleCount, bool shouldSendEvent)
        {
            // Always update VFX buffer (even if data didn't change)
            // This ensures VFX stays in sync with the latest data
            vfx.SetGraphicsBuffer(positionBufferPropID, visiblePoints);
            vfx.SetUInt(positionCountPropID, visibleCount);
            
            // For SingleBurst: send event based on shouldSendEvent parameter
            if (useSingleBurst && visibleCount > 0 && shouldSendEvent)
            {
                string eventName = string.IsNullOrEmpty(burstEventName) ? "Spawn" : burstEventName;
                vfx.SendEvent(eventName);
            }
        }


        /// <summary>
        /// Initialize world mapping parameters from terrain
        /// </summary>
        [ContextMenu("Update World Mapping from Terrain")]
        private void InitializeFromTerrain()
        {
            if (terrain == null)
            {
                Debug.LogWarning("PathVFXGPU: Terrain is not assigned. Cannot update world mapping.");
                return;
            }

            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;

            originXZ = new Vector2(terrainPos.x, terrainPos.z);
            sizeXZ = new Vector2(terrainSize.x, terrainSize.z);
            heightScale = terrainSize.y;
            heightOffset = terrainPos.y;

            Debug.Log($"PathVFXGPU: Updated world mapping from Terrain - originXZ: {originXZ}, sizeXZ: {sizeXZ}, heightScale: {heightScale}, heightOffset: {heightOffset}");
        }

        /// <summary>
        /// Initialize draw material for region drawing
        /// </summary>
        private void InitializeDrawMaterial()
        {
            Shader drawShader = Shader.Find("Hidden/Internal-Colored");
            if (drawShader == null)
            {
                Debug.LogError("PathVFXGPU: Cannot find 'Hidden/Internal-Colored' shader. Region drawing will not work.");
                return;
            }
            regionDrawMaterial = new Material(drawShader);
            regionDrawMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        /// <summary>
        /// Initialize double buffer caches for region map (completely async, no blocking).
        /// Both caches start as black, then async readback fills them.
        /// </summary>
        private void InitializeRegionMapCaches()
        {
            if (regionMap == null) return;

            int width = regionMap.width;
            int height = regionMap.height;

            if (regionMapCacheA == null || regionMapCacheA.width != width || regionMapCacheA.height != height)
            {
                if (regionMapCacheA != null) Destroy(regionMapCacheA);
                regionMapCacheA = new Texture2D(width, height, TextureFormat.RGBA32, false);
                
                // Initialize to black (non-blocking)
                Color32[] blackPixels = new Color32[width * height];
                for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
                regionMapCacheA.SetPixelData(blackPixels, 0);
                regionMapCacheA.Apply();
            }
            
            if (regionMapCacheB == null || regionMapCacheB.width != width || regionMapCacheB.height != height)
            {
                if (regionMapCacheB != null) Destroy(regionMapCacheB);
                regionMapCacheB = new Texture2D(width, height, TextureFormat.RGBA32, false);
                
                // Initialize to black (non-blocking)
                Color32[] blackPixels = new Color32[width * height];
                for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
                regionMapCacheB.SetPixelData(blackPixels, 0);
                regionMapCacheB.Apply();
            }

            usingCacheA = true;
            lastRegionMapCacheUpdate = Time.time;
            IsRegionMapCacheReady = true; // Mark as ready (even if data is black initially)
            
            // Request async readback immediately (non-blocking)
            RequestRegionMapCacheAsync(forceUpdate: true);
        }

        /// <summary>
        /// Convert world position to pixel coordinates
        /// </summary>
        private Vector2Int WorldPosToPixel(Vector3 worldPos)
        {
            if (regionMap == null) return Vector2Int.zero;

            float relativeX = (worldPos.x - originXZ.x) / sizeXZ.x;
            float relativeZ = (worldPos.z - originXZ.y) / sizeXZ.y;

            Vector2 uv = new Vector2(
                relativeX * width,
                (1 - relativeZ) * height
            );

            return new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(uv.x), 0, width - 1),
                Mathf.Clamp(Mathf.FloorToInt(uv.y), 0, height - 1)
            );
        }

        /// <summary>
        /// Convert world radius to pixel radius
        /// </summary>
        private int WorldRadiusToPixelRadius(float worldRadius)
        {
            if (regionMap == null || sizeXZ.x == 0) return 1; // At least 1 pixel
            int pixelRadius = Mathf.RoundToInt((worldRadius / sizeXZ.x) * width);
            return Mathf.Max(1, pixelRadius); // At least 1 pixel
        }

        /// <summary>
        /// Draw a circular region to regionMap (similar to FireSpreadController's DrawZeroCircle)
        /// </summary>
        private void DrawCircularRegion(Vector3 center, float radius, float value = 1f)
        {
            if (regionMap == null || regionDrawMaterial == null)
            {
                Debug.LogWarning($"PathVFXGPU.DrawCircularRegion: regionMap={regionMap}, regionDrawMaterial={regionDrawMaterial}");
                return;
            }

            Vector2Int centerPixel = WorldPosToPixel(center);
            int pixelRadius = WorldRadiusToPixelRadius(radius);

            RenderTexture.active = regionMap;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, regionMap.width, regionMap.height, 0);
            regionDrawMaterial.SetPass(0);

            // Draw circle using horizontal lines (similar to FireSpreadController)
            for (int i = -pixelRadius; i <= pixelRadius; i++)
            {
                int y = centerPixel.y + i;
                if (y < 0 || y >= regionMap.height) continue;

                int half = Mathf.RoundToInt(Mathf.Sqrt(Mathf.Max(0, pixelRadius * pixelRadius - i * i)));
                int x1 = Mathf.Clamp(centerPixel.x - half, 0, regionMap.width - 1);
                int x2 = Mathf.Clamp(centerPixel.x + half, 0, regionMap.width - 1);

                if (x2 <= x1) continue; // Skip if invalid range

                GL.Begin(GL.QUADS);
                GL.Color(new Color(value, value, value, 1f));
                GL.Vertex3(x1, y, 0);
                GL.Vertex3(x2, y, 0);
                GL.Vertex3(x2, y + 1, 0);
                GL.Vertex3(x1, y + 1, 0);
                GL.End();
            }

            GL.PopMatrix();
            RenderTexture.active = null;
        }

        /// <summary>
        /// Clear a circular region (draw black)
        /// </summary>
        private void ClearCircularRegion(Vector3 center, float radius)
        {
            DrawCircularRegion(center, radius, 0f);
        }
        
        /// <summary>
        /// Clear entire regionMap (initialize to black)
        /// </summary>
        private void ClearRegionMap()
        {
            if (regionMap == null) return;
            
            // Method 1: Use GL drawing if material is available (preferred)
            if (regionDrawMaterial != null)
            {
                RenderTexture.active = regionMap;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, regionMap.width, regionMap.height, 0);
                regionDrawMaterial.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(new Color(0, 0, 0, 1f)); // Black color (0,0,0,1) not transparent
                GL.Vertex3(0, 0, 0);
                GL.Vertex3(regionMap.width, 0, 0);
                GL.Vertex3(regionMap.width, regionMap.height, 0);
                GL.Vertex3(0, regionMap.height, 0);
                GL.End();
                GL.PopMatrix();
                RenderTexture.active = null;
            }
            else
            {
                // Method 2: Fallback - use Graphics.ClearRenderTarget (works even without material)
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = regionMap;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = previous;
            }
            
            // Clear both caches to black
            if (regionMapCacheA != null)
            {
                Color32[] blackPixels = new Color32[regionMapCacheA.width * regionMapCacheA.height];
                for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
                regionMapCacheA.SetPixelData(blackPixels, 0);
                regionMapCacheA.Apply();
            }
            if (regionMapCacheB != null)
            {
                Color32[] blackPixels = new Color32[regionMapCacheB.width * regionMapCacheB.height];
                for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
                regionMapCacheB.SetPixelData(blackPixels, 0);
                regionMapCacheB.Apply();
            }
            
            regionMapCacheDirty = true;
            computeNeedsUpdate = true; // Mark that Compute Shader needs to re-run
        }

        /// <summary>
        /// Redraw all registered regions to regionMap (ensures overlapping regions work correctly)
        /// </summary>
        private void RedrawAllRegions()
        {
            if (regionMap == null || regionDrawMaterial == null) return;
            
            // Clear entire regionMap first
            ClearRegionMap();
            
            // Redraw all registered regions
            foreach (var region in registeredRegions.Values)
            {
                DrawCircularRegion(region.center, region.radius, 1f);
            }
            
            regionMapCacheDirty = true;
            computeNeedsUpdate = true;
        }

        /// <summary>
        /// Register a circular region (draws to regionMap)
        /// </summary>
        public void RegisterRegion(Vector3 center, float radius, object owner)
        {
            if (regionMap == null || regionDrawMaterial == null) return;
            
            if (!regionMap.IsCreated())
            {
                regionMap.Create();
            }

            // Check if this owner already has a region registered
            bool wasRegistered = registeredRegions.ContainsKey(owner);
            
            registeredRegions[owner] = new RegionData { center = center, radius = radius };
            
            // If updating an existing region, redraw all to handle overlaps correctly
            // If new region, we can just draw it (regionMap is black by default)
            if (wasRegistered)
            {
                RedrawAllRegions();
            }
            else
            {
                // New region - just draw it (additive over black background)
                DrawCircularRegion(center, radius, 1f);
                regionMapCacheDirty = true;
                computeNeedsUpdate = true;
            }
            
            // Force immediate update to make region visible
            if (Application.isPlaying)
            {
                DispatchAndFeedVFX();
            }
        }

        /// <summary>
        /// Unregister a circular region (clears from regionMap)
        /// </summary>
        public void UnregisterRegion(object owner)
        {
            if (registeredRegions.Remove(owner))
            {
                // Redraw all remaining regions to ensure overlapping regions work correctly
                RedrawAllRegions();
                
                // Force immediate update to reflect removal
                if (Application.isPlaying)
                {
                    DispatchAndFeedVFX();
                }
            }
        }

        /// <summary>
        /// Update a registered region (redraws it)
        /// </summary>
        public void UpdateRegion(object owner, Vector3 center, float radius)
        {
            if (!registeredRegions.ContainsKey(owner))
            {
                // If not registered, register it instead
                RegisterRegion(center, radius, owner);
                return;
            }

            // Update the region data
            registeredRegions[owner] = new RegionData { center = center, radius = radius };
            
            // Redraw all regions to ensure overlapping regions work correctly
            RedrawAllRegions();
            
            // Force immediate update
            if (Application.isPlaying)
            {
                DispatchAndFeedVFX();
            }
        }

        /// <summary>
        /// Request async readback of region map cache from GPU.
        /// </summary>
        private void RequestRegionMapCacheAsync(bool forceUpdate = false)
        {
            if (isRegionMapCachePending || regionMap == null) return;
            
            float currentTime = Time.time;
            float updateInterval = forceUpdate ? 0f : REGION_MAP_CACHE_UPDATE_INTERVAL;
            
            // Check if update is needed
            bool shouldUpdate = forceUpdate || (regionMapCacheDirty && 
                               (currentTime - lastRegionMapCacheUpdate >= updateInterval));
            
            // Determine which cache to write to (the inactive one)
            Texture2D targetCache = usingCacheA ? regionMapCacheB : regionMapCacheA;

            // Create or validate cache size
            if (targetCache == null || targetCache.width != regionMap.width || targetCache.height != regionMap.height)
            {
                if (targetCache != null) Destroy(targetCache);
                targetCache = new Texture2D(regionMap.width, regionMap.height, TextureFormat.RGBA32, false);
                if (usingCacheA) regionMapCacheB = targetCache; else regionMapCacheA = targetCache;
            }
            
            // If no update is needed and cache is valid, just mark as ready and return
            if (!shouldUpdate && targetCache != null)
            {
                IsRegionMapCacheReady = true;
                return;
            }

            // Request async readback (mipmap level 0, RGBA32 format)
            isRegionMapCachePending = true;
            IsRegionMapCacheReady = false; // Mark as not ready until new data is available
            regionMapCacheRequest = AsyncGPUReadback.Request(regionMap, 0, TextureFormat.RGBA32, OnRegionMapCacheReadback);
        }
        
        /// <summary>
        /// Callback for async readback of region map cache.
        /// </summary>
        private void OnRegionMapCacheReadback(AsyncGPUReadbackRequest request)
        {
            isRegionMapCachePending = false;
            
            if (request.hasError)
            {
                Debug.LogError("PathVFXGPU: Failed to async read region map cache");
                IsRegionMapCacheReady = false;
                return;
            }
            
            // Copy async readback data to the inactive cache
            NativeArray<Color32> data = request.GetData<Color32>();
            Texture2D targetCache = usingCacheA ? regionMapCacheB : regionMapCacheA;
            targetCache.SetPixelData(data, 0);
            targetCache.Apply();
            
            // Swap buffers: now the newly updated cache becomes the active one
            usingCacheA = !usingCacheA;
            lastRegionMapCacheUpdate = Time.time;
            regionMapCacheDirty = false;
            IsRegionMapCacheReady = true; // Mark as ready after new data is available
        }
        
        /// <summary>
        /// Update CPU-side cache of regionMap for efficient reading (legacy method, now uses async).
        /// Only updates when cache is dirty AND enough time has passed since last update.
        /// </summary>
        private void UpdateRegionMapCache()
        {
            // Use async readback instead of synchronous ReadPixels
            RequestRegionMapCacheAsync();
        }

        /// <summary>
        /// Sample region value from CPU cache.
        /// Returns true if the world position is within a region (value > 0.5).
        /// Always non-blocking - uses double-buffered cache for immediate access.
        /// </summary>
        public bool IsWorldPositionInRegion(Vector3 worldPos)
        {
            if (regionMap == null || !IsRegionMapCacheReady) return false;

            Vector2Int px = WorldPosToPixel(worldPos);
            float regionValue = SampleRegionValue(px.x, px.y);

            // Check if in region (value > 0.5)
            return regionValue > 0.5f;
        }

        /// <summary>
        /// Sample region value from CPU cache at pixel coordinates.
        /// </summary>
        private float SampleRegionValue(int x, int y)
        {
            // Read from the currently active cache
            Texture2D activeCache = usingCacheA ? regionMapCacheA : regionMapCacheB;
            if (activeCache == null || regionMap == null) return 0f;
            x = Mathf.Clamp(x, 0, regionMap.width - 1);
            y = Mathf.Clamp(y, 0, regionMap.height - 1);
            // Note: Texture2D y-coordinate is from bottom to top, while our UV is from top to bottom, need to flip
            int cacheY = regionMap.height - 1 - y;
            Color c = activeCache.GetPixel(x, cacheY);
            return c.r; // R channel contains region value
        }
    }
}
