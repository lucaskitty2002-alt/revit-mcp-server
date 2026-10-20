namespace revit_mcp_plugin.UI
{
    // Local draft starters aligned with the group's 00–05 instruction books.
    // Selecting one never calls an AI model or changes the Revit project.
    public sealed class WorkflowStarter
    {
        public string Title { get; }
        public string Description { get; }
        public string Prompt { get; }
        private WorkflowStarter(string title, string description, string prompt)
        { Title = title; Description = description; Prompt = prompt; }
        public static WorkflowStarter[] All => new[]
        {
            new WorkflowStarter("01   Tegninger → model", "Overblik over PDF-tegninger, mål og eksisterende typer.", "Gennemgå de vedlagte PDF-tegninger efter bog 01 og den fælles startvejledning. Find relevante sider og revisioner, og kontrollér det åbne Revit-projekt. Beskriv kort det modelomfang, tegningerne understøtter, og spørg kun om nødvendige mål eller konflikter. Brug mine ønsker, hvis jeg har angivet nogen. Modellér ikke endnu. Sig til, hvis vejledningen eller kildefiler mangler."),
            new WorkflowStarter("02   Tegningspakker", "Sammenhold tegningslisten med views, sheets og skabeloner.", "Læs den vedlagte tegningsliste og undersøg det åbne Revit-projekt efter bog 02. Find selv ark, views, titleblocks og view templates. Vis kort, hvilke tegninger der kan genbruges, hvad der skal oprettes, og hvad der mangler. Spørg kun om nødvendige valg. Opret eller ændr intet endnu. Sig til, hvis vejledningen eller tegningslisten mangler."),
            new WorkflowStarter("03   Skemaer og lister", "Find de faktiske parametre til den ønskede oversigt.", "Lav forslag til den ønskede liste efter bog 03. Undersøg det åbne Revit-projekt, og find selv de eksisterende skemaer og de faktiske parametre på objekterne. Vis foreslåede kolonner, afgrænsning og eventuelle datamangler. Opret eller ændr intet endnu. Spørg om listetype og omfang, hvis det ikke er beskrevet, og sig til, hvis vejledningen mangler."),
            new WorkflowStarter("04   Skitse → model", "Afklar enkel geometri og nødvendige mål fra håndskitser.", "Gennemgå de vedlagte håndskitser efter bog 04. Beskriv kort form, etager, vægforløb, tag og eventuelle åbninger. Find selv de eksisterende basic-typer i det åbne Revit-projekt og foreslå en kort typeliste. Angiv nødvendige mål, konflikter og mulige forenklinger. Du må ikke modellere endnu. Sig til, hvis vejledningen eller skitserne mangler."),
            new WorkflowStarter("05   Bygningsdele og opbygninger", "Læs journalen og planlæg kontrollerbare Revit-typer.", "Læs bygningsdelsjournalen efter bog 05. Find alle opbygninger i det aftalte omfang, deres kilder og revisioner. Undersøg det åbne Revit-projekt og foreslå kategori, eksisterende grundtype, navn, materialer og lagrækkefølge for hver bygningsdel. Kontrollér, om integrationen kan læse og skrive de nødvendige typelag. Vis kun nødvendige spørgsmål. Opret eller ændr intet endnu. Sig til, hvis vejledningen eller journalen mangler.")
        };
    }
}
