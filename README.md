# Codex desktop app in Revit

Version 2.6 places the **original Codex desktop window over the Revit panel**, preserving the app's own interface, mouse/keyboard input, chats, approvals, attachments and tools. The user chose one original app window that can move between Revit and the desktop. This is an experimental Windows window-placement integration, not two simultaneously interactive copies.

## Use

1. Install with Revit closed. Start Revit and enable **Revit MCP Switch** for model tools.
2. Open **Codex Chat > Codex-vindue**. Keep Codex open and signed in.
3. Choose **Hent vinduer**, select the Codex app window and choose **Vis Codex i Revit**.
4. Use Codex normally in the panel area. Choose **Tilbage til skrivebord** to restore its frame and original placement.

The original app receives input directly. Its native menus and dialogs can open as normal separate windows. Make the panel larger if Codex refuses its size. Use **Gendan vindue** after an interrupted placement if the previous host is no longer running. Normal panel close/hide/shutdown releases the app first. A hard process crash is not guaranteed to preserve an owned window.

**Kun forhåndsvisning** uses local DWM thumbnails to show a live, view-only image. An early mouse/keyboard-message relay worked for text but failed a native-button test and was removed. Do not describe the preview as an interactive screen-sharing session. **Tekstchat** retains the version 2.5 same-thread text interface, including its limited feature set.

## Window behavior

`DesktopOverlayLease` changes the top-level window's owner (`GWLP_HWNDPARENT`), frame and screen bounds. It never uses cross-process `SetParent` or converts Codex into a child window. Different DPI-awareness modes are preserved. The original owner, styles and placement are saved before mutation, along with PID/start-time identity, in a local recovery file. A live modal's disabled-owner state is preserved when releasing. Source processes are never terminated by the plugin.

This project exposes 138 Revit model tools and builds on [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server), preserving its license and notices.

## Build and verify

```powershell
dotnet build plugin/RevitMCPPlugin.csproj -c "Release R26"
dotnet run --project tests/window-mirror/WindowMirror.Tests.csproj
dotnet run --project tests/shared-chat/SharedChat.Tests.csproj
./tests/codex-panel/Test-Installer.ps1
```

The window test creates its own source app and host. It verifies composed preview pixels, different DPI modes, original-window placement, top-level identity, unchanged DPI, normal owned-modal behavior and restoration. It does not operate user applications or prove compatibility with the actual Codex app. See [validation status](docs/shared-chat-status.md).

Optional manual preview without installing: run `tests/window-mirror/bin/Debug/net8.0-windows/WindowMirror.Tests.exe --preview <absolute path to RevitMCPPlugin.dll>` and use the visible controls yourself. The helper does not automatically select or operate Codex.

A complete installer package also needs the command set, MCP server, Node runtime and dependency DLLs. Keep models, credentials, databases, local settings and backups out of Git.

Windows references: [top-level window owner](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowlongptrw), [DWM thumbnails](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail).
