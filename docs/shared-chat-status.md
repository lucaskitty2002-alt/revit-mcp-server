# Shared chat requirement and current blocker

The requested behavior is **two independently usable chat windows, in Revit and in the Codex desktop app, showing and sending to the same conversation**. This is different from moving one native app window into another application.

## Current installation

Version 2.4 implements native window reparenting. It has no independent chat composer or conversation synchronization. On the user's actual setup, it refuses attachment because the Codex and Revit windows have incompatible DPI-awareness contexts. The app is left on the desktop and the Revit panel remains an instruction screen. The controlled native-window tests did not establish compatibility with the user's actual apps.

Do not describe this version as a working synchronized chat, and do not bypass the DPI check to claim synchronization.

## Read-only investigation

The desktop app's local Codex process uses stdio. No shared control socket was found in the local Codex home. The CLI's default app-server control connection did not connect. Therefore an independent Revit client has not been verified against the desktop app's existing live backend.

The documented app-server protocol supports thread IDs, history, user turns and streamed events. That makes custom chat clients possible, but a new app-server process or the same account login does not by itself prove that the desktop app and the Revit panel share one live conversation.

## Acceptance criteria before another installation

1. Verify a supported connection to the backend that owns the desktop conversation.
2. Identify the same persistent thread from both clients without creating a replacement conversation.
3. Send a user-requested message from each UI and verify that both display it and the same resulting reply.
4. Verify tool actions, approvals, active-turn conflicts, reconnects and history without changing the conversation's permissions.
5. Keep a real composer in the Revit UI and describe any features it does not implement. Do not claim complete desktop-app feature parity from a protocol-only test.

No synchronized-client fix has been installed yet. Do not silently substitute an independent chat or copy message text between unrelated conversations.

Reference: [Codex app-server protocol](https://learn.chatgpt.com/docs/app-server).
