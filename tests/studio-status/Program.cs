using revit_mcp_plugin.Core;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

var off = ConnectionPresentation.From(false, null, 8080);
Check(!off.IsOn && !off.HasError && off.Label == "MCP slukket", "Initial listener state must be visibly off.");
Check(off.Action.Contains("tænde") && off.Detail.Contains("Ingen nye") && off.Detail.Contains("allerede startet"), "Off status must explain its action and permit already admitted commands to finish.");

var on = ConnectionPresentation.From(true, null, 8087);
Check(on.IsOn && !on.HasError && on.Label == "MCP tændt", "A running listener must be visibly on.");
Check(on.Detail.Contains("8087") && on.Detail.Contains("kan forbinde"), "Listening must not falsely claim an AI client is connected.");
Check(on.Action.Contains("slukke"), "Running listener must offer to turn off.");

var failed = ConnectionPresentation.From(false, "Bind failed", 8080);
Check(!failed.IsOn && failed.HasError && failed.Label == "MCP fejl", "A failed listener must never appear ready.");
Check(failed.Detail == "Bind failed", "The actual startup error must remain available.");
Check(failed.StateKey != off.StateKey && on.StateKey != off.StateKey, "Ribbon cache must invalidate when state changes.");
Check(ConnectionPresentation.From(true, "Old error", 8080).IsOn, "A stale diagnostic must not overrule the actual listener state.");
Check(ConnectionPresentation.From(false, " ", 8080).Label == off.Label, "Whitespace diagnostics must not invent an error state.");

Console.WriteLine($"VUE Studio connection presentation: {checks} checks passed.");
