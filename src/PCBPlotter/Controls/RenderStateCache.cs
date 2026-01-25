using System;
#if USE_OPENGL
using OpenTK;
using OpenTK.Graphics.OpenGL;
#endif

namespace PCBPlotter.Controls
{
    /// <summary>
    /// Caches OpenGL render state to minimize redundant state changes.
    /// Tracks current shader, blend mode, VAO binding, and uniform values.
    /// </summary>
    public class RenderStateCache
    {
#if USE_OPENGL
        // Current state tracking
        private int _currentProgram = -1;
        private int _currentVao = -1;
        private BlendingFactor _currentSrcBlend = BlendingFactor.One;
        private BlendingFactor _currentDstBlend = BlendingFactor.Zero;
        private bool _blendEnabled = false;
        private int _currentFramebuffer = -1;

        // Cached uniform values to avoid redundant uploads
        private Matrix4 _lastProjection;
        private Matrix4 _lastView;
        private Vector4 _lastColor;
        private bool _projectionDirty = true;
        private bool _viewDirty = true;
        private bool _colorDirty = true;

        // Statistics
        private int _stateChanges;
        private int _uniformUploads;
        private int _skippedStateChanges;
        private int _skippedUniforms;

        /// <summary>
        /// Resets the cache at the beginning of a frame.
        /// Call this at the start of each render cycle.
        /// </summary>
        public void BeginFrame()
        {
            // Reset state to unknown (force rebind on first use)
            _currentProgram = -1;
            _currentVao = -1;
            _currentFramebuffer = -1;
            _blendEnabled = false;
            _currentSrcBlend = BlendingFactor.One;
            _currentDstBlend = BlendingFactor.Zero;

            // Mark uniforms as dirty
            _projectionDirty = true;
            _viewDirty = true;
            _colorDirty = true;

            // Reset statistics
            _stateChanges = 0;
            _uniformUploads = 0;
            _skippedStateChanges = 0;
            _skippedUniforms = 0;
        }

        /// <summary>
        /// Binds a shader program if it differs from the current one.
        /// </summary>
        /// <returns>True if the program was changed, false if skipped</returns>
        public bool UseProgram(int program)
        {
            if (_currentProgram == program)
            {
                _skippedStateChanges++;
                return false;
            }

            GL.UseProgram(program);
            _currentProgram = program;
            _stateChanges++;

            // Uniforms need to be re-uploaded when program changes
            _projectionDirty = true;
            _viewDirty = true;
            _colorDirty = true;

            return true;
        }

        /// <summary>
        /// Binds a VAO if it differs from the current one.
        /// </summary>
        /// <returns>True if the VAO was changed, false if skipped</returns>
        public bool BindVertexArray(int vao)
        {
            if (_currentVao == vao)
            {
                _skippedStateChanges++;
                return false;
            }

            GL.BindVertexArray(vao);
            _currentVao = vao;
            _stateChanges++;
            return true;
        }

        /// <summary>
        /// Binds a framebuffer if it differs from the current one.
        /// </summary>
        /// <returns>True if the framebuffer was changed, false if skipped</returns>
        public bool BindFramebuffer(FramebufferTarget target, int framebuffer)
        {
            if (_currentFramebuffer == framebuffer)
            {
                _skippedStateChanges++;
                return false;
            }

            GL.BindFramebuffer(target, framebuffer);
            _currentFramebuffer = framebuffer;
            _stateChanges++;
            return true;
        }

        /// <summary>
        /// Enables blending if not already enabled.
        /// </summary>
        public void EnableBlend()
        {
            if (_blendEnabled)
            {
                _skippedStateChanges++;
                return;
            }

            GL.Enable(EnableCap.Blend);
            _blendEnabled = true;
            _stateChanges++;
        }

        /// <summary>
        /// Disables blending if currently enabled.
        /// </summary>
        public void DisableBlend()
        {
            if (!_blendEnabled)
            {
                _skippedStateChanges++;
                return;
            }

            GL.Disable(EnableCap.Blend);
            _blendEnabled = false;
            _stateChanges++;
        }

        /// <summary>
        /// Sets blend function if it differs from the current one.
        /// </summary>
        public void SetBlendFunc(BlendingFactor srcFactor, BlendingFactor dstFactor)
        {
            if (_currentSrcBlend == srcFactor && _currentDstBlend == dstFactor)
            {
                _skippedStateChanges++;
                return;
            }

            GL.BlendFunc(srcFactor, dstFactor);
            _currentSrcBlend = srcFactor;
            _currentDstBlend = dstFactor;
            _stateChanges++;
        }

        /// <summary>
        /// Uploads projection matrix if it changed.
        /// </summary>
        public void SetProjectionMatrix(int location, ref Matrix4 projection)
        {
            if (!_projectionDirty && _lastProjection == projection)
            {
                _skippedUniforms++;
                return;
            }

            GL.UniformMatrix4(location, false, ref projection);
            _lastProjection = projection;
            _projectionDirty = false;
            _uniformUploads++;
        }

        /// <summary>
        /// Uploads view matrix if it changed.
        /// </summary>
        public void SetViewMatrix(int location, ref Matrix4 view)
        {
            if (!_viewDirty && _lastView == view)
            {
                _skippedUniforms++;
                return;
            }

            GL.UniformMatrix4(location, false, ref view);
            _lastView = view;
            _viewDirty = false;
            _uniformUploads++;
        }

        /// <summary>
        /// Uploads color uniform if it changed.
        /// </summary>
        public void SetColor(int location, Vector4 color)
        {
            if (!_colorDirty && _lastColor == color)
            {
                _skippedUniforms++;
                return;
            }

            GL.Uniform4(location, color.X, color.Y, color.Z, color.W);
            _lastColor = color;
            _colorDirty = false;
            _uniformUploads++;
        }

        /// <summary>
        /// Forces the next matrix upload to happen even if values match.
        /// Call when switching framebuffers or viewports.
        /// </summary>
        public void InvalidateMatrices()
        {
            _projectionDirty = true;
            _viewDirty = true;
        }

        /// <summary>
        /// Gets statistics for the current frame.
        /// </summary>
        public RenderStateStats GetStats()
        {
            return new RenderStateStats
            {
                StateChanges = _stateChanges,
                UniformUploads = _uniformUploads,
                SkippedStateChanges = _skippedStateChanges,
                SkippedUniforms = _skippedUniforms
            };
        }

        /// <summary>
        /// Resets all cached state. Call when external code may have modified GL state.
        /// </summary>
        public void Invalidate()
        {
            _currentProgram = -1;
            _currentVao = -1;
            _currentFramebuffer = -1;
            _blendEnabled = false;
            _projectionDirty = true;
            _viewDirty = true;
            _colorDirty = true;
        }
#else
        // Stub implementation
        public void BeginFrame() { }
        public bool UseProgram(int program) => true;
        public bool BindVertexArray(int vao) => true;
        public bool BindFramebuffer(object target, int framebuffer) => true;
        public void EnableBlend() { }
        public void DisableBlend() { }
        public void SetBlendFunc(object srcFactor, object dstFactor) { }
        public void SetProjectionMatrix(int location, ref object projection) { }
        public void SetViewMatrix(int location, ref object view) { }
        public void SetColor(int location, object color) { }
        public void InvalidateMatrices() { }
        public RenderStateStats GetStats() => new RenderStateStats();
        public void Invalidate() { }
#endif
    }

    /// <summary>
    /// Statistics about render state caching efficiency.
    /// </summary>
    public struct RenderStateStats
    {
        public int StateChanges;
        public int UniformUploads;
        public int SkippedStateChanges;
        public int SkippedUniforms;

        public float StateEfficiency =>
            (StateChanges + SkippedStateChanges) > 0
                ? (float)SkippedStateChanges / (StateChanges + SkippedStateChanges)
                : 0;

        public float UniformEfficiency =>
            (UniformUploads + SkippedUniforms) > 0
                ? (float)SkippedUniforms / (UniformUploads + SkippedUniforms)
                : 0;

        public override string ToString()
        {
            return $"State: {StateChanges} changes ({SkippedStateChanges} skipped, {StateEfficiency:P0} efficiency), " +
                   $"Uniforms: {UniformUploads} uploads ({SkippedUniforms} skipped, {UniformEfficiency:P0} efficiency)";
        }
    }
}
