# Shared chat validation status

## Requirement

Two independently usable windows showing and sending to the same persistent Codex conversation. Revit supports a text interface; the desktop remains responsible for tool execution, approvals, input questions and full app features.

## Version 2.5 candidate

The failed version 2.4 native-window approach has been replaced in the panel. The installed 2.4 binary has no composer and cannot synchronize chats; updating source or documentation does not change an already loaded Revit DLL.

Investigation found the desktop's separate `codex-ipc` coordination pipe. The absence of an app-server daemon socket did not mean desktop chat sharing was unavailable. A read-only C# live probe successfully discovered the owner of the current desktop chat and retrieved that same persistent thread's conversation snapshot and transcript. No messages were sent by this probe, and no thread was resumed in a competing backend.

`CodexSharedChat` uses the desktop/IDE follower protocol, snapshot version 11, against installed desktop 26.1013. Requests go to the owner and preserve its settings. This is an internal, version-dependent integration, not a supported public Revit integration. The app-server documentation alone does not document this bridge.

## Completed automated validation

- Revit 2026 build.
- Isolated named-pipe test: fragmented framing, owner selection, same-thread send and Unicode, canonical history rendering, unchanged execution settings and active-turn refusal.
- Read-only live desktop owner discovery and same-thread transcript retrieval.

## Still required in the user's running apps

- Install the 2.5 binary with Revit closed.
- Send a harmless message from the Revit composer and verify its message/reply in both interfaces.
- Send from the desktop and verify the Revit history.
- Check desktop approvals/questions, active-turn conflicts, reconnect and app closure.

Do not describe end-to-end Revit sending as verified until these checks succeed. Revit text/history UI does not implement desktop feature parity. Full snapshots refresh every two seconds; version changes fail closed. There is no automatic retry after an unknown delivery outcome.

Official documentation describes desktop/IDE shared chats: [Developer settings](https://learn.chatgpt.com/docs/developer-settings). The bridge used here was verified from the installed app and a live read-only probe.
