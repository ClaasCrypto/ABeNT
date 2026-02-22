using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABeNT.Model;
using Newtonsoft.Json;

namespace ABeNT.Services
{
    /// <summary>
    /// Formulare: je eine Datei in Forms\, Reihenfolge/Liste in forms-manifest.json.
    /// Universal-Prompt in app-config.json. Standardformulare können wiederhergestellt werden.
    /// </summary>
    public class OutputFormsService
    {
        private static readonly string[] StandardFormIds = { "allgemeinmedizin", "orthopaedie" };

        private static string GetAppFolder()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "ABeNT");
            Directory.CreateDirectory(appFolder);
            return appFolder;
        }

        private static string GetFormsFolder()
        {
            string folder = Path.Combine(GetAppFolder(), "Forms");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string GetManifestPath()
        {
            return Path.Combine(GetAppFolder(), "forms-manifest.json");
        }

        private static string GetAppConfigPath()
        {
            return Path.Combine(GetAppFolder(), "app-config.json");
        }

        /// <summary>Id für Dateinamen bereinigen (nur a-z, 0-9, _, -).</summary>
        private static string SanitizeIdForFile(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "form";
            id = id.Trim().ToLowerInvariant();
            id = Regex.Replace(id, @"[^a-z0-9_\-]", "_");
            id = Regex.Replace(id, @"_+", "_").Trim('_');
            return string.IsNullOrEmpty(id) ? "form" : id;
        }

        private static string GetFormFilePath(string id)
        {
            return Path.Combine(GetFormsFolder(), SanitizeIdForFile(id) + ".json");
        }

        /// <summary>Standardformular-Ids (können per "Standard wiederherstellen" zurückgesetzt werden).</summary>
        public static bool IsStandardForm(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && StandardFormIds.Contains(id.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Manifest laden; fehlt es, ggf. von alter output-forms.json migrieren, sonst Default-Manifest anlegen.</summary>
        private static List<string> LoadManifest()
        {
            string path = GetManifestPath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var list = JsonConvert.DeserializeObject<List<string>>(json);
                    if (list != null && list.Count > 0)
                        return list;
                }
                catch { /* fallback */ }
            }

            // Migration: alte output-forms.json auslesen
            string oldPath = Path.Combine(GetAppFolder(), "output-forms.json");
            if (File.Exists(oldPath))
            {
                try
                {
                    string json = File.ReadAllText(oldPath);
                    var config = JsonConvert.DeserializeObject<OutputFormsConfig>(json);
                    if (config?.Forms != null && config.Forms.Count > 0)
                    {
                        var ids = new List<string>();
                        foreach (var form in config.Forms)
                        {
                            if (string.IsNullOrWhiteSpace(form?.Id)) continue;
                            string id = form.Id.Trim();
                            if (ids.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                            ids.Add(id);
                            SaveFormToFile(form);
                        }
                        if (!string.IsNullOrWhiteSpace(config.UniversalPrompt))
                        {
                            var appConfig = new AppConfig { UniversalPrompt = config.UniversalPrompt };
                            File.WriteAllText(GetAppConfigPath(), JsonConvert.SerializeObject(appConfig, Formatting.Indented));
                        }
                        SaveManifest(ids);
                        return ids;
                    }
                }
                catch { /* ignore */ }
            }

            // Neuer Start: Standardformulare anlegen
            var defaultIds = new List<string> { "allgemeinmedizin", "orthopaedie" };
            foreach (string id in defaultIds)
            {
                var form = GetDefaultFormById(id);
                if (form != null)
                    SaveFormToFile(form);
            }
            SaveManifest(defaultIds);
            return defaultIds;
        }

        private static void SaveManifest(List<string> ids)
        {
            if (ids == null) return;
            string path = GetManifestPath();
            string json = JsonConvert.SerializeObject(ids, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        private static void SaveFormToFile(SubjectForm form)
        {
            if (form == null || string.IsNullOrWhiteSpace(form.Id)) return;
            string path = GetFormFilePath(form.Id);
            string json = JsonConvert.SerializeObject(form, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        private static SubjectForm? LoadFormFromFile(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            string path = GetFormFilePath(id);
            if (!File.Exists(path))
            {
                var standard = GetDefaultFormById(id.Trim());
                if (standard != null)
                {
                    SaveFormToFile(standard);
                    return standard;
                }
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                var form = JsonConvert.DeserializeObject<SubjectForm>(json);
                if (form == null) return null;
                if (string.IsNullOrWhiteSpace(form.Id))
                    form.Id = id.Trim();
                // Migration: Alte Anamnese-Vorlage in Standardformularen durch neue ersetzen
                if (IsStandardForm(id) && IsOldAnamnesePrompt(form.SectionPrompts?.A))
                {
                    if (form.SectionPrompts == null) form.SectionPrompts = new AbentSectionPrompts();
                    form.SectionPrompts.A = GetDefaultAnamnesePrompt();
                    SaveFormToFile(form);
                }
                return form;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Erkennt die alte Anamnese-Vorlage (z. B. "Nutze PATIENTENSPRACHE" ohne neues Schema).</summary>
        private static bool IsOldAnamnesePrompt(string? a)
        {
            if (string.IsNullOrWhiteSpace(a)) return false;
            if (a.Contains("PATIENTENSPRACHE", StringComparison.OrdinalIgnoreCase) && !a.Contains("Vorerkrankungen / Spezielle Anamnese", StringComparison.Ordinal))
                return false; // bereits neue Vorlage
            return a.Contains("Nutze PATIENTENSPRACHE", StringComparison.OrdinalIgnoreCase)
                || a.Contains("Laienbegriffe", StringComparison.OrdinalIgnoreCase);
        }

        private static SubjectForm? GetDefaultFormById(string id)
        {
            if (string.Equals(id, "allgemeinmedizin", StringComparison.OrdinalIgnoreCase))
                return CreateDefaultAllgemeinmedizinForm();
            if (string.Equals(id, "orthopaedie", StringComparison.OrdinalIgnoreCase))
                return CreateDefaultOrthopaedieForm();
            return null;
        }

        /// <summary>
        /// Alle Formulare in der Reihenfolge des Manifests laden.
        /// </summary>
        public static List<SubjectForm> GetForms()
        {
            var ids = LoadManifest();
            var list = new List<SubjectForm>();
            foreach (string id in ids)
            {
                var form = LoadFormFromFile(id);
                if (form != null)
                    list.Add(form);
            }
            return list;
        }

        /// <summary>
        /// Ein Formular nach Id laden.
        /// </summary>
        public static SubjectForm? GetForm(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return LoadFormFromFile(id.Trim());
        }

        /// <summary>
        /// Neues Formular anlegen: Datei schreiben und Id ins Manifest (am Ende).
        /// </summary>
        public static void AddForm(SubjectForm form)
        {
            if (form == null || string.IsNullOrWhiteSpace(form.Id))
                throw new ArgumentException("Form and form.Id are required.");
            string id = form.Id.Trim();
            var ids = LoadManifest();
            if (ids.Any(f => string.Equals(f, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A form with id '{id}' already exists.");
            SaveFormToFile(form);
            ids.Add(id);
            SaveManifest(ids);
        }

        /// <summary>
        /// Formular aktualisieren (nur Datei überschreiben).
        /// </summary>
        public static void UpdateForm(SubjectForm form)
        {
            if (form == null || string.IsNullOrWhiteSpace(form.Id))
                throw new ArgumentException("Form and form.Id are required.");
            string id = form.Id.Trim();
            var ids = LoadManifest();
            if (!ids.Any(f => string.Equals(f, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Form with id '{id}' not found.");
            SaveFormToFile(form);
        }

        /// <summary>
        /// Formular löschen: Datei entfernen und aus Manifest streichen.
        /// </summary>
        public static void RemoveForm(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            string path = GetFormFilePath(id);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { /* ignore */ }
            }
            var ids = LoadManifest();
            ids.RemoveAll(f => string.Equals(f, id, StringComparison.OrdinalIgnoreCase));
            SaveManifest(ids);
        }

        /// <summary>
        /// Standardformular auf Vorlage aus dem Code zurücksetzen (nur für Standard-Ids).
        /// </summary>
        public static void RestoreDefaultForm(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            var form = GetDefaultFormById(id);
            if (form == null)
                throw new InvalidOperationException($"Form '{id}' ist kein Standardformular und kann nicht wiederhergestellt werden.");
            SaveFormToFile(form);
        }

        public static string GetUniversalPrompt()
        {
            string path = GetAppConfigPath();
            if (!File.Exists(path))
                return GetDefaultUniversalPrompt();
            try
            {
                string json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                if (!string.IsNullOrWhiteSpace(config?.UniversalPrompt))
                    return config.UniversalPrompt;
            }
            catch { /* ignore */ }
            return GetDefaultUniversalPrompt();
        }

        public static void SetUniversalPrompt(string universalPrompt)
        {
            string path = GetAppConfigPath();
            AppConfig config;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                else
                    config = new AppConfig();
            }
            catch
            {
                config = new AppConfig();
            }
            config.UniversalPrompt = universalPrompt ?? string.Empty;
            File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        public static string BuildSystemPromptFromConfig(string? formId, string gender, bool includeBefund, bool includeTherapie, bool includeIcd10)
        {
            string universal = GetUniversalPrompt();
            var form = !string.IsNullOrWhiteSpace(formId) ? GetForm(formId) : null;
            form ??= GetForms().FirstOrDefault();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(universal);

            sb.AppendLine("\nGeschlecht:");
            if (gender == "Männlich")
                sb.AppendLine("Nutze 'Der Patient', 'er', 'sein'.");
            else if (gender == "Weiblich")
                sb.AppendLine("Nutze 'Die Patientin', 'sie', 'ihr'.");
            else
                sb.AppendLine("Nutze 'Patient', neutrale Formulierungen ohne Pronomen.");

            bool includeN = includeBefund || includeTherapie || includeIcd10;
            sb.AppendLine("\nOutput-Struktur:");
            sb.AppendLine("Erstelle **A** (Anamnese) immer.");
            if (includeBefund) sb.AppendLine("Erstelle **Be** (Befund).");
            if (includeN) sb.AppendLine("Erstelle **N** (Diagnose/Name)" + (includeIcd10 ? " mit ICD-10-Codes in Klammern." : "."));
            if (includeTherapie) sb.AppendLine("Erstelle **T** (Therapie).");

            sb.AppendLine("\nSTRUKTURVORGABEN:");

            string promptA = form?.SectionPrompts?.A?.Trim() ?? "";
            if (string.IsNullOrEmpty(promptA)) promptA = GetDefaultSectionA();
            sb.AppendLine("\n**A**\n[ANWEISUNG:\n" + promptA + "\n]");

            if (includeBefund)
            {
                string promptBe = form?.SectionPrompts?.Be?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptBe)) promptBe = GetDefaultSectionBe();
                sb.AppendLine("\n**Be**\n[ANWEISUNG:\n" + promptBe + "\n]");
            }
            if (includeN)
            {
                string promptN = form?.SectionPrompts?.N?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptN)) promptN = GetDefaultSectionN();
                if (includeIcd10)
                    promptN += "\nBei jeder Diagnose den passenden ICD-10-Code in Klammern angeben, z.B. Lumbago (M54.5).";
                sb.AppendLine("\n**N**\n[ANWEISUNG:\n" + promptN + "\n]");
            }
            if (includeTherapie)
            {
                string promptT = form?.SectionPrompts?.T?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptT)) promptT = GetDefaultSectionT();
                sb.AppendLine("\n**T**\n[ANWEISUNG:\n" + promptT + "\n]");
            }

            sb.AppendLine("\nSchreibe keinen Text vor dem ersten Marker.");
            return sb.ToString();
        }

        private static string GetDefaultSectionA() => GetDefaultAnamnesePrompt();

        private static string GetDefaultAnamnesePrompt()
        {
            return @"Du bist ein hochqualifizierter, erfahrener Facharzt und verfasst die Anamnese für einen offiziellen, medizinischen Arztbrief.

Deine Aufgabe ist es, aus dem folgenden Transkript eines Arzt-Patienten-Gesprächs ausschließlich die anamnestischen Informationen herauszufiltern und diese in einem professionellen, diktierten Arztbrief-Stil zu formatieren.

Du vergisst kein einziges medizinisches Detail (wie Schmerzcharakter, Ausstrahlung, Vorbehandlungen, Dauer), lässt aber jeglichen Smalltalk, Empathie-Bekundungen und irrelevante Nebensätze rigoros weg. Du erfindest keine Informationen hinzu.

Nutze strikte ärztliche Fachsprache. Beginne direkt mit dem Inhalt, ohne zusätzliche Überschrift vor dem Fließtext.

Erstelle die Ausgabe EXAKT nach folgendem Formatierungs-Schema. Halte die folgenden Überschriften exakt so ein und setze unter jede Überschrift den Text:

[Beginne mit dem prägnanten, verdichteten Fließtext zum jetzigen Leiden im Nominalstil oder in kurzen, objektiven Sätzen.
Beginne typischerweise mit dem Vorstellungsgrund (z.B. ""Vorstellung des Patienten aufgrund von..."").
Fasse die aktuelle Symptomatik, Schmerzlokalisation, Ausstrahlung, Schmerzcharakter, Auslöser (z.B. Trauma, Belastung) und die bisherige Dauer der Symptome zusammen.
Erwähne hier auch spezifische Vorbehandlungen, die exakt dieses Akutereignis betreffen (z.B. ""Z.n. frustraner Infiltrationstherapie alio loco"").
Danach folgen die nachstehenden Überschriften mit Inhalt.]

Vorerkrankungen / Spezielle Anamnese:
[Liste hier alle weiteren genannten Diagnosen, Operationen oder chronischen Begleiterkrankungen auf, getrennt durch Komma.
WICHTIG: Wurde nichts erwähnt, schreibe exakt: ""Keine relevanten Vorerkrankungen eruierbar.""]

Dauermedikation:
[Liste die Medikamente auf. Jedes Medikament in eine neue Zeile. Format: Name + Dosierung (z.B. 'Ibuprofen 600 mg 1-0-1').
WICHTIG: Wenn der Patient keine Medikamente nimmt oder nichts erwähnt wurde, schreibe exakt: ""Aktuell keine regelmäßige Medikamenteneinnahme bekannt.""]

Allergien / Unverträglichkeiten:
[Nenne bekannte Allergien (z.B. Penicillin).
WICHTIG: Wenn nichts erwähnt, schreibe exakt: ""Keine Allergien oder Unverträglichkeiten bekannt.""]

Vegetative Anamnese:
[Fasse hier B-Symptomatik, Appetit, Gewichtsverlust, Miktion, Defäkation oder Schlafstörungen zusammen, falls erwähnt.
WICHTIG: Wurde im Gespräch nichts davon thematisiert, lasse diesen Punkt unerwähnt und schreibe auch die Überschrift ""Vegetative Anamnese:"" nicht hin.]

Noxen / Sozialanamnese:
[Nenne Nikotinkonsum, Alkohol, Beruf, Sport.
WICHTIG: Wenn nichts erwähnt, schreibe exakt: ""Noxen nicht erfragt, Sozialanamnese unauffällig.""]";
        }
        private static string GetDefaultSectionBe() =>
            "Nutze strikte ÄRZTLICHE FACHSPRACHE.\nStruktur: Für jedes Organ einen Block: 'Befund [Organ] [Seite (re./li./bds.)]:' Darunter die Befunde. Formatierung: Trenne Angaben mit KOMMA (,). Ende mit PUNKT. LEERZEILE zwischen Organ-Blöcken. Vitalparameter nur wenn genannt. Wenn gar keine Untersuchung: 'Keine Untersuchungsergebnisse dokumentiert'.";
        private static string GetDefaultSectionN() =>
            "Liste der Diagnosen (Fachsprache). Seitenangabe PFLICHT (re./li./bds.). Kennzeichne Unsicherheiten (V.a., D.D.). Jede Diagnose in eine neue Zeile.";
        private static string GetDefaultSectionT() =>
            "1. Zuerst: Geplante Behandlungen, Verordnungen (Heilmittel/Hilfsmittel), AU und Procedere (mit KOMMA getrennt).\n2. Danach LEERZEILE und exakt: 'Medikation:'\n3. Darunter: Jedes Medikament in eine NEUE ZEILE. Format: Wirkstoff/Name + Stärke + Schema (z.B. 1-0-1, tgl.).";

        private static string GetDefaultUniversalPrompt()
        {
            return @"Du bist ein präziser medizinischer Dokumentations-Assistent.

GLOBALE REGELN:
1. Nutze für den INHALT reinen Text (kein Markdown wie **Fett**).
2. Nutze KEINE automatischen Nummerierungen (1., 2.).
3. Trenne Haupt-Abschnitte NUR mit den Markern **A**, **Be**, **N**, **T**, **ICD-10**.
4. ABKÜRZUNGEN: Kürze Seitenangaben IMMER ab: 're.' (rechts), 'li.' (links), 'bds.' (beidseits). Nutze gängige medizinische Kürzel (z.B. 'o.B.', 'Z.n.', 'V.a.').

Sprecher-Erkennung:
Analysiere das Gespräch. Der Sprecher, der Fragen stellt und Anweisungen gibt, ist der ARZT. Der andere ist der PATIENT. Tausche die Rollen logisch, falls die Labels vertauscht scheinen.

Schreibe keinen Text vor dem ersten Marker.";
        }

        private static SubjectForm CreateDefaultAllgemeinmedizinForm()
        {
            return new SubjectForm
            {
                Id = "allgemeinmedizin",
                DisplayName = "Allgemeinmedizin",
                Description = "Standard-ABeNT für allgemeinmedizinische/internistische Befunde.",
                SectionPrompts = new AbentSectionPrompts
                {
                    A = GetDefaultAnamnesePrompt(),
                    Be = @"Nutze strikte ÄRZTLICHE FACHSPRACHE.

Struktur:
Für jedes Organ/System einen Block:
'Befund [Organ/System] [Seite (re./li./bds.) wenn zutreffend]:'
Darunter die Befunde (Inspektion, Palpation, Perkussion, Auskultation, orientierende Funktion).
Formatierung: Trenne einzelne Angaben innerhalb eines Bereichs mit KOMMA (,). Ende den Block mit einem PUNKT (.).
Mache eine LEERZEILE zwischen verschiedenen Organ-Blöcken.

Vitalparameter: Wenn genannt anführen (RR, Puls, Temperatur, SpO2, Gewicht). Format z.B. 'RR 120/80 mmHg', 'Puls 72/min'.
Allgemeinmedizin/Internistik: Allgemeinzustand, Haut/Schleimhaut, Herz (Auskultation), Lunge, Abdomen, Lymphknoten, Ödeme, orientierend Neurologie – nur dokumentieren, wenn im Gespräch erwähnt.
WICHTIG: Wenn gar keine Untersuchung: 'Keine Untersuchungsergebnisse dokumentiert'.",
                    N = @"Liste der Diagnosen (Fachsprache).
- Seitenangabe PFLICHT wo zutreffend (re./li./bds.).
- Kennzeichne Unsicherheiten (V.a., D.D.).
- Jede Diagnose in eine neue Zeile.
Allgemeinmedizin/Internistik: ICD-10-Orientierung für innere, kardiovaskuläre, respiratorische, gastroenterologische, endokrine und allgemeine Diagnosen wenn sinnvoll.",
                    T = @"Halte dich strikt an diese Reihenfolge:

1. Zuerst: Geplante Behandlungen, Verordnungen (Heilmittel, Labor, Bildgebung, Überweisung), AU und Procedere.
   Formatierung: Liste diese Punkte hintereinander auf, getrennt durch KOMMA (,).

2. Danach: Füge eine LEERZEILE ein und schreibe exakt: 'Medikation:'

3. Darunter:
   - Jedes Medikament MUSS in eine NEUE ZEILE!
   - Format: Wirkstoff/Name + Stärke + Schema (z.B. '1-0-1', 'tgl.', 'wchtl.', bei Tropfen 'ggt').
   - Beispiel:
     Medikation:
     Ibuprofen 400mg 1-0-1
     Metformin 1000mg 1-0-1",
                    Icd10 = string.Empty
                }
            };
        }

        private static SubjectForm CreateDefaultOrthopaedieForm()
        {
            return new SubjectForm
            {
                Id = "orthopaedie",
                DisplayName = "Orthopädie",
                Description = "Standard-ABeNT für orthopädische Befunde.",
                SectionPrompts = new AbentSectionPrompts
                {
                    A = GetDefaultAnamnesePrompt(),
                    Be = @"Nutze strikte ÄRZTLICHE FACHSPRACHE.

Struktur:
Für jedes Organ/Bereich einen Block:
'Befund [Organ/Bereich] [Seite (re./li./bds.)]:'
Darunter die Befunde (Inspektion, Palpation, Funktion, Tests, Bewegungsausmaß).
Formatierung: Trenne einzelne Angaben innerhalb eines Bereichs mit KOMMA (,). Ende den Block mit einem PUNKT (.).
Mache eine LEERZEILE zwischen verschiedenen Organ-Blöcken.

Vitalparameter: Nur wenn Werte genannt (Format: 'RR 120/80 mmHg').
Orthopädie: Ganganalyse, Haltung, Druckschmerz, Bewegungseinschränkung (ROM), Kraft, Stabilitätstests wenn erwähnt.
WICHTIG: Wenn gar keine Untersuchung: 'Keine Untersuchungsergebnisse dokumentiert'.",
                    N = @"Liste der Diagnosen (Fachsprache).
- Seitenangabe PFLICHT (re./li./bds.).
- Kennzeichne Unsicherheiten (V.a., D.D.).
- Jede Diagnose in eine neue Zeile.
Orthopädie: ICD-10-Orientierung für muskuloskelettale Diagnosen wenn sinnvoll.",
                    T = @"Halte dich strikt an diese Reihenfolge:

1. Zuerst: Geplante Behandlungen, Verordnungen (Heilmittel/Hilfsmittel, Physiotherapie, Orthesen), AU und Procedere.
   Formatierung: Liste diese Punkte hintereinander auf, getrennt durch KOMMA (,).

2. Danach: Füge eine LEERZEILE ein und schreibe exakt: 'Medikation:'

3. Darunter:
   - Jedes Medikament MUSS in eine NEUE ZEILE!
   - Format: Wirkstoff/Name + Stärke + Schema (z.B. '1-0-1', 'tgl.', 'wchtl.', bei Tropfen 'ggt').
   - Beispiel:
     Medikation:
     Ibuprofen 400mg 1-0-1
     Paracetamol 500mg tgl.",
                    Icd10 = "Liste die 3 wahrscheinlichsten Codes (Code - Text). Orthopädie: bevorzugt Kapitel M (Muskuloskelett)."
                }
            };
        }

        /// <summary>Für app-config.json (nur UniversalPrompt).</summary>
        private class AppConfig
        {
            public string UniversalPrompt { get; set; } = string.Empty;
        }
    }
}
