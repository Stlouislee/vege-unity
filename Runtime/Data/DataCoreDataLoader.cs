using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UVis.Spec;

#if DATACORE_INSTALLED
using AroAro.DataCore;
using AroAro.DataCore.Events;
#endif

namespace UVis.Data
{
    /// <summary>
    /// Loads data from DataCore datasets using dc:// URL scheme.
    /// Only active when DATACORE_INSTALLED is defined.
    /// </summary>
    public static class DataCoreDataLoader
    {
        public const string DC_SCHEME = "dc://";

        /// <summary>
        /// Check if the URL uses the dc:// scheme.
        /// </summary>
        public static bool CanHandle(string url)
        {
            return !string.IsNullOrEmpty(url) && url.StartsWith(DC_SCHEME, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parse URL and return dataset name and query parameters.
        /// </summary>
        public static (string datasetName, Dictionary<string, string> queryParams) ParseUrl(string url)
        {
            if (!CanHandle(url))
                return (null, null);

            // Remove scheme
            var path = url.Substring(DC_SCHEME.Length);
            
            // Split path and query
            var parts = path.Split(new[] { '?' }, 2);
            var datasetName = parts[0];
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (parts.Length > 1)
            {
                var queryString = parts[1];
                foreach (var param in queryString.Split('&'))
                {
                    var kv = param.Split(new[] { '=' }, 2);
                    if (kv.Length == 2)
                    {
                        queryParams[kv[0]] = Uri.UnescapeDataString(kv[1]);
                    }
                    else if (kv.Length == 1)
                    {
                        queryParams[kv[0]] = "true";
                    }
                }
            }

            return (datasetName, queryParams);
        }

        /// <summary>
        /// Check if sync is enabled in URL or spec.
        /// </summary>
        public static bool IsSyncEnabled(string url, DataSpec dataSpec)
        {
            // Check spec property first
            if (dataSpec?.sync == true)
                return true;

            // Then check URL param
            var (_, queryParams) = ParseUrl(url);
            if (queryParams != null && queryParams.TryGetValue("sync", out var syncValue))
            {
                return syncValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

#if DATACORE_INSTALLED
        /// <summary>
        /// Load data from DataCore and populate the DataSpec.
        /// </summary>
        public static void PopulateSpec(DataSpec spec)
        {
            if (spec == null || !CanHandle(spec.url))
                return;

            var (datasetName, queryParams) = ParseUrl(spec.url);
            if (string.IsNullOrEmpty(datasetName))
            {
                Debug.LogWarning($"[UVis] Invalid dc:// URL: {spec.url}");
                return;
            }

            try
            {
                var store = DataCoreEditorComponent.Instance?.GetStore();
                if (store == null)
                {
                    Debug.LogError("[UVis] DataCoreStore not available. Ensure DataCoreEditorComponent is in the scene.");
                    return;
                }

                // Auto-detect dataset type
                if (store.GraphNames.Contains(datasetName))
                {
                    LoadGraphData(store, datasetName, queryParams, spec);
                }
                else if (store.TabularNames.Contains(datasetName))
                {
                    LoadTabularData(store, datasetName, queryParams, spec);
                }
                else
                {
                    Debug.LogError($"[UVis] Dataset '{datasetName}' not found in DataCore.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UVis] Failed to load data from DataCore: {ex.Message}");
            }
        }

        private static void LoadTabularData(DataCoreStore store, string datasetName, Dictionary<string, string> queryParams, DataSpec spec)
        {
            var dataset = store.Get<ITabularDataset>(datasetName);
            if (dataset == null)
            {
                Debug.LogError($"[UVis] Could not retrieve tabular dataset '{datasetName}'");
                return;
            }

            // Start with query builder
            var query = dataset.Query();

            // Apply where filter
            if (queryParams.TryGetValue("where", out var whereClause))
            {
                query = ApplyWhereClause(query, whereClause);
            }

            // Apply ordering
            if (queryParams.TryGetValue("orderBy", out var orderBy))
            {
                bool descending = queryParams.TryGetValue("orderDesc", out var desc) && 
                                  desc.Equals("true", StringComparison.OrdinalIgnoreCase);
                query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
            }

            // Apply column selection
            if (queryParams.TryGetValue("select", out var selectCols))
            {
                var columns = selectCols.Split(',').Select(c => c.Trim()).ToArray();
                query = query.Select(columns);
            }
            // Execute query - ToDictionaries() returns List<Dictionary<string, object>>
            var results = query.ToDictionaries();

            // Apply limit (after query since ITabularQuery may not have Take)
            if (queryParams.TryGetValue("limit", out var limitStr) && int.TryParse(limitStr, out var limit))
            {
                results = results.Take(limit).ToList();
            }

            spec.values = results;
            Debug.Log($"[UVis] Loaded {results.Count} rows from DataCore dataset '{datasetName}'");
        }

        private static void LoadGraphData(DataCoreStore store, string datasetName, Dictionary<string, string> queryParams, DataSpec spec)
        {
            var dataset = store.Get<IGraphDataset>(datasetName);
            if (dataset == null)
            {
                Debug.LogError($"[UVis] Could not retrieve graph dataset '{datasetName}'");
                return;
            }

            // Load nodes
            var nodes = new List<Dictionary<string, object>>();
            var query = dataset.Query();
            
            // Apply node filter if specified
            if (queryParams.TryGetValue("where", out var whereClause))
            {
                // Parse simple property filter like "property>value"
                var match = Regex.Match(whereClause, @"(\w+)\s*(==|!=|>|>=|<|<=)\s*(.+)");
                if (match.Success)
                {
                    var property = match.Groups[1].Value;
                    var op = ParseQueryOp(match.Groups[2].Value);
                    var value = ParseValue(match.Groups[3].Value);
                    query = query.WhereNodeProperty(property, op, value);
                }
            }

            var nodeIds = query.ToNodeIds().ToList();
            foreach (var nodeId in nodeIds)
            {
                var props = dataset.GetNodeProperties(nodeId);
                var nodeData = new Dictionary<string, object>(props ?? new Dictionary<string, object>())
                {
                    ["id"] = nodeId
                };
                nodes.Add(nodeData);
            }

            // Load edges
            var edges = new List<Dictionary<string, object>>();
            var edgePairs = dataset.Query().ToEdges().ToList();
            foreach (var (source, target) in edgePairs)
            {
                var props = dataset.GetEdgeProperties(source, target);
                var edgeData = new Dictionary<string, object>(props ?? new Dictionary<string, object>())
                {
                    ["source"] = source,
                    ["target"] = target
                };
                edges.Add(edgeData);
            }

            // Apply limit if specified
            if (queryParams.TryGetValue("limit", out var limitStr) && int.TryParse(limitStr, out var limit))
            {
                nodes = nodes.Take(limit).ToList();
            }

            spec.nodes = nodes;
            spec.edges = edges;
            Debug.Log($"[UVis] Loaded {nodes.Count} nodes and {edges.Count} edges from DataCore graph '{datasetName}'");
        }

        private static ITabularQuery ApplyWhereClause(ITabularQuery query, string whereClause)
        {
            // Parse simple expressions like "column>value" or "column==value"
            var match = Regex.Match(whereClause, @"(\w+)\s*(==|!=|>|>=|<|<=|contains|startswith|endswith)\s*(.+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                Debug.LogWarning($"[UVis] Could not parse where clause: {whereClause}");
                return query;
            }

            var column = match.Groups[1].Value;
            var op = ParseQueryOp(match.Groups[2].Value);
            var value = ParseValue(match.Groups[3].Value);

            return query.Where(column, op, value);
        }

        private static QueryOp ParseQueryOp(string op)
        {
            return op.ToLowerInvariant() switch
            {
                "==" => QueryOp.Eq,
                "!=" => QueryOp.Ne,
                ">" => QueryOp.Gt,
                ">=" => QueryOp.Ge,
                "<" => QueryOp.Lt,
                "<=" => QueryOp.Le,
                "contains" => QueryOp.Contains,
                "startswith" => QueryOp.StartsWith,
                "endswith" => QueryOp.EndsWith,
                _ => QueryOp.Eq
            };
        }

        private static object ParseValue(string value)
        {
            value = value.Trim();
            
            // Remove quotes if present
            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                (value.StartsWith("'") && value.EndsWith("'")))
            {
                return value.Substring(1, value.Length - 2);
            }

            // Try parse as number
            if (double.TryParse(value, out var num))
                return num;

            // Try parse as bool
            if (bool.TryParse(value, out var boolVal))
                return boolVal;

            return value;
        }
#else
        /// <summary>
        /// Stub when DataCore is not installed.
        /// </summary>
        public static void PopulateSpec(DataSpec spec)
        {
            if (CanHandle(spec?.url))
            {
                Debug.LogWarning("[UVis] dc:// URL detected but DataCore is not installed. Add com.aroaro.datacore package to enable this feature.");
            }
        }
#endif
    }
}
