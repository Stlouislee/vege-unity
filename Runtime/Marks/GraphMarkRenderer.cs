using System;
using System.Collections.Generic;
using UnityEngine;
using UVis.Spec;
using UVis.Scales;
using TMPro;

namespace UVis.Marks
{
    /// <summary>
    /// Renders graph marks (nodes + edges) with layout algorithms and VR/MR interaction.
    /// </summary>
    public class GraphMarkRenderer : IMarkRenderer
    {
        private readonly List<GameObject> _nodes = new List<GameObject>();
        private readonly List<GameObject> _edges = new List<GameObject>();
        private readonly List<GameObject> _labels = new List<GameObject>();
        
        private const float DEFAULT_NODE_SIZE = 0.2f;
        private const float DEFAULT_EDGE_WIDTH = 0.02f;
        
        // Runtime node positions (for layout and dragging)
        private Dictionary<string, Vector3> _nodePositions = new Dictionary<string, Vector3>();
        private Dictionary<string, Dictionary<string, object>> _nodeDataMap = new Dictionary<string, Dictionary<string, object>>();
        private Dictionary<string, GameObject> _nodeGameObjects = new Dictionary<string, GameObject>();

        public void Render(MarkRenderContext context)
        {
            Clear();
            
            var nodes = context.Spec.data?.nodes;
            var edges = context.Spec.data?.edges;
            
            if (nodes == null || nodes.Count == 0)
            {
                Debug.LogWarning("[UVis] Graph mark requires data.nodes array");
                return;
            }
            
            // Get layout spec
            var layoutSpec = context.Spec.layout ?? new LayoutSpec { type = "force" };
            var nodeEncoding = context.Encoding?.node;
            var edgeEncoding = context.Encoding?.edge;
            var interactionSpec = context.Spec.interaction;
            
            // Calculate auto-sized node and edge dimensions based on container
            float specWidth = context.Spec?.width ?? 500;
            float specHeight = context.Spec?.height ?? 500;
            float pixelScale = context.PixelScale;
            float containerSize = Mathf.Min(specWidth, specHeight) * pixelScale;
            int nodeCount = nodes.Count;
            
            // Auto-scale formula: larger container = larger nodes, more nodes = smaller nodes
            float baseNodeSize = containerSize * 0.03f / Mathf.Sqrt(nodeCount);
            baseNodeSize = Mathf.Clamp(baseNodeSize, 0.02f, containerSize * 0.1f);
            
            float baseEdgeWidth = baseNodeSize * 0.15f;
            float baseLabelSize = baseNodeSize * 3f;
            
            // Build node data map
            foreach (var node in nodes)
            {
                string id = node.TryGetValue("id", out var idVal) ? idVal?.ToString() : Guid.NewGuid().ToString();
                _nodeDataMap[id] = node;
            }
            
            // Calculate positions using layout algorithm (pass baseNodeSize for margin calculation)
            CalculateLayout(layoutSpec, nodes, edges, context, baseNodeSize);
            
            // Render edges first (so nodes appear on top)
            if (edges != null)
            {
                RenderEdges(context, edges, edgeEncoding, baseEdgeWidth);
            }
            
            // Render nodes
            RenderNodes(context, nodes, nodeEncoding, interactionSpec, baseNodeSize, baseLabelSize);
        }

        private void CalculateLayout(LayoutSpec layoutSpec, List<Dictionary<string, object>> nodes, 
            List<Dictionary<string, object>> edges, MarkRenderContext context, float baseNodeSize)
        {
            // Calculate graph size based on spec dimensions scaled by pixelScale
            float specWidth = context.Spec?.width ?? 500;
            float specHeight = context.Spec?.height ?? 500;
            float specDepth = context.Spec?.depth ?? 0;
            if (specDepth <= 0) specDepth = specWidth; // Default depth = width for cube shape
            
            float pixelScale = context.PixelScale;
            
            // In 3D mode, use pixelScale to convert spec dimensions to world units
            float graphWidth = specWidth * pixelScale;
            float graphHeight = specHeight * pixelScale;
            float graphDepth = specDepth * pixelScale;
            float graphSize = Mathf.Min(graphWidth, Mathf.Min(graphHeight, graphDepth));
            
            switch (layoutSpec.type?.ToLower())
            {
                case "force":
                    CalculateForceLayout(nodes, edges, layoutSpec.@params ?? new LayoutParams(), graphSize);
                    break;
                case "circular":
                    CalculateCircularLayout(nodes, layoutSpec.@params ?? new LayoutParams(), graphSize);
                    break;
                case "hierarchical":
                    CalculateHierarchicalLayout(nodes, edges, layoutSpec.@params ?? new LayoutParams(), graphSize);
                    break;
                case "grid":
                    CalculateGridLayout(nodes, layoutSpec.@params ?? new LayoutParams(), graphSize);
                    break;
                case "random":
                    CalculateRandomLayout(nodes, graphSize);
                    break;
                case "preset":
                    CalculatePresetLayout(nodes, context);
                    break;
                default:
                    CalculateForceLayout(nodes, edges, new LayoutParams(), graphSize);
                    break;
            }
            
            // Normalize positions: leave margin for node radius (nodes shouldn't clip cage edges)
            // Use 40% of graphSize to ensure nodes stay well within bounds
            float availableRadius = graphSize * 0.4f - baseNodeSize;
            availableRadius = Mathf.Max(availableRadius, baseNodeSize * 2); // Ensure minimum space
            NormalizePositions(availableRadius);
            
            Debug.Log($"[UVis Graph] graphSize={graphSize}, baseNodeSize={baseNodeSize}, availableRadius={availableRadius}");
        }
        
        private void NormalizePositions(float maxRadius)
        {
            if (_nodePositions.Count == 0) return;
            
            // Find current bounds
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            
            foreach (var pos in _nodePositions.Values)
            {
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            
            // Calculate center and current size
            Vector3 center = (min + max) / 2f;
            Vector3 size = max - min;
            float maxSize = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            
            Debug.Log($"[UVis Graph] Before normalize: min={min}, max={max}, maxSize={maxSize}");
            
            if (maxSize < 0.01f) maxSize = 1f; // Avoid division by zero
            
            // Scale factor to fit in maxRadius (diameter = maxRadius * 2)
            float scaleFactor = (maxRadius * 2f) / maxSize;
            
            // Clamp scale factor to prevent scaling up too much
            scaleFactor = Mathf.Min(scaleFactor, 1f);
            
            Debug.Log($"[UVis Graph] Normalize: maxRadius={maxRadius}, scaleFactor={scaleFactor}");
            
            // Recenter and scale all positions
            var keys = new List<string>(_nodePositions.Keys);
            foreach (var key in keys)
            {
                Vector3 pos = _nodePositions[key];
                pos = (pos - center) * scaleFactor;
                _nodePositions[key] = pos;
            }
            
            // Verify final bounds
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var pos in _nodePositions.Values)
            {
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            Debug.Log($"[UVis Graph] After normalize: min={min}, max={max}");
        }

        #region Layout Algorithms

        private void CalculateForceLayout(List<Dictionary<string, object>> nodes, 
            List<Dictionary<string, object>> edges, LayoutParams p, float graphSize)
        {
            // Initialize random positions
            System.Random rand = new System.Random(42);
            foreach (var node in nodes)
            {
                string id = node["id"].ToString();
                _nodePositions[id] = new Vector3(
                    (float)(rand.NextDouble() - 0.5) * graphSize,
                    (float)(rand.NextDouble() - 0.5) * graphSize,
                    (float)(rand.NextDouble() - 0.5) * graphSize * 0.5f
                );
            }
            
            // Build edge lookup
            var edgeList = new List<(string source, string target)>();
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    if (edge.TryGetValue("source", out var s) && edge.TryGetValue("target", out var t))
                    {
                        edgeList.Add((s.ToString(), t.ToString()));
                    }
                }
            }
            
            // Fruchterman-Reingold algorithm
            float area = graphSize * graphSize;
            float k = Mathf.Sqrt(area / nodes.Count); // Optimal distance
            
            for (int iter = 0; iter < p.iterations; iter++)
            {
                Dictionary<string, Vector3> displacement = new Dictionary<string, Vector3>();
                foreach (var node in nodes)
                {
                    displacement[node["id"].ToString()] = Vector3.zero;
                }
                
                // Repulsive forces between all pairs
                for (int i = 0; i < nodes.Count; i++)
                {
                    string id1 = nodes[i]["id"].ToString();
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        string id2 = nodes[j]["id"].ToString();
                        Vector3 delta = _nodePositions[id1] - _nodePositions[id2];
                        float distance = Mathf.Max(delta.magnitude, 0.01f);
                        float repForce = (k * k) / distance * (p.repulsion / 100f);
                        Vector3 dir = delta.normalized;
                        displacement[id1] += dir * repForce;
                        displacement[id2] -= dir * repForce;
                    }
                }
                
                // Attractive forces along edges
                foreach (var (source, target) in edgeList)
                {
                    if (!_nodePositions.ContainsKey(source) || !_nodePositions.ContainsKey(target))
                        continue;
                        
                    Vector3 delta = _nodePositions[source] - _nodePositions[target];
                    float distance = Mathf.Max(delta.magnitude, 0.01f);
                    float attForce = (distance * distance) / k * p.attraction;
                    Vector3 dir = delta.normalized;
                    displacement[source] -= dir * attForce;
                    displacement[target] += dir * attForce;
                }
                
                // Apply gravity toward center
                foreach (var node in nodes)
                {
                    string id = node["id"].ToString();
                    Vector3 toCenter = -_nodePositions[id];
                    displacement[id] += toCenter * p.gravity;
                }
                
                // Apply displacements with damping
                float temperature = graphSize * (1f - (float)iter / p.iterations);
                foreach (var node in nodes)
                {
                    string id = node["id"].ToString();
                    Vector3 disp = displacement[id];
                    float dispMag = disp.magnitude;
                    if (dispMag > 0)
                    {
                        _nodePositions[id] += disp.normalized * Mathf.Min(dispMag, temperature) * p.damping;
                    }
                }
            }
        }

        private void CalculateCircularLayout(List<Dictionary<string, object>> nodes, LayoutParams p, float graphSize)
        {
            float radius = p.radius > 0 ? p.radius : graphSize * 0.4f;
            float startAngle = p.startAngle * Mathf.Deg2Rad;
            float endAngle = p.endAngle * Mathf.Deg2Rad;
            float angleStep = (endAngle - startAngle) / nodes.Count;
            
            for (int i = 0; i < nodes.Count; i++)
            {
                string id = nodes[i]["id"].ToString();
                float angle = startAngle + i * angleStep;
                _nodePositions[id] = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0
                );
            }
        }

        private void CalculateHierarchicalLayout(List<Dictionary<string, object>> nodes, 
            List<Dictionary<string, object>> edges, LayoutParams p, float graphSize)
        {
            // Build parent-child relationships
            var children = new Dictionary<string, List<string>>();
            var parents = new Dictionary<string, string>();
            
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    if (edge.TryGetValue("source", out var s) && edge.TryGetValue("target", out var t))
                    {
                        string sourceId = s.ToString();
                        string targetId = t.ToString();
                        if (!children.ContainsKey(sourceId))
                            children[sourceId] = new List<string>();
                        children[sourceId].Add(targetId);
                        parents[targetId] = sourceId;
                    }
                }
            }
            
            // Find root nodes (no parents)
            var roots = new List<string>();
            foreach (var node in nodes)
            {
                string id = node["id"].ToString();
                if (!parents.ContainsKey(id))
                    roots.Add(id);
            }
            
            if (roots.Count == 0 && nodes.Count > 0)
            {
                roots.Add(nodes[0]["id"].ToString());
            }
            
            // Assign levels using BFS
            var levels = new Dictionary<string, int>();
            var queue = new Queue<string>();
            foreach (var root in roots)
            {
                levels[root] = 0;
                queue.Enqueue(root);
            }
            
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (children.TryGetValue(current, out var childList))
                {
                    foreach (var child in childList)
                    {
                        if (!levels.ContainsKey(child))
                        {
                            levels[child] = levels[current] + 1;
                            queue.Enqueue(child);
                        }
                    }
                }
            }
            
            // Group nodes by level
            var levelGroups = new Dictionary<int, List<string>>();
            foreach (var kvp in levels)
            {
                if (!levelGroups.ContainsKey(kvp.Value))
                    levelGroups[kvp.Value] = new List<string>();
                levelGroups[kvp.Value].Add(kvp.Key);
            }
            
            // Position nodes
            bool isVertical = p.direction == "TB" || p.direction == "BT";
            bool isReversed = p.direction == "BT" || p.direction == "RL";
            
            foreach (var kvp in levelGroups)
            {
                int level = kvp.Key;
                var nodesInLevel = kvp.Value;
                float levelPos = level * p.levelSeparation * (isReversed ? -1 : 1);
                
                for (int i = 0; i < nodesInLevel.Count; i++)
                {
                    float nodePos = (i - (nodesInLevel.Count - 1) / 2f) * p.nodeSeparation;
                    
                    if (isVertical)
                    {
                        _nodePositions[nodesInLevel[i]] = new Vector3(nodePos, -levelPos, 0);
                    }
                    else
                    {
                        _nodePositions[nodesInLevel[i]] = new Vector3(levelPos, nodePos, 0);
                    }
                }
            }
            
            // Handle nodes without levels
            float orphanY = (levelGroups.Count + 1) * p.levelSeparation;
            int orphanIndex = 0;
            foreach (var node in nodes)
            {
                string id = node["id"].ToString();
                if (!_nodePositions.ContainsKey(id))
                {
                    _nodePositions[id] = new Vector3(orphanIndex * p.nodeSeparation, orphanY, 0);
                    orphanIndex++;
                }
            }
        }

        private void CalculateGridLayout(List<Dictionary<string, object>> nodes, LayoutParams p, float graphSize)
        {
            int columns = p.columns > 0 ? p.columns : Mathf.CeilToInt(Mathf.Sqrt(nodes.Count));
            float spacing = p.spacing > 0 ? p.spacing : graphSize / columns;
            
            for (int i = 0; i < nodes.Count; i++)
            {
                string id = nodes[i]["id"].ToString();
                int row = i / columns;
                int col = i % columns;
                
                float x = (col - (columns - 1) / 2f) * spacing;
                float y = -(row - (Mathf.CeilToInt((float)nodes.Count / columns) - 1) / 2f) * spacing;
                
                _nodePositions[id] = new Vector3(x, y, 0);
            }
        }

        private void CalculateRandomLayout(List<Dictionary<string, object>> nodes, float graphSize)
        {
            System.Random rand = new System.Random();
            foreach (var node in nodes)
            {
                string id = node["id"].ToString();
                _nodePositions[id] = new Vector3(
                    (float)(rand.NextDouble() - 0.5) * graphSize,
                    (float)(rand.NextDouble() - 0.5) * graphSize,
                    (float)(rand.NextDouble() - 0.5) * graphSize * 0.3f
                );
            }
        }

        private void CalculatePresetLayout(List<Dictionary<string, object>> nodes, MarkRenderContext context)
        {
            float scale = context.Is3DMode ? 1f : context.PixelScale;
            
            foreach (var node in nodes)
            {
                string id = node["id"].ToString();
                float x = node.TryGetValue("x", out var xVal) ? Convert.ToSingle(xVal) * scale : 0;
                float y = node.TryGetValue("y", out var yVal) ? Convert.ToSingle(yVal) * scale : 0;
                float z = node.TryGetValue("z", out var zVal) ? Convert.ToSingle(zVal) * scale : 0;
                
                _nodePositions[id] = new Vector3(x, y, z);
            }
        }

        #endregion

        #region Node Rendering

        private void RenderNodes(MarkRenderContext context, List<Dictionary<string, object>> nodes,
            NodeEncodingSpec encoding, InteractionSpec interaction, float baseNodeSize, float baseLabelSize)
        {
            // Build color scale if needed
            OrdinalColorScale colorScale = null;
            if (encoding?.color?.field != null)
            {
                colorScale = context.ColorScale;
            }
            
            // Default color
            Color defaultColor = new Color(0.31f, 0.47f, 0.65f); // #4e79a7
            if (encoding?.color?.value != null)
            {
                ColorUtility.TryParseHtmlString(encoding.color.value, out defaultColor);
            }
            
            foreach (var node in nodes)
            {
                string id = node["id"].ToString();
                if (!_nodePositions.TryGetValue(id, out Vector3 position))
                    continue;
                
                // Get node size (use baseNodeSize as default)
                float size = baseNodeSize;
                if (encoding?.size?.field != null && node.TryGetValue(encoding.size.field, out var sizeVal))
                {
                    size = Convert.ToSingle(sizeVal) * 0.01f;
                    if (encoding.size.scale?.range != null && encoding.size.scale.range.Count >= 2)
                    {
                        // Normalize to range
                        size = Mathf.Lerp(encoding.size.scale.range[0], encoding.size.scale.range[1], 
                            Mathf.Clamp01(size * 10));
                    }
                }
                
                // Get node color
                Color color = defaultColor;
                if (encoding?.color?.field != null && node.TryGetValue(encoding.color.field, out var colorVal))
                {
                    if (colorScale != null)
                    {
                        color = colorScale.Map(colorVal);
                    }
                }
                
                // Get node shape
                string shape = "sphere";
                if (encoding?.shape?.field != null && node.TryGetValue(encoding.shape.field, out var shapeVal))
                {
                    shape = shapeVal?.ToString() ?? "sphere";
                }
                else if (encoding?.shape?.value != null)
                {
                    shape = encoding.shape.value;
                }
                
                // Create node GameObject
                GameObject nodeGo = CreateNodeGameObject(context, id, position, size, color, shape);
                
                // Add interaction components if enabled
                if (interaction?.node != null)
                {
                    AddNodeInteraction(nodeGo, node, interaction.node);
                }
                
                // Create label if specified
                if (encoding?.label?.field != null && node.TryGetValue(encoding.label.field, out var labelVal))
                {
                    CreateNodeLabel(context, nodeGo, labelVal?.ToString() ?? "", size);
                }
                
                _nodeGameObjects[id] = nodeGo;
                _nodes.Add(nodeGo);
            }
        }

        private GameObject CreateNodeGameObject(MarkRenderContext context, string id, Vector3 position, 
            float size, Color color, string shape)
        {
            GameObject go;
            
            if (shape.StartsWith("prefab:"))
            {
                string prefabName = shape.Substring(7);
                var prefab = Resources.Load<GameObject>(prefabName);
                if (prefab != null)
                {
                    go = UnityEngine.Object.Instantiate(prefab);
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
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
            }
            
            go.name = $"Node_{id}";
            go.transform.SetParent(context.PlotRoot, false);
            
            // Calculate plot area center from scales (matching how Render3DFrame calculates bounds)
            float xMin = context.XScale?.RangeMin ?? 0;
            float xMax = context.XScale?.RangeMax ?? (context.Spec?.width ?? 500) * context.PixelScale;
            float yMin = context.YScale?.RangeMin ?? 0;
            float yMax = context.YScale?.RangeMax ?? (context.Spec?.height ?? 500) * context.PixelScale;
            float zMin = context.ZScale?.RangeMin ?? 0;
            float zMax = context.ZScale?.RangeMax ?? xMax; // Z defaults to match X
            
            // Center of the plot area
            Vector3 plotCenter = new Vector3(
                (xMin + xMax) / 2f,
                (yMin + yMax) / 2f,
                (zMin + zMax) / 2f
            );
            
            Vector3 finalPos = position + plotCenter;
            go.transform.localPosition = finalPos;
            go.transform.localScale = Vector3.one * size;
            
            // Debug first node only
            if (_nodes.Count == 0)
            {
                Debug.Log($"[UVis Graph] Node '{id}': inputPos={position}, plotCenter={plotCenter}, finalLocalPos={finalPos}, scales=({xMin}-{xMax}, {yMin}-{yMax}, {zMin}-{zMax})");
            }
            
            // Apply material
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material = CreateMaterial(context, color);
                renderer.material = material;
            }
            
            // Store node data
            var nodeData = go.AddComponent<GraphNodeData>();
            nodeData.NodeId = id;
            nodeData.Data = _nodeDataMap.ContainsKey(id) ? _nodeDataMap[id] : null;
            
            return go;
        }

        private void AddNodeInteraction(GameObject nodeGo, Dictionary<string, object> nodeData, 
            NodeInteractionSpec interactionSpec)
        {
            // Add collider for interaction (if not already present)
            if (nodeGo.GetComponent<Collider>() == null)
            {
                nodeGo.AddComponent<SphereCollider>();
            }
            
            // Add interaction component
            var interactable = nodeGo.AddComponent<GraphNodeInteractable>();
            interactable.IsDraggable = interactionSpec.draggable;
            interactable.IsHoverable = interactionSpec.hoverable;
            interactable.IsSelectable = interactionSpec.selectable;
            interactable.TooltipSpec = interactionSpec.hoverTooltip;
            interactable.HighlightSpec = interactionSpec.highlight;
            interactable.NodeData = nodeData;
        }

        private void CreateNodeLabel(MarkRenderContext context, GameObject nodeGo, string text, float nodeSize)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(nodeGo.transform, false);
            labelGo.transform.localPosition = new Vector3(0, nodeSize + 0.1f, 0);
            
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            
            // Billboard to camera
            var billboard = labelGo.AddComponent<LookAtCamera>();
            
            _labels.Add(labelGo);
        }

        #endregion

        #region Edge Rendering

        private void RenderEdges(MarkRenderContext context, List<Dictionary<string, object>> edges,
            EdgeEncodingSpec encoding, float baseEdgeWidth)
        {
            // Default color
            Color defaultColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            if (encoding?.color?.value != null)
            {
                ColorUtility.TryParseHtmlString(encoding.color.value, out defaultColor);
            }
            
            foreach (var edge in edges)
            {
                if (!edge.TryGetValue("source", out var sourceVal) || 
                    !edge.TryGetValue("target", out var targetVal))
                    continue;
                
                string sourceId = sourceVal.ToString();
                string targetId = targetVal.ToString();
                
                if (!_nodePositions.TryGetValue(sourceId, out Vector3 sourcePos) ||
                    !_nodePositions.TryGetValue(targetId, out Vector3 targetPos))
                    continue;
                
                // Get edge width (use baseEdgeWidth as default, scale by field if specified)
                float width = baseEdgeWidth;
                if (encoding?.width?.field != null && edge.TryGetValue(encoding.width.field, out var widthVal))
                {
                    // Scale the base width by the data value (normalized)
                    float dataValue = Convert.ToSingle(widthVal);
                    // Map data value to a scale multiplier (e.g., 1-10 -> 0.5x-2x)
                    float multiplier = Mathf.Lerp(0.5f, 2f, Mathf.Clamp01(dataValue / 10f));
                    width = baseEdgeWidth * multiplier;
                }
                
                // Get edge color
                Color color = defaultColor;
                if (encoding?.color?.field != null && edge.TryGetValue(encoding.color.field, out var colorVal))
                {
                    // TODO: Use color scale for edges
                }
                
                // Create edge
                GameObject edgeGo = CreateEdge3D(context, sourcePos, targetPos, width, color);
                
                // Store edge data for interaction
                var edgeData = edgeGo.AddComponent<GraphEdgeData>();
                edgeData.SourceId = sourceId;
                edgeData.TargetId = targetId;
                edgeData.Data = edge;
                
                _edges.Add(edgeGo);
            }
        }

        private GameObject CreateEdge3D(MarkRenderContext context, Vector3 start, Vector3 end, 
            float width, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Edge";
            go.transform.SetParent(context.PlotRoot, false);
            
            // Calculate plot area center from scales (same as nodes)
            float xMin = context.XScale?.RangeMin ?? 0;
            float xMax = context.XScale?.RangeMax ?? (context.Spec?.width ?? 500) * context.PixelScale;
            float yMin = context.YScale?.RangeMin ?? 0;
            float yMax = context.YScale?.RangeMax ?? (context.Spec?.height ?? 500) * context.PixelScale;
            float zMin = context.ZScale?.RangeMin ?? 0;
            float zMax = context.ZScale?.RangeMax ?? xMax;
            
            Vector3 plotCenter = new Vector3(
                (xMin + xMax) / 2f,
                (yMin + yMax) / 2f,
                (zMin + zMax) / 2f
            );
            
            // Offset start and end positions
            Vector3 offsetStart = start + plotCenter;
            Vector3 offsetEnd = end + plotCenter;
            
            // Position at midpoint
            Vector3 midpoint = (offsetStart + offsetEnd) / 2f;
            go.transform.localPosition = midpoint;
            
            // Orient toward target
            Vector3 direction = end - start;
            go.transform.up = direction.normalized;
            
            // Scale to length and width
            float length = direction.magnitude;
            go.transform.localScale = new Vector3(width, length / 2f, width);
            
            // Apply material
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material = CreateMaterial(context, color);
                renderer.material = material;
            }
            
            // Remove collider (edges don't need physics by default, can be added for interaction)
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(collider);
                else
                    UnityEngine.Object.DestroyImmediate(collider);
            }
            
            return go;
        }

        #endregion

        #region Helpers

        private Material CreateMaterial(MarkRenderContext context, Color color)
        {
            Material material;
            if (context.DefaultMaterial != null)
            {
                material = new Material(context.DefaultMaterial);
            }
            else
            {
                // Try URP shaders first (Simple Lit is more reliable than Lit), then Standard
                var shader = Shader.Find("Universal Render Pipeline/Simple Lit")
                          ?? Shader.Find("Universal Render Pipeline/Lit") 
                          ?? Shader.Find("Standard")
                          ?? Shader.Find("Unlit/Color");
                
                material = new Material(shader);
            }
            
            // Set color based on shader type
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color); // URP uses _BaseColor
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color); // Standard/Legacy uses _Color
            }
            material.color = color;
            
            // Configure for clean matte appearance
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.1f);
            
            return material;
        }

        #endregion

        public void Clear()
        {
            foreach (var node in _nodes)
            {
                if (node != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(node);
                    else
                        UnityEngine.Object.DestroyImmediate(node);
                }
            }
            _nodes.Clear();
            
            foreach (var edge in _edges)
            {
                if (edge != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(edge);
                    else
                        UnityEngine.Object.DestroyImmediate(edge);
                }
            }
            _edges.Clear();
            
            foreach (var label in _labels)
            {
                if (label != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(label);
                    else
                        UnityEngine.Object.DestroyImmediate(label);
                }
            }
            _labels.Clear();
            
            _nodePositions.Clear();
            _nodeDataMap.Clear();
            _nodeGameObjects.Clear();
        }
    }

    /// <summary>
    /// Component to store node data on GameObject.
    /// </summary>
    public class GraphNodeData : MonoBehaviour
    {
        public string NodeId;
        public Dictionary<string, object> Data;
    }

    /// <summary>
    /// Component to store edge data on GameObject.
    /// </summary>
    public class GraphEdgeData : MonoBehaviour
    {
        public string SourceId;
        public string TargetId;
        public Dictionary<string, object> Data;
    }

    /// <summary>
    /// Component for node interaction (hover, drag, select).
    /// </summary>
    public class GraphNodeInteractable : MonoBehaviour
    {
        public bool IsDraggable = true;
        public bool IsHoverable = true;
        public bool IsSelectable = true;
        public TooltipSpec TooltipSpec;
        public HighlightSpec HighlightSpec;
        public Dictionary<string, object> NodeData;
        
        private Vector3 _originalScale;
        private Color _originalColor;
        private bool _isHovered;
        private bool _isSelected;
        private bool _isDragging;
        private GameObject _tooltipPanel;
        private Renderer _renderer;
        
        // Events
        public static event Action<GraphNodeInteractable> OnNodeHoverEnter;
        public static event Action<GraphNodeInteractable> OnNodeHoverExit;
        public static event Action<GraphNodeInteractable> OnNodeSelected;
        public static event Action<GraphNodeInteractable, Vector3> OnNodeDragStart;
        public static event Action<GraphNodeInteractable, Vector3> OnNodeDragging;
        public static event Action<GraphNodeInteractable, Vector3> OnNodeDragEnd;

        private void Start()
        {
            _originalScale = transform.localScale;
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
            {
                _originalColor = _renderer.material.color;
            }
        }

        private void OnMouseEnter()
        {
            if (!IsHoverable) return;
            
            _isHovered = true;
            ApplyHighlight();
            ShowTooltip();
            OnNodeHoverEnter?.Invoke(this);
        }

        private void OnMouseExit()
        {
            if (!IsHoverable) return;
            
            _isHovered = false;
            if (!_isSelected)
            {
                RemoveHighlight();
            }
            HideTooltip();
            OnNodeHoverExit?.Invoke(this);
        }

        private void OnMouseDown()
        {
            if (IsSelectable)
            {
                _isSelected = !_isSelected;
                OnNodeSelected?.Invoke(this);
            }
            
            if (IsDraggable)
            {
                _isDragging = true;
                OnNodeDragStart?.Invoke(this, transform.position);
            }
        }

        private void OnMouseUp()
        {
            if (_isDragging)
            {
                _isDragging = false;
                OnNodeDragEnd?.Invoke(this, transform.position);
            }
        }

        private void OnMouseDrag()
        {
            if (!IsDraggable || !_isDragging) return;
            
            // Simple drag implementation (camera-based)
            if (Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                Plane plane = new Plane(-Camera.main.transform.forward, transform.position);
                if (plane.Raycast(ray, out float distance))
                {
                    transform.position = ray.GetPoint(distance);
                    OnNodeDragging?.Invoke(this, transform.position);
                }
            }
        }

        private void ApplyHighlight()
        {
            if (HighlightSpec == null) return;
            
            // Scale
            transform.localScale = _originalScale * HighlightSpec.scale;
            
            // Color
            if (_renderer != null && !string.IsNullOrEmpty(HighlightSpec.color))
            {
                if (ColorUtility.TryParseHtmlString(HighlightSpec.color, out Color highlightColor))
                {
                    _renderer.material.color = highlightColor;
                }
            }
        }

        private void RemoveHighlight()
        {
            transform.localScale = _originalScale;
            if (_renderer != null)
            {
                _renderer.material.color = _originalColor;
            }
        }

        private void ShowTooltip()
        {
            if (TooltipSpec == null || TooltipSpec.fields == null || NodeData == null) return;
            
            // Build tooltip text
            string text = TooltipSpec.format;
            if (string.IsNullOrEmpty(text))
            {
                text = string.Join("\n", TooltipSpec.fields.ConvertAll(f => 
                    NodeData.TryGetValue(f, out var v) ? $"{f}: {v}" : ""));
            }
            else
            {
                foreach (var field in TooltipSpec.fields)
                {
                    if (NodeData.TryGetValue(field, out var value))
                    {
                        text = text.Replace($"{{{field}}}", value?.ToString() ?? "");
                    }
                }
            }
            
            // Create tooltip panel
            if (_tooltipPanel == null)
            {
                _tooltipPanel = new GameObject("Tooltip");
                _tooltipPanel.transform.SetParent(transform, false);
                
                var tmp = _tooltipPanel.AddComponent<TextMeshPro>();
                tmp.fontSize = 1.5f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                
                // Position above node
                float offset = transform.localScale.y + 0.3f;
                _tooltipPanel.transform.localPosition = new Vector3(0, offset, 0);
            }
            
            var textMesh = _tooltipPanel.GetComponent<TextMeshPro>();
            if (textMesh != null)
            {
                textMesh.text = text;
            }
            
            _tooltipPanel.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltipPanel != null)
            {
                _tooltipPanel.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Simple billboard component to face camera.
    /// </summary>
    public class LookAtCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Camera.main != null)
            {
                transform.LookAt(transform.position + Camera.main.transform.forward);
            }
        }
    }
}
