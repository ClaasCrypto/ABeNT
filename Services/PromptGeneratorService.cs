using System.Text;

namespace ABeNT.Services
{
    public static class PromptGeneratorService
    {
        public static string BuildMetaPrompt(string fachgebiet, string? untersuchung, bool includeAnamnese)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Du bist ein medizinischer Dokumentations-Experte und Prompt-Engineer.");
            sb.AppendLine("Erstelle Prompt-Anweisungen für ein KI-System, das Arzt-Patienten-Gespräche in strukturierte Berichte umwandelt.");
            sb.AppendLine();
            sb.AppendLine($"Fachgebiet: {fachgebiet}");
            if (!string.IsNullOrWhiteSpace(untersuchung))
                sb.AppendLine($"Spezielle Untersuchung / Kontext: {untersuchung}");
            sb.AppendLine();

            sb.AppendLine("WICHTIGE KONTEXTINFO: Folgende Basisregeln sind BEREITS global definiert und dürfen NICHT in den Fach-Modulen wiederholt werden:");
            sb.AppendLine("- Anamnese: Nominalstil, Unterkategorien als Überschriftszeilen, Standard-Fallbacks für alle Kategorien");
            sb.AppendLine("- Befund: Nur dokumentierte Untersuchungen, Blockformat pro Bereich, Vitalparameter zuerst");
            sb.AppendLine("Die Fach-Module enthalten NUR die fachspezifischen Abweichungen und Details.");
            sb.AppendLine();

            sb.AppendLine("--- REFERENZ (zeigt den erwarteten Stil und Umfang) ---");
            if (includeAnamnese)
            {
                sb.AppendLine(GetReferenceAnamneseAM());
                sb.AppendLine();
            }
            sb.AppendLine(GetReferenceBefundAM());
            sb.AppendLine();
            sb.AppendLine(GetReferenceDiagnosen());
            sb.AppendLine();
            sb.AppendLine(GetReferenceIcd10());
            sb.AppendLine();

            sb.AppendLine("--- DEINE AUFGABE ---");
            sb.AppendLine();
            sb.AppendLine($"Erstelle die Prompt-Module für \"{fachgebiet}\".");
            sb.AppendLine("Regeln:");
            sb.AppendLine("- NUR fachspezifische Delta-Anweisungen, keine allgemeinen Regeln wiederholen");
            sb.AppendLine("- Halte jeden Abschnitt unter 150 Wörter (tokeneffizient)");
            sb.AppendLine("- Verwende typische Untersuchungen, Symptome und Terminologie des Fachgebiets");
            sb.AppendLine("- Priorisiere fachspezifische ICD-10-Kapitel");
            sb.AppendLine();

            if (includeAnamnese)
            {
                sb.AppendLine("Erstelle diese 4 Abschnitte:");
                sb.AppendLine("1. A — Fachspezifische Anamnese (mit Unterkategorien wie in den Referenzen)");
                sb.AppendLine("2. Be — Fachspezifischer Befund (mit typischen Untersuchungen)");
                sb.AppendLine("3. N — Diagnosen-Anweisungen");
                sb.AppendLine("4. Icd10 — ICD-10-Codierung mit fachspezifischer Priorisierung");
            }
            else
            {
                sb.AppendLine("Erstelle diese 3 Abschnitte (KEINE Anamnese, da reine Untersuchung):");
                sb.AppendLine("1. Be — Fachspezifischer Befund (mit typischen Untersuchungen/Messungen)");
                sb.AppendLine("2. N — Diagnosen-Anweisungen");
                sb.AppendLine("3. Icd10 — ICD-10-Codierung mit fachspezifischer Priorisierung");
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
            sb.AppendLine("... (kompletter Prompt-Text hier)");
            sb.AppendLine("===Icd10===");
            sb.AppendLine("... (kompletter Prompt-Text hier)");
            sb.AppendLine("===END===");

            return sb.ToString();
        }

        private static string GetReferenceAnamneseAM()
        {
            return @"FACH-MODUL ANAMNESE: ALLGEMEINMEDIZIN

Jetziges Leiden:
Vorstellungsgrund, aktuelle Symptomatik: Lokalisation, Charakter, Dauer, Auslöser, zeitlicher Verlauf, Begleitsymptome. Bisherige Selbstmedikation oder Akutbehandlungen.

Vorerkrankungen:
Ergänze: Impfstatus, Vorsorgeuntersuchungen, familiäre Belastung (kardiovaskulär, Diabetes, Tumorerkrankungen) sofern erwähnt.

Dauermedikation:
Format: Name Dosierung Einnahmeschema (z.B. Metformin 1000 mg 1-0-1).

Vegetative Anamnese:
B-Symptomatik (Fieber, Nachtschweiß, Gewichtsverlust), Appetit, Schlaf, Miktion, Stuhlgang.

Noxen / Sozialanamnese:
Nikotinkonsum (pack years), Alkohol, Drogen, Beruf, häusliche Situation, Pflegebedarf.";
        }

        private static string GetReferenceBefundAM()
        {
            return @"FACH-MODUL BEFUND: ALLGEMEINMEDIZIN

Vitalparameter: RR, Puls, Temperatur, SpO2, Gewicht, Größe.

Organsysteme (nur wenn untersucht):
AZ/EZ. Haut/Schleimhäute. Kopf/Hals (LK, Schilddrüse). Herz (Rhythmus, Töne, Geräusche). Lunge (Atemgeräusch, RG, Perkussion). Abdomen (DG, Druckschmerz, Resistenzen). Extremitäten (Ödeme, Pulse). Neurologie orientierend.";
        }

        private static string GetReferenceDiagnosen()
        {
            return @"Leite aus der dokumentierten Anamnese und dem Befund die wahrscheinlichsten Diagnosen ab.
Eine Diagnose pro Zeile. Fachterminologie verwenden.
Sortierung: Hauptdiagnose zuerst, dann Nebendiagnosen nach klinischer Relevanz.
Sicherheitsgrad: Gesichert (kein Präfix), Verdacht (""V.a.""), Ausschluss (""Ausschluss""), Zustand nach (""Z.n."").";
        }

        private static string GetReferenceIcd10()
        {
            return @"Gib zu jeder Diagnose den passenden ICD-10-GM-Code an. Eine Zeile pro Diagnose. Format: [ICD-10-Code] [Diagnosentext]
Höchste sinnvolle Spezifität (4- oder 5-stellig). Seitenkennzeichnung: R/L/B.
Diagnosesicherheit: G (gesichert), V (Verdacht), A (Ausschluss), Z (Zustand nach).
Fachspezifische ICD-10-Kapitel priorisieren.";
        }
    }
}
