# VUE Studio i Revit 2026

1. Luk Revit. Pak den komplette kandidat-ZIP ud, og kør `scripts/install.ps1 -RevitVersion 2026 -LocalZip . -Force` fra pakkens mappe. Den tidligere installation sikkerhedskopieres; andre plugins bevares.
2. Åbn Revit og en testmodel. Vælg **VUE Studio > MCP slukket** for at tænde modelværktøjerne. Knappen og panelet viser **MCP tændt**, **MCP slukket** eller **MCP fejl** ud fra den faktiske lyttertilstand.
3. Vælg **Åbn Studio > Codex**. Vælg dit åbne Codex-appvindue, og tryk **Vis i Revit**.
4. Brug appens egne chats, skrivefelt, billeder og godkendelser. Der er ét originalt appvindue; vinduesvisningen opretter ikke en ny AI-samtale.
5. Hvis panelet er for lille, udvid det eller vælg **Brug på skrivebord**. Minimumsstørrelsen bestemmes af Codex-appen og omgås ikke.
6. **Hurtigchat** kan forbinde til en eksisterende samtale i appen og sende tekst. Denne eksperimentelle forbindelse følger appens private protokol; brug det originale appvindue, hvis forbindelsen ikke understøttes.
7. **Arbejdsgange** indsætter en startbesked i kladden. Tilpas den, og vedlæg instruktionsbog og kildefiler i Codex før afsendelse. Valget sender ikke selv en besked eller ændrer modellen.

**Tændt** betyder, at Revit lytter lokalt, ikke at en bestemt Codex-klient er forbundet. Slukning lukker klienterne og afviser nye/ventende kommandoer fra den gamle forbindelse. En allerede påbegyndt kommando kan afsluttes.

Pause/baggrundsstatus vises også, mens vinduet er placeret. **Vis igen** genoptager placeringen; menuen **⋯** indeholder gendannelse og teknisk info. Den levende forhåndsvisning er kun et billede uden betjening. Det originale vindues ejer, ramme og DPI-kontekst bevares.

Før brug i forsøget skal faktisk Codex-indtastning, billeder/godkendelser, vinduesgendannelse og monitor-/skaleringsovergang kontrolleres på skole-pc'en. Denne kandidat er bygget og testet med kontrollerede lokale vinduer; den er ikke installeret eller afprøvet i skolens Revit herfra.

Kredit til de oprindelige projekter og BIMwright-inspirationen findes under **Om Studio** og i **NOTICE.md**. Ingen BIMwright-kode er kopieret i denne udgave.
