using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if USE_OPENGL
using OpenTK.Graphics.OpenGL;
#endif

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Manages GPU buffers for efficient geometry rendering.
    /// Provides VBO/VAO infrastructure with proper buffer management and streaming updates.
    /// </summary>
    public class GeometryBuffer : IDisposable
    {
#if USE_OPENGL
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _instanceVbo;

        private int _vertexCapacity;
        private int _indexCapacity;
        private int _instanceCapacity;

        // Track actual GPU buffer capacities separately from CPU array capacities
        private int _gpuVertexCapacity;
        private int _gpuIndexCapacity;
        private int _gpuInstanceCapacity;

        private int _vertexCount;
        private int _indexCount;
        private int _instanceCount;

        private bool _isDirty;
        private bool _isInitialized;
        private BufferUsageHint _usageHint;

        // Vertex format: position (2 floats), texcoord (2 floats) = 4 floats per vertex
        private const int VERTEX_SIZE = 4;
        private const int INSTANCE_SIZE = 8; // pos(2) + scale(2) + color(4) = 8 floats

        private float[] _vertices;
        private uint[] _indices;
        private float[] _instances;

        /// <summary>
        /// Creates a new geometry buffer with the specified initial capacities.
        /// </summary>
        /// <param name="vertexCapacity">Initial vertex capacity</param>
        /// <param name="indexCapacity">Initial index capacity</param>
        /// <param name="instanceCapacity">Initial instance capacity for instanced rendering</param>
        /// <param name="usageHint">Buffer usage hint (Static for cached geometry, Stream for dynamic)</param>
        public GeometryBuffer(int vertexCapacity = 4096, int indexCapacity = 8192, int instanceCapacity = 1024, BufferUsageHint usageHint = BufferUsageHint.DynamicDraw)
        {
            _vertexCapacity = vertexCapacity;
            _indexCapacity = indexCapacity;
            _instanceCapacity = instanceCapacity;
            _usageHint = usageHint;

            _vertices = new float[vertexCapacity * VERTEX_SIZE];
            _indices = new uint[indexCapacity];
            _instances = new float[instanceCapacity * INSTANCE_SIZE];

            _isDirty = true;
        }

        /// <summary>
        /// Initializes OpenGL resources. Must be called from the GL context thread.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            // Generate VAO
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            // Generate VBO for vertices
            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertexCapacity * VERTEX_SIZE * sizeof(float), IntPtr.Zero, _usageHint);

            // Position attribute (location 0)
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // TexCoord attribute (location 1) - used for per-vertex data
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // Generate EBO for indices
            _ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _indexCapacity * sizeof(uint), IntPtr.Zero, _usageHint);

            // Generate instance VBO
            _instanceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _instanceCapacity * INSTANCE_SIZE * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);

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

            GL.BindVertexArray(0);

            // Track actual GPU buffer capacities
            _gpuVertexCapacity = _vertexCapacity;
            _gpuIndexCapacity = _indexCapacity;
            _gpuInstanceCapacity = _instanceCapacity;

            _isInitialized = true;
        }

        /// <summary>
        /// Clears all geometry data (does not deallocate buffers).
        /// </summary>
        public void Clear()
        {
            _vertexCount = 0;
            _indexCount = 0;
            _instanceCount = 0;
            _isDirty = true;
        }

        /// <summary>
        /// Clears only instance data, preserving vertex and index data.
        /// Use this for instanced geometry where base geometry is static.
        /// </summary>
        public void ClearInstancesOnly()
        {
            _instanceCount = 0;
            _isDirty = true;
        }

        /// <summary>
        /// Adds a vertex to the buffer.
        /// </summary>
        /// <returns>The index of the added vertex</returns>
        public int AddVertex(float x, float y, float u = 0, float v = 0)
        {
            EnsureVertexCapacity(_vertexCount + 1);

            int offset = _vertexCount * VERTEX_SIZE;
            _vertices[offset] = x;
            _vertices[offset + 1] = y;
            _vertices[offset + 2] = u;
            _vertices[offset + 3] = v;

            _isDirty = true;
            return _vertexCount++;
        }

        /// <summary>
        /// Adds multiple vertices from an array.
        /// </summary>
        /// <returns>The starting index of the added vertices</returns>
        public int AddVertices(float[] vertices, int vertexCount)
        {
            EnsureVertexCapacity(_vertexCount + vertexCount);

            int startIndex = _vertexCount;
            Array.Copy(vertices, 0, _vertices, _vertexCount * VERTEX_SIZE, vertexCount * VERTEX_SIZE);
            _vertexCount += vertexCount;

            _isDirty = true;
            return startIndex;
        }

        /// <summary>
        /// Adds a triangle to the index buffer.
        /// </summary>
        public void AddTriangle(uint v0, uint v1, uint v2)
        {
            EnsureIndexCapacity(_indexCount + 3);

            _indices[_indexCount++] = v0;
            _indices[_indexCount++] = v1;
            _indices[_indexCount++] = v2;

            _isDirty = true;
        }

        /// <summary>
        /// Adds a quad (2 triangles) to the index buffer.
        /// </summary>
        public void AddQuad(uint v0, uint v1, uint v2, uint v3)
        {
            EnsureIndexCapacity(_indexCount + 6);

            // First triangle
            _indices[_indexCount++] = v0;
            _indices[_indexCount++] = v1;
            _indices[_indexCount++] = v2;

            // Second triangle
            _indices[_indexCount++] = v0;
            _indices[_indexCount++] = v2;
            _indices[_indexCount++] = v3;

            _isDirty = true;
        }

        /// <summary>
        /// Adds multiple indices from an array.
        /// </summary>
        public void AddIndices(uint[] indices, int count, uint baseVertex = 0)
        {
            EnsureIndexCapacity(_indexCount + count);

            for (int i = 0; i < count; i++)
            {
                _indices[_indexCount++] = indices[i] + baseVertex;
            }

            _isDirty = true;
        }

        /// <summary>
        /// Adds an instance for instanced rendering.
        /// </summary>
        public void AddInstance(float x, float y, float scaleX, float scaleY, float r, float g, float b, float a)
        {
            EnsureInstanceCapacity(_instanceCount + 1);

            int offset = _instanceCount * INSTANCE_SIZE;
            _instances[offset] = x;
            _instances[offset + 1] = y;
            _instances[offset + 2] = scaleX;
            _instances[offset + 3] = scaleY;
            _instances[offset + 4] = r;
            _instances[offset + 5] = g;
            _instances[offset + 6] = b;
            _instances[offset + 7] = a;

            _instanceCount++;
            _isDirty = true;
        }

        /// <summary>
        /// Uploads buffer data to GPU. Call this before rendering.
        /// </summary>
        public void Upload()
        {
            if (!_isInitialized || !_isDirty) return;

            GL.BindVertexArray(_vao);

            // Upload vertices
            if (_vertexCount > 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

                // Check if we need to reallocate GPU buffer (compare against actual GPU buffer capacity)
                if (_vertexCount > _gpuVertexCapacity)
                {
                    int newGpuCapacity = Math.Max(_vertexCount, _gpuVertexCapacity * 2);
                    GL.BufferData(BufferTarget.ArrayBuffer, newGpuCapacity * VERTEX_SIZE * sizeof(float), _vertices, _usageHint);
                    _gpuVertexCapacity = newGpuCapacity;
                }
                else
                {
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertexCount * VERTEX_SIZE * sizeof(float), _vertices);
                }
            }

            // Upload indices
            if (_indexCount > 0)
            {
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);

                // Check if we need to reallocate GPU buffer
                if (_indexCount > _gpuIndexCapacity)
                {
                    int newGpuCapacity = Math.Max(_indexCount, _gpuIndexCapacity * 2);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, newGpuCapacity * sizeof(uint), _indices, _usageHint);
                    _gpuIndexCapacity = newGpuCapacity;
                }
                else
                {
                    GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, _indexCount * sizeof(uint), _indices);
                }
            }

            // Upload instances
            if (_instanceCount > 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);

                // Check if we need to reallocate GPU buffer (compare against actual GPU buffer capacity)
                if (_instanceCount > _gpuInstanceCapacity)
                {
                    int newGpuCapacity = Math.Max(_instanceCount, _gpuInstanceCapacity * 2);
                    GL.BufferData(BufferTarget.ArrayBuffer, newGpuCapacity * INSTANCE_SIZE * sizeof(float), _instances, BufferUsageHint.StreamDraw);
                    _gpuInstanceCapacity = newGpuCapacity;
                }
                else
                {
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _instanceCount * INSTANCE_SIZE * sizeof(float), _instances);
                }
            }

            GL.BindVertexArray(0);
            _isDirty = false;
        }

        /// <summary>
        /// Renders the geometry using indexed drawing.
        /// </summary>
        public void Draw(PrimitiveType primitiveType = PrimitiveType.Triangles)
        {
            if (!_isInitialized || _indexCount == 0) return;

            GL.BindVertexArray(_vao);
            GL.DrawElements(primitiveType, _indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Renders using instanced drawing.
        /// </summary>
        public void DrawInstanced(int baseVertices, int baseIndices, PrimitiveType primitiveType = PrimitiveType.Triangles)
        {
            if (!_isInitialized || _instanceCount == 0) return;

            GL.BindVertexArray(_vao);
            GL.DrawElementsInstanced(primitiveType, baseIndices, DrawElementsType.UnsignedInt, IntPtr.Zero, _instanceCount);
            GL.BindVertexArray(0);
        }

        /// <summary>
        /// Renders using non-indexed drawing (for line strips, etc.).
        /// </summary>
        public void DrawArrays(PrimitiveType primitiveType, int first = 0, int count = -1)
        {
            if (!_isInitialized || _vertexCount == 0) return;

            if (count < 0) count = _vertexCount - first;

            GL.BindVertexArray(_vao);
            GL.DrawArrays(primitiveType, first, count);
            GL.BindVertexArray(0);
        }

        public int VertexCount => _vertexCount;
        public int IndexCount => _indexCount;
        public int InstanceCount => _instanceCount;
        public bool IsDirty => _isDirty;
        public int VAO => _vao;

        private void EnsureVertexCapacity(int required)
        {
            if (required * VERTEX_SIZE <= _vertices.Length) return;

            int newCapacity = Math.Max(required, _vertexCapacity * 2);
            var newVertices = new float[newCapacity * VERTEX_SIZE];
            Array.Copy(_vertices, newVertices, _vertexCount * VERTEX_SIZE);
            _vertices = newVertices;
            _vertexCapacity = newCapacity;
        }

        private void EnsureIndexCapacity(int required)
        {
            if (required <= _indices.Length) return;

            int newCapacity = Math.Max(required, _indexCapacity * 2);
            var newIndices = new uint[newCapacity];
            Array.Copy(_indices, newIndices, _indexCount);
            _indices = newIndices;
            _indexCapacity = newCapacity;
        }

        private void EnsureInstanceCapacity(int required)
        {
            if (required * INSTANCE_SIZE <= _instances.Length) return;

            int newCapacity = Math.Max(required, _instanceCapacity * 2);
            var newInstances = new float[newCapacity * INSTANCE_SIZE];
            Array.Copy(_instances, newInstances, _instanceCount * INSTANCE_SIZE);
            _instances = newInstances;
            _instanceCapacity = newCapacity;
        }

        public void Dispose()
        {
            if (_isInitialized)
            {
                if (_vao != 0) GL.DeleteVertexArray(_vao);
                if (_vbo != 0) GL.DeleteBuffer(_vbo);
                if (_ebo != 0) GL.DeleteBuffer(_ebo);
                if (_instanceVbo != 0) GL.DeleteBuffer(_instanceVbo);

                _vao = 0;
                _vbo = 0;
                _ebo = 0;
                _instanceVbo = 0;
                _isInitialized = false;
            }
        }
#else
        // Stub implementation when OpenGL is not available
        public void Initialize() { }
        public void Clear() { }
        public void ClearInstancesOnly() { }
        public int AddVertex(float x, float y, float u = 0, float v = 0) => 0;
        public int AddVertices(float[] vertices, int vertexCount) => 0;
        public void AddTriangle(uint v0, uint v1, uint v2) { }
        public void AddQuad(uint v0, uint v1, uint v2, uint v3) { }
        public void AddIndices(uint[] indices, int count, uint baseVertex = 0) { }
        public void AddInstance(float x, float y, float scaleX, float scaleY, float r, float g, float b, float a) { }
        public void Upload() { }
        public void Draw() { }
        public void DrawInstanced(int baseVertices, int baseIndices) { }
        public void DrawArrays(int first = 0, int count = -1) { }
        public int VertexCount => 0;
        public int IndexCount => 0;
        public int InstanceCount => 0;
        public bool IsDirty => false;
        public int VAO => 0;
        public void Dispose() { }
#endif
    }

    /// <summary>
    /// Caches pre-built geometry for common primitives (circles, rounded rectangles, etc.)
    /// Enables instanced rendering of repeated shapes.
    /// </summary>
    public class GeometryCache : IDisposable
    {
#if USE_OPENGL
        private GeometryBuffer _circleBuffer;
        private GeometryBuffer _rectangleBuffer;
        private GeometryBuffer _lineCapBuffer;

        private int _circleSegments;
        private int _circleIndexCount;
        private int _rectangleIndexCount;
        private int _lineCapIndexCount;

        private bool _isInitialized;

        public GeometryCache(int circleSegments = 32)
        {
            _circleSegments = circleSegments;
        }

        /// <summary>
        /// Initializes cached geometry. Must be called from GL context thread.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            // Build unit circle geometry (radius 1, centered at origin)
            // Use larger initial capacity to avoid frequent reallocations for PCB files
            _circleBuffer = new GeometryBuffer(
                vertexCapacity: _circleSegments + 1,
                indexCapacity: _circleSegments * 3,
                instanceCapacity: 65536,
                usageHint: BufferUsageHint.StaticDraw);
            _circleBuffer.Initialize();

            BuildCircleGeometry();

            // Build unit rectangle geometry (1x1, centered at origin)
            _rectangleBuffer = new GeometryBuffer(
                vertexCapacity: 4,
                indexCapacity: 6,
                instanceCapacity: 65536,
                usageHint: BufferUsageHint.StaticDraw);
            _rectangleBuffer.Initialize();

            BuildRectangleGeometry();

            // Build semicircle for line caps
            _lineCapBuffer = new GeometryBuffer(
                vertexCapacity: _circleSegments / 2 + 2,
                indexCapacity: _circleSegments / 2 * 3,
                instanceCapacity: 65536,
                usageHint: BufferUsageHint.StaticDraw);
            _lineCapBuffer.Initialize();

            BuildLineCapGeometry();

            _isInitialized = true;
        }

        private void BuildCircleGeometry()
        {
            // Center vertex
            _circleBuffer.AddVertex(0, 0);

            // Circle perimeter vertices
            for (int i = 0; i < _circleSegments; i++)
            {
                float angle = (float)(2 * Math.PI * i / _circleSegments);
                _circleBuffer.AddVertex((float)Math.Cos(angle), (float)Math.Sin(angle));
            }

            // Triangle fan indices
            for (int i = 0; i < _circleSegments; i++)
            {
                _circleBuffer.AddTriangle(0, (uint)(i + 1), (uint)((i + 1) % _circleSegments + 1));
            }

            _circleIndexCount = _circleSegments * 3;
            _circleBuffer.Upload();
        }

        private void BuildRectangleGeometry()
        {
            // Unit rectangle vertices (centered at origin, size 1x1)
            _rectangleBuffer.AddVertex(-0.5f, -0.5f);
            _rectangleBuffer.AddVertex(0.5f, -0.5f);
            _rectangleBuffer.AddVertex(0.5f, 0.5f);
            _rectangleBuffer.AddVertex(-0.5f, 0.5f);

            _rectangleBuffer.AddQuad(0, 1, 2, 3);

            _rectangleIndexCount = 6;
            _rectangleBuffer.Upload();
        }

        private void BuildLineCapGeometry()
        {
            int halfSegments = _circleSegments / 2;

            // Center vertex at line end
            _lineCapBuffer.AddVertex(0, 0);

            // Semicircle vertices
            for (int i = 0; i <= halfSegments; i++)
            {
                float angle = (float)(Math.PI * i / halfSegments);
                _lineCapBuffer.AddVertex((float)Math.Cos(angle), (float)Math.Sin(angle));
            }

            // Triangle fan
            for (int i = 0; i < halfSegments; i++)
            {
                _lineCapBuffer.AddTriangle(0, (uint)(i + 1), (uint)(i + 2));
            }

            _lineCapIndexCount = halfSegments * 3;
            _lineCapBuffer.Upload();
        }

        /// <summary>
        /// Clears instance data for a new frame.
        /// Base geometry (unit circle, unit rectangle) is preserved.
        /// </summary>
        public void ClearInstances()
        {
            // Only clear instance data, don't rebuild base geometry
            _circleBuffer.ClearInstancesOnly();
            _rectangleBuffer.ClearInstancesOnly();
            _lineCapBuffer.ClearInstancesOnly();
        }

        /// <summary>
        /// Adds a circle instance for batched rendering.
        /// </summary>
        public void AddCircle(float x, float y, float radius, float r, float g, float b, float a)
        {
            _circleBuffer.AddInstance(x, y, radius, radius, r, g, b, a);
        }

        /// <summary>
        /// Adds a rectangle instance for batched rendering.
        /// </summary>
        public void AddRectangle(float x, float y, float width, float height, float r, float g, float b, float a)
        {
            _rectangleBuffer.AddInstance(x, y, width, height, r, g, b, a);
        }

        /// <summary>
        /// Uploads all instance data to GPU.
        /// </summary>
        public void Upload()
        {
            _circleBuffer.Upload();
            _rectangleBuffer.Upload();
            _lineCapBuffer.Upload();
        }

        /// <summary>
        /// Draws all batched circles.
        /// </summary>
        public void DrawCircles()
        {
            if (_circleBuffer.InstanceCount > 0)
            {
                _circleBuffer.DrawInstanced(_circleSegments + 1, _circleIndexCount);
            }
        }

        /// <summary>
        /// Draws all batched rectangles.
        /// </summary>
        public void DrawRectangles()
        {
            if (_rectangleBuffer.InstanceCount > 0)
            {
                _rectangleBuffer.DrawInstanced(4, _rectangleIndexCount);
            }
        }

        public int CircleCount => _circleBuffer.InstanceCount;
        public int RectangleCount => _rectangleBuffer.InstanceCount;

        public GeometryBuffer CircleBuffer => _circleBuffer;
        public GeometryBuffer RectangleBuffer => _rectangleBuffer;
        public GeometryBuffer LineCapBuffer => _lineCapBuffer;

        public void Dispose()
        {
            _circleBuffer?.Dispose();
            _rectangleBuffer?.Dispose();
            _lineCapBuffer?.Dispose();
        }
#else
        public void Initialize() { }
        public void ClearInstances() { }
        public void AddCircle(float x, float y, float radius, float r, float g, float b, float a) { }
        public void AddRectangle(float x, float y, float width, float height, float r, float g, float b, float a) { }
        public void Upload() { }
        public void DrawCircles() { }
        public void DrawRectangles() { }
        public int CircleCount => 0;
        public int RectangleCount => 0;
        public void Dispose() { }
#endif
    }
}
