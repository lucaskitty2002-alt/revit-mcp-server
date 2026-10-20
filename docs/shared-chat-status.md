# Desktop window validation status

## Accepted requirement

The user confirmed that placing the original Codex window over the Revit panel and moving it back to the desktop is acceptable. The requirement is the full original app interface inside the Revit area. Two simultaneously interactive copies are not required.

## Version 2.6 candidate

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
