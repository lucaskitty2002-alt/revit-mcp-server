# Codex i Revit 2026

Panelet **Codex for Revit** bruger din installerede Codex og dit eksisterende login. Der skal ikke indtastes en API-nøgle i Revit.

1. Installer Codex, og log ind på din computer.
2. Luk Revit.
3. Pak Codex-versionens installationspakke ud.
4. Kør `scripts/install-codex.ps1 -ReleaseZip <pakke.zip> -Force`.
5. Start Revit, åbn en model, og aktivér **Add-Ins > Revit MCP Switch**.
6. Åbn **Add-Ins > Codex Chat**.

Under **Settings > Codex** kan du kontrollere login, logge ind, vælge codex.exe manuelt eller angive en model. Et tomt modelfelt bruger din Codex-konfiguration. Start en ny chat efter ændringer.

Panelet streamer svar og viser værktøjskald. **Stop** afbryder samtalen; modelændringer, som allerede er udført, bliver i Revit. **New chat** starter en ny samtale. Eksportér chatten, før du lukker Revit, hvis den skal gemmes.

Installationen registrerer også `revit-mcp` i Codex til brug fra skrivebordsappen. Start en ny Codex-chat for at indlæse den.

Tidligere installationer sikkerhedskopieres under `%LOCALAPPDATA%/RevitMCP/Backups`. Andre Revit-plugins bevares. Konti, modeldata, nøgler og lokale indstillinger må ikke lægges på GitHub.

Se [README](../README.md) for bygning og test. Projektet bygger på [originalprojektet](https://github.com/LuDattilo/revit-mcp-server); MIT-licensen og ophavsretsteksterne bevares.
