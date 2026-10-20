# Revit MCP server

This TypeScript server exposes 138 Revit tools to the Codex desktop app over stdio and connects to the Revit plugin through local TCP.

The Revit panel now hosts the actual desktop app window. It does not start a second chat engine. MCP registration and tool access remain managed by Codex regardless of where the window is displayed.

Build with `npm ci` and `npm run build`. The packaged installer registers the bundled runtime and entry point automatically:

```text
codex mcp add revit-mcp -- NODE_PATH SERVER_BUILD_INDEX_PATH
```

Start a new Codex chat after registration and enable Revit MCP Switch in the open model. See [README](../README.md) for setup.
