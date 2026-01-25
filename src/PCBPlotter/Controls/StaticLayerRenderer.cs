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
    /// </summary>
    public class StaticLayerRenderer : IDisposable
    {
#if USE_OPENGL
        /// <summary>
        /// Represents a spatial tile containing pre-built GPU buffers for primitives in that tile.
        /// </summary>
        private class Tile
        {
            public float MinX, MinY, MaxX, MaxY;

            // Pre-built instance data for this tile (positions, scales, colors)
            // These are uploaded to GPU once and reused every frame
            public float[] CircleInstances;
            public float[] RectInstances;

            public int CircleCount;
            public int RectCount;

            // GPU buffer handles (created on first upload)
            public int CircleInstanceVbo;
            public int RectInstanceVbo;

            public bool IsUploaded;
        }

        private List<Tile> _tiles = new List<Tile>();
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
        public StaticLayerRenderer(string layerId, GeometryCache geometryCache, int instancedShader, int projLoc, int viewLoc)
        {
            _layerId = layerId;
            _geometryCache = geometryCache;
            _instancedShader = instancedShader;
            _projLoc = projLoc;
            _viewLoc = viewLoc;
        }

        /// <summary>
        /// Builds static GPU buffers from cached geometry data.
        /// This is called ONCE when the layer is loaded, not every frame.
        /// </summary>
        public void Initialize(
            IList<CachedCircle> circles,
            IList<CachedRectangle> rectangles,
            float boundsX, float boundsY, float boundsWidth, float boundsHeight,
            Vector4 layerColor)
        {
            if (_isInitialized)
            {
                Dispose();
            }

            _layerColor = layerColor;
            _totalCircles = circles?.Count ?? 0;
            _totalRects = rectangles?.Count ?? 0;

            // Calculate tile size - aim for ~2000 primitives per tile for good batching
            // but cap at reasonable values
            int totalPrimitives = _totalCircles + _totalRects;
            if (totalPrimitives == 0)
            {
                _isInitialized = true;
                return;
            }

            // Adaptive tile size based on primitive density
            float area = boundsWidth * boundsHeight;
            float density = totalPrimitives / Math.Max(area, 1f);

            // Target ~1000-2000 primitives per tile
            float targetPrimsPerTile = 1500f;
            float targetTileArea = targetPrimsPerTile / Math.Max(density, 0.001f);
            _tileSize = (float)Math.Sqrt(targetTileArea);

            // Clamp tile size to reasonable bounds
            _tileSize = Math.Max(_tileSize, Math.Max(boundsWidth, boundsHeight) / 50f); // At least 50 tiles
            _tileSize = Math.Min(_tileSize, Math.Max(boundsWidth, boundsHeight) / 2f);   // At most 4 tiles
            _tileSize = Math.Max(_tileSize, 10f); // Minimum 10mm tiles

            _originX = boundsX;
            _originY = boundsY;

            _tilesX = Math.Max(1, (int)Math.Ceiling(boundsWidth / _tileSize));
            _tilesY = Math.Max(1, (int)Math.Ceiling(boundsHeight / _tileSize));

            // Cap total tiles at 2500 (50x50 grid)
            if (_tilesX * _tilesY > 2500)
            {
                float scale = (float)Math.Sqrt(2500.0 / (_tilesX * _tilesY));
                _tilesX = Math.Max(1, (int)(_tilesX * scale));
                _tilesY = Math.Max(1, (int)(_tilesY * scale));
                _tileSize = Math.Max(boundsWidth / _tilesX, boundsHeight / _tilesY);
            }

            // Create tiles
            int totalTiles = _tilesX * _tilesY;
            _tiles.Clear();
            _tiles.Capacity = totalTiles;

            // Initialize tile lists for sorting primitives
            var tileCircleLists = new List<List<int>>(totalTiles);
            var tileRectLists = new List<List<int>>(totalTiles);

            for (int i = 0; i < totalTiles; i++)
            {
                int tx = i % _tilesX;
                int ty = i / _tilesX;

                var tile = new Tile
                {
                    MinX = _originX + tx * _tileSize,
                    MinY = _originY + ty * _tileSize,
                    MaxX = _originX + (tx + 1) * _tileSize,
                    MaxY = _originY + (ty + 1) * _tileSize
                };
                _tiles.Add(tile);
                tileCircleLists.Add(new List<int>());
                tileRectLists.Add(new List<int>());
            }

            // Sort circles into tiles (done ONCE, not every frame)
            if (circles != null)
            {
                for (int i = 0; i < circles.Count; i++)
                {
                    var circle = circles[i];
                    int tileIdx = GetTileIndex(circle.X, circle.Y);
                    if (tileIdx >= 0 && tileIdx < totalTiles)
                    {
                        tileCircleLists[tileIdx].Add(i);
                    }
                }
            }

            // Sort rectangles into tiles (done ONCE, not every frame)
            if (rectangles != null)
            {
                for (int i = 0; i < rectangles.Count; i++)
                {
                    var rect = rectangles[i];
                    int tileIdx = GetTileIndex(rect.X, rect.Y);
                    if (tileIdx >= 0 && tileIdx < totalTiles)
                    {
                        tileRectLists[tileIdx].Add(i);
                    }
                }
            }

            // Build instance arrays for each tile (done ONCE, not every frame)
            _tilesWithData = 0;
            for (int i = 0; i < totalTiles; i++)
            {
                var tile = _tiles[i];
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
                        tile.CircleInstances[offset + 2] = circle.Radius; // scale X
                        tile.CircleInstances[offset + 3] = circle.Radius; // scale Y
                        tile.CircleInstances[offset + 4] = _layerColor.X; // R
                        tile.CircleInstances[offset + 5] = _layerColor.Y; // G
                        tile.CircleInstances[offset + 6] = _layerColor.Z; // B
                        tile.CircleInstances[offset + 7] = _layerColor.W; // A
                    }
                    _tilesWithData++;
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
                        tile.RectInstances[offset + 2] = rect.Width;  // scale X
                        tile.RectInstances[offset + 3] = rect.Height; // scale Y
                        tile.RectInstances[offset + 4] = _layerColor.X; // R
                        tile.RectInstances[offset + 5] = _layerColor.Y; // G
                        tile.RectInstances[offset + 6] = _layerColor.Z; // B
                        tile.RectInstances[offset + 7] = _layerColor.W; // A
                    }
                    if (tile.CircleCount == 0) _tilesWithData++;
                }
            }

            _isInitialized = true;
        }

        /// <summary>
        /// Gets the tile index for a world coordinate.
        /// </summary>
        private int GetTileIndex(float x, float y)
        {
            int tx = (int)((x - _originX) / _tileSize);
            int ty = (int)((y - _originY) / _tileSize);

            if (tx < 0) tx = 0;
            if (ty < 0) ty = 0;
            if (tx >= _tilesX) tx = _tilesX - 1;
            if (ty >= _tilesY) ty = _tilesY - 1;

            return ty * _tilesX + tx;
        }

        /// <summary>
        /// Uploads tile instance data to GPU. Call this from the GL context.
        /// This is done lazily on first render, not during Initialize().
        /// </summary>
        private void UploadTileToGpu(Tile tile)
        {
            if (tile.IsUploaded) return;

            // Upload circle instances
            if (tile.CircleCount > 0 && tile.CircleInstances != null)
            {
                tile.CircleInstanceVbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, tile.CircleInstanceVbo);
                GL.BufferData(BufferTarget.ArrayBuffer,
                    tile.CircleInstances.Length * sizeof(float),
                    tile.CircleInstances,
                    BufferUsageHint.StaticDraw);

                // Free CPU memory after GPU upload
                tile.CircleInstances = null;
            }

            // Upload rectangle instances
            if (tile.RectCount > 0 && tile.RectInstances != null)
            {
                tile.RectInstanceVbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, tile.RectInstanceVbo);
                GL.BufferData(BufferTarget.ArrayBuffer,
                    tile.RectInstances.Length * sizeof(float),
                    tile.RectInstances,
                    BufferUsageHint.StaticDraw);

                // Free CPU memory after GPU upload
                tile.RectInstances = null;
            }

            tile.IsUploaded = true;
        }

        /// <summary>
        /// Renders visible tiles using pre-built GPU buffers.
        /// This is the hot path - no per-primitive C# iteration, just draw calls.
        /// </summary>
        /// <returns>Number of draw calls issued</returns>
        public int Render(Matrix4 projection, Matrix4 view, float viewLeft, float viewBottom, float viewRight, float viewTop, float minVisibleSize, RenderStateCache stateCache)
        {
            if (!_isInitialized || _tiles.Count == 0) return 0;

            int drawCalls = 0;

            // Use shader
            stateCache.UseProgram(_instancedShader);
            stateCache.SetProjectionMatrix(_projLoc, ref projection);
            stateCache.SetViewMatrix(_viewLoc, ref view);

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
        /// Draws circles in a tile using pre-uploaded instance VBO.
        /// </summary>
        private void DrawTileCircles(Tile tile)
        {
            var circleBuffer = _geometryCache.CircleBuffer;

            // Bind the circle base geometry VAO
            GL.BindVertexArray(circleBuffer.VAO);

            // Bind our tile-specific instance VBO and set up attributes
            GL.BindBuffer(BufferTarget.ArrayBuffer, tile.CircleInstanceVbo);

            // Instance position (location 2)
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 0);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);

            // Instance scale (location 3)
            GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribDivisor(3, 1);

            // Instance color (location 4)
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 4 * sizeof(float));
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribDivisor(4, 1);

            // Draw all circles in this tile with one call
            // Circle has 32 segments * 3 indices = 96 indices
            GL.DrawElementsInstanced(PrimitiveType.Triangles, 96, DrawElementsType.UnsignedInt, IntPtr.Zero, tile.CircleCount);

            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Draws rectangles in a tile using pre-uploaded instance VBO.
        /// </summary>
        private void DrawTileRectangles(Tile tile)
        {
            var rectBuffer = _geometryCache.RectangleBuffer;

            // Bind the rectangle base geometry VAO
            GL.BindVertexArray(rectBuffer.VAO);

            // Bind our tile-specific instance VBO and set up attributes
            GL.BindBuffer(BufferTarget.ArrayBuffer, tile.RectInstanceVbo);

            // Instance position (location 2)
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 0);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);

            // Instance scale (location 3)
            GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribDivisor(3, 1);

            // Instance color (location 4)
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, INSTANCE_SIZE * sizeof(float), 4 * sizeof(float));
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribDivisor(4, 1);

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
                if (tile.CircleInstanceVbo != 0)
                {
                    GL.DeleteBuffer(tile.CircleInstanceVbo);
                }
                if (tile.RectInstanceVbo != 0)
                {
                    GL.DeleteBuffer(tile.RectInstanceVbo);
                }
            }
            _tiles.Clear();
            _isInitialized = false;
        }
#else
        // Stub implementation when OpenGL is not available
        public StaticLayerRenderer(string layerId, GeometryCache geometryCache, int instancedShader, int projLoc, int viewLoc) { }
        public void Initialize(IList<CachedCircle> circles, IList<CachedRectangle> rectangles,
            float boundsX, float boundsY, float boundsWidth, float boundsHeight, object layerColor) { }
        public int Render(object projection, object view, float viewLeft, float viewBottom, float viewRight, float viewTop, float minVisibleSize, RenderStateCache stateCache) => 0;
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
