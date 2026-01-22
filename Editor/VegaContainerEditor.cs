using UnityEngine;
using UnityEditor;
using UVis.Core;
using UVis.Data;
using System.Collections.Generic;

#if DATACORE_INSTALLED
using AroAro.DataCore;
#endif

namespace UVis.Editor
{
    /// <summary>
    /// Menu items for creating UVis objects from Hierarchy context menu.
    /// </summary>
    public static class UVisMenuItems
    {
        private const string DEFAULT_BAR_CHART = @"{
  ""data"": {
    ""values"": [
      {""category"": ""A"", ""value"": 30},
      {""category"": ""B"", ""value"": 80},
      {""category"": ""C"", ""value"": 45},
      {""category"": ""D"", ""value"": 60}
    ]
  },
  ""mark"": ""bar"",
  ""encoding"": {
    ""x"": {""field"": ""category"", ""type"": ""ordinal""},
    ""y"": {""field"": ""value"", ""type"": ""quantitative""},
    ""color"": {""value"": ""#4e79a7""}
  },
  ""width"": 640,
  ""height"": 400
}";

        /// <summary>
        /// Create a VegaContainer (2D Canvas mode) from Hierarchy menu.
        /// </summary>
        [MenuItem("GameObject/UVis/Vega Container (2D)", false, 10)]
        public static void CreateVegaContainer2D(MenuCommand menuCommand)
        {
            var go = new GameObject("VegaChart");
            var container = go.AddComponent<VegaContainer>();
            
            // Set default spec via reflection (since field is private)
            var field = typeof(VegaContainer).GetField("_chartSpecJson", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(container, DEFAULT_BAR_CHART);

            // Parent to context object if available
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            // Register undo
            Undo.RegisterCreatedObjectUndo(go, "Create Vega Container (2D)");
            
            // Select the new object
            Selection.activeGameObject = go;
        }

        /// <summary>
        /// Create a VegaContainer (3D World Space mode) from Hierarchy menu.
        /// </summary>
        [MenuItem("GameObject/UVis/Vega Container (3D)", false, 11)]
        public static void CreateVegaContainer3D(MenuCommand menuCommand)
        {
            var go = new GameObject("VegaChart3D");
            var container = go.AddComponent<VegaContainer>();
            
            // Set 3D mode via reflection
            var modeField = typeof(VegaContainer).GetField("_renderMode", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            modeField?.SetValue(container, VegaContainer.RenderMode.WorldSpace3D);
            
            // Set default spec
            var specField = typeof(VegaContainer).GetField("_chartSpecJson", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            specField?.SetValue(container, DEFAULT_BAR_CHART);

            // Parent to context object if available
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            // Register undo
            Undo.RegisterCreatedObjectUndo(go, "Create Vega Container (3D)");
            
            // Select the new object
            Selection.activeGameObject = go;
        }

        /// <summary>
        /// Create a Bar Chart from Hierarchy menu.
        /// </summary>
        [MenuItem("GameObject/UVis/Charts/Bar Chart", false, 20)]
        public static void CreateBarChart(MenuCommand menuCommand)
        {
            CreateVegaContainer2D(menuCommand);
            Selection.activeGameObject.name = "BarChart";
        }

        /// <summary>
        /// Create a Line Chart from Hierarchy menu.
        /// </summary>
        [MenuItem("GameObject/UVis/Charts/Line Chart", false, 21)]
        public static void CreateLineChart(MenuCommand menuCommand)
        {
            var go = new GameObject("LineChart");
            var container = go.AddComponent<VegaContainer>();
            
            var lineChartSpec = @"{
  ""data"": {
    ""values"": [
      {""x"": 0, ""y"": 10},
      {""x"": 1, ""y"": 25},
      {""x"": 2, ""y"": 15},
      {""x"": 3, ""y"": 40},
      {""x"": 4, ""y"": 35}
    ]
  },
  ""mark"": ""line"",
  ""encoding"": {
    ""x"": {""field"": ""x"", ""type"": ""quantitative""},
    ""y"": {""field"": ""y"", ""type"": ""quantitative""},
    ""color"": {""value"": ""#f28e2c""}
  },
  ""width"": 640,
  ""height"": 400
}";
            
            var field = typeof(VegaContainer).GetField("_chartSpecJson", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(container, lineChartSpec);

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Line Chart");
            Selection.activeGameObject = go;
        }

        /// <summary>
        /// Create a Scatter Plot from Hierarchy menu.
        /// </summary>
        [MenuItem("GameObject/UVis/Charts/Scatter Plot", false, 22)]
        public static void CreateScatterPlot(MenuCommand menuCommand)
        {
            var go = new GameObject("ScatterPlot");
            var container = go.AddComponent<VegaContainer>();
            
            var scatterSpec = @"{
  ""data"": {
    ""values"": [
      {""x"": 10, ""y"": 20, ""size"": 5},
      {""x"": 25, ""y"": 15, ""size"": 10},
      {""x"": 40, ""y"": 35, ""size"": 7},
      {""x"": 55, ""y"": 45, ""size"": 12},
      {""x"": 70, ""y"": 30, ""size"": 8}
    ]
  },
  ""mark"": ""point"",
  ""encoding"": {
    ""x"": {""field"": ""x"", ""type"": ""quantitative""},
    ""y"": {""field"": ""y"", ""type"": ""quantitative""},
    ""size"": {""field"": ""size""},
    ""color"": {""value"": ""#e15759""}
  },
  ""width"": 640,
  ""height"": 400
}";
            
            var field = typeof(VegaContainer).GetField("_chartSpecJson", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(container, scatterSpec);

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Scatter Plot");
            Selection.activeGameObject = go;
        }
    }

    /// <summary>
    /// Custom editor for VegaContainer with enhanced specification editing.
    /// </summary>
    [CustomEditor(typeof(VegaContainer))]
    public class VegaContainerEditor : UnityEditor.Editor
    {
        private SerializedProperty _renderModeProp;
        private SerializedProperty _chartSpecAssetProp;
        private SerializedProperty _chartSpecJsonProp;
        private SerializedProperty _targetCanvasProp;
        private SerializedProperty _defaultMaterial2DProp;
        private SerializedProperty _plotRootProp;
        private SerializedProperty _defaultMaterial3DProp;
        private SerializedProperty _pixelScaleProp;
        private SerializedProperty _fontProp;

        private bool _showJsonEditor = true;
        private Vector2 _jsonScrollPos;
        
        // DataCore UI state
        private bool _showDataCoreSection = true;
        private Vector2 _datasetScrollPos;

        private static readonly string[] TEMPLATE_NAMES = new string[]
        {
            "Bar Chart",
            "Stacked Bar Chart",
            "Grouped Bar Chart",
            "3D Bar Chart",
            "3D Scatter Plot",
            "Graph (Force Layout)",
            "Color Scale Demo",
            "Line Chart",
            "Scatter Plot",
            "Custom..."
        };

        private static readonly string[] TEMPLATE_JSON = new string[]
        {
            // Bar Chart
            @"{
  ""data"": {
    ""values"": [
      {""category"": ""A"", ""value"": 30},
      {""category"": ""B"", ""value"": 80},
      {""category"": ""C"", ""value"": 45},
      {""category"": ""D"", ""value"": 60}
    ]
  },
  ""mark"": ""bar"",
  ""encoding"": {
    ""x"": {""field"": ""category"", ""type"": ""ordinal""},
    ""y"": {""field"": ""value"", ""type"": ""quantitative""},
    ""color"": {""value"": ""#4e79a7""}
  },
  ""width"": 640,
  ""height"": 400
}",
            // Stacked Bar Chart
            @"{
  ""data"": {
    ""values"": [
      {""category"": ""A"", ""series"": ""Q1"", ""value"": 30},
      {""category"": ""A"", ""series"": ""Q2"", ""value"": 25},
      {""category"": ""A"", ""series"": ""Q3"", ""value"": 40},
      {""category"": ""B"", ""series"": ""Q1"", ""value"": 50},
      {""category"": ""B"", ""series"": ""Q2"", ""value"": 35},
      {""category"": ""B"", ""series"": ""Q3"", ""value"": 45},
      {""category"": ""C"", ""series"": ""Q1"", ""value"": 40},
      {""category"": ""C"", ""series"": ""Q2"", ""value"": 60},
      {""category"": ""C"", ""series"": ""Q3"", ""value"": 30}
    ]
  },
  ""mark"": ""bar"",
  ""encoding"": {
    ""x"": {""field"": ""category"", ""type"": ""ordinal""},
    ""y"": {""field"": ""value"", ""type"": ""quantitative""},
    ""color"": {""field"": ""series"", ""type"": ""nominal""}
  },
  ""width"": 640,
  ""height"": 400
}",
            // Grouped Bar Chart (stack: null for side-by-side bars)
            @"{
  ""data"": {
    ""values"": [
      {""category"": ""A"", ""quarter"": ""Q1"", ""sales"": 30},
      {""category"": ""A"", ""quarter"": ""Q2"", ""sales"": 45},
      {""category"": ""A"", ""quarter"": ""Q3"", ""sales"": 35},
      {""category"": ""B"", ""quarter"": ""Q1"", ""sales"": 50},
      {""category"": ""B"", ""quarter"": ""Q2"", ""sales"": 40},
      {""category"": ""B"", ""quarter"": ""Q3"", ""sales"": 55},
      {""category"": ""C"", ""quarter"": ""Q1"", ""sales"": 25},
      {""category"": ""C"", ""quarter"": ""Q2"", ""sales"": 60},
      {""category"": ""C"", ""quarter"": ""Q3"", ""sales"": 45}
    ]
  },
  ""mark"": ""bar"",
  ""encoding"": {
    ""x"": {""field"": ""category"", ""type"": ""ordinal""},
    ""y"": {""field"": ""sales"", ""type"": ""quantitative"", ""stack"": null},
    ""color"": {""field"": ""quarter"", ""type"": ""nominal""}
  },
  ""width"": 640,
  ""height"": 400
}",
            // 3D Bar Chart (requires WorldSpace3D mode) - 5x6 grid
            @"{
  ""title"": ""3D Bar Chart - Monthly Sales by Category"",
  ""data"": {
    ""values"": [
      {""month"": ""Jan"", ""category"": ""Electronics"", ""sales"": 150},
      {""month"": ""Jan"", ""category"": ""Clothing"", ""sales"": 85},
      {""month"": ""Jan"", ""category"": ""Food"", ""sales"": 120},
      {""month"": ""Jan"", ""category"": ""Books"", ""sales"": 45},
      {""month"": ""Jan"", ""category"": ""Sports"", ""sales"": 70},
      {""month"": ""Feb"", ""category"": ""Electronics"", ""sales"": 180},
      {""month"": ""Feb"", ""category"": ""Clothing"", ""sales"": 110},
      {""month"": ""Feb"", ""category"": ""Food"", ""sales"": 135},
      {""month"": ""Feb"", ""category"": ""Books"", ""sales"": 55},
      {""month"": ""Feb"", ""category"": ""Sports"", ""sales"": 85},
      {""month"": ""Mar"", ""category"": ""Electronics"", ""sales"": 200},
      {""month"": ""Mar"", ""category"": ""Clothing"", ""sales"": 140},
      {""month"": ""Mar"", ""category"": ""Food"", ""sales"": 125},
      {""month"": ""Mar"", ""category"": ""Books"", ""sales"": 60},
      {""month"": ""Mar"", ""category"": ""Sports"", ""sales"": 110}
    ]
  },
  ""mark"": ""bar"",
  ""encoding"": {
    ""x"": {""field"": ""category"", ""type"": ""ordinal""},
    ""z"": {""field"": ""month"", ""type"": ""ordinal""},
    ""y"": {""field"": ""sales"", ""type"": ""quantitative""},
    ""color"": {""field"": ""category"", ""type"": ""nominal""}
  },
  ""width"": 500,
  ""height"": 350,
  ""depth"": 500
}",
            // 3D Scatter Plot (requires WorldSpace3D mode)
            @"{
  ""title"": ""3D Scatter Plot - Particle Distribution"",
  ""data"": {
    ""values"": [
      {""x"": 10, ""y"": 25, ""z"": 15, ""category"": ""A""},
      {""x"": 35, ""y"": 60, ""z"": 40, ""category"": ""A""},
      {""x"": 55, ""y"": 30, ""z"": 65, ""category"": ""A""},
      {""x"": 20, ""y"": 75, ""z"": 30, ""category"": ""A""},
      {""x"": 70, ""y"": 45, ""z"": 25, ""category"": ""A""},
      {""x"": 25, ""y"": 50, ""z"": 70, ""category"": ""B""},
      {""x"": 60, ""y"": 70, ""z"": 35, ""category"": ""B""},
      {""x"": 40, ""y"": 15, ""z"": 85, ""category"": ""B""},
      {""x"": 85, ""y"": 55, ""z"": 20, ""category"": ""B""},
      {""x"": 30, ""y"": 90, ""z"": 45, ""category"": ""B""},
      {""x"": 5, ""y"": 10, ""z"": 90, ""category"": ""C""},
      {""x"": 65, ""y"": 95, ""z"": 5, ""category"": ""C""},
      {""x"": 95, ""y"": 5, ""z"": 95, ""category"": ""C""},
      {""x"": 45, ""y"": 45, ""z"": 45, ""category"": ""C""}
    ]
  },
  ""mark"": ""point"",
  ""encoding"": {
    ""x"": {""field"": ""x"", ""type"": ""quantitative""},
    ""y"": {""field"": ""y"", ""type"": ""quantitative""},
    ""z"": {""field"": ""z"", ""type"": ""quantitative""},
    ""color"": {""field"": ""category"", ""type"": ""nominal""},
    ""shape"": {""field"": ""category"", ""type"": ""nominal""}
  },
  ""width"": 400,
  ""height"": 400,
  ""depth"": 400
}",
            // Graph (Force Layout) - requires WorldSpace3D mode
            @"{
  ""title"": ""Social Network Graph"",
  ""mark"": ""graph"",
  ""data"": {
    ""nodes"": [
      {""id"": ""alice"", ""label"": ""Alice"", ""dept"": ""Eng"", ""level"": 3},
      {""id"": ""bob"", ""label"": ""Bob"", ""dept"": ""Eng"", ""level"": 2},
      {""id"": ""carol"", ""label"": ""Carol"", ""dept"": ""Mkt"", ""level"": 2},
      {""id"": ""david"", ""label"": ""David"", ""dept"": ""Sales"", ""level"": 1},
      {""id"": ""emma"", ""label"": ""Emma"", ""dept"": ""Mkt"", ""level"": 2},
      {""id"": ""frank"", ""label"": ""Frank"", ""dept"": ""Eng"", ""level"": 1}
    ],
    ""edges"": [
      {""source"": ""alice"", ""target"": ""bob"", ""weight"": 8},
      {""source"": ""alice"", ""target"": ""carol"", ""weight"": 5},
      {""source"": ""bob"", ""target"": ""frank"", ""weight"": 10},
      {""source"": ""carol"", ""target"": ""emma"", ""weight"": 7},
      {""source"": ""carol"", ""target"": ""david"", ""weight"": 3},
      {""source"": ""david"", ""target"": ""emma"", ""weight"": 4}
    ]
  },
  ""layout"": {""type"": ""force""},
  ""encoding"": {
    ""node"": {
      ""color"": {""field"": ""dept"", ""type"": ""nominal""},
      ""label"": {""field"": ""label""}
    },
    ""edge"": {
      ""width"": {""field"": ""weight"", ""type"": ""quantitative""}
    }
  },
  ""interaction"": {
    ""node"": {""draggable"": true, ""hoverable"": true}
  },
  ""width"": 500,
  ""height"": 500,
  ""depth"": 500
}",
            // Color Scale Demo
            @"{
  ""data"": {
    ""values"": [
      {""month"": ""Jan"", ""product"": ""A"", ""sales"": 120},
      {""month"": ""Jan"", ""product"": ""B"", ""sales"": 80},
      {""month"": ""Jan"", ""product"": ""C"", ""sales"": 150},
      {""month"": ""Feb"", ""product"": ""A"", ""sales"": 140},
      {""month"": ""Feb"", ""product"": ""B"", ""sales"": 95},
      {""month"": ""Feb"", ""product"": ""C"", ""sales"": 130},
      {""month"": ""Mar"", ""product"": ""A"", ""sales"": 160},
      {""month"": ""Mar"", ""product"": ""B"", ""sales"": 110},
      {""month"": ""Mar"", ""product"": ""C"", ""sales"": 145}
    ]
  },
  ""mark"": ""bar"",
  ""encoding"": {
    ""x"": {""field"": ""month"", ""type"": ""ordinal""},
    ""y"": {""field"": ""sales"", ""type"": ""quantitative""},
    ""color"": {""field"": ""product"", ""type"": ""nominal""}
  },
  ""width"": 640,
  ""height"": 400
}",
            // Line Chart
            @"{
  ""data"": {
    ""values"": [
      {""x"": 0, ""y"": 10},
      {""x"": 1, ""y"": 25},
      {""x"": 2, ""y"": 15},
      {""x"": 3, ""y"": 40},
      {""x"": 4, ""y"": 35}
    ]
  },
  ""mark"": ""line"",
  ""encoding"": {
    ""x"": {""field"": ""x"", ""type"": ""quantitative""},
    ""y"": {""field"": ""y"", ""type"": ""quantitative""},
    ""color"": {""value"": ""#f28e2c""}
  },
  ""width"": 640,
  ""height"": 400
}",
            // Scatter Plot
            @"{
  ""data"": {
    ""values"": [
      {""x"": 10, ""y"": 20, ""size"": 5},
      {""x"": 25, ""y"": 15, ""size"": 10},
      {""x"": 40, ""y"": 35, ""size"": 7},
      {""x"": 55, ""y"": 45, ""size"": 12},
      {""x"": 70, ""y"": 30, ""size"": 8}
    ]
  },
  ""mark"": ""point"",
  ""encoding"": {
    ""x"": {""field"": ""x"", ""type"": ""quantitative""},
    ""y"": {""field"": ""y"", ""type"": ""quantitative""},
    ""size"": {""field"": ""size""},
    ""color"": {""value"": ""#e15759""}
  },
  ""width"": 640,
  ""height"": 400
}"
        };

        private void OnEnable()
        {
            _renderModeProp = serializedObject.FindProperty("_renderMode");
            _chartSpecAssetProp = serializedObject.FindProperty("_chartSpecAsset");
            _chartSpecJsonProp = serializedObject.FindProperty("_chartSpecJson");
            _targetCanvasProp = serializedObject.FindProperty("_targetCanvas");
            _defaultMaterial2DProp = serializedObject.FindProperty("_defaultMaterial2D");
            _plotRootProp = serializedObject.FindProperty("_plotRoot");
            _defaultMaterial3DProp = serializedObject.FindProperty("_defaultMaterial3D");
            _pixelScaleProp = serializedObject.FindProperty("_pixelScale");
            _fontProp = serializedObject.FindProperty("_font");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var container = (VegaContainer)target;

            // Render Settings
            EditorGUILayout.LabelField("Render Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_renderModeProp);
            EditorGUILayout.Space();
            
            // DataCore Integration Status
            DrawDataCoreSection();

            // Mode-specific settings
            var renderMode = (VegaContainer.RenderMode)_renderModeProp.enumValueIndex;
            if (renderMode == VegaContainer.RenderMode.Canvas2D)
            {
                EditorGUILayout.LabelField("2D Mode Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_targetCanvasProp);
                EditorGUILayout.PropertyField(_defaultMaterial2DProp);
            }
            else
            {
                EditorGUILayout.LabelField("3D Mode Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_plotRootProp);
                EditorGUILayout.PropertyField(_defaultMaterial3DProp);
                EditorGUILayout.PropertyField(_pixelScaleProp);
            }

            EditorGUILayout.Space();

            // Typography
            EditorGUILayout.LabelField("Typography", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_fontProp);
            EditorGUILayout.Space();

            // Specification
            EditorGUILayout.LabelField("Chart Specification", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(_chartSpecAssetProp, new GUIContent("Spec Asset"));
            
            // Template dropdown
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Quick Templates");
            int templateIndex = EditorGUILayout.Popup(-1, TEMPLATE_NAMES);
            EditorGUILayout.EndHorizontal();

            if (templateIndex >= 0 && templateIndex < TEMPLATE_JSON.Length)
            {
                _chartSpecJsonProp.stringValue = TEMPLATE_JSON[templateIndex];
                serializedObject.ApplyModifiedProperties();
            }

            // JSON editor
            _showJsonEditor = EditorGUILayout.Foldout(_showJsonEditor, "JSON Editor", true);
            if (_showJsonEditor)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                _jsonScrollPos = EditorGUILayout.BeginScrollView(_jsonScrollPos, GUILayout.Height(200));
                EditorGUI.BeginChangeCheck();
                string json = EditorGUILayout.TextArea(_chartSpecJsonProp.stringValue, 
                    GUILayout.ExpandHeight(true));
                if (EditorGUI.EndChangeCheck())
                {
                    _chartSpecJsonProp.stringValue = json;
                }
                EditorGUILayout.EndScrollView();

                // Validation
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        Newtonsoft.Json.JsonConvert.DeserializeObject<UVis.Spec.ChartSpec>(json);
                        EditorGUILayout.HelpBox("JSON is valid", MessageType.Info);
                    }
                    catch (System.Exception ex)
                    {
                        EditorGUILayout.HelpBox($"JSON Error: {ex.Message}", MessageType.Error);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();

            // Actions
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Render", GUILayout.Height(30)))
            {
                container.Render();
            }

            if (GUILayout.Button("Clear", GUILayout.Height(30)))
            {
                container.Clear();
            }
            
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Format JSON"))
            {
                try
                {
                    var obj = Newtonsoft.Json.JsonConvert.DeserializeObject(_chartSpecJsonProp.stringValue);
                    _chartSpecJsonProp.stringValue = Newtonsoft.Json.JsonConvert.SerializeObject(obj, 
                        Newtonsoft.Json.Formatting.Indented);
                }
                catch
                {
                    // Ignore format errors
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
        
        /// <summary>
        /// Draw the DataCore integration status section.
        /// </summary>
        private void DrawDataCoreSection()
        {
            _showDataCoreSection = EditorGUILayout.Foldout(_showDataCoreSection, "DataCore Integration", true);
            if (!_showDataCoreSection)
                return;
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
#if DATACORE_INSTALLED
            // DataCore is installed
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status", GUILayout.Width(60));
            
            var statusStyle = new GUIStyle(EditorStyles.label);
            statusStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
            EditorGUILayout.LabelField("✓ DataCore Detected", statusStyle);
            EditorGUILayout.EndHorizontal();
            
            // Try to get datasets
            try
            {
                var storeComponent = Object.FindObjectOfType<DataCoreEditorComponent>();
                if (storeComponent != null)
                {
                    var store = storeComponent.GetStore();
                    if (store != null)
                    {
                        EditorGUILayout.Space(5);
                        DrawDatasetList("Tabular Datasets", store.TabularNames, "dc://");
                        DrawDatasetList("Graph Datasets", store.GraphNames, "dc://");
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("DataCoreStore not initialized.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("DataCoreEditorComponent not found in scene. Add one to access datasets.", MessageType.Info);
                }
            }
            catch (System.Exception ex)
            {
                EditorGUILayout.HelpBox($"Error accessing DataCore: {ex.Message}", MessageType.Error);
            }
#else
            // DataCore is not installed
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status", GUILayout.Width(60));
            
            var statusStyle = new GUIStyle(EditorStyles.label);
            statusStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            EditorGUILayout.LabelField("○ DataCore Not Installed", statusStyle);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.HelpBox("Install com.aroaro.datacore package to enable dc:// data URLs.", MessageType.Info);
#endif
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }
        
#if DATACORE_INSTALLED
        /// <summary>
        /// Draw a list of datasets with copy buttons.
        /// </summary>
        private void DrawDatasetList(string title, IEnumerable<string> names, string urlPrefix)
        {
            var namesList = new List<string>(names);
            if (namesList.Count == 0)
                return;
            
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            
            EditorGUI.indentLevel++;
            foreach (var name in namesList)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(name, GUILayout.ExpandWidth(true));
                
                if (GUILayout.Button("Copy URL", GUILayout.Width(70)))
                {
                    var url = $"{urlPrefix}{name}";
                    EditorGUIUtility.systemCopyBuffer = url;
                    Debug.Log($"[UVis] Copied to clipboard: {url}");
                }
                
                if (GUILayout.Button("Use", GUILayout.Width(40)))
                {
                    // Insert dc:// URL into the spec
                    InsertDataCoreUrl(name);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(3);
        }
        
        /// <summary>
        /// Insert a dc:// URL into the current spec's data section.
        /// </summary>
        private void InsertDataCoreUrl(string datasetName)
        {
            var url = $"dc://{datasetName}?sync=true";
            var json = _chartSpecJsonProp.stringValue;
            
            if (string.IsNullOrWhiteSpace(json))
            {
                // Create minimal spec with dc:// URL
                json = $@"{{
  ""data"": {{ ""url"": ""{url}"" }},
  ""mark"": ""bar"",
  ""encoding"": {{
    ""x"": {{""field"": ""category"", ""type"": ""ordinal""}},
    ""y"": {{""field"": ""value"", ""type"": ""quantitative""}}
  }},
  ""width"": 640,
  ""height"": 400
}}";
            }
            else
            {
                // Try to update existing data.url
                try
                {
                    var spec = Newtonsoft.Json.JsonConvert.DeserializeObject<UVis.Spec.ChartSpec>(json);
                    if (spec.data == null)
                        spec.data = new UVis.Spec.DataSpec();
                    spec.data.url = url;
                    spec.data.sync = true;
                    spec.data.values = null; // Clear inline values
                    json = Newtonsoft.Json.JsonConvert.SerializeObject(spec, Newtonsoft.Json.Formatting.Indented);
                }
                catch
                {
                    Debug.LogWarning("[UVis] Could not parse existing spec, creating new one.");
                    json = $@"{{
  ""data"": {{ ""url"": ""{url}"" }},
  ""mark"": ""bar""
}}";
                }
            }
            
            _chartSpecJsonProp.stringValue = json;
            serializedObject.ApplyModifiedProperties();
            Debug.Log($"[UVis] Set data source to: {url}");
        }
#endif
    }
}
