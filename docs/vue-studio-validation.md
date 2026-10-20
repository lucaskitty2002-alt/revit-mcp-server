# VUE Studio 2.7 candidate validation — 20 October 2026

Based on the shared school-PC branch `codex/physical-panel-bounds`, commit `82dd510`. This record is about a newly built candidate, not an installed or accepted school-PC version.

## Implemented

- Dedicated VUE Studio ribbon and redesigned three-tab panel; vector identity, Danish UI, and visible MIT/upstream and BIMwright inspiration credit.
- Actual listener-based on/off/error state, including failed startup/listener exit; modeless power control enters Revit through ExternalEvent.
- Per-generation client transport: Stop closes accepted clients, interrupts blocked network reads, rejects buffered commands and prevents old clients reviving after restart. Work admitted before Stop may finish.
- Same original Codex window, with persistent pause/background status, explicit desktop fallback and minimum-size rollback. No unsupported cross-process parent/owner change was added.
- Text-only follower remains desktop-owned. Idle snapshot traffic throttles from every 2s to every 30s; active/delta/pending-send traffic uses 2s, coalesces outstanding refreshes and only times out an unanswered explicit request. No automatic turn replay.
- Opening the default Codex panel no longer starts a read-only app-server just to list chats. List retrieval begins when Hurtigchat is selected.
- Five instruction-book starters prepare editable local drafts only. No full instruction documents or source drawings are silently added, and no draft auto-sends.

## Executed checks

- .NET SDK 8.0.426, Revit 2026 plugin: build passed, **0 warnings / 0 errors**.
- Revit 2026 command set: build passed, **0 errors**. Three existing warnings: redundant System.Net.Http reference conflict, unused `ex` in CreateDimensionEventHandler, unused `groupId` in ExportSharedParameterFileEventHandler. These source files were not changed.
- Studio status: **10 checks passed** (truthful ON/OFF/error/provenance/action labels).
- Studio transport: **18 checks passed** using actual local sockets, including idle clients, stop/restart, buffered requests, parse/dispatch guard, fragmented Unicode and unexpected listener failure.
- Shared chat synthetic IPC suite passed, including same-thread history/send, owner loss, active turn conflict, adaptive/coalesced reads, cancellation and concurrent exactly-once send reservation. No real AI request.
- Codex panel suite passed, including usage labels, fake app-server streaming/approval behavior, tool allowlist and attachment regressions. It tests the retained legacy client as well as shared components, not that the legacy client is active in the panel.
- Native window fixture suite passed: composed pixels, top-level/native owner-frame preservation, physical bounds, source/host minimize/restore, modal lifetime, source identity, native minimum-size rejection and exact rollback. No user app was operated.
- SystemAware WPF probe passed conversion against native physical client bounds. This does **not** establish actual Codex/Revit cross-monitor compatibility.
- TypeScript `tsc --noEmit`, esbuild server bundle, schema generation and MCP initialize/list smoke test passed: **138 tools**, no model call.
- Installer fixture passed fresh install, required explicit replacement, backup, stale-file cleanup, invalid-package rejection and preservation of other add-ins. All fixture paths were isolated from the real Revit installation.
- Actual compiled WPF controls rendered offscreen at 360×700 and 900×800 DIP: Codex, Hurtigchat, Arbejdsgange and native-panel layouts inspected. This is static layout QA: no HWND/Loaded handlers, native app discovery, account or inference used.
- The complete archive was extracted and started with its bundled Node runtime independently of source node_modules; initialization/listing returned 138 tools. The packaged DLL hash matched the tested Revit 2026 build. A full installation into isolated APPDATA/LOCALAPPDATA fixture paths also matched the package and did not alter the real Revit installation or Codex registration.

## Dependency refresh after the baseline GitHub alert

The baseline production lock had 9 vulnerable packages in npm's aggregate audit. A non-forced, semver-compatible npm lock refresh raised the direct MCP SDK minimum to ^1.31.0 and resolved SDK 1.32.1, retaining Zod 3.25.76 and sql.js 1.14.1. Transitive fixes include Hono 4.13.13, @hono/node-server 2.1.5, body-parser 2.3.0, fast-uri 3.1.9, ip-address 10.7.4, proxy-addr 2.0.8 and qs 6.16.1. No direct dependency major upgrade or audit --force was used.

Fresh npm 11.21.1 `npm ci --ignore-scripts` completed (137 installed / 138 audited packages) with **0 known npm vulnerabilities**. The production-only lock audit likewise returned zero at every severity. This is a database snapshot, not a guarantee against unknown vulnerabilities or a statement that the repository's default branch alerts have been cleared. These fixes are on the VUE Studio branch; other open dependency PRs were not merged.

TypeScript checking, direct esbuild bundling and the dependency-free stdio initialization/listing test passed again against the updated lock (VUE Studio 2.7.0 / 138 tools). The chosen adapter requires Node 20+, now declared in the package; the installation archive bundles Node 24.19.0 and its matching LICENSE. JavaScript and NuGet dependency notices are included with the archive. No HTTP/OAuth listener was enabled; this plugin still uses local stdio MCP.

## Consumption conclusion

The active native window path contains no inference request. The shared follower sends one explicit desktop-owned turn and inherits its settings. Old `CodexRevitClient` remains unconstructed by the active panel; it creates an independent thread and registers all 138 schemas (219,964 bytes of JSON in this checkout). That is a plausible explanation for earlier differences, not a measurement of the user's installed version.

Idle local snapshot requests reduce from 15 to one per 30s. This is local traffic, not demonstrated token savings. No assertion is made that a Revit task costs the same as an ordinary text task: models, effort, history, tool results and workload differ. Cumulative token processing/latest usage/account quotas are explicitly distinguished.

## Manual acceptance still required

1. Install the complete 2.7 candidate on the school PC with Revit closed, preserving the backup.
2. Confirm the VUE Studio ribbon, actual MCP status changes, startup failure status and power control.
3. Attach the real Codex window; type, select chats, scroll, attach an image, answer an approval and use native menus.
4. Enlarge/float a too-small panel; confirm its clear error and use the same-window desktop fallback.
5. Move/resize/minimize/restore Revit and Codex, test multiple monitors/scalings, hide the panel, close normally, and verify recovery/desktop placement.
6. Use a copied test model for a read-only MCP check, then an explicitly requested small reversible model action; confirm model identity and output.
7. Compare the same task/model/effort/context with quota snapshots if investigating actual consumption. Current code inspection is not a usage benchmark.

The previous actual-app failures and installation records remain in [shared-chat-status.md](shared-chat-status.md). No success for these new manual checks, school-PC installation or current open-model access is claimed by the local builds/tests.
