# Codex for Revit

Chat with Codex inside Autodesk Revit and work with the active model through 138 Revit tools.

The dockable **Codex Chat** panel connects to the installed Codex app-server and uses the user's existing Codex sign-in. Replies stream into the panel; tool activity, errors, cancellation, new chat, and chat export are supported. No separate API key is entered in Revit. The model follows the user's Codex configuration unless changed under **Settings > Codex**.

This fork builds on [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server). The original MIT license and copyright notices are preserved.

## Install

1. Install Codex and sign in on your computer.
2. Close Revit.
3. Extract a **Codex for Revit** release package for your Revit version.
4. Run `INSTALLA.bat`. For an update, run:
   `powershell -ExecutionPolicy Bypass -File scripts/install.ps1 -LocalZip . -RevitVersion 2026 -Force`
5. Start Revit, approve the add-in if prompted, open a model, and enable **Add-Ins > Revit MCP Switch**.
6. Open **Add-Ins > Codex Chat**.

The installer makes a dated backup before replacement and preserves other add-ins. The chat panel discovers Codex automatically; **Settings > Codex** supports a custom executable path, login check, sign-in, and optional model selection. Start a new chat after changing settings.

The installer also registers `revit-mcp` for use in the Codex desktop app. Start a new Codex desktop chat to load that connection.

## Architecture

```text
Revit Codex Chat panel → local Codex app-server → existing Codex account
                       ← streamed messages and Revit tool requests
Revit tool requests → local plugin TCP bridge → active Revit document
```

The panel supplies the Revit tool schemas directly to app-server. Other configured MCP servers are disabled for this panel's conversation. The plugin does not read or copy authentication tokens. Panel conversations are ephemeral; export the chat before closing Revit if you need a copy.

The external MCP server in `server/` remains available for Codex desktop. Revit tools use the active model; coordinates are in millimeters. Tool descriptions and parameters are in [COMMANDS.md](COMMANDS.md).

The app-server integration uses the documented experimental dynamic-tool interface. A compatible current Codex installation is required. See [OpenAI's app-server documentation](https://learn.chatgpt.com/docs/app-server).

## Build

Revit 2026 needs the .NET 8 SDK. Other existing configurations target Revit 2023–2027.

```powershell
dotnet build plugin/RevitMCPPlugin.csproj -c "Release R26"
dotnet build commandset/RevitMCPCommandSet.csproj -c "Release R26"
cd server
npm ci
npm run build
```

A deployment package must contain the plugin output, matching command set, tool schemas, and bundled MCP server/runtime. See the release workflow for packaging. Never install only source files.

## Verify

```powershell
dotnet run --project tests/codex-panel/CodexPanel.Tests.csproj
dotnet run --project tests/codex-panel/CodexPanel.Tests.csproj -- --probe plugin/tool_schemas.json
```

The first command tests the actual client with a simulated app-server and Revit TCP bridge: Unicode round trips, tool errors, unknown-tool rejection, failed turns, reconnection, default approval denial, new chat, cancellation, and process exit. The probe verifies the real installed Codex login and registration of all tool schemas without requesting model inference.

A live model read must be verified separately after opening Revit. Use **Verify Connection** in the panel or `scripts/test-codex-mcp.mjs NODE SERVER_ENTRY --revit`. Model edits require a test model and a separate functional test.

## Local data

Do not commit model files, databases, credentials, user configuration, exports, or installation backups. Plugin preferences and a scratch workspace are stored under `%LOCALAPPDATA%/RevitMCP/Codex`. Codex manages account authentication.
