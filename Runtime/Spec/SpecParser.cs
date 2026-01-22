using System;
using System.Collections.Generic;
using UnityEngine;

namespace UVis.Spec
{
    /// <summary>
    /// JSON parser for chart specifications.
    /// Uses Unity's built-in JsonUtility with fallback to manual parsing for complex types.
    /// </summary>
    public static class SpecParser
    {
        /// <summary>
        /// Parse JSON string to ChartSpec with defaults applied.
        /// </summary>
        public static ChartSpec Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON specification cannot be null or empty");
            }

            try
            {
                // Use Newtonsoft.Json for parsing
                var settings = new Newtonsoft.Json.JsonSerializerSettings
                {
                    // This helps properly deserialize Dictionary<string, object>
                    Converters = { new DictionaryConverter() }
                };
                
                var spec = Newtonsoft.Json.JsonConvert.DeserializeObject<ChartSpec>(json, settings);
                ApplyDefaults(spec);
                Validate(spec);
                
                Debug.Log($"[UVis] Parsed spec: mark={spec.mark}, data count={spec.data?.values?.Count ?? 0}");
                
                return spec;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UVis] Failed to parse chart specification: {ex.Message}");
                throw new FormatException($"Failed to parse chart specification: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Custom converter to properly handle Dictionary values from JSON.
        /// </summary>
        private class DictionaryConverter : Newtonsoft.Json.JsonConverter<Dictionary<string, object>>
        {
            public override Dictionary<string, object> ReadJson(
                Newtonsoft.Json.JsonReader reader, 
                Type objectType, 
                Dictionary<string, object> existingValue, 
                bool hasExistingValue, 
                Newtonsoft.Json.JsonSerializer serializer)
            {
                var dict = new Dictionary<string, object>();
                var jObject = Newtonsoft.Json.Linq.JObject.Load(reader);
                
                foreach (var prop in jObject.Properties())
                {
                    dict[prop.Name] = ConvertJToken(prop.Value);
                }
                
                return dict;
            }

            private object ConvertJToken(Newtonsoft.Json.Linq.JToken token)
            {
                switch (token.Type)
                {
                    case Newtonsoft.Json.Linq.JTokenType.Integer:
                        return (long)token;
                    case Newtonsoft.Json.Linq.JTokenType.Float:
                        return (double)token;
                    case Newtonsoft.Json.Linq.JTokenType.String:
                        return (string)token;
                    case Newtonsoft.Json.Linq.JTokenType.Boolean:
                        return (bool)token;
                    case Newtonsoft.Json.Linq.JTokenType.Null:
                        return null;
                    case Newtonsoft.Json.Linq.JTokenType.Array:
                        var list = new List<object>();
                        foreach (var item in token)
                            list.Add(ConvertJToken(item));
                        return list;
                    case Newtonsoft.Json.Linq.JTokenType.Object:
                        var dict = new Dictionary<string, object>();
                        foreach (var prop in ((Newtonsoft.Json.Linq.JObject)token).Properties())
                            dict[prop.Name] = ConvertJToken(prop.Value);
                        return dict;
                    default:
                        return token.ToString();
                }
            }

            public override void WriteJson(
                Newtonsoft.Json.JsonWriter writer, 
                Dictionary<string, object> value, 
                Newtonsoft.Json.JsonSerializer serializer)
            {
                serializer.Serialize(writer, value);
            }
        }

        /// <summary>
        /// Apply default values to specification.
        /// </summary>
        private static void ApplyDefaults(ChartSpec spec)
        {
            spec.padding ??= new PaddingSpec();
            spec.encoding ??= new EncodingSpec();
            spec.axis ??= new AxisContainerSpec();
            spec.axis.x ??= new AxisSpec();
            spec.axis.y ??= new AxisSpec();
            spec.data ??= new DataSpec { values = new List<Dictionary<string, object>>() };
            spec.data.values ??= new List<Dictionary<string, object>>();

            // Apply defaults to encoding channels
            if (spec.encoding.x != null)
            {
                spec.encoding.x.scale ??= new ScaleSpec();
            }
            if (spec.encoding.y != null)
            {
                spec.encoding.y.scale ??= new ScaleSpec();
            }
            if (spec.encoding.color != null)
            {
                spec.encoding.color.scale ??= new ScaleSpec();
            }
            if (spec.encoding.size != null)
            {
                spec.encoding.size.scale ??= new ScaleSpec();
            }
        }

        /// <summary>
        /// Validate specification and log warnings for issues.
        /// </summary>
        private static void Validate(ChartSpec spec)
        {
            bool isGraph = spec.mark?.ToLower() == "graph";
            
            // Graph marks use nodes/edges, other marks use values
            if (isGraph)
            {
                if ((spec.data.nodes == null || spec.data.nodes.Count == 0))
                {
                    Debug.LogWarning("[UVis] Graph specification has no data.nodes array");
                }
            }
            else
            {
                if (spec.data.values == null || spec.data.values.Count == 0)
                {
                    Debug.LogWarning("[UVis] Chart specification has no data values");
                }
            }

            if (string.IsNullOrEmpty(spec.mark))
            {
                Debug.LogWarning("[UVis] No mark type specified, defaulting to 'bar'");
                spec.mark = "bar";
            }

            // Graph marks don't require x/y encoding
            if (!isGraph && spec.encoding.x == null && spec.encoding.y == null)
            {
                Debug.LogWarning("[UVis] No x or y encoding specified");
            }

            if (spec.width <= 0)
            {
                Debug.LogWarning("[UVis] Invalid width, using default 640");
                spec.width = 640;
            }

            if (spec.height <= 0)
            {
                Debug.LogWarning("[UVis] Invalid height, using default 400");
                spec.height = 400;
            }
        }

        /// <summary>
        /// Serialize ChartSpec back to JSON.
        /// </summary>
        public static string ToJson(ChartSpec spec, bool prettyPrint = true)
        {
            var formatting = prettyPrint 
                ? Newtonsoft.Json.Formatting.Indented 
                : Newtonsoft.Json.Formatting.None;
            return Newtonsoft.Json.JsonConvert.SerializeObject(spec, formatting);
        }
    }
}
