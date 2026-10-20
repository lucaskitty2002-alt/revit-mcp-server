# Codex shared chat in Revit

Version 2.5 replaces the failed native-window panel with a **text composer connected to an existing Codex desktop conversation**. The desktop app remains the conversation owner and executes its configured tools, including `revit-mcp`. This is an experimental integration with the app's internal desktop/IDE coordination protocol, verified against desktop 26.1013; it is not a supported public embedding API or full copy of the app UI.

## Use

1. Install and sign in to Codex. Keep the desktop app open.
2. Close Revit and install the matching package using `INSTALLA.bat`.
3. Start Revit and enable **Revit MCP Switch** for model tools.
4. Open the desired existing chat in Codex.
5. In **Codex Chat**, choose **Hent chats**, select that same chat, and choose **Forbind chat**.
6. Type in the Revit composer. Enter sends; Shift+Enter inserts a newline. Messages and text answers come from the same conversation. Snapshots refresh every two seconds.

Approvals, questions, attachments, model selection and other desktop features remain in the Codex app. The panel shows the last 30 loaded conversation turns as selectable plain text. It disables sends during an active turn and until a previous message is acknowledged in shared history. A delivery timeout never automatically retries a model operation: check the app before reconnecting and retrying. Hiding the panel disconnects the follower; reconnect when reopening it.

## Architecture and limitations

`CodexSharedChat` connects to the local `codex-ipc` named pipe, discovers the owner of the selected persistent thread, follows snapshots and forwards `thread-follower-start-turn` requests. It does not acquire thread ownership, resume the conversation in another server, change permissions/model/tools, read credentials, or write session files. The short-lived CLI app-server used for the chat list only calls `thread/list`.

The internal protocol can change when Codex updates. Unknown snapshot versions fail closed with an update message. The desktop must already own the selected local chat. Revit supports text sending and text replies here; full app feature parity and complete approval UI are not implemented. See [validation status](docs/shared-chat-status.md).

The Revit MCP server exposes 138 model tools. This project builds on [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server); its license and notices are preserved.

## Build and verify

```powershell
dotnet build plugin/RevitMCPPlugin.csproj -c "Release R26"
dotnet run --project tests/shared-chat/SharedChat.Tests.csproj
dotnet run --project tests/codex-panel/CodexPanel.Tests.csproj
./tests/codex-panel/Test-Installer.ps1
```

Optional read-only live probe: set `CODEX_THREAD_ID` to a local conversation currently open in the desktop, then run `dotnet run --project tests/shared-chat/SharedChat.Tests.csproj -- --probe`. It verifies owner discovery and a transcript snapshot without sending a message or resuming the thread. It never stores chat content.

After installing, send a harmless text message from Revit and verify the same message and reply in the app; send from the app and verify the Revit transcript; check an approval in the desktop; test busy-state refusal, reconnect and app shutdown. Mock tests and the read-only live probe do not establish these UI acceptance checks.

A deployment package also needs the matching command set, MCP server, Node runtime and dependency DLLs. Keep models, credentials, databases, local settings and backups out of Git.
