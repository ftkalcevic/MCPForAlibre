using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace MCPForAlibre.AddOn
{
    // A request's params, read by name with clear errors for missing or wrong values.
    sealed class Args
    {
        readonly Dictionary<string, object> m_values;

        public Args(Dictionary<string, object> values)
        {
            m_values = values ?? new Dictionary<string, object>();
        }

        public bool Has(string name) => m_values.TryGetValue(name, out object v) && v != null;

        public string String(string name)
        {
            string s = OptionalString(name);
            if (string.IsNullOrEmpty(s))
                throw new ArgumentException("'" + name + "' is required.");
            return s;
        }

        public string OptionalString(string name) =>
            m_values.TryGetValue(name, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        public double Number(string name)
        {
            if (!Has(name))
                throw new ArgumentException("'" + name + "' is required.");
            return ToNumber(name, m_values[name]);
        }

        public double? OptionalNumber(string name) => Has(name) ? ToNumber(name, m_values[name]) : (double?)null;

        public bool Bool(string name, bool fallback) =>
            Has(name) ? Convert.ToBoolean(m_values[name], CultureInfo.InvariantCulture) : fallback;

        public int Int(string name, int fallback) =>
            Has(name) ? (int)ToNumber(name, m_values[name]) : fallback;

        public List<Args> List(string name)
        {
            if (!Has(name) || !(m_values[name] is IEnumerable items) || m_values[name] is string)
                throw new ArgumentException("'" + name + "' must be a list.");
            var list = new List<Args>();
            foreach (object item in items)
                list.Add(new Args(item as Dictionary<string, object> ??
                                  throw new ArgumentException("Each item of '" + name + "' must be an object.")));
            return list;
        }

        // A nested object, or null when absent.
        public Args Object(string name)
        {
            if (!Has(name))
                return null;
            return new Args(m_values[name] as Dictionary<string, object> ??
                            throw new ArgumentException("'" + name + "' must be an object."));
        }

        // A list of plain values, such as [3, 7] or ["XY-Plane", "face:2"]; empty when absent.
        public List<object> Values(string name)
        {
            var list = new List<object>();
            if (!Has(name))
                return list;
            if (!(m_values[name] is IEnumerable items) || m_values[name] is string)
                throw new ArgumentException("'" + name + "' must be a list.");
            foreach (object item in items)
                list.Add(item);
            return list;
        }

        public List<int> Ints(string name) =>
            Values(name).ConvertAll(v => (int)ToNumber(name, v));

        public List<double> Numbers(string name) =>
            Values(name).ConvertAll(v => ToNumber(name, v));

        public List<string> Strings(string name) =>
            Values(name).ConvertAll(v => Convert.ToString(v, CultureInfo.InvariantCulture));

        static double ToNumber(string name, object value)
        {
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch (Exception) { throw new ArgumentException("'" + name + "' must be a number."); }
        }
    }
}
