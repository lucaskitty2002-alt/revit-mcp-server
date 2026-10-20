# Desktop window validation status

## Accepted requirement

The user confirmed that placing the original Codex window over the Revit panel and moving it back to the desktop is acceptable. The requirement is the full original app interface inside the Revit area. Two simultaneously interactive copies are not required.

## Version 2.6 installed

`DesktopOverlayLease` positions the original top-level app window over the panel and gives it the host as an owner. It preserves its top-level identity and DPI context; it does not use the version 2.4 cross-process SetParent approach. Original owner/frame/placement and process identity are recorded before changes. Normal close/hide/shutdown releases the app, and recovery is available after an interrupted placement. An owned source window is not guaranteed to survive a hard host crash.

A DWM preview is available as a separate view-only option. An attempted window-message input relay passed Unicode text input but failed a native-button click test. That relay was removed. The interactive mode therefore uses the original app's actual native input and controls, not synthetic mouse/keyboard forwarding.

## Completed controlled checks

- Revit 2026 build, zero errors/warnings.
- Actual composed GPU-preview pixels from a controlled source window.
- Different DPI-awareness modes, with the source DPI context preserved.
- Original source remains top-level and fits the panel bounds in physical pixels.
- The source app's normal control opens an owned modal dialog.
- Release preserves the live modal's disabled-owner state.
- Modal closure, owner/style restoration, recovery cleanup and source process preservation.
- Existing shared-chat protocol regression tests.

## Required manual acceptance

- Select the actual Codex app in the new Revit tab and attach it.
- Verify typing, shortcuts, scrolling, existing chat selection, images and approvals using the real app UI.
- Test native menus/dialogs, panel move/resize, desktop return and normal shutdown.

These checks have not been established by controlled-window tests. Do not claim actual Codex/Revit compatibility before the manual trial.

## Previous installed version

Version 2.5.0.0 was installed for Revit 2026 on 2026-10-20. Its limited same-thread text client remains available in the Textchat tab. The earlier live probe verified current-thread owner discovery and transcript retrieval. Version 2.5 did not provide the complete app interface.

## Local installation verification

Version 2.6.0.0 was installed for Revit 2026 on 2026-10-20 with Revit closed. Its installed DLL matches the tested package from source commit 4dcc601. SHA-256: 27268E64F93366EC1673E7326775FEA035C59E9A85D2753D5455A28F198DDA95. The installer backed up the previous add-in and registered revit-mcp. The actual Codex/Revit manual acceptance checks above are still pending.

## Version 2.6.1 installed: reproduced placement bug and clearer UI

A read-only check in the actual Revit session confirmed that version 2.6 was loaded and its button handler ran. Later status reported rejected placement while the Codex source was maximized. The old attach sequence reapplied saved WS_MAXIMIZE/WS_MINIMIZE bits after restoring the source window. Controlled tests reproduced both failures before the fix.

Version 2.6.1 uses SW_RESTORE and clears only the live max/min state while keeping the original show state in the recovery snapshot. All four physical bounds, current window ownership and visual placement are checked. Actual overlay pixel/hit-test checks now pass for normal, maximized and minimized controlled source windows; release restores their original show command, normal placement and styles. An owned dialog without GetLastActivePopup tracking is also covered.

The controlled host uses a temporary topmost band without requesting foreground activation. The overlay matches the actual host's band and restores the source's original extended style/band on release. This covers a second case where a correctly sized source could stay behind its host. Automatic raising is limited to the foreground host/app family and pauses for panel menus.

The UI now has a primary Vis i Revit action, Frigør, a compact menu, visible status/error cards and responsive controls. Window labels use Codex-app instead of exposing ChatGPT plus a PID. A local last-operation diagnostic includes window metadata and errors only. The rendered panel was inspected at 669 × 700 and 360 × 560 pixels.

Version 2.6.1.0 was installed for Revit 2026 on 2026-10-20 with Revit closed. The installer completed its backup, replacement and MCP registration. The installed DLL matches source commit b753ac7 and the tested package, SHA-256: 4C5EBE767B687262CCD5103585DA3696CEC4D85DF3869F6A4017A1C5C10AA920. The canonical add-in manifest was verified. Actual Codex/Revit typing and full-interface acceptance still need the user's trial; installation and controlled tests do not establish those results.

## Version 2.6.1 actual trial failed

The user initially selected a response saying the window appeared but typing failed, then clarified with two screenshots that the Codex content never appeared in the panel. One image shows a blank white area with a false green ready status. The second shows the source reduced to 237 × 39 pixels. The local diagnostic confirms a minimized source at −32000, −32000, not a minimum-size constraint. Recovery rolled the lease back. Do not interpret the initial multiple-choice response as a successful visibility check.

Read-only native metadata identifies the selected HWND as the actual app primary Chrome_WidgetWin_1 with Chrome_RenderWidgetHostHWND and Intermediate D3D Window children. This is a compositor-based app, unlike the WinForms test fixture.

## Version 2.6.2 native-window placement

New leases preserve the source's original owner and frame/styles. They do not assign Revit as owner or force WS_POPUP. Cross-process ownership implicitly attaches input queues ([Microsoft documentation](https://devblogs.microsoft.com/oldnewthing/20130412-00/?p=4683/)); removing it addresses a plausible cause of the blank app but is not proof that the actual app now renders.

Host minimize/hide pauses and temporarily hides a normal source without minimizing it. Host restore resumes its position without activation. A manually minimized/maximized source retains its lease and waits for the explicit Vis igen action. Source pause restores its original topmost band. Metadata diagnostics include native classes/styles, cloaking, host/source state and foreground/focus handles. A neutral placement status replaces the false green ready claim. Actual Codex rendering and input must be confirmed by the user after installation.

Controlled checks pass for native owner/frame preservation, host minimize/restore with actual overlay pixels, independent source minimize/maximize pause with retained lease, original band restoration and explicit resume. Existing mixed-DPI, normal/max/min attach, modal lifetime and exact saved-state restoration checks also pass. Revit 2026 builds with zero warnings/errors; shared-chat and installer regressions pass.

Version 2.6.2.0 was installed for Revit 2026 on 2026-10-20 with Revit closed. The installer backed up version 2.6.1, replaced the add-in and registered revit-mcp. Installed DLL SHA-256 A26AF0E6B5B201B660496EB5B7F0BF457A6C80CDE0BE16B3E9975F08C5D275FA matches the package from source commit e1531f7. The canonical add-in manifest was verified. Actual Codex rendering and input remain pending user acceptance.
