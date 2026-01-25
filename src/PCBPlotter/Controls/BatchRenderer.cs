using System;
using System.Collections.Generic;
#if USE_OPENGL
using OpenTK;
using OpenTK.Graphics.OpenGL;
#endif

namespace PCBPlotter.Controls
{
    /// <summary>
    /// High-performance batch renderer that minimizes draw calls by grouping
    /// primitives by type and rendering them in batches.
    /// </summary>
    public class BatchRenderer : IDisposable
    {
#if USE_OPENGL
        // Shaders
        private int _instancedShader;
        private int _solidShader;

        // Frustum culling
        private FrustumCuller _frustumCuller;
        private bool _useFrustumCulling = true;

        // Render state caching
        private RenderStateCache _stateCache;

        // Uniform locations
        private int _instancedProjLoc;
        private int _instancedViewLoc;
        private int _solidProjLoc;
        private int _solidViewLoc;
        private int _solidColorLoc;

        // Geometry cache for instanced primitives
        private GeometryCache _geometryCache;

        // Dynamic buffer for arbitrary geometry (polygons, lines, etc.)
        private GeometryBuffer _dynamicBuffer;

        // Batched primitives for the current frame
        private List<CircleBatch> _circleBatches = new List<CircleBatch>();
        private List<RectBatch> _rectBatches = new List<RectBatch>();
        private List<LineBatch> _lineBatches = new List<LineBatch>();
        private List<PolygonBatch> _polygonBatches = new List<PolygonBatch>();

        // Static GPU buffers for pre-triangulated line/polygon meshes per layer
        private Dictionary<string, StaticLayerBuffer> _staticLayerBuffers = new Dictionary<string, StaticLayerBuffer>();

        // Pending static mesh renders for the current frame
        private List<StaticMeshRender> _pendingStaticLineRenders = new List<StaticMeshRender>();
        private List<StaticMeshRender> _pendingStaticPolygonRenders = new List<StaticMeshRender>();

        // Statistics
        private int _drawCalls;
        private int _trianglesRendered;

        private bool _isInitialized;

        private const int CIRCLE_SEGMENTS = 32;

        public void Initialize()
        {
            if (_isInitialized) return;

            // Create instanced shader (for circles, rectangles)
            CreateInstancedShader();

            // Create solid shader (for polygons, lines)
            CreateSolidShader();

            // Initialize geometry cache
            _geometryCache = new GeometryCache(CIRCLE_SEGMENTS);
            _geometryCache.Initialize();

            // Initialize dynamic buffer with larger initial capacities to reduce reallocations
            // PCB files often have many lines and polygons, so start with generous capacities
            _dynamicBuffer = new GeometryBuffer(65536, 131072, 0, BufferUsageHint.StreamDraw);
            _dynamicBuffer.Initialize();

            // Initialize frustum culler
            _frustumCuller = new FrustumCuller();

            // Initialize render state cache
            _stateCache = new RenderStateCache();

            _isInitialized = true;
        }

        /// <summary>
        /// Sets the view bounds for frustum culling.
        /// Call this at the beginning of each frame with the current view parameters.
        /// </summary>
        public void SetViewBounds(System.Windows.Rect viewBounds, double zoom)
        {
            _frustumCuller?.SetViewBounds(viewBounds, zoom);
        }

        /// <summary>
        /// Enables or disables frustum culling.
        /// </summary>
        public bool UseFrustumCulling
        {
            get => _useFrustumCulling;
            set => _useFrustumCulling = value;
        }

        private void CreateInstancedShader()
        {
            string vertexSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec2 aInstancePos;
layout (location = 3) in vec2 aInstanceScale;
layout (location = 4) in vec4 aInstanceColor;

uniform mat4 projection;
uniform mat4 view;

out vec4 vertexColor;

void main()
{
    vec2 worldPos = aPos * aInstanceScale + aInstancePos;
    gl_Position = projection * view * vec4(worldPos, 0.0, 1.0);
    vertexColor = aInstanceColor;
}
";

            string fragmentSource = @"#version 330 core
in vec4 vertexColor;
out vec4 FragColor;

void main()
{
    FragColor = vertexColor;
}
";

            _instancedShader = CreateShaderProgram(vertexSource, fragmentSource);
            _instancedProjLoc = GL.GetUniformLocation(_instancedShader, "projection");
            _instancedViewLoc = GL.GetUniformLocation(_instancedShader, "view");
        }

        private void CreateSolidShader()
        {
            string vertexSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;

uniform mat4 projection;
uniform mat4 view;

void main()
{
    gl_Position = projection * view * vec4(aPos, 0.0, 1.0);
}
";

            string fragmentSource = @"#version 330 core
uniform vec4 color;
out vec4 FragColor;

void main()
{
    FragColor = color;
}
";

            _solidShader = CreateShaderProgram(vertexSource, fragmentSource);
            _solidProjLoc = GL.GetUniformLocation(_solidShader, "projection");
            _solidViewLoc = GL.GetUniformLocation(_solidShader, "view");
            _solidColorLoc = GL.GetUniformLocation(_solidShader, "color");
        }

        private int CreateShaderProgram(string vertexSource, string fragmentSource)
        {
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);

            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vsSuccess);
            if (vsSuccess == 0)
            {
                string log = GL.GetShaderInfoLog(vertexShader);
                throw new Exception($"Vertex shader compilation failed: {log}");
            }

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fsSuccess);
            if (fsSuccess == 0)
            {
                string log = GL.GetShaderInfoLog(fragmentShader);
                throw new Exception($"Fragment shader compilation failed: {log}");
            }

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkSuccess);
            if (linkSuccess == 0)
            {
                string log = GL.GetProgramInfoLog(program);
                throw new Exception($"Shader program linking failed: {log}");
            }

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return program;
        }

        /// <summary>
        /// Begins a new frame. Call this before adding primitives.
        /// </summary>
        public void BeginFrame()
        {
            _drawCalls = 0;
            _trianglesRendered = 0;

            _circleBatches.Clear();
            _rectBatches.Clear();
            _lineBatches.Clear();
            _polygonBatches.Clear();

            // Clear pending static mesh renders
            _pendingStaticLineRenders.Clear();
            _pendingStaticPolygonRenders.Clear();

            _geometryCache.ClearInstances();
            _dynamicBuffer.Clear();

            _stateCache?.BeginFrame();
            _frustumCuller?.ResetStats();
        }

        /// <summary>
        /// Adds a circle to the batch.
        /// </summary>
        public void AddCircle(float x, float y, float radius, Vector4 color)
        {
            // Frustum culling check
            if (_useFrustumCulling && _frustumCuller != null)
            {
                if (!_frustumCuller.IsVisible(x, y, radius))
                    return;
            }

            _geometryCache.AddCircle(x, y, radius, color.X, color.Y, color.Z, color.W);
        }

        /// <summary>
        /// Adds a rectangle to the batch.
        /// </summary>
        public void AddRectangle(float x, float y, float width, float height, Vector4 color)
        {
            // Frustum culling check
            if (_useFrustumCulling && _frustumCuller != null)
            {
                if (!_frustumCuller.IsVisible(x, y, width, height))
                    return;
            }

            _geometryCache.AddRectangle(x, y, width, height, color.X, color.Y, color.Z, color.W);
        }

        /// <summary>
        /// Adds an obround (rounded rectangle) to the batch.
        /// </summary>
        public void AddObround(float x, float y, float width, float height, Vector4 color)
        {
            float hw = width / 2;
            float hh = height / 2;

            if (width > height)
            {
                float radius = hh;
                float rectHw = hw - radius;

                // Center rectangle
                _geometryCache.AddRectangle(x, y, rectHw * 2, height, color.X, color.Y, color.Z, color.W);

                // End circles
                _geometryCache.AddCircle(x - rectHw, y, radius, color.X, color.Y, color.Z, color.W);
                _geometryCache.AddCircle(x + rectHw, y, radius, color.X, color.Y, color.Z, color.W);
            }
            else
            {
                float radius = hw;
                float rectHh = hh - radius;

                // Center rectangle
                _geometryCache.AddRectangle(x, y, width, rectHh * 2, color.X, color.Y, color.Z, color.W);

                // End circles
                _geometryCache.AddCircle(x, y - rectHh, radius, color.X, color.Y, color.Z, color.W);
                _geometryCache.AddCircle(x, y + rectHh, radius, color.X, color.Y, color.Z, color.W);
            }
        }

        /// <summary>
        /// Adds a line with round caps to the batch.
        /// </summary>
        public void AddLine(float x1, float y1, float x2, float y2, float width, Vector4 color)
        {
            // Frustum culling check
            if (_useFrustumCulling && _frustumCuller != null)
            {
                if (!_frustumCuller.IsLineVisible(x1, y1, x2, y2, width))
                    return;
            }

            float dx = x2 - x1;
            float dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001f) return;

            float nx = -dy / len * width / 2;
            float ny = dx / len * width / 2;

            // Store line batch for later rendering
            _lineBatches.Add(new LineBatch
            {
                X1 = x1, Y1 = y1,
                X2 = x2, Y2 = y2,
                Nx = nx, Ny = ny,
                Width = width,
                Color = color
            });
        }

        /// <summary>
        /// Adds a polygon to the batch.
        /// </summary>
        public void AddPolygon(IList<System.Windows.Point> points, Vector4 color)
        {
            if (points.Count < 3) return;

            // Frustum culling check
            if (_useFrustumCulling && _frustumCuller != null)
            {
                if (!_frustumCuller.IsPolygonVisible(points))
                    return;
            }

            _polygonBatches.Add(new PolygonBatch
            {
                Points = points,
                Color = color
            });
        }

        /// <summary>
        /// Uploads static line mesh data to GPU (only called once per layer).
        /// </summary>
        public void UploadStaticLineMesh(string layerId, float[] vertices, int vertexCount, uint[] indices, int indexCount)
        {
            if (vertexCount == 0 || indexCount == 0) return;

            if (!_staticLayerBuffers.TryGetValue(layerId, out var layerBuffer))
            {
                layerBuffer = new StaticLayerBuffer();
                _staticLayerBuffers[layerId] = layerBuffer;
            }

            if (layerBuffer.LineBuffer == null)
            {
                layerBuffer.LineBuffer = new GeometryBuffer(vertexCount, indexCount, 0, BufferUsageHint.StaticDraw);
                layerBuffer.LineBuffer.Initialize();
            }

            // Add vertices (x,y pairs)
            for (int i = 0; i < vertexCount; i++)
            {
                layerBuffer.LineBuffer.AddVertex(vertices[i * 2], vertices[i * 2 + 1]);
            }

            // Add indices
            layerBuffer.LineBuffer.AddIndices(indices, indexCount);
            layerBuffer.LineBuffer.Upload();
            layerBuffer.LineBufferReady = true;
        }

        /// <summary>
        /// Uploads static polygon mesh data to GPU (only called once per layer).
        /// </summary>
        public void UploadStaticPolygonMesh(string layerId, float[] vertices, int vertexCount, uint[] indices, int indexCount)
        {
            if (vertexCount == 0 || indexCount == 0) return;

            if (!_staticLayerBuffers.TryGetValue(layerId, out var layerBuffer))
            {
                layerBuffer = new StaticLayerBuffer();
                _staticLayerBuffers[layerId] = layerBuffer;
            }

            if (layerBuffer.PolygonBuffer == null)
            {
                layerBuffer.PolygonBuffer = new GeometryBuffer(vertexCount, indexCount, 0, BufferUsageHint.StaticDraw);
                layerBuffer.PolygonBuffer.Initialize();
            }

            // Add vertices
            for (int i = 0; i < vertexCount; i++)
            {
                layerBuffer.PolygonBuffer.AddVertex(vertices[i * 2], vertices[i * 2 + 1]);
            }

            // Add indices
            layerBuffer.PolygonBuffer.AddIndices(indices, indexCount);
            layerBuffer.PolygonBuffer.Upload();
            layerBuffer.PolygonBufferReady = true;
        }

        /// <summary>
        /// Queues a static line mesh for rendering.
        /// </summary>
        public void RenderStaticLineMesh(string layerId, Vector4 color)
        {
            _pendingStaticLineRenders.Add(new StaticMeshRender
            {
                LayerId = layerId,
                IsLineMesh = true,
                Color = color
            });
        }

        /// <summary>
        /// Queues a static polygon mesh for rendering.
        /// </summary>
        public void RenderStaticPolygonMesh(string layerId, Vector4 color)
        {
            _pendingStaticPolygonRenders.Add(new StaticMeshRender
            {
                LayerId = layerId,
                IsLineMesh = false,
                Color = color
            });
        }

        /// <summary>
        /// Checks if static buffers exist for a layer.
        /// </summary>
        public bool HasStaticLineMesh(string layerId)
        {
            return _staticLayerBuffers.TryGetValue(layerId, out var buffer) && buffer.LineBufferReady;
        }

        /// <summary>
        /// Checks if static polygon buffers exist for a layer.
        /// </summary>
        public bool HasStaticPolygonMesh(string layerId)
        {
            return _staticLayerBuffers.TryGetValue(layerId, out var buffer) && buffer.PolygonBufferReady;
        }

        /// <summary>
        /// Invalidates static buffers for a layer (call when layer data changes).
        /// </summary>
        public void InvalidateStaticBuffers(string layerId)
        {
            if (_staticLayerBuffers.TryGetValue(layerId, out var buffer))
            {
                buffer.Dispose();
                _staticLayerBuffers.Remove(layerId);
            }
        }

        /// <summary>
        /// Uploads all batched geometry to GPU and renders.
        /// </summary>
        public void Render(Matrix4 projection, Matrix4 view)
        {
            // Upload instanced geometry
            _geometryCache.Upload();

            // Use state cache for efficient state management
            _stateCache.UseProgram(_instancedShader);
            _stateCache.SetProjectionMatrix(_instancedProjLoc, ref projection);
            _stateCache.SetViewMatrix(_instancedViewLoc, ref view);

            // Render instanced circles
            _geometryCache.DrawCircles();
            if (_geometryCache.CircleCount > 0)
            {
                _drawCalls++;
                _trianglesRendered += _geometryCache.CircleCount * CIRCLE_SEGMENTS;
            }

            // Render instanced rectangles (same shader, no state change needed)
            _geometryCache.DrawRectangles();
            if (_geometryCache.RectangleCount > 0)
            {
                _drawCalls++;
                _trianglesRendered += _geometryCache.RectangleCount * 2;
            }

            // Render dynamic lines (fallback)
            RenderLines(projection, view);

            // Render dynamic polygons (fallback)
            RenderPolygons(projection, view);

            // Render static line meshes (pre-triangulated, no per-frame rebuild)
            RenderStaticLineMeshes(projection, view);

            // Render static polygon meshes (pre-triangulated, no per-frame rebuild)
            RenderStaticPolygonMeshes(projection, view);
        }

        private void RenderStaticLineMeshes(Matrix4 projection, Matrix4 view)
        {
            if (_pendingStaticLineRenders.Count == 0) return;

            _stateCache.UseProgram(_solidShader);
            _stateCache.SetProjectionMatrix(_solidProjLoc, ref projection);
            _stateCache.SetViewMatrix(_solidViewLoc, ref view);

            foreach (var render in _pendingStaticLineRenders)
            {
                if (!_staticLayerBuffers.TryGetValue(render.LayerId, out var buffer))
                    continue;
                if (!buffer.LineBufferReady || buffer.LineBuffer == null)
                    continue;

                _stateCache.SetColor(_solidColorLoc, render.Color);
                buffer.LineBuffer.Draw();
                _drawCalls++;
                _trianglesRendered += buffer.LineBuffer.IndexCount / 3;
            }
        }

        private void RenderStaticPolygonMeshes(Matrix4 projection, Matrix4 view)
        {
            if (_pendingStaticPolygonRenders.Count == 0) return;

            _stateCache.UseProgram(_solidShader);
            _stateCache.SetProjectionMatrix(_solidProjLoc, ref projection);
            _stateCache.SetViewMatrix(_solidViewLoc, ref view);

            foreach (var render in _pendingStaticPolygonRenders)
            {
                if (!_staticLayerBuffers.TryGetValue(render.LayerId, out var buffer))
                    continue;
                if (!buffer.PolygonBufferReady || buffer.PolygonBuffer == null)
                    continue;

                _stateCache.SetColor(_solidColorLoc, render.Color);
                buffer.PolygonBuffer.Draw();
                _drawCalls++;
                _trianglesRendered += buffer.PolygonBuffer.IndexCount / 3;
            }
        }

        private void RenderLines(Matrix4 projection, Matrix4 view)
        {
            if (_lineBatches.Count == 0) return;

            // Use state cache for efficient state management
            _stateCache.UseProgram(_solidShader);
            _stateCache.SetProjectionMatrix(_solidProjLoc, ref projection);
            _stateCache.SetViewMatrix(_solidViewLoc, ref view);

            // Group lines by color for efficient batching
            var colorBatches = new Dictionary<uint, List<LineBatch>>();

            foreach (var line in _lineBatches)
            {
                uint colorKey = ColorToKey(line.Color);
                if (!colorBatches.ContainsKey(colorKey))
                    colorBatches[colorKey] = new List<LineBatch>();
                colorBatches[colorKey].Add(line);
            }

            foreach (var kvp in colorBatches)
            {
                _dynamicBuffer.Clear();

                // Build line geometry for this color batch
                foreach (var line in kvp.Value)
                {
                    uint baseVertex = (uint)_dynamicBuffer.VertexCount;

                    // Line quad
                    _dynamicBuffer.AddVertex(line.X1 - line.Nx, line.Y1 - line.Ny);
                    _dynamicBuffer.AddVertex(line.X1 + line.Nx, line.Y1 + line.Ny);
                    _dynamicBuffer.AddVertex(line.X2 + line.Nx, line.Y2 + line.Ny);
                    _dynamicBuffer.AddVertex(line.X2 - line.Nx, line.Y2 - line.Ny);

                    _dynamicBuffer.AddQuad(baseVertex, baseVertex + 1, baseVertex + 2, baseVertex + 3);

                    // Round caps - add circle geometry
                    AddCircleToDynamic(line.X1, line.Y1, line.Width / 2);
                    AddCircleToDynamic(line.X2, line.Y2, line.Width / 2);
                }

                _dynamicBuffer.Upload();

                var color = kvp.Value[0].Color;
                _stateCache.SetColor(_solidColorLoc, color);

                _dynamicBuffer.Draw();
                _drawCalls++;
                _trianglesRendered += _dynamicBuffer.IndexCount / 3;
            }
        }

        private void AddCircleToDynamic(float x, float y, float radius)
        {
            uint centerIdx = (uint)_dynamicBuffer.VertexCount;
            _dynamicBuffer.AddVertex(x, y);

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = (float)(2 * Math.PI * i / CIRCLE_SEGMENTS);
                _dynamicBuffer.AddVertex(
                    x + radius * (float)Math.Cos(angle),
                    y + radius * (float)Math.Sin(angle));
            }

            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                _dynamicBuffer.AddTriangle(
                    centerIdx,
                    centerIdx + (uint)(i + 1),
                    centerIdx + (uint)((i + 1) % CIRCLE_SEGMENTS + 1));
            }
        }

        private void RenderPolygons(Matrix4 projection, Matrix4 view)
        {
            if (_polygonBatches.Count == 0) return;

            // Use state cache - shader may already be bound from lines
            _stateCache.UseProgram(_solidShader);
            _stateCache.SetProjectionMatrix(_solidProjLoc, ref projection);
            _stateCache.SetViewMatrix(_solidViewLoc, ref view);

            // Group polygons by color for batching
            var colorBatches = new Dictionary<uint, List<PolygonBatch>>();

            foreach (var poly in _polygonBatches)
            {
                uint colorKey = ColorToKey(poly.Color);
                if (!colorBatches.ContainsKey(colorKey))
                    colorBatches[colorKey] = new List<PolygonBatch>();
                colorBatches[colorKey].Add(poly);
            }

            foreach (var kvp in colorBatches)
            {
                _dynamicBuffer.Clear();

                foreach (var poly in kvp.Value)
                {
                    // Simple triangle fan from first vertex (works for convex polygons)
                    uint baseVertex = (uint)_dynamicBuffer.VertexCount;

                    foreach (var pt in poly.Points)
                    {
                        _dynamicBuffer.AddVertex((float)pt.X, (float)pt.Y);
                    }

                    // Triangle fan indices
                    for (int i = 1; i < poly.Points.Count - 1; i++)
                    {
                        _dynamicBuffer.AddTriangle(baseVertex, baseVertex + (uint)i, baseVertex + (uint)(i + 1));
                    }
                }

                _dynamicBuffer.Upload();

                var color = kvp.Value[0].Color;
                _stateCache.SetColor(_solidColorLoc, color);

                _dynamicBuffer.Draw();
                _drawCalls++;
                _trianglesRendered += _dynamicBuffer.IndexCount / 3;
            }
        }

        private uint ColorToKey(Vector4 color)
        {
            uint r = (uint)(color.X * 255) & 0xFF;
            uint g = (uint)(color.Y * 255) & 0xFF;
            uint b = (uint)(color.Z * 255) & 0xFF;
            uint a = (uint)(color.W * 255) & 0xFF;
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        /// <summary>
        /// Ends the frame and returns statistics.
        /// </summary>
        public RenderStats EndFrame()
        {
            var stats = new RenderStats
            {
                DrawCalls = _drawCalls,
                TrianglesRendered = _trianglesRendered,
                CircleCount = _geometryCache.CircleCount,
                RectangleCount = _geometryCache.RectangleCount,
                // Include both dynamic batches and static mesh renders
                LineCount = _lineBatches.Count + _pendingStaticLineRenders.Count,
                PolygonCount = _polygonBatches.Count + _pendingStaticPolygonRenders.Count
            };

            // Include culling statistics
            if (_frustumCuller != null)
            {
                var cullingStats = _frustumCuller.GetStats();
                stats.CulledCount = cullingStats.CulledCount;
                stats.LodFiltered = cullingStats.LodFiltered;
            }

            // Include state cache statistics
            if (_stateCache != null)
            {
                var stateStats = _stateCache.GetStats();
                stats.SkippedStateChanges = stateStats.SkippedStateChanges;
            }

            return stats;
        }

        /// <summary>
        /// Gets the culling statistics for the current frame.
        /// </summary>
        public CullingStats GetCullingStats()
        {
            return _frustumCuller?.GetStats() ?? new CullingStats();
        }

        /// <summary>
        /// Gets the render state statistics for the current frame.
        /// </summary>
        public RenderStateStats GetStateStats()
        {
            return _stateCache?.GetStats() ?? new RenderStateStats();
        }

        // Public accessors for StaticLayerRenderer integration
        /// <summary>
        /// Gets the instanced shader program handle.
        /// </summary>
        public int InstancedShader => _instancedShader;

        /// <summary>
        /// Gets the solid color shader program handle.
        /// </summary>
        public int SolidShader => _solidShader;

        /// <summary>
        /// Gets the projection uniform location for the instanced shader.
        /// </summary>
        public int InstancedProjectionLocation => _instancedProjLoc;

        /// <summary>
        /// Gets the view uniform location for the instanced shader.
        /// </summary>
        public int InstancedViewLocation => _instancedViewLoc;

        /// <summary>
        /// Gets the geometry cache containing base primitives (unit circle, unit rectangle).
        /// </summary>
        public GeometryCache GeometryCache => _geometryCache;

        /// <summary>
        /// Gets the render state cache for efficient state management.
        /// </summary>
        public RenderStateCache StateCache => _stateCache;

        public void Dispose()
        {
            if (_instancedShader != 0) GL.DeleteProgram(_instancedShader);
            if (_solidShader != 0) GL.DeleteProgram(_solidShader);

            _geometryCache?.Dispose();
            _dynamicBuffer?.Dispose();

            // Dispose static layer buffers
            foreach (var buffer in _staticLayerBuffers.Values)
            {
                buffer.Dispose();
            }
            _staticLayerBuffers.Clear();
        }

        private struct CircleBatch
        {
            public float X, Y, Radius;
            public Vector4 Color;
        }

        private struct RectBatch
        {
            public float X, Y, Width, Height;
            public Vector4 Color;
        }

        private struct LineBatch
        {
            public float X1, Y1, X2, Y2;
            public float Nx, Ny;
            public float Width;
            public Vector4 Color;
        }

        private struct PolygonBatch
        {
            public IList<System.Windows.Point> Points;
            public Vector4 Color;
        }

        private struct StaticMeshRender
        {
            public string LayerId;
            public bool IsLineMesh; // true for line mesh, false for polygon mesh
            public Vector4 Color;
        }

        /// <summary>
        /// Holds static GPU buffers for a layer's pre-triangulated meshes.
        /// </summary>
        private class StaticLayerBuffer : IDisposable
        {
            public GeometryBuffer LineBuffer;
            public GeometryBuffer PolygonBuffer;
            public bool LineBufferReady;
            public bool PolygonBufferReady;

            public void Dispose()
            {
                LineBuffer?.Dispose();
                PolygonBuffer?.Dispose();
            }
        }
#else
        // Stub implementation
        public void Initialize() { }
        public void SetViewBounds(System.Windows.Rect viewBounds, double zoom) { }
        public bool UseFrustumCulling { get; set; }
        public void BeginFrame() { }
        public void AddCircle(float x, float y, float radius, object color) { }
        public void AddRectangle(float x, float y, float width, float height, object color) { }
        public void AddObround(float x, float y, float width, float height, object color) { }
        public void AddLine(float x1, float y1, float x2, float y2, float width, object color) { }
        public void AddPolygon(IList<System.Windows.Point> points, object color) { }
        public void Render(object projection, object view) { }
        public RenderStats EndFrame() => new RenderStats();
        public CullingStats GetCullingStats() => new CullingStats();
        public RenderStateStats GetStateStats() => new RenderStateStats();
        public int InstancedShader => 0;
        public int SolidShader => 0;
        public int InstancedProjectionLocation => 0;
        public int InstancedViewLocation => 0;
        public GeometryCache GeometryCache => null;
        public RenderStateCache StateCache => null;
        public void Dispose() { }
#endif
    }

    /// <summary>
    /// Rendering statistics for performance monitoring.
    /// </summary>
    public struct RenderStats
    {
        public int DrawCalls;
        public int TrianglesRendered;
        public int CircleCount;
        public int RectangleCount;
        public int LineCount;
        public int PolygonCount;
        public int CulledCount;
        public int LodFiltered;
        public int SkippedStateChanges;

        public int TotalPrimitives => CircleCount + RectangleCount + LineCount + PolygonCount;
        public int TotalCulled => CulledCount + LodFiltered;

        public override string ToString()
        {
            return $"Draw calls: {DrawCalls}, Triangles: {TrianglesRendered}, " +
                   $"Primitives: {TotalPrimitives} (Culled: {TotalCulled}), " +
                   $"State skips: {SkippedStateChanges}";
        }
    }
}
