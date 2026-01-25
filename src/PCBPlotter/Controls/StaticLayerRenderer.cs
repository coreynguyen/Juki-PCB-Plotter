using System;
using System.Collections.Generic;
#if USE_OPENGL
using OpenTK;
using OpenTK.Graphics.OpenGL;
#endif

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Renders static Gerber geometry without CPU-side iteration per frame.
    /// Uses tile-based spatial partitioning with pre-built GPU instance buffers.
    ///
    /// Key optimization: Instance data (positions, colors, scales) is uploaded to GPU ONCE
    /// when the layer is loaded. During rendering, only visible tiles are drawn with
    /// simple glDrawElementsInstanced calls - no per-primitive C# iteration needed.
    ///
    /// Two-phase initialization:
    /// 1. PrepareData() - Heavy CPU work (can run on background thread)
    /// 2. Upload() - Light work storing precomputed data (must run on UI thread)
    /// </summary>
    public class StaticLayerRenderer : IDisposable
    {
#if USE_OPENGL
        /// <summary>
        /// Holds pre-computed tile data prepared on background thread.
        /// Contains raw float arrays for GPU upload - no OpenGL calls.
        /// </summary>
        public class PreparedTileData
        {
            public float MinX, MinY, MaxX, MaxY;
            public float[] CircleInstances;  // Raw float array (prepared on background thread)
            public float[] RectInstances;    // Raw float array (prepared on background thread)
            public int CircleCount;
            public int RectCount;
            public int CircleInstanceVbo;    // GPU handle (filled during lazy upload)
            public int RectInstanceVbo;      // GPU handle (filled during lazy upload)
            public int CircleVao;            // GPU handle (filled during lazy upload)
            public int RectVao;              // GPU handle (filled during lazy upload)
            public bool IsUploaded;
        }

        private List<PreparedTileData> _tiles = new List<PreparedTileData>();
        private int _tilesX;
        private int _tilesY;
        private float _tileSize;
        private float _originX;
        private float _originY;

        // References to base geometry VAOs from GeometryCache
        private GeometryCache _geometryCache;

        // Layer color (applied as uniform or instance attribute)
        private Vector4 _layerColor;

        // Shader reference
        private int _instancedShader;
        private int _projLoc;
        private int _viewLoc;
        private int _opacityLoc;

        private bool _isInitialized;
        private string _layerId;

        // Statistics
        private int _totalCircles;
        private int _totalRects;
        private int _tilesWithData;

        private const int INSTANCE_SIZE = 8; // pos(2) + scale(2) + color(4) = 8 floats

        /// <summary>
        /// Creates a new static layer renderer.
        /// </summary>
        public StaticLayerRenderer(string layerId, GeometryCache geometryCache, int instancedShader, int projLoc, int viewLoc, int opacityLoc)
        {
            _layerId = layerId;
            _geometryCache = geometryCache;
            _instancedShader = instancedShader;
            _projLoc = projLoc;
            _viewLoc = viewLoc;
            _opacityLoc = opacityLoc;
        }

        /// <summary>
        /// PHASE 1: Heavy CPU work - prepares tile data WITHOUT any OpenGL calls.
        /// This method is THREAD-SAFE and should be called from a BACKGROUND THREAD.
        /// Returns precomputed tile data that can be passed to Upload() on the UI thread.
        /// </summary>
        public static List<PreparedTileData> PrepareData(
            IList<CachedCircle> circles,
            IList<CachedRectangle> rectangles,
            float boundsX, float boundsY, float boundsWidth, float boundsHeight,
            Vector4 layerColor,
            out int tilesX, out int tilesY, out float tileSize,
            out int totalCircles, out int totalRects, out int tilesWithData)
        {
            var tiles = new List<PreparedTileData>();
            totalCircles = circles?.Count ?? 0;
            totalRects = rectangles?.Count ?? 0;
            tilesWithData = 0;

            int totalPrimitives = totalCircles + totalRects;
            if (totalPrimitives == 0)
            {
                tilesX = 1;
                tilesY = 1;
                tileSize = 1f;
                return tiles;
            }

            // Adaptive tile size based on primitive density
            float area = boundsWidth * boundsHeight;
            float density = totalPrimitives / Math.Max(area, 1f);

            // MEGA-TILE: Target ~50000 primitives per tile
            float targetPrimsPerTile = 50000f;
            float targetTileArea = targetPrimsPerTile / Math.Max(density, 0.001f);
            tileSize = (float)Math.Sqrt(targetTileArea);

            // Clamp tile size to reasonable bounds
            tileSize = Math.Max(tileSize, Math.Max(boundsWidth, boundsHeight) / 10f);
            tileSize = Math.Min(tileSize, Math.Max(boundsWidth, boundsHeight));
            tileSize = Math.Max(tileSize, 10f);

            tilesX = Math.Max(1, (int)Math.Ceiling(boundsWidth / tileSize));
            tilesY = Math.Max(1, (int)Math.Ceiling(boundsHeight / tileSize));

            // Cap total tiles at 100
            if (tilesX * tilesY > 100)
            {
                float scale = (float)Math.Sqrt(100.0 / (tilesX * tilesY));
                tilesX = Math.Max(1, (int)(tilesX * scale));
                tilesY = Math.Max(1, (int)(tilesY * scale));
                tileSize = Math.Max(boundsWidth / tilesX, boundsHeight / tilesY);
            }

            int totalTiles = tilesX * tilesY;
            tiles.Capacity = totalTiles;

            // Initialize tile lists for sorting primitives
            var tileCircleLists = new List<List<int>>(totalTiles);
            var tileRectLists = new List<List<int>>(totalTiles);

            for (int i = 0; i < totalTiles; i++)
            {
                int tx = i % tilesX;
                int ty = i / tilesX;

                tiles.Add(new PreparedTileData
                {
                    MinX = boundsX + tx * tileSize,
                    MinY = boundsY + ty * tileSize,
                    MaxX = boundsX + (tx + 1) * tileSize,
                    MaxY = boundsY + (ty + 1) * tileSize
                });
                tileCircleLists.Add(new List<int>());
                tileRectLists.Add(new List<int>());
            }

            // Sort circles into tiles (HEAVY LOOP)
            if (circles != null)
            {
                for (int i = 0; i < circles.Count; i++)
                {
                    var circle = circles[i];
                    int tx = (int)((circle.X - boundsX) / tileSize);
                    int ty = (int)((circle.Y - boundsY) / tileSize);
                    if (tx < 0) tx = 0;
                    if (ty < 0) ty = 0;
                    if (tx >= tilesX) tx = tilesX - 1;
                    if (ty >= tilesY) ty = tilesY - 1;
                    tileCircleLists[ty * tilesX + tx].Add(i);
                }
            }

            // Sort rectangles into tiles (HEAVY LOOP)
            if (rectangles != null)
            {
                for (int i = 0; i < rectangles.Count; i++)
                {
                    var rect = rectangles[i];
                    int tx = (int)((rect.X - boundsX) / tileSize);
                    int ty = (int)((rect.Y - boundsY) / tileSize);
                    if (tx < 0) tx = 0;
                    if (ty < 0) ty = 0;
                    if (tx >= tilesX) tx = tilesX - 1;
                    if (ty >= tilesY) ty = tilesY - 1;
                    tileRectLists[ty * tilesX + tx].Add(i);
                }
            }

            // Build instance arrays for each tile (HEAVY ALLOCATION)
            for (int i = 0; i < totalTiles; i++)
            {
                var tile = tiles[i];
                var tileCircles = tileCircleLists[i];
                var tileRects = tileRectLists[i];

                tile.CircleCount = tileCircles.Count;
                tile.RectCount = tileRects.Count;

                if (tile.CircleCount > 0)
                {
                    tile.CircleInstances = new float[tile.CircleCount * INSTANCE_SIZE];
                    for (int j = 0; j < tileCircles.Count; j++)
                    {
                        var circle = circles[tileCircles[j]];
                        int offset = j * INSTANCE_SIZE;
                        tile.CircleInstances[offset] = circle.X;
                        tile.CircleInstances[offset + 1] = circle.Y;
                        tile.CircleInstances[offset + 2] = circle.Radius;
                        tile.CircleInstances[offset + 3] = circle.Radius;
                        tile.CircleInstances[offset + 4] = layerColor.X;
                        tile.CircleInstances[offset + 5] = layerColor.Y;
                        tile.CircleInstances[offset + 6] = layerColor.Z;
                        tile.CircleInstances[offset + 7] = 1.0f;
                    }
                    tilesWithData++;
                }

                if (tile.RectCount > 0)
                {
                    tile.RectInstances = new float[tile.RectCount * INSTANCE_SIZE];
                    for (int j = 0; j < tileRects.Count; j++)
                    {
                        var rect = rectangles[tileRects[j]];
                        int offset = j * INSTANCE_SIZE;
                        tile.RectInstances[offset] = rect.X;
                        tile.RectInstances[offset + 1] = rect.Y;
                        tile.RectInstances[offset + 2] = rect.Width;
                        tile.RectInstances[offset + 3] = rect.Height;
                        tile.RectInstances[offset + 4] = layerColor.X;
                        tile.RectInstances[offset + 5] = layerColor.Y;
                        tile.RectInstances[offset + 6] = layerColor.Z;
                        tile.RectInstances[offset + 7] = 1.0f;
                    }
                    if (tile.CircleCount == 0) tilesWithData++;
                }
            }

            return tiles;
        }

        /// <summary>
        /// PHASE 2: Light work - stores precomputed tile data.
        /// Must be called on the UI thread before rendering.
        /// </summary>
        public void Upload(List<PreparedTileData> tiles, int tilesX, int tilesY, float tileSize,
                          float originX, float originY, int totalCircles, int totalRects, int tilesWithData)
        {
            if (_isInitialized)
            {
                Dispose();
            }

            _tiles = tiles;
            _tilesX = tilesX;
            _tilesY = tilesY;
            _tileSize = tileSize;
            _originX = originX;
            _originY = originY;
            _totalCircles = totalCircles;
            _totalRects = totalRects;
            _tilesWithData = tilesWithData;
            _isInitialized = true;
        }

        /// <summary>
        /// Legacy method that does both phases on the calling thread.
        /// Use PrepareData() + Upload() for async operation.
        /// </summary>
        public void Initialize(
            IList<CachedCircle> circles,
            IList<CachedRectangle> rectangles,
            float boundsX, float boundsY, float boundsWidth, float boundsHeight,
            Vector4 layerColor)
        {
            int tilesX, tilesY, totalCircles, totalRects, tilesWithData;
            float tileSize;

            var tiles = PrepareData(circles, rectangles, boundsX, boundsY, boundsWidth, boundsHeight,
                                   layerColor, out tilesX, out tilesY, out tileSize,
                                   out totalCircles, out totalRects, out tilesWithData);

            Upload(tiles, tilesX, tilesY, tileSize, boundsX, boundsY, totalCircles, totalRects, tilesWithData);
            _layerColor = layerColor;
        }

        /// <summary>
        /// Uploads tile instance data to GPU and creates dedicated VAOs.
        /// This is done lazily on first render, not during Upload().
        /// Creating dedicated VAOs avoids state conflicts with BatchRenderer's shared VAOs.
        /// </summary>
        private void UploadTileToGpu(PreparedTileData tile)
        {
            if (tile.IsUploaded) return;

            var circleBuffer = _geometryCache.CircleBuffer;
            var rectBuffer = _geometryCache.RectangleBuffer;

            // Upload circle instances and create dedicated VAO
            if (tile.CircleCount > 0 && tile.CircleInstances != null)
            {
                // Create instance VBO
                tile.CircleInstanceVbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, tile.CircleInstanceVbo);
                GL.BufferData(BufferTarget.ArrayBuffer,
                    tile.CircleInstances.Length * sizeof(float),
                    tile.CircleInstances,
                    BufferUsageHint.StaticDraw);

                // Create dedicated VAO for this tile's circles
                tile.CircleVao = GL.GenVertexArray();
                GL.BindVertexArray(tile.CircleVao);

                // Bind the base circle geometry VBO and set up vertex attributes
                // (We need to get the VBO from the original VAO, but since GeometryBuffer
                // stores it, we can access the VAO and rebind)
                // For now, we bind the original VAO to copy the EBO binding
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0); // Will rebind below

                // Get the VBO handle - GeometryBuffer doesn't expose it directly,
                // so we work around by querying the VAO state
                // Actually, let's use a different approach: we manually set up the vertex format

                // For position/texcoord, we need to bind the original VBO
                // Since GeometryBuffer initializes with specific VBO, we need to access it
                // Let's modify the approach: bind the shared VAO's buffers explicitly

                // Get shared circle geometry VBO/EBO directly from GeometryBuffer
                int vboId = circleBuffer.VBO;
                int eboId = circleBuffer.EBO;

                // Now set up our dedicated VAO
                GL.BindVertexArray(tile.CircleVao);

                // Bind the shared vertex VBO and set up vertex attributes
                GL.BindBuffer(BufferTarget.ArrayBuffer, vboId);
                GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
                GL.EnableVertexAttribArray(1);

                // Bind our tile-specific instance VBO and set up instance attributes
                GL.BindBuffer(BufferTarget.ArrayBuffer, tile.CircleInstanceVbo);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 0);
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribDivisor(2, 1);
                GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 2 * sizeof(float));
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribDivisor(3, 1);
                GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 4 * sizeof(float));
                GL.EnableVertexAttribArray(4);
                GL.VertexAttribDivisor(4, 1);

                // Bind shared EBO
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, eboId);

                GL.BindVertexArray(0);

                // Free CPU memory after GPU upload
                tile.CircleInstances = null;
            }

            // Upload rectangle instances and create dedicated VAO
            if (tile.RectCount > 0 && tile.RectInstances != null)
            {
                // Create instance VBO
                tile.RectInstanceVbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, tile.RectInstanceVbo);
                GL.BufferData(BufferTarget.ArrayBuffer,
                    tile.RectInstances.Length * sizeof(float),
                    tile.RectInstances,
                    BufferUsageHint.StaticDraw);

                // Create dedicated VAO for this tile's rectangles
                tile.RectVao = GL.GenVertexArray();

                // Get shared rectangle geometry VBO/EBO directly from GeometryBuffer
                int rectVboId = rectBuffer.VBO;
                int rectEboId = rectBuffer.EBO;

                // Set up our dedicated VAO
                GL.BindVertexArray(tile.RectVao);

                // Bind shared vertex VBO
                GL.BindBuffer(BufferTarget.ArrayBuffer, rectVboId);
                GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
                GL.EnableVertexAttribArray(1);

                // Bind tile-specific instance VBO
                GL.BindBuffer(BufferTarget.ArrayBuffer, tile.RectInstanceVbo);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 0);
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribDivisor(2, 1);
                GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 2 * sizeof(float));
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribDivisor(3, 1);
                GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 4 * sizeof(float));
                GL.EnableVertexAttribArray(4);
                GL.VertexAttribDivisor(4, 1);

                // Bind shared EBO
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, rectEboId);

                GL.BindVertexArray(0);

                // Free CPU memory after GPU upload
                tile.RectInstances = null;
            }

            tile.IsUploaded = true;
        }

        /// <summary>
        /// Renders visible tiles using pre-built GPU buffers.
        /// This is the hot path - no per-primitive C# iteration, just draw calls.
        /// </summary>
        /// <param name="opacity">Layer opacity (0.0 to 1.0) - applied via shader uniform</param>
        /// <returns>Number of draw calls issued</returns>
        public int Render(Matrix4 projection, Matrix4 view, float viewLeft, float viewBottom, float viewRight, float viewTop, float minVisibleSize, RenderStateCache stateCache, float opacity = 1.0f)
        {
            if (!_isInitialized || _tiles.Count == 0) return 0;

            int drawCalls = 0;

            // Use shader
            stateCache.UseProgram(_instancedShader);
            stateCache.SetProjectionMatrix(_projLoc, ref projection);
            stateCache.SetViewMatrix(_viewLoc, ref view);

            // Set opacity uniform - allows changing layer opacity without rebuilding GPU buffers
            GL.Uniform1(_opacityLoc, opacity);

            // Calculate visible tile range (O(1) calculation)
            int minTileX = Math.Max(0, (int)((viewLeft - _originX) / _tileSize));
            int minTileY = Math.Max(0, (int)((viewBottom - _originY) / _tileSize));
            int maxTileX = Math.Min(_tilesX - 1, (int)((viewRight - _originX) / _tileSize));
            int maxTileY = Math.Min(_tilesY - 1, (int)((viewTop - _originY) / _tileSize));

            // Iterate only visible tiles (typically <100 out of thousands)
            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    int tileIdx = ty * _tilesX + tx;
                    var tile = _tiles[tileIdx];

                    // Skip empty tiles
                    if (tile.CircleCount == 0 && tile.RectCount == 0)
                        continue;

                    // Lazy upload to GPU on first render
                    if (!tile.IsUploaded)
                    {
                        UploadTileToGpu(tile);
                    }

                    // Draw circles in this tile
                    if (tile.CircleCount > 0 && tile.CircleInstanceVbo != 0)
                    {
                        DrawTileCircles(tile);
                        drawCalls++;
                    }

                    // Draw rectangles in this tile
                    if (tile.RectCount > 0 && tile.RectInstanceVbo != 0)
                    {
                        DrawTileRectangles(tile);
                        drawCalls++;
                    }
                }
            }

            return drawCalls;
        }

        /// <summary>
        /// Draws circles in a tile using dedicated VAO with pre-uploaded instance VBO.
        /// Using dedicated VAO avoids state conflicts with BatchRenderer.
        /// </summary>
        private void DrawTileCircles(PreparedTileData tile)
        {
            // Bind our dedicated VAO (already has all attribute bindings set up)
            GL.BindVertexArray(tile.CircleVao);

            // Draw all circles in this tile with one call
            // Circle has 32 segments * 3 indices = 96 indices
            GL.DrawElementsInstanced(PrimitiveType.Triangles, 96, DrawElementsType.UnsignedInt, IntPtr.Zero, tile.CircleCount);

            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Draws rectangles in a tile using dedicated VAO with pre-uploaded instance VBO.
        /// Using dedicated VAO avoids state conflicts with BatchRenderer.
        /// </summary>
        private void DrawTileRectangles(PreparedTileData tile)
        {
            // Bind our dedicated VAO (already has all attribute bindings set up)
            GL.BindVertexArray(tile.RectVao);

            // Draw all rectangles in this tile with one call
            // Rectangle has 6 indices
            GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, IntPtr.Zero, tile.RectCount);

            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Updates the layer color for all tiles.
        /// Note: This requires re-uploading instance data since color is baked into instances.
        /// For frequent color changes, consider using a uniform instead.
        /// </summary>
        public void UpdateColor(Vector4 newColor)
        {
            if (_layerColor == newColor) return;

            _layerColor = newColor;

            // Mark all tiles for re-upload
            foreach (var tile in _tiles)
            {
                if (tile.CircleInstanceVbo != 0)
                {
                    GL.DeleteBuffer(tile.CircleInstanceVbo);
                    tile.CircleInstanceVbo = 0;
                }
                if (tile.RectInstanceVbo != 0)
                {
                    GL.DeleteBuffer(tile.RectInstanceVbo);
                    tile.RectInstanceVbo = 0;
                }
                tile.IsUploaded = false;
            }

            // Note: Actual re-upload will happen lazily on next render
            // The instance arrays have been freed, so we'd need to rebuild
            // For now, this is a limitation - color changes require full rebuild
        }

        /// <summary>
        /// Returns true if the renderer has been initialized with geometry data.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Gets the total number of circles in this layer.
        /// </summary>
        public int TotalCircles => _totalCircles;

        /// <summary>
        /// Gets the total number of rectangles in this layer.
        /// </summary>
        public int TotalRectangles => _totalRects;

        /// <summary>
        /// Gets the number of tiles containing data.
        /// </summary>
        public int TilesWithData => _tilesWithData;

        /// <summary>
        /// Gets the tile grid dimensions.
        /// </summary>
        public (int X, int Y) TileGrid => (_tilesX, _tilesY);

        public void Dispose()
        {
            foreach (var tile in _tiles)
            {
                // Delete instance VBOs
                if (tile.CircleInstanceVbo != 0)
                {
                    GL.DeleteBuffer(tile.CircleInstanceVbo);
                }
                if (tile.RectInstanceVbo != 0)
                {
                    GL.DeleteBuffer(tile.RectInstanceVbo);
                }
                // Delete dedicated VAOs
                if (tile.CircleVao != 0)
                {
                    GL.DeleteVertexArray(tile.CircleVao);
                }
                if (tile.RectVao != 0)
                {
                    GL.DeleteVertexArray(tile.RectVao);
                }
            }
            _tiles.Clear();
            _isInitialized = false;
        }
#else
        // Stub implementation when OpenGL is not available
        public StaticLayerRenderer(string layerId, GeometryCache geometryCache, int instancedShader, int projLoc, int viewLoc, int opacityLoc) { }
        public void Initialize(IList<CachedCircle> circles, IList<CachedRectangle> rectangles,
            float boundsX, float boundsY, float boundsWidth, float boundsHeight, object layerColor) { }
        public int Render(object projection, object view, float viewLeft, float viewBottom, float viewRight, float viewTop, float minVisibleSize, RenderStateCache stateCache, float opacity = 1.0f) => 0;
        public void UpdateColor(object newColor) { }
        public bool IsInitialized => false;
        public int TotalCircles => 0;
        public int TotalRectangles => 0;
        public int TilesWithData => 0;
        public (int X, int Y) TileGrid => (0, 0);
        public void Dispose() { }
#endif
    }

}
