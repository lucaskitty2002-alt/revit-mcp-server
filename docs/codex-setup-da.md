# Revit MCP i Codex

Denne fork bygger på [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server), release v2.2.1, commit `2c33b848602ca56a4043de603f2914d6fdf0c104`.
Den oprindelige MIT-licens og alle copyright-angivelser bevares i `LICENSE`. Det gælder også kopien installeret sammen med pluginet. Afhængighedernes licenser skal fortsat bevares.

## Installation til Revit 2026

Hent `mcp-servers-for-revit-v2.2.1-Revit2026.zip` fra upstreams release. SHA256:
`2c46ad557b69ac3b36fade076b16e36d7bbb7b1108a2d273eed60a5f2426749b`.

Luk Revit. Kør `scripts/install-codex.ps1 -ReleaseZip <zip> -CodexExe <codex.exe>` med de konkrete lokale stier.
Scriptet installerer kun dette plugin i brugerens Revit 2026 Addins-mappe og registrerer `revit-mcp` i Codex. En eksisterende installation bliver ikke overskrevet. Andre plugins bevares. Claude-konfiguration og Anthropic-nøgler er ikke nødvendige for denne Codex-forbindelse.

Start Revit, åbn en model og aktivér **Revit MCP Switch** under **Add-Ins**. Eventuel tillidsgodkendelse til tredjepartspluginet skal håndteres ved første start. Start derefter en ny Codex-chat for at indlæse den nye MCP-konfiguration.

## Test

Brug `scripts/test-codex-mcp.mjs NODE SERVER_ENTRY` til at kontrollere MCP-initialisering og værktøjslisten. Tilføj `--revit` for at teste en læseoperation mod den aktive model. NODE og SERVER_ENTRY findes under `revit_mcp_plugin/Commands/RevitMCPCommandSet/server`, henholdsvis `runtime/node.exe` og `build/index.js`.

Værktøjslisten kan fungere uden Revit. Det beviser ikke forbindelse til en model. Læsetesten skal bestås separat; tegning og parameterændringer kræver derefter særskilt test på en testmodel.

Pluginets TCP-forbindelse lytter på loopback. Arbejdsmodeller, database, rendering, nøgler og brugerens lokale konfiguration skal holdes ude af GitHub.
