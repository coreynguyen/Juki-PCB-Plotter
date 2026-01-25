using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
#if USE_OPENGL
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
#endif
using PCBPlotter.Core.Models;

namespace PCBPlotter.Controls
{
    /// <summary>
    /// High-performance OpenGL-accelerated canvas for rendering PCB layouts.
    /// Uses GPU batching, instancing, and proper screen blend mode support.
    /// NOTE: Requires OpenTK NuGet packages. Run 'nuget restore' if you see compile errors.
    /// To disable OpenGL support, remove USE_OPENGL from project DefineConstants.
    /// </summary>
    public class OpenGLCanvas : ContentControl
    {
#if USE_OPENGL
        private OpenTK.GLControl _glControl;
        private WindowsFormsHost _host;
        private bool _glInitialized;
        private DispatcherTimer _renderTimer;

        // GPU resources
        private int _shaderProgram;
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _instanceVbo;

        // Modern batch renderer
        private BatchRenderer _batchRenderer;
        private bool _useModernPipeline = true;
        private RenderStats _lastRenderStats;

        // Framebuffers for layer compositing
        private int _layerFbo;
        private int _layerTexture;
        private int _compositeFboA;
        private int _compositeTextureA;
        private int _compositeFboB;
        private int _compositeTextureB;
        private bool _useCompositeA = true; // Ping-pong flag
        private int _screenBlendShader;
        private int _quadVao;
        private int _quadVbo;

        // Uniform locations (basic shader)
        private int _projectionLoc;
        private int _viewLoc;
        private int _colorLoc;

        // Cached uniform locations for screen blend shader (avoid GetUniformLocation in render loop)
        private int _screenBlendBaseTextureLoc;
        private int _screenBlendBlendTextureLoc;
        private int _screenBlendModeLoc;
        private int _screenBlendOpacityLoc;

        // Geometry batches for different primitive types
        private List<float> _circleVertices = new List<float>();
        private List<uint> _circleIndices = new List<uint>();
        private List<float> _rectVertices = new List<float>();
        private List<uint> _rectIndices = new List<uint>();
        private List<float> _lineVertices = new List<float>();
        private List<float> _polygonVertices = new List<float>();
        private List<uint> _polygonIndices = new List<uint>();

        // Instance data for batched rendering
        private List<PrimitiveInstance> _instances = new List<PrimitiveInstance>();

        // Pre-generated circle geometry
        private const int CIRCLE_SEGMENTS = 32;
        private float[] _unitCircle;
        private uint[] _unitCircleIndices;

        // Render state
        private Matrix4 _projection;
        private Matrix4 _view;
        private bool _needsRebuild = true;
        private bool _needsRedraw = true; // Dirty flag to avoid continuous rendering
        private int _lastWidth, _lastHeight;

        // View state tracking for change detection
        private double _lastZoom;
        private double _lastPanX;
        private double _lastPanY;
        private bool _viewChanged = true;

        // Layer geometry caching - avoid rebuilding every frame
        private Dictionary<string, LayerGeometryCache> _layerGeometryCache = new Dictionary<string, LayerGeometryCache>();
        private bool _geometryCacheDirty = true;

        // Static layer renderers - upload instance data to GPU once, render with just draw calls
        private Dictionary<string, StaticLayerRenderer> _staticLayerRenderers = new Dictionary<string, StaticLayerRenderer>();
        private bool _useStaticRendering = true; // Toggle for static vs dynamic rendering

        // Quadtrees for spatial indexing (parallel building)
        private Dictionary<string, GerberQuadtree> _layerQuadtrees = new Dictionary<string, GerberQuadtree>();

        // Async cache building - track which layers are being built in background
        private HashSet<string> _pendingCacheBuilds = new HashSet<string>();
        private object _cacheLock = new object();

        // Track disposal state to prevent background tasks from accessing disposed resources
        private volatile bool _isDisposed = false;

        // Mouse interaction
        private Point _lastMousePosition;
        private Point _panStart;
        private bool _isPanning;
        private bool _isSelecting;
        private Point _selectionStart;
        private Rect _selectionRect;
#else
        // Stub fields when OpenGL is not available
        private bool _needsRebuild = true;
        private Dictionary<string, GerberQuadtree> _layerQuadtrees = new Dictionary<string, GerberQuadtree>();
        private Point _lastMousePosition;
        private Point _panStart;
        private bool _isPanning;
        private bool _isSelecting;
        private Point _selectionStart;
        private Rect _selectionRect;
#endif

        #region Dependency Properties

        public static readonly DependencyProperty ZoomProperty =
            DependencyProperty.Register("Zoom", typeof(double), typeof(OpenGLCanvas),
                new PropertyMetadata(20.0, OnViewChanged));

        public static readonly DependencyProperty PanXProperty =
            DependencyProperty.Register("PanX", typeof(double), typeof(OpenGLCanvas),
                new PropertyMetadata(0.0, OnViewChanged));

        public static readonly DependencyProperty PanYProperty =
            DependencyProperty.Register("PanY", typeof(double), typeof(OpenGLCanvas),
                new PropertyMetadata(0.0, OnViewChanged));

        public static readonly DependencyProperty GerberLayersProperty =
            DependencyProperty.Register("GerberLayers", typeof(ObservableCollection<GerberLayer>), typeof(OpenGLCanvas),
                new PropertyMetadata(null, OnLayersChanged));

        public static readonly DependencyProperty UseScreenBlendProperty =
            DependencyProperty.Register("UseScreenBlend", typeof(bool), typeof(OpenGLCanvas),
                new PropertyMetadata(true, OnViewChanged));

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register("BackgroundColor", typeof(Color), typeof(OpenGLCanvas),
                new PropertyMetadata(Color.FromRgb(30, 30, 30), OnViewChanged));

        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register("ShowGrid", typeof(bool), typeof(OpenGLCanvas),
                new PropertyMetadata(true, OnViewChanged));

        public static readonly DependencyProperty GridSpacingProperty =
            DependencyProperty.Register("GridSpacing", typeof(double), typeof(OpenGLCanvas),
                new PropertyMetadata(1.0, OnViewChanged));

        public static readonly DependencyProperty UseModernPipelineProperty =
            DependencyProperty.Register("UseModernPipeline", typeof(bool), typeof(OpenGLCanvas),
                new PropertyMetadata(true, OnPipelineChanged));

        public double Zoom
        {
            get => (double)GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        public double PanX
        {
            get => (double)GetValue(PanXProperty);
            set => SetValue(PanXProperty, value);
        }

        public double PanY
        {
            get => (double)GetValue(PanYProperty);
            set => SetValue(PanYProperty, value);
        }

        public ObservableCollection<GerberLayer> GerberLayers
        {
            get => (ObservableCollection<GerberLayer>)GetValue(GerberLayersProperty);
            set => SetValue(GerberLayersProperty, value);
        }

        public bool UseScreenBlend
        {
            get => (bool)GetValue(UseScreenBlendProperty);
            set => SetValue(UseScreenBlendProperty, value);
        }

        public Color BackgroundColor
        {
            get => (Color)GetValue(BackgroundColorProperty);
            set => SetValue(BackgroundColorProperty, value);
        }

        public bool ShowGrid
        {
            get => (bool)GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }

        public double GridSpacing
        {
            get => (double)GetValue(GridSpacingProperty);
            set => SetValue(GridSpacingProperty, value);
        }

        public bool UseModernPipeline
        {
            get => (bool)GetValue(UseModernPipelineProperty);
            set => SetValue(UseModernPipelineProperty, value);
        }

        #endregion

        private static void OnViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = (OpenGLCanvas)d;
            canvas.Invalidate();
        }

        private static void OnLayersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = (OpenGLCanvas)d;
            canvas._needsRebuild = true;
            canvas._layerQuadtrees.Clear();
            canvas._geometryCacheDirty = true;

            // Clear pending cache builds and geometry cache (thread-safe)
            lock (canvas._cacheLock)
            {
                canvas._pendingCacheBuilds.Clear();
                canvas._layerGeometryCache.Clear();
            }

            // Dispose and clear static layer renderers
            foreach (var renderer in canvas._staticLayerRenderers.Values)
            {
                renderer.Dispose();
            }
            canvas._staticLayerRenderers.Clear();

            // Unsubscribe from old collection events
            if (e.OldValue is ObservableCollection<GerberLayer> oldCollection)
            {
                oldCollection.CollectionChanged -= canvas.OnLayerCollectionChanged;
                foreach (var layer in oldCollection)
                {
                    layer.PropertyChanged -= canvas.OnLayerPropertyChanged;
                }
            }

            // Subscribe to new collection events
            if (e.NewValue is ObservableCollection<GerberLayer> newCollection)
            {
                newCollection.CollectionChanged += canvas.OnLayerCollectionChanged;
                foreach (var layer in newCollection)
                {
                    layer.PropertyChanged += canvas.OnLayerPropertyChanged;
                }
            }

            canvas.Invalidate();
        }

        private void OnLayerCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Unsubscribe from removed layers
            if (e.OldItems != null)
            {
                foreach (GerberLayer layer in e.OldItems)
                {
                    layer.PropertyChanged -= OnLayerPropertyChanged;
                }
            }

            // Subscribe to new layers
            if (e.NewItems != null)
            {
                foreach (GerberLayer layer in e.NewItems)
                {
                    layer.PropertyChanged += OnLayerPropertyChanged;
                }
            }

            // Mark for rebuild when layers are added/removed
            _needsRebuild = true;
            _geometryCacheDirty = true;
            Invalidate();
        }

        private void OnLayerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Invalidate on visibility, color, or opacity changes
            if (e.PropertyName == nameof(GerberLayer.IsVisible) ||
                e.PropertyName == nameof(GerberLayer.ColorArgb) ||
                e.PropertyName == nameof(GerberLayer.Opacity))
            {
                Invalidate();
            }
        }

        private static void OnPipelineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
#if USE_OPENGL
            var canvas = (OpenGLCanvas)d;
            canvas._useModernPipeline = (bool)e.NewValue;
            canvas.Invalidate();
#endif
        }

        public OpenGLCanvas()
        {
#if USE_OPENGL
            InitializeUnitCircle();
#endif
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

#if USE_OPENGL
        private void InitializeUnitCircle()
        {
            // Pre-generate unit circle vertices for instanced circle rendering
            _unitCircle = new float[(CIRCLE_SEGMENTS + 1) * 2];
            _unitCircleIndices = new uint[CIRCLE_SEGMENTS * 3];

            // Center vertex
            _unitCircle[0] = 0;
            _unitCircle[1] = 0;

            // Circle vertices
            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = (float)(2 * Math.PI * i / CIRCLE_SEGMENTS);
                _unitCircle[(i + 1) * 2] = (float)Math.Cos(angle);
                _unitCircle[(i + 1) * 2 + 1] = (float)Math.Sin(angle);
            }

            // Triangle fan indices
            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                _unitCircleIndices[i * 3] = 0;
                _unitCircleIndices[i * 3 + 1] = (uint)(i + 1);
                _unitCircleIndices[i * 3 + 2] = (uint)((i + 1) % CIRCLE_SEGMENTS + 1);
            }
        }
#endif

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
#if USE_OPENGL
            try
            {
                InitializeOpenGL();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenGL initialization failed: {ex.Message}");
                // Fallback: Show error message
                Content = new TextBlock
                {
                    Text = $"OpenGL not available: {ex.Message}\nFalling back to software rendering.",
                    Foreground = Brushes.Red,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
#else
            // OpenGL not available - show message
            Content = new TextBlock
            {
                Text = "OpenGL renderer not available.\nPlease restore NuGet packages and rebuild with USE_OPENGL defined.",
                Foreground = Brushes.Orange,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
#endif
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
#if USE_OPENGL
            CleanupOpenGL();
#endif
        }

#if USE_OPENGL
        private void InitializeOpenGL()
        {
            // Create GLControl
            _glControl = new OpenTK.GLControl(new GraphicsMode(32, 24, 8, 4))
            {
                Dock = System.Windows.Forms.DockStyle.Fill
            };

            _glControl.Load += GlControl_Load;
            _glControl.Paint += GlControl_Paint;
            _glControl.Resize += GlControl_Resize;
            _glControl.MouseDown += GlControl_MouseDown;
            _glControl.MouseUp += GlControl_MouseUp;
            _glControl.MouseMove += GlControl_MouseMove;
            _glControl.MouseWheel += GlControl_MouseWheel;

            // Host in WPF
            _host = new WindowsFormsHost { Child = _glControl };
            Content = _host;

            // Render timer - only render when dirty to reduce GPU load
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += (s, e) =>
            {
                if (_glInitialized && _needsRedraw)
                {
                    _needsRedraw = false;
                    _glControl.Invalidate();
                }
            };
            _renderTimer.Start();
        }

        private void CleanupOpenGL()
        {
            // Mark as disposed FIRST to stop background tasks from accessing resources
            _isDisposed = true;

            _renderTimer?.Stop();

            if (_glInitialized && _glControl != null)
            {
                _glControl.MakeCurrent();

                // Delete GPU resources
                if (_shaderProgram != 0) GL.DeleteProgram(_shaderProgram);
                if (_screenBlendShader != 0) GL.DeleteProgram(_screenBlendShader);
                if (_vao != 0) GL.DeleteVertexArray(_vao);
                if (_vbo != 0) GL.DeleteBuffer(_vbo);
                if (_ebo != 0) GL.DeleteBuffer(_ebo);
                if (_instanceVbo != 0) GL.DeleteBuffer(_instanceVbo);
                if (_quadVao != 0) GL.DeleteVertexArray(_quadVao);
                if (_quadVbo != 0) GL.DeleteBuffer(_quadVbo);
                if (_layerFbo != 0) GL.DeleteFramebuffer(_layerFbo);
                if (_layerTexture != 0) GL.DeleteTexture(_layerTexture);
                if (_compositeFboA != 0) GL.DeleteFramebuffer(_compositeFboA);
                if (_compositeTextureA != 0) GL.DeleteTexture(_compositeTextureA);
                if (_compositeFboB != 0) GL.DeleteFramebuffer(_compositeFboB);
                if (_compositeTextureB != 0) GL.DeleteTexture(_compositeTextureB);

                // Cleanup static layer renderers
                foreach (var renderer in _staticLayerRenderers.Values)
                {
                    renderer.Dispose();
                }
                _staticLayerRenderers.Clear();

                // Cleanup batch renderer
                _batchRenderer?.Dispose();
            }

            _host?.Dispose();
        }

        private void GlControl_Load(object sender, EventArgs e)
        {
            _glControl.MakeCurrent();

            // Check OpenGL version
            string version = GL.GetString(StringName.Version);
            System.Diagnostics.Debug.WriteLine($"OpenGL Version: {version}");

            // Initialize shaders
            InitializeShaders();
            InitializeScreenBlendShader();
            InitializeQuadVAO();

            // Initialize batch renderer for modern pipeline
            try
            {
                _batchRenderer = new BatchRenderer();
                _batchRenderer.Initialize();
                // Disable frustum culling in BatchRenderer since quadtree already does spatial filtering
                // and RenderLayerBatched already does LOD filtering - avoids redundant double-culling
                _batchRenderer.UseFrustumCulling = false;
                System.Diagnostics.Debug.WriteLine("BatchRenderer initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BatchRenderer initialization failed: {ex.Message}");
                _useModernPipeline = false;
            }

            // Enable required states
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.Multisample);

            _glInitialized = true;
        }

        private void InitializeShaders()
        {
            // Vertex shader for primitives
            string vertexSource = "#version 330 core\n" + @"layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aInstancePos;
layout (location = 2) in vec2 aInstanceScale;
layout (location = 3) in vec4 aInstanceColor;

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

            // Fragment shader
            string fragmentSource = "#version 330 core\n" + @"in vec4 vertexColor;
out vec4 FragColor;

void main()
{
    FragColor = vertexColor;
}
";

            _shaderProgram = CreateShaderProgram(vertexSource, fragmentSource);
            _projectionLoc = GL.GetUniformLocation(_shaderProgram, "projection");
            _viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
        }

        private void InitializeScreenBlendShader()
        {
            // Vertex shader for full-screen quad
            string vertexSource = "#version 330 core\n" + @"layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;

out vec2 TexCoord;

void main()
{
    gl_Position = vec4(aPos, 0.0, 1.0);
    TexCoord = aTexCoord;
}
";

            // Fragment shader with screen blend mode
            string fragmentSource = "#version 330 core\n" + @"in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D baseTexture;
uniform sampler2D blendTexture;
uniform int blendMode; // 0=normal, 1=screen, 2=multiply, 3=overlay
uniform float opacity;

vec4 screenBlend(vec4 base, vec4 blend, float blendOpacity)
{
    // Screen blend: 1 - (1 - base) * (1 - blend)
    // This creates an additive-like effect where colors lighten
    vec3 screenResult = vec3(1.0) - (vec3(1.0) - base.rgb) * (vec3(1.0) - blend.rgb);
    // Mix with base based on blend alpha and layer opacity
    float effectiveAlpha = blend.a * blendOpacity;
    vec3 finalRgb = mix(base.rgb, screenResult, effectiveAlpha);
    return vec4(finalRgb, max(base.a, effectiveAlpha));
}

vec4 multiplyBlend(vec4 base, vec4 blend, float blendOpacity)
{
    vec3 result = base.rgb * blend.rgb;
    float effectiveAlpha = blend.a * blendOpacity;
    return vec4(mix(base.rgb, result, effectiveAlpha), max(base.a, effectiveAlpha));
}

vec4 overlayBlend(vec4 base, vec4 blend, float blendOpacity)
{
    vec3 result;
    for (int i = 0; i < 3; i++)
    {
        if (base[i] < 0.5)
            result[i] = 2.0 * base[i] * blend[i];
        else
            result[i] = 1.0 - 2.0 * (1.0 - base[i]) * (1.0 - blend[i]);
    }
    float effectiveAlpha = blend.a * blendOpacity;
    return vec4(mix(base.rgb, result, effectiveAlpha), max(base.a, effectiveAlpha));
}

void main()
{
    vec4 base = texture(baseTexture, TexCoord);
    vec4 blend = texture(blendTexture, TexCoord);

    if (blendMode == 0) // Normal
    {
        float effectiveAlpha = blend.a * opacity;
        FragColor = vec4(mix(base.rgb, blend.rgb, effectiveAlpha), max(base.a, effectiveAlpha));
    }
    else if (blendMode == 1) // Screen
    {
        FragColor = screenBlend(base, blend, opacity);
    }
    else if (blendMode == 2) // Multiply
    {
        FragColor = multiplyBlend(base, blend, opacity);
    }
    else if (blendMode == 3) // Overlay
    {
        FragColor = overlayBlend(base, blend, opacity);
    }
    else
    {
        FragColor = base;
    }
}
";

            _screenBlendShader = CreateShaderProgram(vertexSource, fragmentSource);

            // Cache uniform locations to avoid GetUniformLocation calls in render loop
            _screenBlendBaseTextureLoc = GL.GetUniformLocation(_screenBlendShader, "baseTexture");
            _screenBlendBlendTextureLoc = GL.GetUniformLocation(_screenBlendShader, "blendTexture");
            _screenBlendModeLoc = GL.GetUniformLocation(_screenBlendShader, "blendMode");
            _screenBlendOpacityLoc = GL.GetUniformLocation(_screenBlendShader, "opacity");
        }

        private void InitializeQuadVAO()
        {
            // Full-screen quad for compositing
            float[] quadVertices = {
                // positions   // texCoords
                -1.0f,  1.0f,  0.0f, 1.0f,
                -1.0f, -1.0f,  0.0f, 0.0f,
                 1.0f, -1.0f,  1.0f, 0.0f,

                -1.0f,  1.0f,  0.0f, 1.0f,
                 1.0f, -1.0f,  1.0f, 0.0f,
                 1.0f,  1.0f,  1.0f, 1.0f
            };

            _quadVao = GL.GenVertexArray();
            _quadVbo = GL.GenBuffer();

            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(0);
        }

        private int CreateShaderProgram(string vertexSource, string fragmentSource)
        {
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompilation(vertexShader, "vertex");

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompilation(fragmentShader, "fragment");

            int program = GL.CreateProgram();
            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);
            CheckProgramLinking(program);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return program;
        }

        private void CheckShaderCompilation(int shader, string type)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                throw new Exception($"{type} shader compilation failed: {log}");
            }
        }

        private void CheckProgramLinking(int program)
        {
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
            if (success == 0)
            {
                string log = GL.GetProgramInfoLog(program);
                throw new Exception($"Shader program linking failed: {log}");
            }
        }

        private void GlControl_Resize(object sender, EventArgs e)
        {
            if (!_glInitialized) return;

            _glControl.MakeCurrent();
            GL.Viewport(0, 0, _glControl.Width, _glControl.Height);

            // Recreate framebuffers if size changed
            if (_glControl.Width != _lastWidth || _glControl.Height != _lastHeight)
            {
                _lastWidth = _glControl.Width;
                _lastHeight = _glControl.Height;
                CreateFramebuffers();
            }
        }

        private void CreateFramebuffers()
        {
            if (_lastWidth <= 0 || _lastHeight <= 0) return;

            // Delete old framebuffers
            if (_layerFbo != 0) GL.DeleteFramebuffer(_layerFbo);
            if (_layerTexture != 0) GL.DeleteTexture(_layerTexture);
            if (_compositeFboA != 0) GL.DeleteFramebuffer(_compositeFboA);
            if (_compositeTextureA != 0) GL.DeleteTexture(_compositeTextureA);
            if (_compositeFboB != 0) GL.DeleteFramebuffer(_compositeFboB);
            if (_compositeTextureB != 0) GL.DeleteTexture(_compositeTextureB);

            // Create layer render target
            _layerTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _layerTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _lastWidth, _lastHeight, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            _layerFbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _layerFbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _layerTexture, 0);

            // Create composite render target A (ping-pong buffer)
            _compositeTextureA = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _compositeTextureA);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _lastWidth, _lastHeight, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            _compositeFboA = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositeFboA);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _compositeTextureA, 0);

            // Create composite render target B (ping-pong buffer)
            _compositeTextureB = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _compositeTextureB);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _lastWidth, _lastHeight, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            _compositeFboB = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositeFboB);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _compositeTextureB, 0);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void GlControl_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            if (!_glInitialized) return;

            _glControl.MakeCurrent();

            // Detect view changes for optimization
            bool viewChanged = Math.Abs(_lastZoom - Zoom) > 0.001 ||
                              Math.Abs(_lastPanX - PanX) > 0.1 ||
                              Math.Abs(_lastPanY - PanY) > 0.1;
            if (viewChanged)
            {
                _lastZoom = Zoom;
                _lastPanX = PanX;
                _lastPanY = PanY;
                _viewChanged = true;
            }

            // Update view matrices
            UpdateMatrices();

            // Clear with background color
            var bg = BackgroundColor;
            GL.ClearColor(bg.R / 255f, bg.G / 255f, bg.B / 255f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            // Render layers
            if (GerberLayers != null && GerberLayers.Count > 0)
            {
                // Use fast path (no screen blend) for better performance during interaction
                if (UseScreenBlend && !_isPanning && !_isSelecting)
                {
                    RenderLayersWithScreenBlend();
                }
                else
                {
                    // Fast path: simple alpha blending, much faster
                    RenderLayersNormal();
                }
            }

            // Render grid
            if (ShowGrid)
            {
                RenderGrid();
            }

            // Render selection rectangle if active
            if (_isSelecting && !_selectionRect.IsEmpty)
            {
                RenderSelectionRect();
            }

            _glControl.SwapBuffers();
        }

        private void RenderSelectionRect()
        {
            // Disable depth test and enable blending for overlay
            GL.UseProgram(0);

            // Use screen-space coordinates for selection rectangle
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0, _glControl.Width, _glControl.Height, 0, -1, 1);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();

            // Draw selection rectangle fill
            GL.Begin(PrimitiveType.Quads);
            GL.Color4(0.3f, 0.5f, 0.8f, 0.2f);
            GL.Vertex2(_selectionRect.Left, _selectionRect.Top);
            GL.Vertex2(_selectionRect.Right, _selectionRect.Top);
            GL.Vertex2(_selectionRect.Right, _selectionRect.Bottom);
            GL.Vertex2(_selectionRect.Left, _selectionRect.Bottom);
            GL.End();

            // Draw selection rectangle border
            GL.Begin(PrimitiveType.LineLoop);
            GL.Color4(0.3f, 0.5f, 0.8f, 0.8f);
            GL.Vertex2(_selectionRect.Left, _selectionRect.Top);
            GL.Vertex2(_selectionRect.Right, _selectionRect.Top);
            GL.Vertex2(_selectionRect.Right, _selectionRect.Bottom);
            GL.Vertex2(_selectionRect.Left, _selectionRect.Bottom);
            GL.End();

            // Restore matrices
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();
        }

        private void UpdateMatrices()
        {
            float width = _glControl.Width;
            float height = _glControl.Height;

            if (width <= 0 || height <= 0) return;

            // Match CPU coordinate system (DesignCanvas):
            // CPU: screenX = world.X * Zoom + PanX
            // CPU: screenY = ActualHeight - (world.Y * Zoom + PanY)
            //
            // For GPU, we need to compute the world bounds that map to the screen.
            // From CPU transform: world.X = (screenX - PanX) / Zoom
            // At screenX = 0: worldLeft = -PanX / Zoom
            // At screenX = width: worldRight = (width - PanX) / Zoom
            //
            // For Y: world.Y = (ActualHeight - screenY - PanY) / Zoom
            // At screenY = 0: worldTop = (height - PanY) / Zoom
            // At screenY = height: worldBottom = -PanY / Zoom

            float worldLeft = (float)(-PanX / Zoom);
            float worldRight = (float)((width - PanX) / Zoom);
            float worldBottom = (float)(-PanY / Zoom);
            float worldTop = (float)((height - PanY) / Zoom);

            _projection = Matrix4.CreateOrthographicOffCenter(
                worldLeft, worldRight,
                worldBottom, worldTop,
                -1, 1);

            _view = Matrix4.Identity;
        }

        private void RenderLayersNormal()
        {
            // Get visible bounds
            var visibleBounds = GetVisibleBounds();

            if (_useModernPipeline && _batchRenderer != null)
            {
                // Set view bounds for frustum culling
                _batchRenderer.SetViewBounds(visibleBounds, Zoom);

                // Modern batched rendering
                _batchRenderer.BeginFrame();

                foreach (var layer in GerberLayers)
                {
                    if (!layer.IsVisible) continue;
                    RenderLayerBatched(layer, visibleBounds);
                }

                _batchRenderer.Render(_projection, _view);
                _lastRenderStats = _batchRenderer.EndFrame();
            }
            else
            {
                // Legacy fixed-function pipeline for immediate mode rendering
                GL.UseProgram(0);

                // Set up matrices for fixed-function pipeline
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadMatrix(ref _projection);
                GL.MatrixMode(MatrixMode.Modelview);
                GL.LoadMatrix(ref _view);

                foreach (var layer in GerberLayers)
                {
                    if (!layer.IsVisible) continue;
                    RenderLayer(layer, visibleBounds);
                }
            }
        }

        private void RenderLayersWithScreenBlend()
        {
            if (_layerFbo == 0 || _compositeFboA == 0 || _compositeFboB == 0) return;

            // Count visible layers to optimize rendering
            int visibleLayerCount = 0;
            foreach (var layer in GerberLayers)
            {
                if (layer.IsVisible) visibleLayerCount++;
            }

            // If only one visible layer, skip compositing and render directly
            if (visibleLayerCount <= 1)
            {
                RenderLayersNormal();
                return;
            }

            // Initialize composite A with background
            _useCompositeA = true;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositeFboA);
            var bg = BackgroundColor;
            GL.ClearColor(bg.R / 255f, bg.G / 255f, bg.B / 255f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            var visibleBounds = GetVisibleBounds();

            // Set view bounds for frustum culling (once before all layers)
            if (_useModernPipeline && _batchRenderer != null)
            {
                _batchRenderer.SetViewBounds(visibleBounds, Zoom);
            }

            // Use cached uniform locations (initialized in InitializeScreenBlendShader)
            // This avoids expensive GL.GetUniformLocation calls in the render loop

            foreach (var layer in GerberLayers)
            {
                if (!layer.IsVisible) continue;

                // Render layer to layer FBO
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _layerFbo);
                GL.ClearColor(0, 0, 0, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit);

                if (_useModernPipeline && _batchRenderer != null)
                {
                    // Modern batched rendering
                    _batchRenderer.BeginFrame();
                    RenderLayerBatched(layer, visibleBounds);
                    _batchRenderer.Render(_projection, _view);
                    _lastRenderStats = _batchRenderer.EndFrame();
                }
                else
                {
                    // Use fixed-function pipeline for immediate mode rendering
                    GL.UseProgram(0);
                    GL.MatrixMode(MatrixMode.Projection);
                    GL.LoadMatrix(ref _projection);
                    GL.MatrixMode(MatrixMode.Modelview);
                    GL.LoadMatrix(ref _view);

                    RenderLayer(layer, visibleBounds);
                }

                // Ping-pong: read from current composite, write to other
                int srcTexture = _useCompositeA ? _compositeTextureA : _compositeTextureB;
                int dstFbo = _useCompositeA ? _compositeFboB : _compositeFboA;

                // Blend layer onto composite using screen blend
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, dstFbo);
                GL.UseProgram(_screenBlendShader);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, srcTexture);
                GL.Uniform1(_screenBlendBaseTextureLoc, 0);

                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, _layerTexture);
                GL.Uniform1(_screenBlendBlendTextureLoc, 1);

                GL.Uniform1(_screenBlendModeLoc, 1); // Screen blend
                GL.Uniform1(_screenBlendOpacityLoc, (float)layer.Opacity);

                GL.BindVertexArray(_quadVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

                // Swap buffers for next iteration
                _useCompositeA = !_useCompositeA;
            }

            // Final result is in the buffer we just wrote to (which is now the "current" one after swap)
            int finalTexture = _useCompositeA ? _compositeTextureA : _compositeTextureB;

            // Blit composite to screen
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.UseProgram(_screenBlendShader);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, finalTexture);
            GL.Uniform1(_screenBlendBaseTextureLoc, 0);

            // Use a 1x1 transparent texture for blend instead of null (more reliable)
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _layerTexture); // Reuse layer texture
            GL.Uniform1(_screenBlendModeLoc, 0); // Normal (just copy)
            GL.Uniform1(_screenBlendOpacityLoc, 0.0f); // Zero opacity = pure passthrough

            GL.BindVertexArray(_quadVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        private void RenderLayer(GerberLayer layer, Rect visibleBounds)
        {
            if (layer.Primitives == null || layer.Primitives.Count == 0)
                return;

            // Build quadtree if needed
            if (!_layerQuadtrees.ContainsKey(layer.Id))
            {
                BuildLayerQuadtreeAsync(layer);
            }

            // Get visible primitives
            List<GerberPrimitive> visiblePrimitives;
            if (_layerQuadtrees.TryGetValue(layer.Id, out var quadtree))
            {
                var wpfRect = new System.Windows.Rect(visibleBounds.X, visibleBounds.Y, visibleBounds.Width, visibleBounds.Height);
                visiblePrimitives = quadtree.QueryRect(wpfRect);
            }
            else
            {
                visiblePrimitives = layer.Primitives;
            }

            // Get layer color
            var color = GetColorFromArgb(layer.ColorArgb);

            // Minimum size for LOD filtering
            float minSize = (float)(0.5 / Zoom);

            // Batch render primitives by type
            foreach (var prim in visiblePrimitives)
            {
                // Skip clear/negative primitives in rendering for now (they need special handling)
                if (!prim.IsDark)
                    continue;

                // LOD culling
                float size = (float)Math.Max(prim.Width, prim.Height);
                if (size < minSize && prim.Type != GerberPrimitiveType.Line && prim.Type != GerberPrimitiveType.Arc)
                    continue;

                RenderPrimitive(prim, color, (float)layer.Opacity);
            }
        }

        private void RenderLayerBatched(GerberLayer layer, Rect visibleBounds)
        {
            if (layer.Primitives == null || layer.Primitives.Count == 0)
                return;

            // Get layer color as Vector4
            var color = GetColorFromArgb(layer.ColorArgb);
            color.W *= (float)layer.Opacity;

            // Check if we have a valid geometry cache for this layer
            LayerGeometryCache cache;
            bool needsRebuild = false;

            lock (_cacheLock)
            {
                if (!_layerGeometryCache.TryGetValue(layer.Id, out cache))
                {
                    needsRebuild = true;
                }
                else if (!cache.IsValid || cache.PrimitiveCount != layer.Primitives.Count)
                {
                    needsRebuild = true;
                }
            }

            // Build cache if needed - ASYNC to prevent UI freeze
            if (needsRebuild)
            {
                // Check if already building in background
                lock (_cacheLock)
                {
                    if (_pendingCacheBuilds.Contains(layer.Id))
                    {
                        // Already building - skip rendering this frame
                        return;
                    }
                    _pendingCacheBuilds.Add(layer.Id);
                }

                // Snapshot data needed for background thread (avoid threading issues)
                var layerId = layer.Id;
                var primitivesSnapshot = new List<GerberPrimitive>(layer.Primitives);
                var bounds = layer.Bounds;
                var layerColorArgb = layer.ColorArgb;
                var layerOpacity = layer.Opacity;

                Task.Run(() =>
                {
                    try
                    {
                        // Heavy math happens here (off UI thread)
                        var newCache = new LayerGeometryCache { LayerId = layerId };
                        BuildLayerGeometryCacheInternal(primitivesSnapshot, bounds, newCache, layerColorArgb, layerOpacity);

                        // Update UI on main thread
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Check if control was disposed while background task was running
                            if (_isDisposed) return;

                            lock (_cacheLock)
                            {
                                _layerGeometryCache[layerId] = newCache;

                                // Invalidate static GPU buffers when cache is rebuilt
                                _batchRenderer?.InvalidateStaticBuffers(layerId);

                                // Invalidate static layer renderer when cache is rebuilt
                                if (_staticLayerRenderers.TryGetValue(layerId, out var oldRenderer))
                                {
                                    oldRenderer.Dispose();
                                    _staticLayerRenderers.Remove(layerId);
                                }

                                _pendingCacheBuilds.Remove(layerId);
                            }
                            Invalidate(); // Trigger redraw
                        }));
                    }
                    catch (Exception ex)
                    {
                        // LOG THE FULL ERROR so you can see it in Visual Studio Output window
                        System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR building cache for layer {layerId}: {ex}");

                        // BREAK THE LOOP: Mark the cache as valid but empty so we stop trying to rebuild it
                        // Without this, the next frame would see "no cache" and try again = infinite retry loop
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Check if control was disposed while background task was running
                            if (_isDisposed) return;

                            lock (_cacheLock)
                            {
                                // Create a "Dummy" empty cache to stop the infinite retry loop
                                var failedCache = new LayerGeometryCache
                                {
                                    LayerId = layerId,
                                    IsValid = true, // Lie to the system to stop retrying
                                    PrimitiveCount = 0,
                                    Circles = new List<CachedCircle>(),
                                    Rectangles = new List<CachedRectangle>(),
                                    Lines = new List<CachedLine>(),
                                    Polygons = new List<CachedPolygon>()
                                };
                                _layerGeometryCache[layerId] = failedCache;

                                _pendingCacheBuilds.Remove(layerId);
                            }
                        }));
                    }
                });

                // Skip rendering this layer for this frame (will "pop in" when ready)
                return;
            }

            // Get the cache again (it may have been set while we were checking)
            lock (_cacheLock)
            {
                if (!_layerGeometryCache.TryGetValue(layer.Id, out cache) || !cache.IsValid)
                {
                    return; // Cache not ready yet
                }
            }

            // Upload static meshes to GPU if needed (only once per layer)
            UploadStaticMeshesIfNeeded(layer.Id, cache);

            // Aggressive LOD filtering during interaction for smoother panning/zooming
            // Skip small primitives that won't be visible at current zoom
            float minVisibleSize = (_isPanning || _isSelecting) ? (float)(3.0 / Zoom) : (float)(0.5 / Zoom);
            float viewLeft = (float)visibleBounds.Left;
            float viewRight = (float)visibleBounds.Right;
            float viewBottom = (float)visibleBounds.Top; // Note: Rect uses Top for min Y
            float viewTop = (float)visibleBounds.Bottom;

            // Use static layer renderer for circles/rectangles (zero per-primitive C# iteration)
            if (_useStaticRendering && cache.Circles.Count + cache.Rectangles.Count > 0)
            {
                // Check if we need to rebuild static renderer due to COLOR change
                // (Opacity is now handled via shader uniform, so no rebuild needed for opacity changes)
                if (_staticLayerRenderers.TryGetValue(layer.Id, out var existingRenderer))
                {
                    // Only rebuild if color changed (opacity is handled via uniform)
                    if (cache.ColorArgb != layer.ColorArgb)
                    {
                        existingRenderer.Dispose();
                        _staticLayerRenderers.Remove(layer.Id);
                        // Update cache color info
                        cache.ColorArgb = layer.ColorArgb;
                    }
                }

                // Initialize static renderer if needed (done ONCE per layer)
                if (!_staticLayerRenderers.TryGetValue(layer.Id, out var staticRenderer))
                {
                    staticRenderer = new StaticLayerRenderer(
                        layer.Id,
                        _batchRenderer.GeometryCache,
                        _batchRenderer.InstancedShader,
                        _batchRenderer.InstancedProjectionLocation,
                        _batchRenderer.InstancedViewLocation,
                        _batchRenderer.InstancedOpacityLocation);

                    // Get layer bounds for tile partitioning
                    // Note: color.W (opacity) is NOT baked into instance data anymore
                    // It's applied via shader uniform in Render() for instant opacity changes
                    var bounds = layer.Bounds;
                    staticRenderer.Initialize(
                        cache.Circles,
                        cache.Rectangles,
                        (float)bounds.X, (float)bounds.Y,
                        (float)bounds.Width, (float)bounds.Height,
                        color);

                    _staticLayerRenderers[layer.Id] = staticRenderer;
                }

                // Render circles and rectangles using pre-uploaded GPU buffers
                // Pass layer opacity as uniform - allows instant opacity changes without rebuild
                staticRenderer.Render(_projection, _view, viewLeft, viewBottom, viewRight, viewTop, minVisibleSize, _batchRenderer.StateCache, (float)layer.Opacity);

                // Still render lines/polygons using existing static mesh system
                RenderStaticMeshesOnly(cache, color);
            }
            // Use tile-based spatial index for O(visible) instead of O(N) iteration (fallback)
            else if (cache.TileIndex != null)
            {
                RenderWithTileIndex(cache, color, minVisibleSize, viewLeft, viewBottom, viewRight, viewTop);
            }
            else
            {
                // Fallback to O(N) iteration if no tile index
                RenderWithFullIteration(cache, color, minVisibleSize, viewLeft, viewBottom, viewRight, viewTop);
            }
        }

        /// <summary>
        /// Renders only static meshes (lines/polygons) - used when StaticLayerRenderer handles circles/rectangles.
        /// </summary>
        private void RenderStaticMeshesOnly(LayerGeometryCache cache, OpenTK.Vector4 color)
        {
            // Render lines using static mesh (no per-frame geometry rebuild)
            // Skip during panning for performance
            if (!_isPanning && cache.LineMesh != null && cache.LineMesh.HasData)
            {
                _batchRenderer.RenderStaticLineMesh(cache.LayerId, color);
            }

            // Render polygons using static mesh (no per-frame geometry rebuild)
            // Skip during panning for performance
            if (!_isPanning && cache.PolygonMesh != null && cache.PolygonMesh.HasData)
            {
                _batchRenderer.RenderStaticPolygonMesh(cache.LayerId, color);
            }
        }

        /// <summary>
        /// O(visible) rendering using tile-based spatial index.
        /// Circles/rectangles: iterates only visible tiles
        /// Lines/polygons: uses pre-triangulated static meshes (single draw call)
        /// </summary>
        private void RenderWithTileIndex(LayerGeometryCache cache, OpenTK.Vector4 color,
            float minVisibleSize, float viewLeft, float viewBottom, float viewRight, float viewTop)
        {
            var tileIndex = cache.TileIndex;
            tileIndex.GetVisibleTileRange(viewLeft, viewBottom, viewRight, viewTop,
                out int minTileX, out int minTileY, out int maxTileX, out int maxTileY);

            // Use HashSets to avoid duplicate rendering (primitives can span tiles)
            var renderedCircles = new HashSet<int>();
            var renderedRects = new HashSet<int>();

            // Iterate only visible tiles for circles and rectangles
            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    int tileIdx = ty * tileIndex.TilesX + tx;

                    // Render circles in this tile
                    foreach (int idx in tileIndex.CircleIndices[tileIdx])
                    {
                        if (renderedCircles.Contains(idx)) continue;
                        renderedCircles.Add(idx);

                        var circle = cache.Circles[idx];
                        float r = circle.Radius;

                        // LOD filtering
                        if (r * 2 < minVisibleSize)
                            continue;

                        // Precise bounds check (tile may extend beyond view)
                        if (circle.X + r < viewLeft || circle.X - r > viewRight ||
                            circle.Y + r < viewBottom || circle.Y - r > viewTop)
                            continue;

                        _batchRenderer.AddCircle(circle.X, circle.Y, circle.Radius, color);
                    }

                    // Render rectangles in this tile
                    foreach (int idx in tileIndex.RectangleIndices[tileIdx])
                    {
                        if (renderedRects.Contains(idx)) continue;
                        renderedRects.Add(idx);

                        var rect = cache.Rectangles[idx];
                        float hw = rect.Width / 2;
                        float hh = rect.Height / 2;

                        // LOD filtering
                        if (Math.Max(rect.Width, rect.Height) < minVisibleSize)
                            continue;

                        // Precise bounds check
                        if (rect.X + hw < viewLeft || rect.X - hw > viewRight ||
                            rect.Y + hh < viewBottom || rect.Y - hh > viewTop)
                            continue;

                        _batchRenderer.AddRectangle(rect.X, rect.Y, rect.Width, rect.Height, color);
                    }
                }
            }

            // Render lines using static mesh (no per-frame geometry rebuild)
            // Skip during panning for performance
            if (!_isPanning && cache.LineMesh != null && cache.LineMesh.HasData)
            {
                _batchRenderer.RenderStaticLineMesh(cache.LayerId, color);
            }

            // Render polygons using static mesh (no per-frame geometry rebuild)
            // Skip during panning for performance
            if (!_isPanning && cache.PolygonMesh != null && cache.PolygonMesh.HasData)
            {
                _batchRenderer.RenderStaticPolygonMesh(cache.LayerId, color);
            }
        }

        /// <summary>
        /// Fallback O(N) rendering when tile index is not available.
        /// </summary>
        private void RenderWithFullIteration(LayerGeometryCache cache, OpenTK.Vector4 color,
            float minVisibleSize, float viewLeft, float viewBottom, float viewRight, float viewTop)
        {
            // Fast path: replay cached geometry to batch renderer with frustum culling
            foreach (var circle in cache.Circles)
            {
                // Quick bounds check (frustum culling)
                float r = circle.Radius;
                if (circle.X + r < viewLeft || circle.X - r > viewRight ||
                    circle.Y + r < viewBottom || circle.Y - r > viewTop)
                    continue;

                // LOD filtering
                if (r * 2 < minVisibleSize)
                    continue;

                _batchRenderer.AddCircle(circle.X, circle.Y, circle.Radius, color);
            }

            foreach (var rect in cache.Rectangles)
            {
                float hw = rect.Width / 2;
                float hh = rect.Height / 2;

                // Quick bounds check
                if (rect.X + hw < viewLeft || rect.X - hw > viewRight ||
                    rect.Y + hh < viewBottom || rect.Y - hh > viewTop)
                    continue;

                // LOD filtering
                if (Math.Max(rect.Width, rect.Height) < minVisibleSize)
                    continue;

                _batchRenderer.AddRectangle(rect.X, rect.Y, rect.Width, rect.Height, color);
            }

            // Lines and polygons - use static meshes for no per-frame geometry rebuild
            // Skip during panning for performance
            if (!_isPanning)
            {
                if (cache.LineMesh != null && cache.LineMesh.HasData)
                {
                    _batchRenderer.RenderStaticLineMesh(cache.LayerId, color);
                }

                if (cache.PolygonMesh != null && cache.PolygonMesh.HasData)
                {
                    _batchRenderer.RenderStaticPolygonMesh(cache.LayerId, color);
                }
            }
        }

        private void BuildLayerGeometryCache(GerberLayer layer, LayerGeometryCache cache)
        {
            cache.Clear();
            cache.PrimitiveCount = layer.Primitives.Count;
            cache.ColorArgb = layer.ColorArgb;
            cache.Opacity = layer.Opacity;

            // Pre-allocate lists based on expected counts
            int estimatedCount = layer.Primitives.Count;
            cache.Circles = new List<CachedCircle>(estimatedCount / 2);
            cache.Rectangles = new List<CachedRectangle>(estimatedCount / 4);
            cache.Lines = new List<CachedLine>(estimatedCount / 4);
            cache.Polygons = new List<CachedPolygon>(estimatedCount / 10);

            // Build cache from primitives (done once, not every frame)
            foreach (var prim in layer.Primitives)
            {
                // Skip clear/negative primitives
                if (!prim.IsDark)
                    continue;

                switch (prim.Type)
                {
                    case GerberPrimitiveType.Circle:
                    case GerberPrimitiveType.Flash:
                        cache.Circles.Add(new CachedCircle
                        {
                            X = (float)prim.X,
                            Y = (float)prim.Y,
                            Radius = (float)(prim.Width / 2)
                        });
                        break;

                    case GerberPrimitiveType.Rectangle:
                        cache.Rectangles.Add(new CachedRectangle
                        {
                            X = (float)prim.X,
                            Y = (float)prim.Y,
                            Width = (float)prim.Width,
                            Height = (float)prim.Height
                        });
                        break;

                    case GerberPrimitiveType.Obround:
                        // Decompose obround into rect + 2 circles (cached)
                        float hw = (float)(prim.Width / 2);
                        float hh = (float)(prim.Height / 2);
                        if (prim.Width > prim.Height)
                        {
                            float radius = hh;
                            float rectHw = hw - radius;
                            cache.Rectangles.Add(new CachedRectangle
                            {
                                X = (float)prim.X,
                                Y = (float)prim.Y,
                                Width = rectHw * 2,
                                Height = (float)prim.Height
                            });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X - rectHw, Y = (float)prim.Y, Radius = radius });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X + rectHw, Y = (float)prim.Y, Radius = radius });
                        }
                        else
                        {
                            float radius = hw;
                            float rectHh = hh - radius;
                            cache.Rectangles.Add(new CachedRectangle
                            {
                                X = (float)prim.X,
                                Y = (float)prim.Y,
                                Width = (float)prim.Width,
                                Height = rectHh * 2
                            });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X, Y = (float)prim.Y - rectHh, Radius = radius });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X, Y = (float)prim.Y + rectHh, Radius = radius });
                        }
                        break;

                    case GerberPrimitiveType.Line:
                        if (prim.Points != null && prim.Points.Count >= 2)
                        {
                            var p1 = prim.Points[0];
                            var p2 = prim.Points[1];
                            cache.Lines.Add(new CachedLine
                            {
                                X1 = (float)p1.X,
                                Y1 = (float)p1.Y,
                                X2 = (float)p2.X,
                                Y2 = (float)p2.Y,
                                Width = (float)prim.Width
                            });
                        }
                        break;

                    case GerberPrimitiveType.Contour:
                    case GerberPrimitiveType.Polygon:
                        if (prim.Points != null && prim.Points.Count >= 3)
                        {
                            cache.Polygons.Add(new CachedPolygon { Points = prim.Points });
                        }
                        break;
                }
            }

            cache.CircleCount = cache.Circles.Count;
            cache.RectangleCount = cache.Rectangles.Count;
            cache.LineCount = cache.Lines.Count;
            cache.PolygonCount = cache.Polygons.Count;

            // Build tile-based spatial index for O(visible) rendering
            BuildTileIndex(cache, layer.Bounds);

            // Pre-triangulate lines and polygons for static GPU rendering
            BuildStaticLineMesh(cache);
            BuildStaticPolygonMesh(cache);

            cache.IsValid = true;
        }

        /// <summary>
        /// Thread-safe version of BuildLayerGeometryCache that takes a snapshot of primitives.
        /// Can be called from a background thread without accessing UI-bound objects.
        /// </summary>
        private void BuildLayerGeometryCacheInternal(
            List<GerberPrimitive> primitives,
            Rect bounds,
            LayerGeometryCache cache,
            uint colorArgb,
            double opacity)
        {
            cache.Clear();
            cache.PrimitiveCount = primitives.Count;
            cache.ColorArgb = colorArgb;
            cache.Opacity = opacity;

            // Pre-allocate lists based on expected counts
            int estimatedCount = primitives.Count;
            cache.Circles = new List<CachedCircle>(estimatedCount / 2);
            cache.Rectangles = new List<CachedRectangle>(estimatedCount / 4);
            cache.Lines = new List<CachedLine>(estimatedCount / 4);
            cache.Polygons = new List<CachedPolygon>(estimatedCount / 10);

            // Build cache from primitives (done once, not every frame)
            foreach (var prim in primitives)
            {
                // Skip clear/negative primitives
                if (!prim.IsDark)
                    continue;

                switch (prim.Type)
                {
                    case GerberPrimitiveType.Circle:
                    case GerberPrimitiveType.Flash:
                        cache.Circles.Add(new CachedCircle
                        {
                            X = (float)prim.X,
                            Y = (float)prim.Y,
                            Radius = (float)(prim.Width / 2)
                        });
                        break;

                    case GerberPrimitiveType.Rectangle:
                        cache.Rectangles.Add(new CachedRectangle
                        {
                            X = (float)prim.X,
                            Y = (float)prim.Y,
                            Width = (float)prim.Width,
                            Height = (float)prim.Height
                        });
                        break;

                    case GerberPrimitiveType.Obround:
                        // Decompose obround into rect + 2 circles (cached)
                        float hw = (float)(prim.Width / 2);
                        float hh = (float)(prim.Height / 2);
                        if (prim.Width > prim.Height)
                        {
                            float radius = hh;
                            float rectHw = hw - radius;
                            cache.Rectangles.Add(new CachedRectangle
                            {
                                X = (float)prim.X,
                                Y = (float)prim.Y,
                                Width = rectHw * 2,
                                Height = (float)prim.Height
                            });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X - rectHw, Y = (float)prim.Y, Radius = radius });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X + rectHw, Y = (float)prim.Y, Radius = radius });
                        }
                        else
                        {
                            float radius = hw;
                            float rectHh = hh - radius;
                            cache.Rectangles.Add(new CachedRectangle
                            {
                                X = (float)prim.X,
                                Y = (float)prim.Y,
                                Width = (float)prim.Width,
                                Height = rectHh * 2
                            });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X, Y = (float)prim.Y - rectHh, Radius = radius });
                            cache.Circles.Add(new CachedCircle { X = (float)prim.X, Y = (float)prim.Y + rectHh, Radius = radius });
                        }
                        break;

                    case GerberPrimitiveType.Line:
                        if (prim.Points != null && prim.Points.Count >= 2)
                        {
                            var p1 = prim.Points[0];
                            var p2 = prim.Points[1];
                            cache.Lines.Add(new CachedLine
                            {
                                X1 = (float)p1.X,
                                Y1 = (float)p1.Y,
                                X2 = (float)p2.X,
                                Y2 = (float)p2.Y,
                                Width = (float)prim.Width
                            });
                        }
                        break;

                    case GerberPrimitiveType.Contour:
                    case GerberPrimitiveType.Polygon:
                        if (prim.Points != null && prim.Points.Count >= 3)
                        {
                            // Copy points to avoid threading issues
                            cache.Polygons.Add(new CachedPolygon { Points = new List<Point>(prim.Points) });
                        }
                        break;
                }
            }

            cache.CircleCount = cache.Circles.Count;
            cache.RectangleCount = cache.Rectangles.Count;
            cache.LineCount = cache.Lines.Count;
            cache.PolygonCount = cache.Polygons.Count;

            // Build tile-based spatial index for O(visible) rendering
            BuildTileIndex(cache, bounds);

            // Pre-triangulate lines and polygons for static GPU rendering
            BuildStaticLineMesh(cache);
            BuildStaticPolygonMesh(cache);

            cache.IsValid = true;
        }

        /// <summary>
        /// Pre-triangulate all lines into a static mesh (built once, not per-frame).
        /// Lines are converted to quads + circle caps.
        /// </summary>
        private void BuildStaticLineMesh(LayerGeometryCache cache)
        {
            if (cache.Lines.Count == 0) return;

            const int CIRCLE_SEGMENTS = 32;

            // Calculate required buffer sizes
            // Each line: 4 vertices for quad + (CIRCLE_SEGMENTS + 1) * 2 for caps
            // Each line: 6 indices for quad + CIRCLE_SEGMENTS * 3 * 2 for caps
            int verticesPerLine = 4 + (CIRCLE_SEGMENTS + 1) * 2;
            int indicesPerLine = 6 + CIRCLE_SEGMENTS * 3 * 2;

            var mesh = new StaticMeshData();
            mesh.Vertices = new float[cache.Lines.Count * verticesPerLine * 2]; // x,y pairs
            mesh.Indices = new uint[cache.Lines.Count * indicesPerLine];

            int vIdx = 0;
            int iIdx = 0;

            foreach (var line in cache.Lines)
            {
                float dx = line.X2 - line.X1;
                float dy = line.Y2 - line.Y1;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.0001f) continue;

                float nx = -dy / len * line.Width / 2;
                float ny = dx / len * line.Width / 2;

                uint baseVertex = (uint)(vIdx / 2);

                // Line quad vertices
                mesh.Vertices[vIdx++] = line.X1 - nx;
                mesh.Vertices[vIdx++] = line.Y1 - ny;
                mesh.Vertices[vIdx++] = line.X1 + nx;
                mesh.Vertices[vIdx++] = line.Y1 + ny;
                mesh.Vertices[vIdx++] = line.X2 + nx;
                mesh.Vertices[vIdx++] = line.Y2 + ny;
                mesh.Vertices[vIdx++] = line.X2 - nx;
                mesh.Vertices[vIdx++] = line.Y2 - ny;

                // Quad indices
                mesh.Indices[iIdx++] = baseVertex;
                mesh.Indices[iIdx++] = baseVertex + 1;
                mesh.Indices[iIdx++] = baseVertex + 2;
                mesh.Indices[iIdx++] = baseVertex;
                mesh.Indices[iIdx++] = baseVertex + 2;
                mesh.Indices[iIdx++] = baseVertex + 3;

                // Start cap (circle)
                uint capBase = (uint)(vIdx / 2);
                float radius = line.Width / 2;

                // Center vertex
                mesh.Vertices[vIdx++] = line.X1;
                mesh.Vertices[vIdx++] = line.Y1;

                // Circle perimeter
                for (int i = 0; i < CIRCLE_SEGMENTS; i++)
                {
                    float angle = (float)(2 * Math.PI * i / CIRCLE_SEGMENTS);
                    mesh.Vertices[vIdx++] = line.X1 + radius * (float)Math.Cos(angle);
                    mesh.Vertices[vIdx++] = line.Y1 + radius * (float)Math.Sin(angle);
                }

                // Circle indices
                for (int i = 0; i < CIRCLE_SEGMENTS; i++)
                {
                    mesh.Indices[iIdx++] = capBase;
                    mesh.Indices[iIdx++] = capBase + (uint)(i + 1);
                    mesh.Indices[iIdx++] = capBase + (uint)((i + 1) % CIRCLE_SEGMENTS + 1);
                }

                // End cap (circle)
                capBase = (uint)(vIdx / 2);

                mesh.Vertices[vIdx++] = line.X2;
                mesh.Vertices[vIdx++] = line.Y2;

                for (int i = 0; i < CIRCLE_SEGMENTS; i++)
                {
                    float angle = (float)(2 * Math.PI * i / CIRCLE_SEGMENTS);
                    mesh.Vertices[vIdx++] = line.X2 + radius * (float)Math.Cos(angle);
                    mesh.Vertices[vIdx++] = line.Y2 + radius * (float)Math.Sin(angle);
                }

                for (int i = 0; i < CIRCLE_SEGMENTS; i++)
                {
                    mesh.Indices[iIdx++] = capBase;
                    mesh.Indices[iIdx++] = capBase + (uint)(i + 1);
                    mesh.Indices[iIdx++] = capBase + (uint)((i + 1) % CIRCLE_SEGMENTS + 1);
                }
            }

            mesh.VertexCount = vIdx / 2;
            mesh.IndexCount = iIdx;
            cache.LineMesh = mesh;
        }

        /// <summary>
        /// Pre-triangulate all polygons into a static mesh.
        /// Uses ear-clipping algorithm for correct concave polygon support.
        /// </summary>
        private void BuildStaticPolygonMesh(LayerGeometryCache cache)
        {
            if (cache.Polygons.Count == 0) return;

            // Maximum polygon size for full ear-clipping triangulation
            // Larger polygons use fast triangle fan (may look wrong for concave shapes but won't freeze)
            const int MAX_EAR_CLIP_VERTICES = 2000;

            // Use lists since ear clipping may produce varying triangle counts
            var allVertices = new List<float>();
            var allIndices = new List<uint>();

            foreach (var poly in cache.Polygons)
            {
                if (poly.Points.Count < 3) continue;

                uint baseVertex = (uint)(allVertices.Count / 2);

                // Add vertices
                foreach (var pt in poly.Points)
                {
                    allVertices.Add((float)pt.X);
                    allVertices.Add((float)pt.Y);
                }

                List<int> polyIndices;

                // SAFETY VALVE: If polygon is massive, use simple triangle fan (instant)
                // This prevents app freeze on complex ground planes with thousands of vertices
                // May render incorrectly for deeply concave shapes, but better than freezing
                if (poly.Points.Count > MAX_EAR_CLIP_VERTICES)
                {
                    // Simple triangle fan from first vertex - O(n) instead of O(n³)
                    polyIndices = new List<int>((poly.Points.Count - 2) * 3);
                    for (int i = 1; i < poly.Points.Count - 1; i++)
                    {
                        polyIndices.Add(0);
                        polyIndices.Add(i);
                        polyIndices.Add(i + 1);
                    }
                }
                else
                {
                    // Full ear clipping for high-quality concave polygon support
                    polyIndices = Triangulator.Triangulate(poly.Points);
                }

                // Add indices offset by the current base vertex
                foreach (int index in polyIndices)
                {
                    allIndices.Add(baseVertex + (uint)index);
                }
            }

            var mesh = new StaticMeshData();
            mesh.Vertices = allVertices.ToArray();
            mesh.Indices = allIndices.ToArray();
            mesh.VertexCount = allVertices.Count / 2;
            mesh.IndexCount = allIndices.Count;
            cache.PolygonMesh = mesh;
        }

        /// <summary>
        /// Upload static mesh data to GPU if not already uploaded.
        /// This is called once per layer, not per-frame.
        /// </summary>
        private void UploadStaticMeshesIfNeeded(string layerId, LayerGeometryCache cache)
        {
            // Upload static line mesh if needed
            if (cache.LineMesh != null && cache.LineMesh.HasData && cache.LineMesh.NeedsUpload)
            {
                if (!_batchRenderer.HasStaticLineMesh(layerId))
                {
                    _batchRenderer.UploadStaticLineMesh(layerId,
                        cache.LineMesh.Vertices, cache.LineMesh.VertexCount,
                        cache.LineMesh.Indices, cache.LineMesh.IndexCount);
                }
                cache.LineMesh.NeedsUpload = false;
            }

            // Upload static polygon mesh if needed
            if (cache.PolygonMesh != null && cache.PolygonMesh.HasData && cache.PolygonMesh.NeedsUpload)
            {
                if (!_batchRenderer.HasStaticPolygonMesh(layerId))
                {
                    _batchRenderer.UploadStaticPolygonMesh(layerId,
                        cache.PolygonMesh.Vertices, cache.PolygonMesh.VertexCount,
                        cache.PolygonMesh.Indices, cache.PolygonMesh.IndexCount);
                }
                cache.PolygonMesh.NeedsUpload = false;
            }
        }

        /// <summary>
        /// Build tile index for efficient viewport culling.
        /// </summary>
        private void BuildTileIndex(LayerGeometryCache cache, Rect bounds)
        {
            // Skip if no primitives
            int totalPrimitives = cache.CircleCount + cache.RectangleCount + cache.LineCount + cache.PolygonCount;
            if (totalPrimitives == 0)
                return;

            // Choose tile size based on bounds - aim for ~100 tiles per dimension max
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float tileSize = Math.Max(width, height) / 50.0f; // ~50x50 grid max
            tileSize = Math.Max(tileSize, 1.0f); // Minimum 1mm tiles

            cache.TileIndex = new TileIndex();
            cache.TileIndex.Initialize(
                (float)bounds.Left, (float)bounds.Top,
                (float)bounds.Right, (float)bounds.Bottom,
                tileSize);

            // Add circles to tile index
            for (int i = 0; i < cache.Circles.Count; i++)
            {
                var c = cache.Circles[i];
                cache.TileIndex.AddCircle(i, c.X, c.Y, c.Radius);
            }

            // Add rectangles to tile index
            for (int i = 0; i < cache.Rectangles.Count; i++)
            {
                var r = cache.Rectangles[i];
                cache.TileIndex.AddRectangle(i, r.X, r.Y, r.Width, r.Height);
            }

            // Add lines to tile index
            for (int i = 0; i < cache.Lines.Count; i++)
            {
                var l = cache.Lines[i];
                cache.TileIndex.AddLine(i, l.X1, l.Y1, l.X2, l.Y2, l.Width);
            }

            // Add polygons to tile index
            for (int i = 0; i < cache.Polygons.Count; i++)
            {
                cache.TileIndex.AddPolygon(i, cache.Polygons[i].Points);
            }
        }

        private void RenderPrimitive(GerberPrimitive prim, OpenTK.Vector4 color, float opacity)
        {
            color.W *= opacity;

            switch (prim.Type)
            {
                case GerberPrimitiveType.Circle:
                    RenderCircle((float)prim.X, (float)prim.Y, (float)(prim.Width / 2), color);
                    break;

                case GerberPrimitiveType.Rectangle:
                    RenderRectangle((float)prim.X, (float)prim.Y, (float)prim.Width, (float)prim.Height, color);
                    break;

                case GerberPrimitiveType.Obround:
                    RenderObround((float)prim.X, (float)prim.Y, (float)prim.Width, (float)prim.Height, color);
                    break;

                case GerberPrimitiveType.Line:
                    if (prim.Points != null && prim.Points.Count >= 2)
                    {
                        var p1 = prim.Points[0];
                        var p2 = prim.Points[1];
                        RenderLine((float)p1.X, (float)p1.Y, (float)p2.X, (float)p2.Y, (float)prim.Width, color);
                    }
                    break;

                case GerberPrimitiveType.Contour:
                case GerberPrimitiveType.Polygon:
                    if (prim.Points != null && prim.Points.Count >= 3)
                    {
                        RenderPolygon(prim.Points, color);
                    }
                    break;
            }
        }

        private void RenderCircle(float x, float y, float radius, OpenTK.Vector4 color)
        {
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Color4(color.X, color.Y, color.Z, color.W);
            GL.Vertex2(x, y);

            for (int i = 0; i <= CIRCLE_SEGMENTS; i++)
            {
                float angle = (float)(2 * Math.PI * i / CIRCLE_SEGMENTS);
                GL.Vertex2(x + radius * Math.Cos(angle), y + radius * Math.Sin(angle));
            }
            GL.End();
        }

        private void RenderRectangle(float x, float y, float width, float height, OpenTK.Vector4 color)
        {
            float hw = width / 2;
            float hh = height / 2;

            GL.Begin(PrimitiveType.Quads);
            GL.Color4(color.X, color.Y, color.Z, color.W);
            GL.Vertex2(x - hw, y - hh);
            GL.Vertex2(x + hw, y - hh);
            GL.Vertex2(x + hw, y + hh);
            GL.Vertex2(x - hw, y + hh);
            GL.End();
        }

        private void RenderObround(float x, float y, float width, float height, OpenTK.Vector4 color)
        {
            // Obround = rectangle with semicircle ends
            float hw = width / 2;
            float hh = height / 2;

            if (width > height)
            {
                float radius = hh;
                float rectHw = hw - radius;

                // Center rectangle
                GL.Begin(PrimitiveType.Quads);
                GL.Color4(color.X, color.Y, color.Z, color.W);
                GL.Vertex2(x - rectHw, y - hh);
                GL.Vertex2(x + rectHw, y - hh);
                GL.Vertex2(x + rectHw, y + hh);
                GL.Vertex2(x - rectHw, y + hh);
                GL.End();

                // End circles
                RenderCircle(x - rectHw, y, radius, color);
                RenderCircle(x + rectHw, y, radius, color);
            }
            else
            {
                float radius = hw;
                float rectHh = hh - radius;

                GL.Begin(PrimitiveType.Quads);
                GL.Color4(color.X, color.Y, color.Z, color.W);
                GL.Vertex2(x - hw, y - rectHh);
                GL.Vertex2(x + hw, y - rectHh);
                GL.Vertex2(x + hw, y + rectHh);
                GL.Vertex2(x - hw, y + rectHh);
                GL.End();

                RenderCircle(x, y - rectHh, radius, color);
                RenderCircle(x, y + rectHh, radius, color);
            }
        }

        private void RenderLine(float x1, float y1, float x2, float y2, float width, OpenTK.Vector4 color)
        {
            // Line with width = rotated rectangle
            float dx = x2 - x1;
            float dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001f) return;

            float nx = -dy / len * width / 2;
            float ny = dx / len * width / 2;

            GL.Begin(PrimitiveType.Quads);
            GL.Color4(color.X, color.Y, color.Z, color.W);
            GL.Vertex2(x1 - nx, y1 - ny);
            GL.Vertex2(x1 + nx, y1 + ny);
            GL.Vertex2(x2 + nx, y2 + ny);
            GL.Vertex2(x2 - nx, y2 - ny);
            GL.End();

            // Round caps
            float radius = width / 2;
            RenderCircle(x1, y1, radius, color);
            RenderCircle(x2, y2, radius, color);
        }

        private void RenderPolygon(IList<System.Windows.Point> points, OpenTK.Vector4 color)
        {
            if (points.Count < 3) return;

            // Simple triangle fan from first vertex (works for convex polygons)
            // For complex polygons, we'd need triangulation
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Color4(color.X, color.Y, color.Z, color.W);

            foreach (var pt in points)
            {
                GL.Vertex2(pt.X, pt.Y);
            }
            GL.End();
        }

        private void RenderGrid()
        {
            // Ensure fixed-function pipeline for grid rendering
            GL.UseProgram(0);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadMatrix(ref _projection);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref _view);

            var bounds = GetVisibleBounds();
            double spacing = GridSpacing;

            // Adaptive grid spacing based on zoom
            while (spacing * Zoom < 20)
                spacing *= 5;

            float startX = (float)(Math.Floor(bounds.Left / spacing) * spacing);
            float endX = (float)(Math.Ceiling(bounds.Right / spacing) * spacing);
            float startY = (float)(Math.Floor(bounds.Top / spacing) * spacing);
            float endY = (float)(Math.Ceiling(bounds.Bottom / spacing) * spacing);

            GL.Begin(PrimitiveType.Lines);
            GL.Color4(0.15f, 0.15f, 0.15f, 1.0f);

            // Vertical lines
            for (float x = startX; x <= endX; x += (float)spacing)
            {
                GL.Vertex2(x, startY);
                GL.Vertex2(x, endY);
            }

            // Horizontal lines
            for (float y = startY; y <= endY; y += (float)spacing)
            {
                GL.Vertex2(startX, y);
                GL.Vertex2(endX, y);
            }

            GL.End();

            // Origin cross
            GL.Begin(PrimitiveType.Lines);
            GL.Color4(0.4f, 0.1f, 0.1f, 1.0f);
            GL.Vertex2(-1000, 0);
            GL.Vertex2(1000, 0);
            GL.Vertex2(0, -1000);
            GL.Vertex2(0, 1000);
            GL.End();
        }

        private Rect GetVisibleBounds()
        {
            float width = _glControl.Width;
            float height = _glControl.Height;

            // Match CPU coordinate system:
            // worldLeft = -PanX / Zoom
            // worldRight = (width - PanX) / Zoom
            // worldBottom = -PanY / Zoom
            // worldTop = (height - PanY) / Zoom
            double worldLeft = -PanX / Zoom;
            double worldRight = (width - PanX) / Zoom;
            double worldBottom = -PanY / Zoom;
            double worldTop = (height - PanY) / Zoom;

            return new Rect(
                worldLeft,
                worldBottom,
                worldRight - worldLeft,
                worldTop - worldBottom);
        }

        private OpenTK.Vector4 GetColorFromArgb(uint argb)
        {
            float a = ((argb >> 24) & 0xFF) / 255f;
            float r = ((argb >> 16) & 0xFF) / 255f;
            float g = ((argb >> 8) & 0xFF) / 255f;
            float b = (argb & 0xFF) / 255f;
            return new OpenTK.Vector4(r, g, b, a);
        }

        private void BuildLayerQuadtreeAsync(GerberLayer layer)
        {
            var bounds = layer.Bounds;
            if (bounds.IsEmpty) return;

            bounds.Inflate(bounds.Width * 0.02, bounds.Height * 0.02);
            var quadtree = new GerberQuadtree(bounds);

            // For smaller layers, build synchronously to avoid first-frame stutter
            // Only use async for very large layers
            if (layer.Primitives.Count <= 5000)
            {
                // Build synchronously
                foreach (var prim in layer.Primitives)
                {
                    quadtree.Insert(prim);
                }

                lock (_layerQuadtrees)
                {
                    _layerQuadtrees[layer.Id] = quadtree;
                }
            }
            else
            {
                // Build quadtree in parallel for large layers
                Task.Run(() =>
                {
                    // Parallel insertion for large layers
                    Parallel.ForEach(layer.Primitives, prim =>
                    {
                        lock (quadtree)
                        {
                            quadtree.Insert(prim);
                        }
                    });

                    lock (_layerQuadtrees)
                    {
                        _layerQuadtrees[layer.Id] = quadtree;
                    }

                    Dispatcher.BeginInvoke(new Action(Invalidate));
                });
            }
        }

        #region Mouse Handling

        private void GlControl_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Middle ||
                (e.Button == System.Windows.Forms.MouseButtons.Left && System.Windows.Forms.Control.ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift)))
            {
                _isPanning = true;
                _panStart = new Point(e.X, e.Y);
                _glControl.Capture = true;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                // Start potential selection
                _selectionStart = new Point(e.X, e.Y);
                _selectionRect = Rect.Empty;
            }
        }

        private void GlControl_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                _glControl.Capture = false;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Point screenPos = new Point(e.X, e.Y);

                if (_isSelecting)
                {
                    // Complete rectangle selection
                    _isSelecting = false;
                    _glControl.Capture = false;

                    // Convert screen rect to world rect
                    Point worldTL = ScreenToWorld(new Point(_selectionRect.Left, _selectionRect.Top));
                    Point worldBR = ScreenToWorld(new Point(_selectionRect.Right, _selectionRect.Bottom));

                    // Normalize (screen Y and world Y are inverted)
                    double worldLeft = Math.Min(worldTL.X, worldBR.X);
                    double worldRight = Math.Max(worldTL.X, worldBR.X);
                    double worldBottom = Math.Min(worldTL.Y, worldBR.Y);
                    double worldTop = Math.Max(worldTL.Y, worldBR.Y);

                    Rect worldRect = new Rect(worldLeft, worldBottom, worldRight - worldLeft, worldTop - worldBottom);
                    SelectionRectCompleted?.Invoke(this, worldRect);

                    _selectionRect = Rect.Empty;
                    Invalidate();
                }
                else
                {
                    // Single click - raise point clicked event
                    Point worldPos = ScreenToWorld(screenPos);
                    PointClicked?.Invoke(this, worldPos);
                }
            }
        }

        private void GlControl_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            Point screenPos = new Point(e.X, e.Y);
            Point worldPos = ScreenToWorld(screenPos);

            // Always report cursor position
            CursorPositionChanged?.Invoke(this, worldPos);

            if (_isPanning)
            {
                // Match CPU coordinate system: PanX/PanY are screen offsets
                double dx = e.X - _panStart.X;
                double dy = e.Y - _panStart.Y;
                PanX += dx;
                PanY -= dy; // Y is inverted (screen Y increases down, world Y increases up)
                _panStart = new Point(e.X, e.Y);
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Left && !_isSelecting)
            {
                // Start rectangle selection after small movement
                double dist = Math.Sqrt(Math.Pow(screenPos.X - _selectionStart.X, 2) +
                                       Math.Pow(screenPos.Y - _selectionStart.Y, 2));
                if (dist > 3)
                {
                    _isSelecting = true;
                    _glControl.Capture = true;
                }
            }
            else if (_isSelecting)
            {
                // Update selection rectangle
                _selectionRect = new Rect(
                    Math.Min(_selectionStart.X, screenPos.X),
                    Math.Min(_selectionStart.Y, screenPos.Y),
                    Math.Abs(screenPos.X - _selectionStart.X),
                    Math.Abs(screenPos.Y - _selectionStart.Y)
                );
                Invalidate();
            }

            _lastMousePosition = screenPos;
        }

        private void GlControl_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            // Match CPU coordinate system for zoom towards mouse position
            // CPU ScreenToWorld: worldX = (screenX - PanX) / Zoom
            // CPU ScreenToWorld: worldY = (ActualHeight - screenY - PanY) / Zoom
            double mouseWorldX = (e.X - PanX) / Zoom;
            double mouseWorldY = (_glControl.Height - e.Y - PanY) / Zoom;

            double zoomFactor = e.Delta > 0 ? 1.2 : 1 / 1.2;
            double newZoom = Zoom * zoomFactor;
            newZoom = Math.Max(0.1, Math.Min(10000, newZoom));

            // CPU WorldToScreen: screenX = world.X * newZoom + PanX
            // We want: e.X = mouseWorldX * newZoom + newPanX
            // So: newPanX = e.X - mouseWorldX * newZoom
            // Similarly: screenY = ActualHeight - (world.Y * newZoom + PanY)
            // e.Y = ActualHeight - (mouseWorldY * newZoom + newPanY)
            // newPanY = ActualHeight - e.Y - mouseWorldY * newZoom
            PanX = e.X - mouseWorldX * newZoom;
            PanY = _glControl.Height - e.Y - mouseWorldY * newZoom;

            Zoom = newZoom;
        }

        #endregion
#endif

        public void Invalidate()
        {
#if USE_OPENGL
            _needsRedraw = true;
#endif
        }

        #region Coordinate Conversion

        /// <summary>
        /// Converts world coordinates to screen coordinates (matches DesignCanvas)
        /// </summary>
        public Point WorldToScreen(Point world)
        {
#if USE_OPENGL
            return new Point(
                world.X * Zoom + PanX,
                (_glControl?.Height ?? 0) - (world.Y * Zoom + PanY)
            );
#else
            return world;
#endif
        }

        /// <summary>
        /// Converts screen coordinates to world coordinates (matches DesignCanvas)
        /// </summary>
        public Point ScreenToWorld(Point screen)
        {
#if USE_OPENGL
            double height = _glControl?.Height ?? 0;
            return new Point(
                (screen.X - PanX) / Zoom,
                (height - screen.Y - PanY) / Zoom
            );
#else
            return screen;
#endif
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised when the cursor position changes (in world coordinates)
        /// </summary>
        public event EventHandler<Point> CursorPositionChanged;

        /// <summary>
        /// Raised when user clicks in the canvas (for selection)
        /// </summary>
        public event EventHandler<Point> PointClicked;

        /// <summary>
        /// Raised when user completes a selection rectangle
        /// </summary>
        public event EventHandler<Rect> SelectionRectCompleted;

        #endregion

        #region Public API

        /// <summary>
        /// Gets GPU information for diagnostics
        /// </summary>
        public static GpuInfo GetGpuInfo()
        {
#if USE_OPENGL
            try
            {
                using (var tempControl = new OpenTK.GLControl())
                {
                    tempControl.MakeCurrent();
                    return new GpuInfo
                    {
                        Vendor = GL.GetString(StringName.Vendor),
                        Renderer = GL.GetString(StringName.Renderer),
                        Version = GL.GetString(StringName.Version),
                        ShadingLanguageVersion = GL.GetString(StringName.ShadingLanguageVersion),
                        IsHighPerformance = !GL.GetString(StringName.Renderer).ToLower().Contains("intel")
                    };
                }
            }
            catch
            {
                return new GpuInfo { Vendor = "Unknown", Renderer = "Unknown", Version = "Unknown", ShadingLanguageVersion = "Unknown", IsHighPerformance = false };
            }
#else
            return new GpuInfo { Vendor = "N/A", Renderer = "OpenGL not available", Version = "N/A", ShadingLanguageVersion = "N/A", IsHighPerformance = false };
#endif
        }

        /// <summary>
        /// Zoom to fit all content
        /// </summary>
        public void ZoomToFit()
        {
            if (GerberLayers == null || GerberLayers.Count == 0) return;

            var bounds = Rect.Empty;
            foreach (var layer in GerberLayers)
            {
                if (!layer.Bounds.IsEmpty)
                    bounds.Union(layer.Bounds);
            }

            if (bounds.IsEmpty) return;

#if USE_OPENGL
            double width = _glControl?.Width ?? ActualWidth;
            double height = _glControl?.Height ?? ActualHeight;
#else
            double width = ActualWidth;
            double height = ActualHeight;
#endif

            double zoomX = width / bounds.Width * 0.9;
            double zoomY = height / bounds.Height * 0.9;
            Zoom = Math.Min(zoomX, zoomY);

            PanX = bounds.X + bounds.Width / 2;
            PanY = bounds.Y + bounds.Height / 2;
        }

        /// <summary>
        /// Gets the last frame's render statistics
        /// </summary>
        public RenderStats GetRenderStats()
        {
#if USE_OPENGL
            return _lastRenderStats;
#else
            return new RenderStats();
#endif
        }

        #endregion
    }

    /// <summary>
    /// Cached layer geometry to avoid rebuilding every frame.
    /// Stores pre-built GPU buffers for each layer.
    /// </summary>
    internal class LayerGeometryCache
    {
        public string LayerId { get; set; }
        public int PrimitiveCount { get; set; }
        public int CircleCount { get; set; }
        public int RectangleCount { get; set; }
        public int LineCount { get; set; }
        public int PolygonCount { get; set; }
        public uint ColorArgb { get; set; }
        public double Opacity { get; set; }
        public bool IsValid { get; set; }

        // Cached primitive data for quick re-rendering
        public List<CachedCircle> Circles { get; set; } = new List<CachedCircle>();
        public List<CachedRectangle> Rectangles { get; set; } = new List<CachedRectangle>();
        public List<CachedLine> Lines { get; set; } = new List<CachedLine>();
        public List<CachedPolygon> Polygons { get; set; } = new List<CachedPolygon>();

        // Tile-based spatial index for O(visible) instead of O(N) iteration
        public TileIndex TileIndex { get; set; }

        // Pre-triangulated static mesh data for lines and polygons (built once, uploaded once)
        public StaticMeshData LineMesh { get; set; }
        public StaticMeshData PolygonMesh { get; set; }

        public void Clear()
        {
            Circles.Clear();
            Rectangles.Clear();
            Lines.Clear();
            Polygons.Clear();
            CircleCount = 0;
            RectangleCount = 0;
            LineCount = 0;
            PolygonCount = 0;
            TileIndex?.Clear();
            TileIndex = null;
            LineMesh?.Clear();
            LineMesh = null;
            PolygonMesh?.Clear();
            PolygonMesh = null;
            IsValid = false;
        }
    }

    /// <summary>
    /// Cached circle data structure used by retained-mode rendering.
    /// </summary>
    public struct CachedCircle
    {
        public float X, Y, Radius;
    }

    /// <summary>
    /// Cached rectangle data structure used by retained-mode rendering.
    /// </summary>
    public struct CachedRectangle
    {
        public float X, Y, Width, Height;
    }

    internal struct CachedLine
    {
        public float X1, Y1, X2, Y2, Width;
    }

    internal struct CachedPolygon
    {
        public IList<System.Windows.Point> Points;
    }

    /// <summary>
    /// Pre-triangulated mesh data for static GPU rendering.
    /// Built once during cache creation, uploaded to GPU once.
    /// </summary>
    internal class StaticMeshData
    {
        // CPU-side triangulated geometry (built once)
        public float[] Vertices;  // x, y pairs
        public uint[] Indices;    // triangle indices
        public int VertexCount;
        public int IndexCount;

        // Indicates if data needs to be uploaded to GPU
        public bool NeedsUpload = true;

        public void Clear()
        {
            Vertices = null;
            Indices = null;
            VertexCount = 0;
            IndexCount = 0;
            NeedsUpload = true;
        }

        public bool HasData => VertexCount > 0 && IndexCount > 0;
    }

    /// <summary>
    /// Uniform grid tile index for O(visible) instead of O(N) iteration.
    /// Each tile stores indices into the cached primitive arrays.
    /// </summary>
    internal class TileIndex
    {
        public float MinX, MinY, MaxX, MaxY;
        public float TileSize;
        public int TilesX, TilesY;

        // 2D array of lists [tileY * TilesX + tileX] -> list of indices
        public List<int>[] CircleIndices;
        public List<int>[] RectangleIndices;
        public List<int>[] LineIndices;
        public List<int>[] PolygonIndices;

        public void Initialize(float minX, float minY, float maxX, float maxY, float tileSize)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            TileSize = tileSize;

            // Calculate grid dimensions
            TilesX = Math.Max(1, (int)Math.Ceiling((maxX - minX) / tileSize));
            TilesY = Math.Max(1, (int)Math.Ceiling((maxY - minY) / tileSize));

            // Cap to reasonable size to avoid memory explosion
            if (TilesX > 500) { TileSize = (maxX - minX) / 500; TilesX = 500; }
            if (TilesY > 500) { TileSize = Math.Max(TileSize, (maxY - minY) / 500); TilesY = 500; }

            int totalTiles = TilesX * TilesY;
            CircleIndices = new List<int>[totalTiles];
            RectangleIndices = new List<int>[totalTiles];
            LineIndices = new List<int>[totalTiles];
            PolygonIndices = new List<int>[totalTiles];

            for (int i = 0; i < totalTiles; i++)
            {
                CircleIndices[i] = new List<int>();
                RectangleIndices[i] = new List<int>();
                LineIndices[i] = new List<int>();
                PolygonIndices[i] = new List<int>();
            }
        }

        public void Clear()
        {
            CircleIndices = null;
            RectangleIndices = null;
            LineIndices = null;
            PolygonIndices = null;
        }

        /// <summary>
        /// Add a primitive to all tiles it intersects.
        /// </summary>
        public void AddCircle(int index, float x, float y, float radius)
        {
            int minTileX = GetTileX(x - radius);
            int maxTileX = GetTileX(x + radius);
            int minTileY = GetTileY(y - radius);
            int maxTileY = GetTileY(y + radius);

            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    int tileIdx = ty * TilesX + tx;
                    CircleIndices[tileIdx].Add(index);
                }
            }
        }

        public void AddRectangle(int index, float x, float y, float width, float height)
        {
            float hw = width / 2, hh = height / 2;
            int minTileX = GetTileX(x - hw);
            int maxTileX = GetTileX(x + hw);
            int minTileY = GetTileY(y - hh);
            int maxTileY = GetTileY(y + hh);

            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    int tileIdx = ty * TilesX + tx;
                    RectangleIndices[tileIdx].Add(index);
                }
            }
        }

        public void AddLine(int index, float x1, float y1, float x2, float y2, float width)
        {
            float hw = width / 2;
            float minX = Math.Min(x1, x2) - hw;
            float maxX = Math.Max(x1, x2) + hw;
            float minY = Math.Min(y1, y2) - hw;
            float maxY = Math.Max(y1, y2) + hw;

            int minTileX = GetTileX(minX);
            int maxTileX = GetTileX(maxX);
            int minTileY = GetTileY(minY);
            int maxTileY = GetTileY(maxY);

            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    int tileIdx = ty * TilesX + tx;
                    LineIndices[tileIdx].Add(index);
                }
            }
        }

        public void AddPolygon(int index, IList<System.Windows.Point> points)
        {
            if (points == null || points.Count == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var pt in points)
            {
                if (pt.X < minX) minX = (float)pt.X;
                if (pt.X > maxX) maxX = (float)pt.X;
                if (pt.Y < minY) minY = (float)pt.Y;
                if (pt.Y > maxY) maxY = (float)pt.Y;
            }

            int minTileX = GetTileX(minX);
            int maxTileX = GetTileX(maxX);
            int minTileY = GetTileY(minY);
            int maxTileY = GetTileY(maxY);

            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    int tileIdx = ty * TilesX + tx;
                    PolygonIndices[tileIdx].Add(index);
                }
            }
        }

        private int GetTileX(float x)
        {
            int tx = (int)((x - MinX) / TileSize);
            return Math.Max(0, Math.Min(TilesX - 1, tx));
        }

        private int GetTileY(float y)
        {
            int ty = (int)((y - MinY) / TileSize);
            return Math.Max(0, Math.Min(TilesY - 1, ty));
        }

        /// <summary>
        /// Get the range of tile indices that intersect the given bounds.
        /// </summary>
        public void GetVisibleTileRange(float viewMinX, float viewMinY, float viewMaxX, float viewMaxY,
            out int minTileX, out int minTileY, out int maxTileX, out int maxTileY)
        {
            minTileX = GetTileX(viewMinX);
            maxTileX = GetTileX(viewMaxX);
            minTileY = GetTileY(viewMinY);
            maxTileY = GetTileY(viewMaxY);
        }
    }

    /// <summary>
    /// GPU information for capability detection
    /// </summary>
    public class GpuInfo
    {
        public string Vendor { get; set; }
        public string Renderer { get; set; }
        public string Version { get; set; }
        public string ShadingLanguageVersion { get; set; }
        public bool IsHighPerformance { get; set; }
    }

    /// <summary>
    /// Instance data for batched GPU rendering
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PrimitiveInstance
    {
        public float X, Y;
        public float ScaleX, ScaleY;
        public float R, G, B, A;
    }
}
