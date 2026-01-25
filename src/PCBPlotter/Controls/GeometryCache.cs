using System;
using System.Collections.Generic;
#if USE_OPENGL
using OpenTK.Graphics.OpenGL;
#endif

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Caches geometry for instanced rendering of circles and rectangles.
    /// Uses VBOs/VAOs for efficient GPU batching.
    /// </summary>
    public class GeometryCache : IDisposable
    {
#if USE_OPENGL
        private readonly int _circleSegments;

        // Circle geometry
        private int _circleVao;
        private int _circleVbo;
        private int _circleEbo;
        private int _circleInstanceVbo;
        private List<float> _circleInstances = new List<float>();
        private int _circleCount;

        // Rectangle geometry
        private int _rectVao;
        private int _rectVbo;
        private int _rectEbo;
        private int _rectInstanceVbo;
        private List<float> _rectInstances = new List<float>();
        private int _rectCount;

        // Instance data: x, y, scaleX, scaleY, r, g, b, a (8 floats per instance)
        private const int INSTANCE_STRIDE = 8;

        public int CircleCount => _circleCount;
        public int RectangleCount => _rectCount;

        public GeometryCache(int circleSegments)
        {
            _circleSegments = circleSegments;
        }

        public void Initialize()
        {
            CreateCircleGeometry();
            CreateRectangleGeometry();
        }

        private void CreateCircleGeometry()
        {
            // Create unit circle vertices (center at origin, radius 1)
            var vertices = new List<float>();
            var indices = new List<uint>();

            // Center vertex
            vertices.Add(0); // x
            vertices.Add(0); // y
            vertices.Add(0); // u
            vertices.Add(0); // v

            // Perimeter vertices
            for (int i = 0; i < _circleSegments; i++)
            {
                float angle = (float)(2 * Math.PI * i / _circleSegments);
                float x = (float)Math.Cos(angle);
                float y = (float)Math.Sin(angle);
                vertices.Add(x);
                vertices.Add(y);
                vertices.Add((x + 1) / 2);
                vertices.Add((y + 1) / 2);
            }

            // Triangle fan indices
            for (int i = 0; i < _circleSegments; i++)
            {
                indices.Add(0); // center
                indices.Add((uint)(i + 1));
                indices.Add((uint)((i + 1) % _circleSegments + 1));
            }

            // Create VAO
            _circleVao = GL.GenVertexArray();
            GL.BindVertexArray(_circleVao);

            // Vertex buffer
            _circleVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _circleVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);

            // Position attribute
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);

            // TexCoord attribute
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            // Element buffer
            _circleEbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _circleEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);

            // Instance buffer
            _circleInstanceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _circleInstanceVbo);

            // Instance position
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, INSTANCE_STRIDE * sizeof(float), 0);
            GL.VertexAttribDivisor(2, 1);

            // Instance scale
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, INSTANCE_STRIDE * sizeof(float), 2 * sizeof(float));
            GL.VertexAttribDivisor(3, 1);

            // Instance color
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, INSTANCE_STRIDE * sizeof(float), 4 * sizeof(float));
            GL.VertexAttribDivisor(4, 1);

            GL.BindVertexArray(0);
        }

        private void CreateRectangleGeometry()
        {
            // Unit rectangle centered at origin (-0.5 to 0.5)
            float[] vertices = {
                -0.5f, -0.5f, 0, 0,  // bottom-left
                 0.5f, -0.5f, 1, 0,  // bottom-right
                 0.5f,  0.5f, 1, 1,  // top-right
                -0.5f,  0.5f, 0, 1   // top-left
            };

            uint[] indices = { 0, 1, 2, 0, 2, 3 };

            // Create VAO
            _rectVao = GL.GenVertexArray();
            GL.BindVertexArray(_rectVao);

            // Vertex buffer
            _rectVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _rectVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            // Position attribute
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);

            // TexCoord attribute
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            // Element buffer
            _rectEbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _rectEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            // Instance buffer
            _rectInstanceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _rectInstanceVbo);

            // Instance position
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, INSTANCE_STRIDE * sizeof(float), 0);
            GL.VertexAttribDivisor(2, 1);

            // Instance scale
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, INSTANCE_STRIDE * sizeof(float), 2 * sizeof(float));
            GL.VertexAttribDivisor(3, 1);

            // Instance color
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, INSTANCE_STRIDE * sizeof(float), 4 * sizeof(float));
            GL.VertexAttribDivisor(4, 1);

            GL.BindVertexArray(0);
        }

        public void ClearInstances()
        {
            _circleInstances.Clear();
            _rectInstances.Clear();
            _circleCount = 0;
            _rectCount = 0;
        }

        public void AddCircle(float x, float y, float radius, float r, float g, float b, float a)
        {
            _circleInstances.Add(x);
            _circleInstances.Add(y);
            _circleInstances.Add(radius); // scaleX
            _circleInstances.Add(radius); // scaleY
            _circleInstances.Add(r);
            _circleInstances.Add(g);
            _circleInstances.Add(b);
            _circleInstances.Add(a);
            _circleCount++;
        }

        public void AddRectangle(float x, float y, float width, float height, float r, float g, float b, float a)
        {
            _rectInstances.Add(x);
            _rectInstances.Add(y);
            _rectInstances.Add(width);  // scaleX
            _rectInstances.Add(height); // scaleY
            _rectInstances.Add(r);
            _rectInstances.Add(g);
            _rectInstances.Add(b);
            _rectInstances.Add(a);
            _rectCount++;
        }

        public void Upload()
        {
            // Upload circle instances
            if (_circleCount > 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _circleInstanceVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, _circleInstances.Count * sizeof(float), _circleInstances.ToArray(), BufferUsageHint.StreamDraw);
            }

            // Upload rectangle instances
            if (_rectCount > 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _rectInstanceVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, _rectInstances.Count * sizeof(float), _rectInstances.ToArray(), BufferUsageHint.StreamDraw);
            }
        }

        public void DrawCircles()
        {
            if (_circleCount == 0) return;

            GL.BindVertexArray(_circleVao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, _circleSegments * 3, DrawElementsType.UnsignedInt, IntPtr.Zero, _circleCount);
            GL.BindVertexArray(0);
        }

        public void DrawRectangles()
        {
            if (_rectCount == 0) return;

            GL.BindVertexArray(_rectVao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, IntPtr.Zero, _rectCount);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_circleVao != 0) GL.DeleteVertexArray(_circleVao);
            if (_circleVbo != 0) GL.DeleteBuffer(_circleVbo);
            if (_circleEbo != 0) GL.DeleteBuffer(_circleEbo);
            if (_circleInstanceVbo != 0) GL.DeleteBuffer(_circleInstanceVbo);

            if (_rectVao != 0) GL.DeleteVertexArray(_rectVao);
            if (_rectVbo != 0) GL.DeleteBuffer(_rectVbo);
            if (_rectEbo != 0) GL.DeleteBuffer(_rectEbo);
            if (_rectInstanceVbo != 0) GL.DeleteBuffer(_rectInstanceVbo);
        }
#else
        public int CircleCount => 0;
        public int RectangleCount => 0;

        public GeometryCache(int circleSegments) { }
        public void Initialize() { }
        public void ClearInstances() { }
        public void AddCircle(float x, float y, float radius, float r, float g, float b, float a) { }
        public void AddRectangle(float x, float y, float width, float height, float r, float g, float b, float a) { }
        public void Upload() { }
        public void DrawCircles() { }
        public void DrawRectangles() { }
        public void Dispose() { }
#endif
    }
}
