# Det rigtige Codex-vindue i Revit

Revit-panelet kan nu indlejre selve Codex-appens vindue. Der oprettes ikke en separat chat. Vælg modeller, chats, filer og værktøjer i Codex, som du plejer.

1. Luk Revit og installer pakken med `INSTALLA.bat`.
2. Start Revit, åbn en model og aktivér **Revit MCP Switch**.
3. Åbn **Codex Chat** i Revit.
4. Vælg **Open Codex**, derefter **Refresh windows**.
5. Vælg det ønskede Codex-vindue og tryk **Show in Revit**.

**Release to desktop** frigør appen igen. Den frigøres også ved normal lukning af panelet eller Revit. **Recover window** kan gendanne et overlevende vindue, hvis dets tidligere vært ikke længere findes.

Dette er en eksperimentel Windows-integration. Appsene skal have kompatible rettigheder og skærmskalering. Uforenelig skalering afvises, før vinduet ændres. Nogle dialoger og ekstra appvinduer kan åbne separat på skrivebordet. Ved et hårdt Revit-nedbrud kan Windows lukke et indlejret vindue; gendannelse kan ikke genskabe et vindue, der allerede er ødelagt.

Modelforbindelsen fungerer fortsat gennem `revit-mcp` i Codex. Efter ny registrering af MCP-forbindelsen skal du starte en ny Codex-chat. Tilgængelige værktøjer og kontofunktioner følger den rigtige app.

Se [README](../README.md) for bygning og test. Projektets MIT-licens og oprindelige ophavsretstekster bevares.
