using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.UI
{
    // Account quotas are shared with other Codex sessions. Thread tokens are separate.
    public sealed class CodexUsage
    {
        private JObject _limits;
        private JObject _tokens;
        private DateTimeOffset? _updated;
        private readonly object _gate = new object();
        public void SetLimits(JObject limits)
        { lock (_gate) { _limits = (JObject)limits.DeepClone(); _updated = DateTimeOffset.Now; } }
        public void SetTokens(JObject tokens)
        { lock (_gate) _tokens = (JObject)tokens.DeepClone(); }
        public void ClearLimits() { lock (_gate) { _limits = null; _updated = null; } }
        public void Reset() { lock (_gate) { _limits = null; _tokens = null; _updated = null; } }

        public string Describe()
        {
            lock (_gate)
            {
                var lines = new List<string>();
                double? total = Number(_tokens?["total"]?["totalTokens"]);
                double? last = Number(_tokens?["last"]?["totalTokens"]);
                lines.Add("This chat: " + (total.HasValue ? total.Value.ToString("N0") + " tokens" : "token usage unavailable"));
                if (last.HasValue) lines.Add("Last turn: " + last.Value.ToString("N0") + " tokens");
                bool found = false;
                if (_limits?["rateLimitsByLimitId"] is JObject pools && pools.Count > 0)
                {
                    foreach (var pool in pools.Properties())
                        found |= AddPool(lines, pool.Value as JObject, pool.Name);
                }
                else found = AddPool(lines, _limits?["rateLimits"] as JObject, "Codex");
                if (!found) lines.Add("Account limits: unavailable for this connection.");
                if (_updated.HasValue) lines.Add("Account snapshot: " + _updated.Value.ToString("g") + " (local time)");
                lines.Add("Account limits are shared across Codex. Tokens are not a price estimate.");
                return string.Join("\n", lines);
            }
        }

        private static bool AddPool(List<string> lines, JObject pool, string fallback)
        {
            if (pool == null) return false;
            string name = (string)pool["limitName"] ?? (string)pool["limitId"] ?? fallback;
            bool found = false;
            foreach (string key in new[] { "primary", "secondary" })
            {
                var window = pool[key] as JObject;
                double? used = Number(window?["usedPercent"]);
                if (!used.HasValue) continue;
                double? minutes = Number(window["windowDurationMins"]);
                string duration = !minutes.HasValue ? key : minutes.Value % 1440 == 0 ? (minutes.Value / 1440).ToString("0.#") + "d" : minutes.Value % 60 == 0 ? (minutes.Value / 60).ToString("0.#") + "h" : minutes.Value.ToString("0.#") + "m";
                string reset = "reset time unavailable";
                double? epoch = Number(window["resetsAt"]);
                if (epoch.HasValue)
                {
                    try { reset = "resets " + DateTimeOffset.FromUnixTimeSeconds((long)epoch.Value).ToLocalTime().ToString("g"); }
                    catch (ArgumentOutOfRangeException) { }
                }
                lines.Add(name + " · " + duration + ": " + Math.Max(0, Math.Min(100, 100 - used.Value)).ToString("0.#") + "% left · " + reset);
                found = true;
            }
            return found;
        }

        private static double? Number(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return null;
            if (!double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number) || number < 0) return null;
            return number;
        }
    }
}
