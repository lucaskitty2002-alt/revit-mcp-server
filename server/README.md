# Revit MCP server for Codex

This TypeScript MCP server exposes Revit tools to the Codex desktop app over stdio and connects to the Revit plugin over local TCP. It is separate from the in-Revit chat panel's direct app-server tool bridge.

Run `npm ci` and `npm run build` to compile. Register with:

```text
codex mcp add revit-mcp -- NODE_PATH SERVER_BUILD_INDEX_PATH
```

The Revit installer registers the bundled runtime and server automatically. Start a new Codex chat after registration, open a model in Revit, and enable Revit MCP Switch.

See the repository [README](../README.md) for setup and the Codex chat panel.
