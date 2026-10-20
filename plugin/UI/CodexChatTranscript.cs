using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.UI
{
    public static class CodexChatTranscript
    {
        public static IEnumerable<JObject> Turns(JObject state)
        {
            var history = state["turnHistory"]?["history"];
            if ((string)state["turnHistory"]?["kind"] == "canonical")
            {
                var entities = history?["entitiesByKey"] as JObject;
                var seen = new HashSet<string>();
                foreach (var island in history?["islands"] as JArray ?? new JArray())
                    foreach (var entry in island["entries"] as JArray ?? new JArray())
                    {
                        string key = (string)entry["value"];
                        if (key != null && seen.Add(key) && entities?[key] is JObject turn) yield return turn;
                    }
            }
            else foreach (var turn in (state["turns"] as JArray ?? new JArray()).OfType<JObject>()) yield return turn;
        }

        public static string Render(JObject state)
        {
            var output = new StringBuilder();
            var turns = Turns(state).ToList();
            if (turns.Count > 30) output.AppendLine("Viser de seneste 30 indlæste ture. Hele historikken findes i Codex-appen.\n");
            foreach (var turn in turns.Skip(System.Math.Max(0, turns.Count - 30)))
            {
                string input = string.Join("\n", (turn["params"]?["input"] as JArray ?? new JArray())
                    .Where(t => (string)t["type"] == "text").Select(t => (string)t["text"]));
                if (!string.IsNullOrWhiteSpace(input)) output.AppendLine("Dig\n" + input.Trim() + "\n");
                foreach (var item in turn["items"] as JArray ?? new JArray())
                {
                    string type = (string)item["type"];
                    if (type == "agentMessage" && !string.IsNullOrWhiteSpace((string)item["text"]))
                        output.AppendLine("Codex\n" + (string)item["text"] + "\n");
                }
                if (turn["error"] != null && turn["error"].Type != JTokenType.Null)
                    output.AppendLine("Fejl: " + ((string)turn["error"]?["message"] ?? turn["error"].ToString()) + "\n");
            }
            return output.Length == 0 ? "Chatten er forbundet. Skriv en besked nedenfor." : output.ToString();
        }
    }
}
