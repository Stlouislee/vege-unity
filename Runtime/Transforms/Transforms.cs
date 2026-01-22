using System;
using System.Collections.Generic;
using System.Linq;
using UVis.Spec;

namespace UVis.Transforms
{
    /// <summary>
    /// Interface for data transform operations.
    /// </summary>
    public interface ITransform
    {
        List<Dictionary<string, object>> Apply(List<Dictionary<string, object>> data);
    }

    /// <summary>
    /// Transform executor that processes all transforms in order.
    /// </summary>
    public static class TransformExecutor
    {
        public static List<Dictionary<string, object>> Execute(
            List<Dictionary<string, object>> data,
            List<TransformSpec> transforms)
        {
            if (transforms == null || transforms.Count == 0)
                return data;

            var result = data;
            foreach (var spec in transforms)
            {
                var transform = CreateTransform(spec);
                if (transform != null)
                {
                    result = transform.Apply(result);
                }
            }
            return result;
        }

        private static ITransform CreateTransform(TransformSpec spec)
        {
            if (!string.IsNullOrEmpty(spec.filter))
                return new FilterTransform(spec.filter);

            if (spec.aggregate != null && spec.aggregate.Count > 0)
                return new AggregateTransform(spec.aggregate, spec.groupby);

            if (spec.sort != null && spec.sort.Count > 0)
                return new SortTransform(spec.sort);

            if (spec.bin != null && !string.IsNullOrEmpty(spec.binField))
                return new BinTransform(spec.binField, spec.@as, spec.bin);

            return null;
        }
    }

    /// <summary>
    /// Filter transform - keeps only rows matching a simple expression.
    /// Supports: field == value, field != value, field > value, field >= value, field < value, field <= value
    /// </summary>
    public class FilterTransform : ITransform
    {
        private readonly string _expression;

        public FilterTransform(string expression)
        {
            _expression = expression;
        }

        public List<Dictionary<string, object>> Apply(List<Dictionary<string, object>> data)
        {
            return data.Where(row => EvaluateExpression(row, _expression)).ToList();
        }

        private bool EvaluateExpression(Dictionary<string, object> row, string expr)
        {
            // Simple expression parser for: datum.field op value
            expr = expr.Trim();
            
            // Remove "datum." prefix if present
            expr = expr.Replace("datum.", "");

            // Parse operators
            string[] operators = { ">=", "<=", "!=", "==", ">", "<" };
            foreach (var op in operators)
            {
                var parts = expr.Split(new[] { op }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    string field = parts[0].Trim();
                    string valueStr = parts[1].Trim().Trim('"', '\'');

                    if (!row.TryGetValue(field, out var fieldValue))
                        return false;

                    return CompareValues(fieldValue, valueStr, op);
                }
            }

            return true; // If can't parse, include the row
        }

        private bool CompareValues(object fieldValue, string compareValue, string op)
        {
            // Try numeric comparison first
            if (double.TryParse(fieldValue?.ToString(), out double numField) &&
                double.TryParse(compareValue, out double numCompare))
            {
                return op switch
                {
                    "==" => Math.Abs(numField - numCompare) < double.Epsilon,
                    "!=" => Math.Abs(numField - numCompare) >= double.Epsilon,
                    ">" => numField > numCompare,
                    ">=" => numField >= numCompare,
                    "<" => numField < numCompare,
                    "<=" => numField <= numCompare,
                    _ => true
                };
            }

            // String comparison
            string strField = fieldValue?.ToString() ?? "";
            return op switch
            {
                "==" => strField.Equals(compareValue, StringComparison.OrdinalIgnoreCase),
                "!=" => !strField.Equals(compareValue, StringComparison.OrdinalIgnoreCase),
                _ => string.Compare(strField, compareValue, StringComparison.OrdinalIgnoreCase) switch
                {
                    > 0 when op == ">" => true,
                    >= 0 when op == ">=" => true,
                    < 0 when op == "<" => true,
                    <= 0 when op == "<=" => true,
                    _ => false
                }
            };
        }
    }

    /// <summary>
    /// Aggregate transform - groups by fields and computes aggregations.
    /// </summary>
    public class AggregateTransform : ITransform
    {
        private readonly List<AggregateOpSpec> _operations;
        private readonly List<string> _groupBy;

        public AggregateTransform(List<AggregateOpSpec> operations, List<string> groupBy)
        {
            _operations = operations ?? new List<AggregateOpSpec>();
            _groupBy = groupBy ?? new List<string>();
        }

        public List<Dictionary<string, object>> Apply(List<Dictionary<string, object>> data)
        {
            // Group data
            var groups = new Dictionary<string, List<Dictionary<string, object>>>();

            foreach (var row in data)
            {
                string key = string.Join("|", _groupBy.Select(f => row.TryGetValue(f, out var v) ? v?.ToString() : ""));
                if (!groups.ContainsKey(key))
                    groups[key] = new List<Dictionary<string, object>>();
                groups[key].Add(row);
            }

            // Compute aggregations per group
            var result = new List<Dictionary<string, object>>();

            foreach (var group in groups)
            {
                var rows = group.Value;
                var newRow = new Dictionary<string, object>();

                // Copy group-by fields from first row
                foreach (var field in _groupBy)
                {
                    if (rows[0].TryGetValue(field, out var val))
                        newRow[field] = val;
                }

                // Compute each aggregation
                foreach (var op in _operations)
                {
                    string outputField = op.@as ?? $"{op.op}_{op.field}";
                    newRow[outputField] = ComputeAggregate(rows, op.field, op.op);
                }

                result.Add(newRow);
            }

            return result;
        }

        private object ComputeAggregate(List<Dictionary<string, object>> rows, string field, string op)
        {
            var values = rows
                .Where(r => r.ContainsKey(field) && r[field] != null)
                .Select(r => Convert.ToDouble(r[field]))
                .ToList();

            return op?.ToLower() switch
            {
                "count" => rows.Count,
                "sum" => values.Sum(),
                "mean" or "average" or "avg" => values.Count > 0 ? values.Average() : 0,
                "min" => values.Count > 0 ? values.Min() : 0,
                "max" => values.Count > 0 ? values.Max() : 0,
                "median" => values.Count > 0 ? Median(values) : 0,
                _ => values.Sum()
            };
        }

        private static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[mid - 1] + sorted[mid]) / 2
                : sorted[mid];
        }
    }

    /// <summary>
    /// Sort transform - sorts data by specified fields.
    /// </summary>
    public class SortTransform : ITransform
    {
        private readonly List<SortFieldSpec> _sortFields;

        public SortTransform(List<SortFieldSpec> sortFields)
        {
            _sortFields = sortFields ?? new List<SortFieldSpec>();
        }

        public List<Dictionary<string, object>> Apply(List<Dictionary<string, object>> data)
        {
            if (_sortFields.Count == 0)
                return data;

            IOrderedEnumerable<Dictionary<string, object>> ordered = null;

            for (int i = 0; i < _sortFields.Count; i++)
            {
                var field = _sortFields[i];
                bool descending = field.order?.ToLower() == "descending";

                if (i == 0)
                {
                    ordered = descending
                        ? data.OrderByDescending(r => GetSortValue(r, field.field))
                        : data.OrderBy(r => GetSortValue(r, field.field));
                }
                else
                {
                    ordered = descending
                        ? ordered.ThenByDescending(r => GetSortValue(r, field.field))
                        : ordered.ThenBy(r => GetSortValue(r, field.field));
                }
            }

            return ordered?.ToList() ?? data;
        }

        private class MultiFieldComparer : IComparer<int>
        {
            private readonly List<SortFieldSpec> _fields;
            private readonly List<Dictionary<string, object>> _data;

            public MultiFieldComparer(List<SortFieldSpec> fields, List<Dictionary<string, object>> data)
            {
                _fields = fields;
                _data = data;
            }

            public int Compare(int x, int y)
            {
                foreach (var field in _fields)
                {
                    var rowX = _data[x];
                    var rowY = _data[y];

                    rowX.TryGetValue(field.field, out var valX);
                    rowY.TryGetValue(field.field, out var valY);

                    int result = CompareValues(valX, valY);
                    if (field.order?.ToLower() == "descending")
                        result = -result;

                    if (result != 0)
                        return result;
                }
                return 0;
            }

            private int CompareValues(object a, object b)
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;

                if (double.TryParse(a.ToString(), out double numA) &&
                    double.TryParse(b.ToString(), out double numB))
                {
                    return numA.CompareTo(numB);
                }

                return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public List<Dictionary<string, object>> ApplyCorrect(List<Dictionary<string, object>> data)
        {
            if (_sortFields.Count == 0)
                return data;

            IOrderedEnumerable<Dictionary<string, object>> ordered = null;

            for (int i = 0; i < _sortFields.Count; i++)
            {
                var field = _sortFields[i];
                bool descending = field.order?.ToLower() == "descending";

                if (i == 0)
                {
                    ordered = descending
                        ? data.OrderByDescending(r => GetSortValue(r, field.field))
                        : data.OrderBy(r => GetSortValue(r, field.field));
                }
                else
                {
                    ordered = descending
                        ? ordered.ThenByDescending(r => GetSortValue(r, field.field))
                        : ordered.ThenBy(r => GetSortValue(r, field.field));
                }
            }

            return ordered?.ToList() ?? data;
        }

        private object GetSortValue(Dictionary<string, object> row, string field)
        {
            if (!row.TryGetValue(field, out var val))
                return null;

            if (double.TryParse(val?.ToString(), out double num))
                return num;

            return val?.ToString() ?? "";
        }
    }

    /// <summary>
    /// Bin transform - creates histogram bins for continuous data.
    /// </summary>
    public class BinTransform : ITransform
    {
        private readonly string _field;
        private readonly string _outputField;
        private readonly BinSpec _binSpec;

        public BinTransform(string field, string outputField, BinSpec binSpec)
        {
            _field = field;
            _outputField = outputField ?? $"{field}_bin";
            _binSpec = binSpec ?? new BinSpec();
        }

        public List<Dictionary<string, object>> Apply(List<Dictionary<string, object>> data)
        {
            // Get numeric values
            var values = data
                .Where(r => r.ContainsKey(_field))
                .Select(r => Convert.ToDouble(r[_field]))
                .ToList();

            if (values.Count == 0)
                return data;

            double min = _binSpec.extent_min ?? values.Min();
            double max = _binSpec.extent_max ?? values.Max();

            double step = _binSpec.step ?? (max - min) / _binSpec.maxbins;
            if (step <= 0) step = 1;

            // Assign bins
            foreach (var row in data)
            {
                if (row.TryGetValue(_field, out var val))
                {
                    double v = Convert.ToDouble(val);
                    int binIndex = (int)Math.Floor((v - min) / step);
                    double binStart = min + binIndex * step;
                    double binEnd = binStart + step;
                    
                    row[_outputField] = binStart;
                    row[$"{_outputField}_end"] = binEnd;
                }
            }

            return data;
        }
    }
}
