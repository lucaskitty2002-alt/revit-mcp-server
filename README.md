# Codex desktop app in Revit

Version 2.6.1 places the **original Codex desktop window over the Revit panel**, preserving the app's own interface, mouse/keyboard input, chats, approvals, attachments and tools. The user chose one original app window that can move between Revit and the desktop. This is an experimental Windows window-placement integration, not two simultaneously interactive copies.

## Use

1. Install with Revit closed. Start Revit and enable **Revit MCP Switch** for model tools.
2. Open **Codex Chat > Codex-vindue**. Keep Codex open and signed in.
3. Select the Codex app window and choose **Vis i Revit**. Use the refresh button if the window is missing.
4. Use Codex normally in the panel area. Choose **Frigør** to restore its frame and original placement.

The original app receives input directly. Its native menus and dialogs can open as normal separate windows. Make the panel larger if Codex refuses its size. Use **⋯ > Gendan tidligere placering** after an interrupted placement if the previous host is no longer running. Normal panel close/hide/shutdown releases the app first. A hard process crash is not guaranteed to preserve an owned window.

**⋯ > Levende forhåndsvisning** uses local DWM thumbnails to show a live, view-only image. An early mouse/keyboard-message relay worked for text but failed a native-button test and was removed. Do not describe the preview as an interactive screen-sharing session. **Tekstchat** retains the version 2.5 same-thread text interface, including its limited feature set.

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

## Version 2.6.1 fix and interface

Maximized and minimized source windows are restored before placement. The attached style no longer reapplies the saved maximize/minimize bits; desktop return still restores the original state. Regression tests failed with the old implementation and now pass for normal, maximized and minimized fixtures, including actual composed overlay pixels and native hit testing. Placement checks all four physical screen bounds and confirms the current owner. A green status requires a visible source or owned modal; an existing window handle alone is not treated as success.

The refreshed interface has one primary action, a readable Codex window label, a visible status card and a compact extra-actions menu. Controls adapt to narrow panels. Automatic raising pauses while the panel's own menus are open. A local window-diagnostics.json stores the last operation's window metadata and error, without chat content or credentials.

The source matches its current host's topmost band so a floating host cannot cover it. Desktop return restores the original band. Controlled tests keep their own host visible without taking foreground focus and verify original extended-style restoration.

![Codex panel](docs/images/codex-panel.png)
