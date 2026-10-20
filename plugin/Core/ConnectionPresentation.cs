namespace revit_mcp_plugin.Core
{
    /// <summary>Listening does not imply that an AI client is connected.</summary>
    public sealed class ConnectionPresentation
    {
        public bool IsOn { get; private set; }
        public bool HasError { get; private set; }
        public string Label { get; private set; }
        public string Detail { get; private set; }
        public string Action { get; private set; }
        public string StateKey { get; private set; }

        public static ConnectionPresentation From(bool isRunning, string error, int port)
        {
            bool hasError = !isRunning && !string.IsNullOrWhiteSpace(error);
            return new ConnectionPresentation
            {
                IsOn = isRunning,
                HasError = hasError,
                Label = isRunning ? "MCP tændt" : hasError ? "MCP fejl" : "MCP slukket",
                Detail = isRunning
                    ? "Revit lytter lokalt på port " + port + ". En AI-klient kan forbinde."
                    : hasError ? error : "MCP er slukket. Ingen nye modelkommandoer accepteres. En allerede startet kommando kan afsluttes.",
                Action = isRunning ? "Klik for at slukke MCP." : "Klik for at tænde MCP.",
                StateKey = isRunning + "|" + port + "|" + (error ?? "")
            };
        }
    }
}
