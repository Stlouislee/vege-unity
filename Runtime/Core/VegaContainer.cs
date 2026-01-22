using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UVis.Spec;
using UVis.Scales;
using UVis.Transforms;
using UVis.Marks;
using UVis.Layout;
using UVis.Data;

#if DATACORE_INSTALLED
using AroAro.DataCore.Events;
#endif

namespace UVis.Core
{
    /// <summary>
    /// Main container component for Vega-Lite style charts in Unity.
    /// Supports both Canvas 2D and World Space 3D rendering modes.
    /// </summary>
    [AddComponentMenu("UVis/Vega Container")]
    public class VegaContainer : MonoBehaviour
    {
        /// <summary>
        /// Render mode for the chart.
        /// </summary>
        public enum RenderMode
        {
            Canvas2D,
            WorldSpace3D
        }

        [Header("Render Settings")]
        [SerializeField] private RenderMode _renderMode = RenderMode.Canvas2D;
        
        [Header("Specification")]
        [SerializeField] private TextAsset _chartSpecAsset;
        [SerializeField, TextArea(10, 30)] private string _chartSpecJson;

        [Header("2D Mode Settings")]
        [SerializeField] private RectTransform _targetCanvas;
        [SerializeField] private Material _defaultMaterial2D;

        [Header("3D Mode Settings")]
        [SerializeField] private Transform _plotRoot;
        [SerializeField] private Material _defaultMaterial3D;
        [SerializeField] private float _pixelScale = 0.01f;

        [Header("Typography")]
        [SerializeField] private TMP_FontAsset _font;

        // Internal state
        private ChartSpec _currentSpec;
        private IMarkRenderer _markRenderer;
        private AxisRenderer _axisRenderer;
        private LegendRenderer _legendRenderer;
        private LayoutCalculator _layoutCalculator;
        private RectTransform _plotAreaRect;
        private List<Dictionary<string, object>> _processedData;
        
        // DataCore sync state
        private string _boundDataset;
        private bool _isSyncEnabled;
        private float _lastSyncTime;
        private const float SYNC_DEBOUNCE_SECONDS = 0.1f;

        /// <summary>
        /// Current render mode.
        /// </summary>
        public RenderMode CurrentRenderMode => _renderMode;

        /// <summary>
        /// Event fired when specification changes.
        /// </summary>
        public event Action<ChartSpec> OnSpecChanged;

        /// <summary>
        /// Event fired after chart is rendered.
        /// </summary>
        public event Action OnChartRendered;

        private void Awake()
        {
            _layoutCalculator = new LayoutCalculator();
            _axisRenderer = new AxisRenderer(_font);
            _legendRenderer = new LegendRenderer(_font);
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(GetSpecJson()))
            {
                Render();
            }
        }

        /// <summary>
        /// Get the current specification JSON.
        /// </summary>
        public string GetSpecJson()
        {
            if (_chartSpecAsset != null)
                return _chartSpecAsset.text;
            return _chartSpecJson;
        }

        /// <summary>
        /// Set the specification from a JSON string and render.
        /// </summary>
        public void SetSpec(string json)
        {
            _chartSpecJson = json;
            _chartSpecAsset = null;
            Render();
        }

        /// <summary>
        /// Set the specification from a TextAsset and render.
        /// </summary>
        public void SetSpec(TextAsset asset)
        {
            _chartSpecAsset = asset;
            _chartSpecJson = null;
            Render();
        }

        /// <summary>
        /// Parse and render the current specification.
        /// </summary>
        [ContextMenu("Render Chart")]
        public void Render()
        {
            try
            {
                string json = GetSpecJson();
                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug.LogWarning("[UVis] No specification provided");
                    return;
                }

                // Parse specification
                _currentSpec = SpecParser.Parse(json);
                OnSpecChanged?.Invoke(_currentSpec);
                
                // Load data from external sources (e.g., DataCore dc:// URLs)
                LoadDataFromSource(_currentSpec);

                // Clear previous render
                Clear();

                // Setup render context
                SetupRenderContext();

                // Process data through transforms
                _processedData = TransformExecutor.Execute(
                    _currentSpec.data.values ?? new List<Dictionary<string, object>>(),
                    _currentSpec.transform
                );

                // Build scales
                var (xScale, yScale, zScale) = BuildScales(_currentSpec, _processedData);

                // Create mark renderer
                var markType = _currentSpec.mark.ToMarkType();
                _markRenderer = MarkRendererFactory.Create(markType);

                // Build color scale if color field is specified
                OrdinalColorScale colorScale = null;
                var colorChannel = _currentSpec.encoding?.color;
                if (colorChannel?.field != null)
                {
                    var colorValues = _processedData
                        .Where(r => r.ContainsKey(colorChannel.field))
                        .Select(r => r[colorChannel.field]);
                    colorScale = OrdinalColorScale.FromData(colorValues);
                }

                // Render marks
                var context = new MarkRenderContext
                {
                    Spec = _currentSpec,
                    Data = _processedData,
                    Encoding = _currentSpec.encoding,
                    XScale = xScale,
                    YScale = yScale,
                    ZScale = zScale,
                    ColorScale = colorScale,
                    PlotArea = _plotAreaRect,
                    PlotRoot = _plotRoot,
                    DefaultMaterial = _renderMode == RenderMode.Canvas2D ? _defaultMaterial2D : _defaultMaterial3D,
                    Is3DMode = _renderMode == RenderMode.WorldSpace3D,
                    PixelScale = _pixelScale
                };

                _markRenderer.Render(context);

                // Render axes
                if (_currentSpec.axis != null)
                {
                    _axisRenderer.RenderXAxis(xScale, _currentSpec.axis.x, _plotAreaRect, 
                        _renderMode == RenderMode.WorldSpace3D, _plotRoot);
                    _axisRenderer.RenderYAxis(yScale, _currentSpec.axis.y, _plotAreaRect, 
                        _renderMode == RenderMode.WorldSpace3D, _plotRoot);
                    
                    // Render 3D Frame/Cage and Z axis if in 3D mode
                    if (_renderMode == RenderMode.WorldSpace3D)
                    {
                        // Z Axis rendering (if Z encoding present or always for 3D frame)
                        _axisRenderer.RenderZAxis(zScale, _currentSpec.axis?.z, _plotRoot);
                        
                        // Calculate plot bounds for 3D frame
                        float xMin = xScale.RangeMin;
                        float xMax = xScale.RangeMax;
                        float yMin = yScale.RangeMin;
                        float yMax = yScale.RangeMax;
                        float zMin = zScale.RangeMin;
                        float zMax = zScale.RangeMax;
                        Rect bounds = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
                        float depth = zMax - zMin;
                        if (depth == 0) depth = 1f; // Minimum depth for 2D fallback

                        _axisRenderer.Render3DFrame(bounds, depth, _plotRoot);
                    }
                }

                OnChartRendered?.Invoke();
                Debug.Log($"[UVis] Chart rendered: {_processedData.Count} data points, mark type: {markType}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UVis] Failed to render chart: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Clear all rendered elements.
        /// </summary>
        [ContextMenu("Clear Chart")]
        public void Clear()
        {
            _markRenderer?.Clear();
            _axisRenderer?.Clear();
            _legendRenderer?.Clear();

            // Clear plot area children
            if (_plotAreaRect != null)
            {
                for (int i = _plotAreaRect.childCount - 1; i >= 0; i--)
                {
                    var child = _plotAreaRect.GetChild(i).gameObject;
                    if (Application.isPlaying)
                        Destroy(child);
                    else
                        DestroyImmediate(child);
                }
            }

            if (_plotRoot != null)
            {
                for (int i = _plotRoot.childCount - 1; i >= 0; i--)
                {
                    var child = _plotRoot.GetChild(i).gameObject;
                    if (Application.isPlaying)
                        Destroy(child);
                    else
                        DestroyImmediate(child);
                }
            }
        }

        private void SetupRenderContext()
        {
            if (_renderMode == RenderMode.Canvas2D)
            {
                SetupCanvas2D();
            }
            else
            {
                SetupWorldSpace3D();
            }
        }

        private void SetupCanvas2D()
        {
            // Create or get target canvas
            if (_targetCanvas == null)
            {
                var canvasGo = new GameObject("ChartCanvas");
                canvasGo.transform.SetParent(transform, false);

                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
                
                // Configure CanvasScaler for consistent sizing
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                
                canvasGo.AddComponent<GraphicRaycaster>();

                _targetCanvas = canvasGo.GetComponent<RectTransform>();
            }

            // Calculate the full chart area (including padding)
            float chartWidth = _currentSpec.width;
            float chartHeight = _currentSpec.height;
            var padding = _currentSpec.padding ?? new PaddingSpec();

            // Create chart container (full spec size)
            var chartContainer = new GameObject("ChartContainer");
            chartContainer.transform.SetParent(_targetCanvas, false);
            
            var chartRect = chartContainer.AddComponent<RectTransform>();
            chartRect.anchorMin = new Vector2(0.5f, 0.5f);  // Center anchor
            chartRect.anchorMax = new Vector2(0.5f, 0.5f);
            chartRect.pivot = new Vector2(0.5f, 0.5f);
            chartRect.anchoredPosition = Vector2.zero;
            chartRect.sizeDelta = new Vector2(chartWidth, chartHeight);

            // Add chart background
            var chartBg = chartContainer.AddComponent<Image>();
            chartBg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            // Create plot area inside chart container
            var plotArea = _layoutCalculator.CalculatePlotArea(_currentSpec);

            var plotGo = new GameObject("PlotArea");
            plotGo.transform.SetParent(chartContainer.transform, false);

            _plotAreaRect = plotGo.AddComponent<RectTransform>();
            // Anchor to bottom-left of chart container
            _plotAreaRect.anchorMin = Vector2.zero;
            _plotAreaRect.anchorMax = Vector2.zero;
            _plotAreaRect.pivot = Vector2.zero;
            _plotAreaRect.anchoredPosition = new Vector2(padding.left, padding.bottom);
            _plotAreaRect.sizeDelta = new Vector2(plotArea.width, plotArea.height);

            // Add plot area background (slightly different color)
            var bg = plotGo.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        }

        private void SetupWorldSpace3D()
        {
            if (_plotRoot == null)
            {
                var plotGo = new GameObject("PlotRoot");
                plotGo.transform.SetParent(transform, false);
                _plotRoot = plotGo.transform;
            }
        }

        private (IScale xScale, IScale yScale, IScale zScale) BuildScales(ChartSpec spec, List<Dictionary<string, object>> data)
        {
            var plotArea = _layoutCalculator.CalculatePlotArea(spec);
            float rangeMinX = 0;
            float rangeMaxX = _renderMode == RenderMode.Canvas2D ? plotArea.width : plotArea.width * _pixelScale;
            float rangeMinY = 0;
            float rangeMaxY = _renderMode == RenderMode.Canvas2D ? plotArea.height : plotArea.height * _pixelScale;
            
            // Z range: depth for 3D chart. Use similar sizing to X for a cube-like space.
            float rangeMinZ = 0;
            float rangeMaxZ = _renderMode == RenderMode.Canvas2D ? 0 : rangeMaxX * 0.75f; // 75% of X for depth

            IScale xScale = BuildScale(spec.encoding.x, data, rangeMinX, rangeMaxX);
            
            // Y Scale: check if we need stacked calculation
            IScale yScale;
            var yChannel = spec.encoding?.y;
            var colorChannel = spec.encoding?.color;
            bool hasColorField = colorChannel?.field != null;
            // Check stack mode from y.stack property
            string stackMode = yChannel?.stack;
            bool hasZEncoding = spec.encoding?.z?.field != null;
            
            // 3D bar charts (with Z encoding) should NOT be stacked
            // Only stack if: has color field, is bar mark, no Z encoding, and stack is not null
            bool isStackedBarChart = hasColorField 
                && spec.mark?.ToLower() == "bar" 
                && !hasZEncoding
                && !string.IsNullOrEmpty(stackMode) 
                && stackMode.ToLower() != "null";
            
            if (isStackedBarChart && yChannel != null)
            {
                // Calculate stacked totals per X category
                var xField = spec.encoding?.x?.field;
                var yField = yChannel.field;
                var colorField = colorChannel.field;
                
                // Group by X category and sum Y values for stacking
                var stackedTotals = new Dictionary<string, double>();
                foreach (var row in data)
                {
                    if (!row.ContainsKey(xField) || !row.ContainsKey(yField)) continue;
                    
                    string xCategory = row[xField]?.ToString() ?? "";
                    double yValue = Convert.ToDouble(row[yField]);
                    
                    if (!stackedTotals.ContainsKey(xCategory))
                        stackedTotals[xCategory] = 0;
                    stackedTotals[xCategory] += yValue;
                }
                
                // Build Y scale using stacked totals as domain
                var maxStackedValue = stackedTotals.Values.Count > 0 ? stackedTotals.Values.Max() : 1;
                bool includeZero = yChannel.scale?.zero ?? true;
                bool nice = yChannel.scale?.nice ?? true;
                
                var stackedValues = new List<double> { 0, maxStackedValue };
                yScale = new LinearScale(stackedValues, rangeMinY, rangeMaxY, includeZero, nice);
            }
            else
            {
                yScale = BuildScale(spec.encoding.y, data, rangeMinY, rangeMaxY);
            }
            
            // Build Z scale: if no Z encoding, create a single-category "dummy" scale
            IScale zScale;
            if (spec.encoding?.z != null && !string.IsNullOrEmpty(spec.encoding.z.field))
            {
                zScale = BuildScale(spec.encoding.z, data, rangeMinZ, rangeMaxZ);
            }
            else
            {
                // Fallback: single category so all bars at Z center
                zScale = new BandScale(new List<string> { "_default" }, rangeMinZ, rangeMaxZ, 0.1, 0.05);
            }

            return (xScale, yScale, zScale);
        }

        private IScale BuildScale(ChannelSpec channel, List<Dictionary<string, object>> data, float rangeMin, float rangeMax)
        {
            if (channel == null)
            {
                return new LinearScale(0, 1, rangeMin, rangeMax);
            }

            var fieldType = channel.type.ToFieldType();
            var scaleType = channel.scale?.type?.ToLowerInvariant() ?? "linear";

            if (fieldType.IsOrdinal() || scaleType == "band" || scaleType == "point")
            {
                // Ordinal/categorical scale
                var categories = data
                    .Where(r => r.ContainsKey(channel.field))
                    .Select(r => r[channel.field]?.ToString() ?? "")
                    .Distinct()
                    .ToList();

                double paddingInner = channel.scale?.paddingInner ?? 0.1;
                double paddingOuter = channel.scale?.paddingOuter ?? 0.05;

                return new BandScale(categories, rangeMin, rangeMax, paddingInner, paddingOuter);
            }
            else if (scaleType == "log")
            {
                // Logarithmic scale
                var values = data
                    .Where(r => r.ContainsKey(channel.field))
                    .Select(r => Convert.ToDouble(r[channel.field]))
                    .Where(v => v > 0);

                return new LogScale(values, rangeMin, rangeMax);
            }
            else
            {
                // Linear scale (default)
                var values = data
                    .Where(r => r.ContainsKey(channel.field))
                    .Select(r => Convert.ToDouble(r[channel.field]));

                bool includeZero = channel.scale?.zero ?? true;
                bool nice = channel.scale?.nice ?? true;

                return new LinearScale(values, rangeMin, rangeMax, includeZero, nice);
            }
        }

        /// <summary>
        /// Take a screenshot of the chart and save to file.
        /// </summary>
        public void SaveScreenshot(string filePath)
        {
            if (_renderMode == RenderMode.Canvas2D)
            {
                // For Canvas2D, use ScreenCapture
                ScreenCapture.CaptureScreenshot(filePath);
                Debug.Log($"[UVis] Screenshot saved to: {filePath}");
            }
            else
            {
                // For 3D, render to texture
                var camera = Camera.main;
                if (camera != null)
                {
                    var rt = new RenderTexture(_currentSpec.width, _currentSpec.height, 24);
                    camera.targetTexture = rt;
                    camera.Render();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();

                    var bytes = tex.EncodeToPNG();
                    System.IO.File.WriteAllBytes(filePath, bytes);

                    camera.targetTexture = null;
                    RenderTexture.active = null;
                    Destroy(rt);
                    Destroy(tex);

                    Debug.Log($"[UVis] Screenshot saved to: {filePath}");
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Editor preview could be added here
        }
#endif

        private void OnDestroy()
        {
            UnsubscribeFromDataChanges();
        }

        /// <summary>
        /// Load data from external sources (e.g., DataCore dc:// URLs).
        /// </summary>
        private void LoadDataFromSource(ChartSpec spec)
        {
            // Unsubscribe from previous dataset if any
            UnsubscribeFromDataChanges();

            if (spec?.data == null)
                return;

            // Check for DataCore URL
            if (DataCoreDataLoader.CanHandle(spec.data.url))
            {
                // Parse dataset name for sync subscription
                var (datasetName, _) = DataCoreDataLoader.ParseUrl(spec.data.url);
                
                // Load data from DataCore
                DataCoreDataLoader.PopulateSpec(spec.data);
                
                // Setup sync if enabled
                if (DataCoreDataLoader.IsSyncEnabled(spec.data.url, spec.data))
                {
                    SubscribeToDataChanges(datasetName);
                }
            }
        }

        /// <summary>
        /// Subscribe to DataCore dataset changes for live sync.
        /// </summary>
        private void SubscribeToDataChanges(string datasetName)
        {
#if DATACORE_INSTALLED
            _boundDataset = datasetName;
            _isSyncEnabled = true;
            DataCoreEventManager.DatasetModified += OnDatasetModified;
            Debug.Log($"[UVis] Subscribed to DataCore changes for '{datasetName}'");
#endif
        }

        /// <summary>
        /// Unsubscribe from DataCore dataset changes.
        /// </summary>
        private void UnsubscribeFromDataChanges()
        {
#if DATACORE_INSTALLED
            if (_isSyncEnabled)
            {
                DataCoreEventManager.DatasetModified -= OnDatasetModified;
                Debug.Log($"[UVis] Unsubscribed from DataCore changes for '{_boundDataset}'");
            }
#endif
            _boundDataset = null;
            _isSyncEnabled = false;
        }

#if DATACORE_INSTALLED
        /// <summary>
        /// Handle DataCore dataset modification events.
        /// </summary>
        private void OnDatasetModified(object sender, DatasetModifiedEventArgs e)
        {
            if (e.DatasetName != _boundDataset)
                return;

            // Debounce to prevent excessive re-renders
            if (Time.time - _lastSyncTime < SYNC_DEBOUNCE_SECONDS)
                return;

            _lastSyncTime = Time.time;
            Debug.Log($"[UVis] Dataset '{e.DatasetName}' modified (op: {e.Operation}), re-rendering...");
            
            // Re-render chart with updated data
            Render();
        }
#endif
    }
}
