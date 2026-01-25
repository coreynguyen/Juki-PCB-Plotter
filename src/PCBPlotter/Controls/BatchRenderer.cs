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

            // Initialize dynamic buffer
            _dynamicBuffer = new GeometryBuffer(16384, 32768, 0, BufferUsageHint.StreamDraw);
            _dynamicBuffer.Initialize();

            _isInitialized = true;
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

            _geometryCache.ClearInstances();
            _dynamicBuffer.Clear();
        }

        /// <summary>
        /// Adds a circle to the batch.
        /// </summary>
        public void AddCircle(float x, float y, float radius, Vector4 color)
        {
            _geometryCache.AddCircle(x, y, radius, color.X, color.Y, color.Z, color.W);
        }

        /// <summary>
        /// Adds a rectangle to the batch.
        /// </summary>
        public void AddRectangle(float x, float y, float width, float height, Vector4 color)
        {
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

            _polygonBatches.Add(new PolygonBatch
            {
                Points = points,
                Color = color
            });
        }

        /// <summary>
        /// Uploads all batched geometry to GPU and renders.
        /// </summary>
        public void Render(Matrix4 projection, Matrix4 view)
        {
            // Upload instanced geometry
            _geometryCache.Upload();

            // Render instanced circles
            GL.UseProgram(_instancedShader);
            GL.UniformMatrix4(_instancedProjLoc, false, ref projection);
            GL.UniformMatrix4(_instancedViewLoc, false, ref view);

            _geometryCache.DrawCircles();
            if (_geometryCache.CircleCount > 0)
            {
                _drawCalls++;
                _trianglesRendered += _geometryCache.CircleCount * CIRCLE_SEGMENTS;
            }

            // Render instanced rectangles
            _geometryCache.DrawRectangles();
            if (_geometryCache.RectangleCount > 0)
            {
                _drawCalls++;
                _trianglesRendered += _geometryCache.RectangleCount * 2;
            }

            // Render lines (each line is a batch for now)
            RenderLines(projection, view);

            // Render polygons
            RenderPolygons(projection, view);
        }

        private void RenderLines(Matrix4 projection, Matrix4 view)
        {
            if (_lineBatches.Count == 0) return;

            _dynamicBuffer.Clear();

            // Build line geometry
            foreach (var line in _lineBatches)
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

            // Render with solid shader
            GL.UseProgram(_solidShader);
            GL.UniformMatrix4(_solidProjLoc, false, ref projection);
            GL.UniformMatrix4(_solidViewLoc, false, ref view);

            // For now, render all lines with the color of the first line
            // A more sophisticated approach would batch by color
            if (_lineBatches.Count > 0)
            {
                var color = _lineBatches[0].Color;
                GL.Uniform4(_solidColorLoc, color.X, color.Y, color.Z, color.W);
            }

            _dynamicBuffer.Draw();
            _drawCalls++;
            _trianglesRendered += _dynamicBuffer.IndexCount / 3;
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

            GL.UseProgram(_solidShader);
            GL.UniformMatrix4(_solidProjLoc, false, ref projection);
            GL.UniformMatrix4(_solidViewLoc, false, ref view);

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
                GL.Uniform4(_solidColorLoc, color.X, color.Y, color.Z, color.W);

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
            return new RenderStats
            {
                DrawCalls = _drawCalls,
                TrianglesRendered = _trianglesRendered,
                CircleCount = _geometryCache.CircleCount,
                RectangleCount = _geometryCache.RectangleCount,
                LineCount = _lineBatches.Count,
                PolygonCount = _polygonBatches.Count
            };
        }

        public void Dispose()
        {
            if (_instancedShader != 0) GL.DeleteProgram(_instancedShader);
            if (_solidShader != 0) GL.DeleteProgram(_solidShader);

            _geometryCache?.Dispose();
            _dynamicBuffer?.Dispose();
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
#else
        // Stub implementation
        public void Initialize() { }
        public void BeginFrame() { }
        public void AddCircle(float x, float y, float radius, object color) { }
        public void AddRectangle(float x, float y, float width, float height, object color) { }
        public void AddObround(float x, float y, float width, float height, object color) { }
        public void AddLine(float x1, float y1, float x2, float y2, float width, object color) { }
        public void AddPolygon(IList<System.Windows.Point> points, object color) { }
        public void Render(object projection, object view) { }
        public RenderStats EndFrame() => new RenderStats();
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

        public override string ToString()
        {
            return $"Draw calls: {DrawCalls}, Triangles: {TrianglesRendered}, " +
                   $"Circles: {CircleCount}, Rects: {RectangleCount}, Lines: {LineCount}, Polys: {PolygonCount}";
        }
    }
}
