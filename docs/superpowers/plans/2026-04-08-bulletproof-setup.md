# Setup implementation reference

Current setup code:

- `scripts/install.ps1`: validated installation with backup and rollback.
- `scripts/common.ps1`: Codex discovery and MCP registration.
- `scripts/diagnose.ps1`: installation and Codex diagnostics.
- `plugin/Helpers/HealthChecker.cs`: plugin file, Codex, and TCP checks.
- `plugin/UI/CodexAppServer.cs`: JSON-RPC process transport.
- `plugin/UI/CodexRevitClient.cs`: conversation and Revit tool bridge.
- `tests/codex-panel`: integration tests and real Codex protocol probe.

See [README](../../../README.md) for current build and validation commands.
