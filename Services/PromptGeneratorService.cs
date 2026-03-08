using System.Text;

namespace ABeNT.Services
{
    public static class PromptGeneratorService
    {
        public static string BuildMetaPrompt(string fachgebiet, string? untersuchung, bool includeAnamnese)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Du bist ein medizinischer Dokumentations-Experte und Prompt-Engineer.");
            sb.AppendLine("Deine Aufgabe: Erstelle Prompt-Anweisungen für ein KI-System, das Arzt-Patienten-Gespräche");
            sb.AppendLine("in strukturierte medizinische Berichte (nach dem ABeNT-Schema) umwandelt.");
            sb.AppendLine();
            sb.AppendLine($"Fachgebiet: {fachgebiet}");
            if (!string.IsNullOrWhiteSpace(untersuchung))
                sb.AppendLine($"Spezielle Untersuchung / Kontext: {untersuchung}");
            sb.AppendLine();

            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("REFERENZ-BEISPIELE (Stil und Detailtiefe übernehmen)");
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine();

            if (includeAnamnese)
            {
                sb.AppendLine("--- REFERENZ: Allgemeinmedizin-Anamnese (EXAKT diese Tag-Struktur übernehmen!) ---");
                sb.AppendLine(GetReferenceAnamneseAM());
                sb.AppendLine();
            }

            sb.AppendLine("--- REFERENZ: Allgemeinmedizin-Befund (EXAKT diese Tag-Struktur übernehmen!) ---");
            sb.AppendLine(GetReferenceBefundAM());
            sb.AppendLine();

            sb.AppendLine("--- REFERENZ: Diagnosen inkl. ICD-10 (EXAKT diese Tag-Struktur übernehmen!) ---");
            sb.AppendLine(GetReferenceDiagnosenIcd10());
            sb.AppendLine();

            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("DEINE AUFGABE");
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Erstelle jetzt für das Fachgebiet \"{fachgebiet}\" die folgenden Prompt-Module.");
            sb.AppendLine("Orientiere dich exakt am Stil, der Struktur und der Detailtiefe der Referenz-Beispiele.");
            sb.AppendLine("ZWINGEND: Übernimm die Tag-Struktur der Referenzen (<system_instruktion>, <formatierungs_regeln>, <ausgabe_template> bzw. <extraktions_parameter>). Jedes Modul MUSS diese Tags enthalten.");
            sb.AppendLine("Passe die Inhalte fachspezifisch an:");
            sb.AppendLine("- Verwende die typischen Untersuchungen, Symptome und Terminologie des Fachgebiets");
            sb.AppendLine("- Definiere fachspezifische Fallback-Sätze");
            sb.AppendLine("- Priorisiere die für das Fachgebiet relevanten ICD-10-Kapitel");
            sb.AppendLine("- Keine ### oder andere Markdown-Syntax in Überschriften");
            sb.AppendLine();

            if (includeAnamnese)
            {
                sb.AppendLine("Erstelle diese 3 Abschnitte:");
                sb.AppendLine("1. A — Fachspezifische Anamnese (mit Unterkategorien wie in den Referenzen)");
                sb.AppendLine("2. Be — Fachspezifischer Befund (mit typischen Untersuchungen)");
                sb.AppendLine("3. N — Diagnosen inkl. ICD-10-Codierung (kombinierter Prompt, eine Zeile pro Diagnose mit Code)");
            }
            else
            {
                sb.AppendLine("Erstelle diese 2 Abschnitte (KEINE Anamnese, da reine Untersuchung):");
                sb.AppendLine("1. Be — Fachspezifischer Befund (mit typischen Untersuchungen/Messungen)");
                sb.AppendLine("2. N — Diagnosen/Beurteilung inkl. ICD-10-Codierung (kombinierter Prompt)");
            }

            sb.AppendLine();
            sb.AppendLine("WICHTIG: Verwende exakt das folgende Delimiter-Format. Jeder Abschnitt beginnt mit einer Marker-Zeile und endet vor dem nächsten Marker. Kein JSON, kein Markdown, kein Erklärungstext.");
            sb.AppendLine();
            if (includeAnamnese)
            {
                sb.AppendLine("===A===");
                sb.AppendLine("FACH-MODUL ANAMNESE: ... (kompletter Prompt-Text hier)");
                sb.AppendLine("===Be===");
            }
            else
            {
                sb.AppendLine("===A===");
                sb.AppendLine("(leer lassen)");
                sb.AppendLine("===Be===");
            }
            sb.AppendLine("FACH-MODUL BEFUND: ... (kompletter Prompt-Text hier)");
            sb.AppendLine("===N===");
            sb.AppendLine("FACH-MODUL DIAGNOSEN: ... (kompletter Prompt-Text inkl. ICD-10-Anweisungen, eine Zeile pro Diagnose mit Code)");
            sb.AppendLine("===END===");

            return sb.ToString();
        }

        private static string GetReferenceAnamneseAM() => OutputFormsService.GetDefaultAnamnesePromptAM();

        private static string GetReferenceBefundAM() => OutputFormsService.GetDefaultBefundPromptAM();

        private static string GetReferenceDiagnosenIcd10() => OutputFormsService.GetDefaultDiagnosenPrompt("Allgemeinmedizin", includeIcd10: true);
    }
}
