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
                sb.AppendLine("--- REFERENZ: Allgemeinmedizin-Anamnese ---");
                sb.AppendLine(GetReferenceAnamneseAM());
                sb.AppendLine();
                sb.AppendLine("--- REFERENZ: Orthopädie-Anamnese (Auszug, zeigt fachspezifische Anpassung) ---");
                sb.AppendLine(GetReferenceAnamneseOR());
                sb.AppendLine();
            }

            sb.AppendLine("--- REFERENZ: Allgemeinmedizin-Befund ---");
            sb.AppendLine(GetReferenceBefundAM());
            sb.AppendLine();

            sb.AppendLine("--- REFERENZ: Diagnosen ---");
            sb.AppendLine(GetReferenceDiagnosen());
            sb.AppendLine();

            sb.AppendLine("--- REFERENZ: ICD-10 ---");
            sb.AppendLine(GetReferenceIcd10());
            sb.AppendLine();

            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("DEINE AUFGABE");
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Erstelle jetzt für das Fachgebiet \"{fachgebiet}\" die folgenden Prompt-Module.");
            sb.AppendLine("Orientiere dich exakt am Stil, der Struktur und der Detailtiefe der Referenz-Beispiele.");
            sb.AppendLine("Passe die Inhalte fachspezifisch an:");
            sb.AppendLine("- Verwende die typischen Untersuchungen, Symptome und Terminologie des Fachgebiets");
            sb.AppendLine("- Definiere fachspezifische Fallback-Sätze");
            sb.AppendLine("- Priorisiere die für das Fachgebiet relevanten ICD-10-Kapitel");
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

Erstelle aus dem Transkript die Anamnese im Nominalstil oder in kurzen, objektiven Sätzen. Vergiss kein medizinisches Detail, lasse aber Smalltalk und irrelevante Gesprächsanteile rigoros weg.

Formatierung: Schreibe jede Unterkategorie als eigene Überschriftszeile, darunter den Inhalt als Fließtext oder kommagetrennte Aufzählung. Trenne Unterkategorien durch eine Leerzeile.

Jetziges Leiden:
Beginne mit dem Vorstellungsgrund. Fasse die aktuelle Symptomatik zusammen: Lokalisation, Charakter, Dauer, Auslöser, zeitlicher Verlauf, Begleitsymptome.

Vorerkrankungen / Spezielle Anamnese:
Alle genannten chronischen Erkrankungen, relevante Vordiagnosen, Operationen, Krankenhausaufenthalte.
Fallback: ""k. A."" wenn nicht erwähnt, ""keine"" wenn Patient explizit keine Nebendiagnosen oder Vorerkrankungen hat.

Dauermedikation:
Jedes Medikament in eine neue Zeile. Format: Name Dosierung Einnahmeschema.
Fallback: ""Aktuell keine regelmäßige Medikamenteneinnahme bekannt.""

Allergien / Unverträglichkeiten:
Auslöser und Reaktionstyp sofern genannt.
Fallback: ""Keine Allergien oder Unverträglichkeiten bekannt.""

Vegetative Anamnese:
B-Symptomatik, Appetit, Schlaf, Miktion, Stuhlgang.
Fallback: ""Vegetative Anamnese im Gespräch nicht erhoben.""

Noxen / Sozialanamnese:
Nikotinkonsum, Alkoholkonsum, Beruf, häusliche Situation.
Fallback: ""Noxenanamnese nicht erhoben. Sozialanamnese unauffällig.""";
        }

        private static string GetReferenceAnamneseOR()
        {
            return @"FACH-MODUL ANAMNESE: ORTHOPÄDIE
[Gleiche Struktur wie Allgemeinmedizin, aber fachspezifisch angepasst:]
- Jetziges Leiden betont: Schmerzlokalisation, Seitenangabe, Ausstrahlung, Schmerzcharakter, Auslöser (Trauma/Belastung/spontan)
- Vorerkrankungen betont: orthopädische Voroperationen, Frakturen, degenerative Veränderungen
- Noxen/Sozialanamnese betont: körperliche Belastung, sportliche Aktivität, Hilfsmittelbedarf";
        }

        private static string GetReferenceBefundAM()
        {
            return @"FACH-MODUL BEFUND: ALLGEMEINMEDIZIN

Erstelle aus dem Transkript den klinischen Untersuchungsbefund. Dokumentiere ausschließlich im Gespräch genannte oder durchgeführte Untersuchungen. Erfinde keine Befunde hinzu.

Formatierung: Für jedes untersuchte Organsystem einen Block. Überschrift: ""Befund [Organsystem]:"". Darunter die Einzelbefunde, durch Komma getrennt, Block mit Punkt abschließen.

Vitalparameter:
Wenn genannt: RR, Puls, Temperatur, SpO2, Gewicht, Größe.

Relevante Organsysteme (nur dokumentieren wenn untersucht):
Allgemeinzustand, Haut, Kopf/Hals, Herz, Lunge, Abdomen, Extremitäten, Neurologie orientierend.

Fallback: ""Keine Untersuchungsergebnisse dokumentiert.""";
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
