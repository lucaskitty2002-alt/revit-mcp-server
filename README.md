# Codex desktop window in Revit

This Revit add-in can host the **actual Codex desktop app window** inside its dockable panel. It moves the selected app window into Revit; it does not stream a screenshot, create another chat, or reproduce the Codex interface. Chats, models, skills, approvals, files and other app features remain owned by the Codex desktop app.

The Revit MCP server still exposes 138 model tools to Codex. This project builds on [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server); its MIT license and copyright notices are preserved.

## Install and use

1. Install the Codex desktop app and sign in.
2. Close Revit, extract the matching package, and run `INSTALLA.bat`. The installer backs up this add-in and preserves other add-ins.
3. Start Revit, open a model, and enable **Add-Ins > Revit MCP Switch**.
4. Open **Add-Ins > Codex Chat**.
5. Choose **Open Codex**, then **Refresh windows**, select the app window and choose **Show in Revit**.
6. Choose the conversation in the real Codex app. Use its registered `revit-mcp` tools for Revit operations.

Use **Release to desktop** to return Codex to its normal window. Hiding the panel or closing Revit normally releases it as well. The Codex app is never intentionally closed or terminated by the embedding component.

The installer registers `revit-mcp` in Codex; start a new desktop chat after changing MCP registration. This registration is independent of where the app window is displayed. Configure models, account, tools and skills in Codex itself. The panel's former separate app-server conversation is no longer used by the UI.

## Windows compatibility

Native window embedding is experimental and is not an officially supported Codex embedding API. The implementation uses Windows `SetParent` with a WPF `HwndHost`.

- Codex and Revit must run at compatible permission levels.
- The host refuses incompatible DPI-awareness contexts before changing the app. Windows can otherwise reset another process's DPI mode.
- Menus, dialogs, sign-in windows and additional app windows may remain separate desktop windows.
- Only one panel can host a selected window at a time. Select another window or release the current host first.
- On orderly unload, original parent, styles, visibility and window placement are restored.
- A recovery record is kept under `%LOCALAPPDATA%/RevitMCP/Codex/desktop-window-recovery.json`. **Recover window** can restore a surviving orphaned window after its host is gone. Windows may destroy child windows during a hard process crash; recovery cannot recreate a destroyed app window.
- The plugin does not read or copy Codex credentials, chat content, or the app's internal UI data.

Embedding itself does not add capabilities that the actual Codex app/account does not have. Use Codex beside Revit if Windows rejects the embedding on your display setup.

References: [Windows SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent), [WPF HwndHost](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/hosting-win32-content-in-wpf).

## Build and verify

Revit 2026 uses the .NET 8 SDK on Windows.

```powershell
dotnet build plugin/RevitMCPPlugin.csproj -c "Release R26"
dotnet run --project tests/desktop-window/DesktopWindow.Tests.csproj
dotnet run --project tests/codex-panel/CodexPanel.Tests.csproj
./tests/codex-panel/Test-Installer.ps1
```

Native tests create their own hidden window in a separate test process. They verify cross-process attachment, resize, responsiveness, restoration, recovery, mismatched-window rejection, DPI rejection and app exit without moving any user application. Existing protocol tests retain coverage for the earlier client components; those components are not started by the new window panel.

After installation, test actual Codex discovery, attachment, typing, shortcuts, resizing, popup dialogs, release and normal Revit shutdown. Real Codex/Revit interaction must be verified in the user's running apps; a controlled native test window does not establish compatibility with every Electron/Revit/display configuration.

A deployment package also needs the matching command set, MCP server, Node runtime and dependency DLLs. See the release workflow for complete packaging. Keep models, exports, local settings, credentials, databases and backups out of Git.
