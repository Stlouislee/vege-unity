using System;
using System.Collections.Generic;
using UnityEngine;
using UVis.Spec;
using UVis.Scales;

namespace UVis.Marks
{
    /// <summary>
    /// Interface for mark renderers.
    /// </summary>
    public interface IMarkRenderer
    {
        void Render(MarkRenderContext context);
        void Clear();
    }

    /// <summary>
    /// Context passed to mark renderers containing all necessary data.
    /// </summary>
    public class MarkRenderContext
    {
        public ChartSpec Spec { get; set; }                  // Full spec for graph and advanced marks
        public List<Dictionary<string, object>> Data { get; set; }
        public EncodingSpec Encoding { get; set; }
        public IScale XScale { get; set; }
        public IScale YScale { get; set; }
        public IScale ZScale { get; set; }                 // Z-axis scale for true 3D charts
        public OrdinalColorScale ColorScale { get; set; }  // Color scale for categorical colors
        public RectTransform PlotArea { get; set; }       // For Canvas2D mode
        public Transform PlotRoot { get; set; }           // For WorldSpace3D mode
        public Material DefaultMaterial { get; set; }
        public bool Is3DMode { get; set; }
        public float PixelScale { get; set; } = 1f;       // Unity units per chart pixel
    }

    /// <summary>
    /// Factory for creating mark renderers.
    /// </summary>
    public static class MarkRendererFactory
    {
        public static IMarkRenderer Create(MarkType markType)
        {
            return markType switch
            {
                MarkType.Bar => new BarMarkRenderer(),
                MarkType.Line => new LineMarkRenderer(),
                MarkType.Point => new PointMarkRenderer(),
                MarkType.Graph => new GraphMarkRenderer(),
                _ => new BarMarkRenderer()
            };
        }
    }

    /// <summary>
    /// Renders bar marks (rectangles).
    /// </summary>
    public class BarMarkRenderer : IMarkRenderer
    {
        private readonly List<GameObject> _bars = new List<GameObject>();

        public void Render(MarkRenderContext context)
        {
            Clear();

            var xChannel = context.Encoding.x;
            var yChannel = context.Encoding.y;
            var zChannel = context.Encoding.z;  // Z encoding for true 3D
            var colorChannel = context.Encoding.color;

            if (xChannel == null || yChannel == null)
            {
                Debug.LogWarning("[UVis] Bar mark requires both x and y encoding");
                return;
            }

            // Determine if using band scale (categorical x-axis)
            bool isXBandScale = context.XScale is BandScale;
            bool isZBandScale = context.ZScale is BandScale;
            
            float barWidth = isXBandScale ? ((BandScale)context.XScale).BandWidth : 10f;
            float barDepth = isZBandScale ? ((BandScale)context.ZScale).BandWidth : barWidth;

            // Default color handling
            Color defaultColor = Color.white;
            if (colorChannel?.value != null)
            {
                ColorUtility.TryParseHtmlString(colorChannel.value, out defaultColor);
            }

            // Determine if we're doing stacking or grouping
            bool hasColorField = colorChannel?.field != null;
            bool hasZEncoding = zChannel?.field != null; // 3D charts with Z should NOT stack
            // Check y.stack property: null or empty = no stacking (grouped), "zero"/"center"/"normalize" = stacking
            string stackMode = yChannel.stack;
            // Disable stacking for 3D bar charts with Z encoding
            bool isStacked = hasColorField 
                && !hasZEncoding 
                && !string.IsNullOrEmpty(stackMode) 
                && stackMode.ToLower() != "null";
            
            // For grouped bars (xOffset), track categories and calculate offset
            Dictionary<string, int> colorIndexMap = new Dictionary<string, int>();
            int colorCategoryCount = 1;
            if (hasColorField && context.ColorScale != null)
            {
                var categories = context.ColorScale.Domain;
                colorCategoryCount = categories.Count;
                for (int i = 0; i < categories.Count; i++)
                {
                    colorIndexMap[categories[i]] = i;
                }
            }
            
            // Calculate sub-bar width for grouped bars
            float groupedBarWidth = hasColorField && !isStacked ? barWidth / colorCategoryCount : barWidth;

            // For stacking: track cumulative RAW VALUES per X category (not screen positions)
            Dictionary<string, double> stackRawValue = new Dictionary<string, double>();

            foreach (var row in context.Data)
            {
                if (!row.TryGetValue(xChannel.field, out var xVal) ||
                    !row.TryGetValue(yChannel.field, out var yVal))
                {
                    continue;
                }

                string xCategory = xVal?.ToString() ?? "";
                double rawYValue = Convert.ToDouble(yVal);

                // X position
                float xPos;
                if (isXBandScale)
                {
                    xPos = ((BandScale)context.XScale).MapBandStart(xVal);
                }
                else
                {
                    xPos = context.XScale.Map(xVal);
                }

                // Y position and height - support stacking
                float yPos, barHeight;
                if (isStacked && hasColorField)
                {
                    // Get the current stack base for this X category
                    double baseRawValue = 0;
                    if (stackRawValue.TryGetValue(xCategory, out double existingBase))
                    {
                        baseRawValue = existingBase;
                    }
                    
                    // Calculate screen positions from raw values
                    yPos = context.YScale.Map(baseRawValue);
                    barHeight = context.YScale.Map(baseRawValue + rawYValue) - yPos;
                    
                    // Update the stack base with the new cumulative value
                    stackRawValue[xCategory] = baseRawValue + rawYValue;
                }
                else
                {
                    // Non-stacked: bar starts at 0
                    yPos = 0;
                    barHeight = context.YScale.Map(yVal);
                }

                // Apply xOffset for grouped bars (non-stacked, non-3D with Z)
                // 3D bar charts with Z encoding use Z position for grouping, not xOffset
                if (hasColorField && !isStacked && !hasZEncoding && row.TryGetValue(colorChannel.field, out var colorVal))
                {
                    string colorCategory = colorVal?.ToString() ?? "";
                    if (colorIndexMap.TryGetValue(colorCategory, out int colorIndex))
                    {
                        xPos += colorIndex * groupedBarWidth;
                    }
                }

                // Z position (for 3D charts)
                float zPos = 0;
                if (context.Is3DMode && zChannel != null && !string.IsNullOrEmpty(zChannel.field))
                {
                    if (row.TryGetValue(zChannel.field, out var zVal) && isZBandScale)
                    {
                        zPos = ((BandScale)context.ZScale).MapBandStart(zVal);
                    }
                    else if (row.TryGetValue(zChannel.field, out zVal))
                    {
                        zPos = context.ZScale.Map(zVal);
                    }
                }
                else if (context.Is3DMode)
                {
                    zPos = (context.ZScale?.RangeMin ?? 0 + context.ZScale?.RangeMax ?? 0) / 2f;
                }

                // Get color for this bar
                Color barColor = defaultColor;
                if (hasColorField && row.TryGetValue(colorChannel.field, out var colorFieldVal))
                {
                    if (context.ColorScale != null)
                    {
                        barColor = context.ColorScale.Map(colorFieldVal);
                    }
                }

                float effectiveBarWidth = hasColorField && !isStacked ? groupedBarWidth : barWidth;

                if (context.Is3DMode)
                {
                    CreateBar3D(context, xPos, yPos, zPos, effectiveBarWidth, barHeight, barDepth, barColor);
                }
                else
                {
                    CreateBar2D(context, xPos, yPos, effectiveBarWidth, barHeight, barColor);
                }
            }
        }

        private void CreateBar2D(MarkRenderContext context, float x, float y, float width, float height, Color color)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(context.PlotArea, false);

            var rectTransform = go.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0, 0);
            rectTransform.anchoredPosition = new Vector2(x, y);
            rectTransform.sizeDelta = new Vector2(width, height);

            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = color;

            _bars.Add(go);
        }

        private void CreateBar3D(MarkRenderContext context, float x, float y, float z, float width, float height, float depth, Color color)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(context.PlotRoot, false);

            // Position the bar at (x, y, z) - bar grows upwards from Y=0
            go.transform.localPosition = new Vector3(x, y, z);

            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();

            // Create Cube mesh with specified width, height, and depth
            float w = width;
            float h = Mathf.Max(height, 0.001f); // Avoid zero height
            float d = Mathf.Max(depth, 0.1f);

            meshFilter.sharedMesh = CreateCubeMesh(w, h, d);

            // Create material with proper color - use lit shader for 3D shading with depth
            Material material;
            if (context.DefaultMaterial != null)
            {
                // Clone the default material so we can set color per-bar
                material = new Material(context.DefaultMaterial);
                material.color = color;
            }
            else
            {
                // Try URP shaders first (Simple Lit is more reliable than Lit), then Standard
                var shader = Shader.Find("Universal Render Pipeline/Simple Lit")
                          ?? Shader.Find("Universal Render Pipeline/Lit") 
                          ?? Shader.Find("Standard")
                          ?? Shader.Find("Unlit/Color");
                
                material = new Material(shader);
                
                // Set color based on shader type
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color); // URP uses _BaseColor
                }
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", color); // Standard/Legacy uses _Color
                }
                
                // Configure for clean matte appearance (no reflections/specularity)
                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", 0f);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", 0.1f);
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness", 0.1f);
            }
            
            meshRenderer.material = material;

            _bars.Add(go);
        }

        private Mesh CreateCubeMesh(float w, float h, float d)
        {
            var mesh = new Mesh();
            
            // 8 vertices, but we need 24 for hard edges (distinct normals)
            // Pivot at bottom-left-front (0,0,0) to match 2D logic
            
            Vector3 p0 = new Vector3(0, 0, 0);
            Vector3 p1 = new Vector3(w, 0, 0);
            Vector3 p2 = new Vector3(0, h, 0);
            Vector3 p3 = new Vector3(w, h, 0);
            Vector3 p4 = new Vector3(0, 0, d);
            Vector3 p5 = new Vector3(w, 0, d);
            Vector3 p6 = new Vector3(0, h, d);
            Vector3 p7 = new Vector3(w, h, d);

            Vector3[] vertices = new Vector3[]
            {
                // Front
                p0, p1, p2, p3,
                // Back
                p5, p4, p7, p6,
                // Left
                p4, p0, p6, p2,
                // Right
                p1, p5, p3, p7,
                // Top
                p2, p3, p6, p7,
                // Bottom
                p4, p5, p0, p1
            };

            int[] triangles = new int[]
            {
                // Front
                0, 2, 1, 2, 3, 1,
                // Back
                4, 6, 5, 6, 7, 5,
                // Left
                8, 10, 9, 10, 11, 9,
                // Right
                12, 14, 13, 14, 15, 13,
                // Top
                16, 18, 17, 18, 19, 17,
                // Bottom
                20, 22, 21, 22, 23, 21
            };

            // Normals - 24 elements to match vertices (4 per face x 6 faces)
            Vector3[] normals = new Vector3[]
            {
                // Front face (Z=0, facing -Z)
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                // Back face (Z=d, facing +Z)
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                // Left face (X=0, facing -X)
                Vector3.left, Vector3.left, Vector3.left, Vector3.left, 
                // Right face (X=w, facing +X)
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                // Top face (Y=h, facing +Y)
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                // Bottom face (Y=0, facing -Y)
                Vector3.down, Vector3.down, Vector3.down, Vector3.down
            };
            
            // UVs
            Vector2 _00 = new Vector2(0, 0);
            Vector2 _10 = new Vector2(1, 0);
            Vector2 _01 = new Vector2(0, 1);
            Vector2 _11 = new Vector2(1, 1);
            
            Vector2[] uvs = new Vector2[]
            {
                _00, _10, _01, _11, // Front
                _00, _10, _01, _11, // Back
                _00, _10, _01, _11, // Left
                _00, _10, _01, _11, // Right
                _00, _10, _01, _11, // Top
                _00, _10, _01, _11, // Bottom
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uvs;
            
            return mesh;
        }

        private Material GetDefaultMaterial()
        {
            return new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
        }

        public void Clear()
        {
            foreach (var bar in _bars)
            {
                if (bar != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(bar);
                    else
                        UnityEngine.Object.DestroyImmediate(bar);
                }
            }
            _bars.Clear();
        }
    }

    /// <summary>
    /// Renders line marks (connected points).
    /// </summary>
    public class LineMarkRenderer : IMarkRenderer
    {
        private readonly List<GameObject> _lines = new List<GameObject>();

        public void Render(MarkRenderContext context)
        {
            Clear();

            var xChannel = context.Encoding.x;
            var yChannel = context.Encoding.y;
            var colorChannel = context.Encoding.color;

            if (xChannel == null || yChannel == null)
            {
                Debug.LogWarning("[UVis] Line mark requires both x and y encoding");
                return;
            }

            // Collect points
            var points = new List<Vector2>();

            foreach (var row in context.Data)
            {
                if (!row.TryGetValue(xChannel.field, out var xVal) ||
                    !row.TryGetValue(yChannel.field, out var yVal))
                {
                    continue;
                }

                float x = context.XScale.Map(xVal);
                float y = context.YScale.Map(yVal);
                points.Add(new Vector2(x, y));
            }

            if (points.Count < 2)
            {
                Debug.LogWarning("[UVis] Line mark requires at least 2 data points");
                return;
            }

            // Get line color
            Color lineColor = Color.white;
            if (colorChannel?.value != null)
            {
                ColorUtility.TryParseHtmlString(colorChannel.value, out lineColor);
            }

            if (context.Is3DMode)
            {
                CreateLine3D(context, points, lineColor);
            }
            else
            {
                CreateLine2D(context, points, lineColor);
            }
        }

        private void CreateLine2D(MarkRenderContext context, List<Vector2> points, Color color)
        {
            // Create line using UI elements (simple approach with small rectangles)
            float lineWidth = 2f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                var go = new GameObject($"LineSegment_{i}");
                go.transform.SetParent(context.PlotArea, false);

                var rectTransform = go.AddComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.zero;

                Vector2 start = points[i];
                Vector2 end = points[i + 1];
                Vector2 direction = end - start;
                float length = direction.magnitude;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

                rectTransform.pivot = new Vector2(0, 0.5f);
                rectTransform.anchoredPosition = start;
                rectTransform.sizeDelta = new Vector2(length, lineWidth);
                rectTransform.localRotation = Quaternion.Euler(0, 0, angle);

                var image = go.AddComponent<UnityEngine.UI.Image>();
                image.color = color;

                _lines.Add(go);
            }
        }

        private void CreateLine3D(MarkRenderContext context, List<Vector2> points, Color color)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(context.PlotRoot, false);

            var lineRenderer = go.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = points.Count;
            // Width is fixed pixel-like size, so we apply PixelScale to make it reasonable in 3D
            lineRenderer.startWidth = 2f * context.PixelScale;
            lineRenderer.endWidth = 2f * context.PixelScale;

            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = color;
            lineRenderer.material = material;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;

            for (int i = 0; i < points.Count; i++)
            {
                // Points are already World Units
                lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, 0));
            }

            _lines.Add(go);
        }

        public void Clear()
        {
            foreach (var line in _lines)
            {
                if (line != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(line);
                    else
                        UnityEngine.Object.DestroyImmediate(line);
                }
            }
            _lines.Clear();
        }
    }

    /// <summary>
    /// Renders point marks (circles/dots) with 3D scatter plot support.
    /// Supports shape mapping: sphere, cube, cylinder, capsule, or prefab:PrefabName
    /// </summary>
    public class PointMarkRenderer : IMarkRenderer
    {
        private readonly List<GameObject> _points = new List<GameObject>();
        private const float DEFAULT_POINT_SIZE_2D = 8f;
        private const float DEFAULT_POINT_SIZE_3D = 0.15f;

        public void Render(MarkRenderContext context)
        {
            Clear();

            var xChannel = context.Encoding.x;
            var yChannel = context.Encoding.y;
            var zChannel = context.Encoding.z;
            var colorChannel = context.Encoding.color;
            var sizeChannel = context.Encoding.size;
            var shapeChannel = context.Encoding.shape;

            if (xChannel == null || yChannel == null)
            {
                Debug.LogWarning("[UVis] Point mark requires both x and y encoding");
                return;
            }

            // Default color
            Color defaultColor = new Color(0.31f, 0.47f, 0.65f); // #4e79a7
            if (colorChannel?.value != null)
            {
                ColorUtility.TryParseHtmlString(colorChannel.value, out defaultColor);
            }

            // Build shape mapping if shape channel exists
            Dictionary<string, string> shapeMap = new Dictionary<string, string>();
            List<string> shapeRange = new List<string> { "sphere", "cube", "cylinder", "capsule" };
            if (shapeChannel?.scale?.range != null)
            {
                foreach (var r in shapeChannel.scale.range)
                {
                    shapeRange.Add(r.ToString());
                }
            }
            if (shapeChannel?.field != null)
            {
                // Build category to shape mapping
                var categories = new HashSet<string>();
                foreach (var row in context.Data)
                {
                    if (row.TryGetValue(shapeChannel.field, out var catVal))
                    {
                        categories.Add(catVal?.ToString() ?? "");
                    }
                }
                int i = 0;
                foreach (var cat in categories)
                {
                    shapeMap[cat] = shapeRange[i % shapeRange.Count];
                    i++;
                }
            }

            foreach (var row in context.Data)
            {
                if (!row.TryGetValue(xChannel.field, out var xVal) ||
                    !row.TryGetValue(yChannel.field, out var yVal))
                {
                    continue;
                }

                float x = context.XScale.Map(xVal);
                float y = context.YScale.Map(yVal);
                
                // Z position for 3D scatter
                float z = 0;
                if (context.Is3DMode && zChannel?.field != null && 
                    row.TryGetValue(zChannel.field, out var zVal) && context.ZScale != null)
                {
                    z = context.ZScale.Map(zVal);
                }

                // Get size for this point
                float pointSize = context.Is3DMode ? DEFAULT_POINT_SIZE_3D : DEFAULT_POINT_SIZE_2D;
                if (sizeChannel?.field != null && row.TryGetValue(sizeChannel.field, out var sizeVal))
                {
                    float rawSize = (float)Convert.ToDouble(sizeVal);
                    // Scale size based on range if defined
                    if (sizeChannel.scale?.range != null && sizeChannel.scale.range.Count >= 2)
                    {
                        float minSize = sizeChannel.scale.range[0];
                        float maxSize = sizeChannel.scale.range[1];
                        // Normalize and map to range (simplified)
                        pointSize = Mathf.Lerp(minSize, maxSize, Mathf.Clamp01(rawSize / 100f));
                    }
                    else
                    {
                        pointSize = rawSize * (context.Is3DMode ? 0.01f : 1f);
                    }
                }
                else if (sizeChannel?.value != null && float.TryParse(sizeChannel.value, out float sv))
                {
                    pointSize = sv;
                }

                // Get color for this point
                Color pointColor = defaultColor;
                if (colorChannel?.field != null && row.TryGetValue(colorChannel.field, out var colorFieldVal))
                {
                    if (context.ColorScale != null)
                    {
                        pointColor = context.ColorScale.Map(colorFieldVal);
                    }
                }

                // Get shape for this point
                string shapeName = "sphere";
                if (shapeChannel?.field != null && row.TryGetValue(shapeChannel.field, out var shapeFieldVal))
                {
                    string cat = shapeFieldVal?.ToString() ?? "";
                    if (shapeMap.TryGetValue(cat, out var mappedShape))
                    {
                        shapeName = mappedShape;
                    }
                }
                else if (shapeChannel?.value != null)
                {
                    shapeName = shapeChannel.value;
                }

                if (context.Is3DMode)
                {
                    CreatePoint3D(context, x, y, z, pointSize, pointColor, shapeName);
                }
                else
                {
                    CreatePoint2D(context, x, y, pointSize, pointColor);
                }
            }
        }

        private void CreatePoint2D(MarkRenderContext context, float x, float y, float size, Color color)
        {
            var go = new GameObject("Point");
            go.transform.SetParent(context.PlotRoot, false);

            var rectTransform = go.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(x, y);
            rectTransform.sizeDelta = new Vector2(size, size);

            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            // For round points, you'd set a circular sprite here

            _points.Add(go);
        }

        private void CreatePoint3D(MarkRenderContext context, float x, float y, float z, float size, Color color, string shape)
        {
            GameObject go;
            
            // Handle prefab or primitive shapes
            if (shape.StartsWith("prefab:"))
            {
                string prefabName = shape.Substring(7);
                var prefab = Resources.Load<GameObject>(prefabName);
                if (prefab != null)
                {
                    go = UnityEngine.Object.Instantiate(prefab);
                    go.name = $"Point_{prefabName}";
                }
                else
                {
                    Debug.LogWarning($"[UVis] Prefab not found: {prefabName}, using sphere");
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "Point";
                }
            }
            else
            {
                PrimitiveType primitiveType = shape.ToLower() switch
                {
                    "cube" => PrimitiveType.Cube,
                    "cylinder" => PrimitiveType.Cylinder,
                    "capsule" => PrimitiveType.Capsule,
                    _ => PrimitiveType.Sphere
                };
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = $"Point_{shape}";
            }
            
            go.transform.SetParent(context.PlotRoot, false);
            go.transform.localPosition = new Vector3(x, y, z);
            go.transform.localScale = Vector3.one * size;

            // Apply material
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material;
                if (context.DefaultMaterial != null)
                {
                    material = new Material(context.DefaultMaterial);
                }
                else
                {
                    // Try multiple shader names for compatibility
                    Shader shader = Shader.Find("Standard");
                    if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("HDRP/Lit");
                    if (shader == null) shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    
                    if (shader != null)
                    {
                        material = new Material(shader);
                    }
                    else
                    {
                        // Last resort: use primitive's default material
                        material = new Material(renderer.sharedMaterial);
                    }
                }
                material.color = color;
                // Also set _BaseColor for URP/HDRP compatibility
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }
                renderer.material = material;
            }

            // Remove collider for performance
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(collider);
                else
                    UnityEngine.Object.DestroyImmediate(collider);
            }

            _points.Add(go);
        }

        public void Clear()
        {
            foreach (var point in _points)
            {
                if (point != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(point);
                    else
                        UnityEngine.Object.DestroyImmediate(point);
                }
            }
            _points.Clear();
        }
    }
}

