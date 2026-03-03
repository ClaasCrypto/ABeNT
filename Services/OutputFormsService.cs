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
        private static readonly string[] StandardFormIds = { "allgemeinmedizin", "orthopaedie", "neurologie", "echokardiographie" };

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
                    {
                        bool changed = false;
                        foreach (string stdId in StandardFormIds)
                        {
                            if (!list.Any(f => string.Equals(f, stdId, StringComparison.OrdinalIgnoreCase)))
                            {
                                var form = GetDefaultFormById(stdId);
                                if (form != null)
                                {
                                    SaveFormToFile(form);
                                    list.Add(stdId);
                                    changed = true;
                                }
                            }
                        }
                        if (changed) SaveManifest(list);
                        return list;
                    }
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
            var defaultIds = new List<string> { "allgemeinmedizin", "orthopaedie", "neurologie", "echokardiographie" };
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
                if (IsStandardForm(id) && NeedsPromptRefresh(form.SectionPrompts))
                {
                    var updated = GetDefaultFormById(id);
                    if (updated != null)
                    {
                        form.SectionPrompts = updated.SectionPrompts;
                        SaveFormToFile(form);
                    }
                }
                return form;
            }
            catch
            {
                return null;
            }
        }

        private static bool NeedsPromptRefresh(AbentSectionPrompts? p)
        {
            if (p == null) return false;
            if (!string.IsNullOrWhiteSpace(p.A))
            {
                if (!p.A.Contains("FACH-MODUL ANAMNESE:", StringComparison.Ordinal))
                    return true;
                if (!p.A.Contains("Erstelle aus dem Transkript", StringComparison.Ordinal))
                    return true;
                if (p.A.Contains("Jetziges Leiden:", StringComparison.Ordinal))
                    return true;
                if (p.A.Contains("Noxen / Sozialanamnese", StringComparison.Ordinal))
                    return true;
            }
            if (!string.IsNullOrWhiteSpace(p.Be))
            {
                if (!p.Be.Contains("FACH-MODUL BEFUND:", StringComparison.Ordinal))
                    return true;
                if (!p.Be.Contains("Erstelle aus dem Transkript", StringComparison.Ordinal)
                    && !p.Be.Contains("Dokumentiere die echokardiographischen", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static SubjectForm? GetDefaultFormById(string id)
        {
            if (string.Equals(id, "allgemeinmedizin", StringComparison.OrdinalIgnoreCase))
                return CreateDefaultAllgemeinmedizinForm();
            if (string.Equals(id, "orthopaedie", StringComparison.OrdinalIgnoreCase))
                return CreateDefaultOrthopaedieForm();
            if (string.Equals(id, "neurologie", StringComparison.OrdinalIgnoreCase))
                return CreateDefaultNeurologieForm();
            if (string.Equals(id, "echokardiographie", StringComparison.OrdinalIgnoreCase))
                return CreateDefaultEchokardiographieForm();
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

        public static string BuildSystemPromptFromConfig(string? formId, string gender, bool includeBefund, bool includeTherapie, bool includeIcd10, string recordingMode = "Neupatient")
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

            sb.AppendLine(GetModePrompt(recordingMode));

            bool includeA = !string.IsNullOrWhiteSpace(form?.SectionPrompts?.A);
            if (!includeA && form == null) includeA = true;
            bool includeN = true;
            sb.AppendLine("\nOutput-Struktur:");
            if (includeA) sb.AppendLine("Erstelle **A** (Anamnese).");
            if (includeBefund) sb.AppendLine("Erstelle **Be** (Befund).");
            if (includeN) sb.AppendLine("Erstelle **N** (Diagnosen).");
            if (includeIcd10) sb.AppendLine("Erstelle **ICD-10** (ICD-10-Codierung).");
            if (includeTherapie) sb.AppendLine("Erstelle **T** (Therapie).");

            sb.AppendLine("\nSTRUKTURVORGABEN:");

            if (includeA)
            {
                string promptA = form?.SectionPrompts?.A?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptA)) promptA = GetDefaultSectionA();
                sb.AppendLine("\n**A**\n[ANWEISUNG:\n" + promptA + "\n]");
            }

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
                sb.AppendLine("\n**N**\n[ANWEISUNG:\n" + promptN + "\n]");
            }
            if (includeIcd10)
            {
                string promptIcd = form?.SectionPrompts?.Icd10?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptIcd)) promptIcd = GetDefaultIcd10Prompt(form?.DisplayName ?? "Allgemeinmedizin");
                sb.AppendLine("\n**ICD-10**\n[ANWEISUNG:\n" + promptIcd + "\n]");
            }
            if (includeTherapie)
            {
                string promptT = form?.SectionPrompts?.T?.Trim() ?? "";
                if (!string.IsNullOrEmpty(promptT))
                    sb.AppendLine("\n**T**\n[ANWEISUNG:\n" + promptT + "\n]");
            }
            return sb.ToString();
        }

        private static string GetDefaultSectionA() => GetDefaultAnamnesePromptAM();

        private static string GetDefaultAnamnesePromptAM()
        {
            return @"FACH-MODUL ANAMNESE: ALLGEMEINMEDIZIN

Erstelle aus dem Transkript die Anamnese im Nominalstil oder in kurzen, objektiven Sätzen. Vergiss kein medizinisches Detail, lasse aber Smalltalk und irrelevante Gesprächsanteile rigoros weg.

Formatierung: Schreibe jede Unterkategorie als eigene Überschriftszeile, darunter den Inhalt als Fließtext oder kommagetrennte Aufzählung. Beginne nach jeder Unterkategorie eine neue Zeile, aber keine Leerzeile dazwischen.

Aktuell:
Beginne mit dem Vorstellungsgrund. Fasse die aktuelle Symptomatik zusammen: Lokalisation, Charakter, Dauer, Auslöser, zeitlicher Verlauf, Begleitsymptome. Erwähne bisherige Selbstmedikation oder Akutbehandlungen, die dieses Ereignis betreffen.
Nebendiagnosen / Vorerkrankungen:
Nur chronische Erkrankungen, Vordiagnosen, Operationen, Krankenhausaufenthalte – kommagetrennt. Kein Verlauf und keine aktuelle Symptomatik (diese gehören unter ""Aktuell"").
Fachspezifische Ergänzung: Impfstatus, Vorsorgeuntersuchungen, familiäre Belastung (kardiovaskulär, Diabetes, Tumorerkrankungen) sofern erwähnt.
Fallback: ""k. A."" wenn nicht erwähnt, ""keine"" wenn Patient explizit keine Nebendiagnosen oder Vorerkrankungen hat.
Dauermedikation:
Jedes Medikament in eine neue Zeile. Format: Name Dosierung Einnahmeschema (z.B. Metformin 1000 mg 1-0-1).
Fallback: ""k. A."" wenn nicht erwähnt, ""keine"" wenn Patient explizit keine Medikamente nimmt.
Allergien / Unverträglichkeiten:
Auslöser und Reaktionstyp sofern genannt (z.B. Penicillin - Exanthem).
Fallback: ""k. A."" wenn nicht erwähnt, ""keine bekannt"" wenn Patient explizit verneint.
Vegetative Anamnese:
Nur B-Symptomatik (Fieber, Nachtschweiß, ungewollter Gewichtsverlust), Appetit, Schlaf, Miktion, Stuhlgang. RR und Gewicht gehören nicht hierher (Vitalparameter/Befund).
Fallback: Wenn im Gespräch nicht thematisiert, diese Kategorie komplett weglassen (auch die Überschrift nicht nennen).
Sozialanamnese:
Beruf (insbesondere körperliche Belastung, Überkopfarbeit, sitzende Tätigkeit), sportliche Aktivität, häusliche Situation, Pflegebedarf, Mobilität. Nikotinkonsum, Alkohol.
Fallback: ""Sozialanamnese unauffällig."" wenn besprochen aber unauffällig. ""k. A."" wenn nicht erwähnt.";
        }

        private static string GetDefaultAnamnesePromptOR()
        {
            return @"FACH-MODUL ANAMNESE: ORTHOPÄDIE

Erstelle aus dem Transkript die Anamnese im Nominalstil oder in kurzen, objektiven Sätzen. Vergiss kein medizinisches Detail (Schmerzcharakter, Ausstrahlung, Vorbehandlungen, Dauer), lasse aber Smalltalk und irrelevante Gesprächsanteile rigoros weg.

Formatierung: Schreibe jede Unterkategorie als eigene Überschriftszeile, darunter den Inhalt als Fließtext oder kommagetrennte Aufzählung. Beginne nach jeder Unterkategorie eine neue Zeile, aber keine Leerzeile dazwischen.

Aktuell:
Beginne mit dem Vorstellungsgrund (z.B. ""Vorstellung aufgrund persistierender Gonalgie re.""). Fasse zusammen: Schmerzlokalisation, Seitenangabe, Ausstrahlung, Schmerzcharakter (stechend, ziehend, dumpf, brennend), Auslöser (Trauma, Belastung, spontan), Dauer, tageszeitliche Dynamik, belastungsabhängige Komponente, Einschränkungen im Alltag. Erwähne akutbezogene Vorbehandlungen (z.B. Z.n. frustraner Infiltrationstherapie alio loco, bisherige Analgesie, Physiotherapie).
Nebendiagnosen / Vorerkrankungen:
Nur orthopädische Voroperationen, Frakturen, degenerative Veränderungen, rheumatologische Grunderkrankungen, sonstige relevante Begleitdiagnosen – kommagetrennt. Kein Verlauf und keine aktuelle Symptomatik (diese gehören unter ""Aktuell"").
Fallback: ""k. A."" wenn nicht erwähnt, ""keine"" wenn Patient explizit keine Nebendiagnosen oder Vorerkrankungen hat.
Dauermedikation:
Jedes Medikament in eine neue Zeile. Format: Name Dosierung Einnahmeschema (z.B. Ibuprofen 600 mg 1-0-1).
Fallback: ""k. A."" wenn nicht erwähnt, ""keine"" wenn Patient explizit keine Medikamente nimmt.
Allergien / Unverträglichkeiten:
Auslöser und Reaktionstyp sofern genannt.
Fallback: ""k. A."" wenn nicht erwähnt, ""keine bekannt"" wenn Patient explizit verneint.
Vegetative Anamnese:
Nur Schlafstörungen (z.B. durch Schmerzen), B-Symptomatik sofern erwähnt. RR und Gewicht gehören nicht hierher (Vitalparameter/Befund).
Fallback: Wenn im Gespräch nicht thematisiert, diese Kategorie komplett weglassen (auch die Überschrift nicht nennen).
Sozialanamnese:
Beruf (insbesondere körperliche Belastung, Überkopfarbeit, sitzende Tätigkeit), sportliche Aktivität, häusliche Situation, Pflegebedarf, Mobilität. Nikotinkonsum, Alkohol.
Fallback: ""Sozialanamnese unauffällig."" wenn besprochen aber unauffällig. ""k. A."" wenn nicht erwähnt.";
        }

        private static string GetDefaultAnamnesePromptNE()
        {
            return @"FACH-MODUL ANAMNESE: NEUROLOGIE

Erstelle aus dem Transkript die Anamnese im Nominalstil oder in kurzen, objektiven Sätzen. Vergiss kein medizinisches Detail (Symptomcharakter, zeitlicher Verlauf, Auslöser, Begleitsymptome), lasse aber Smalltalk und irrelevante Gesprächsanteile rigoros weg.

Formatierung: Schreibe jede Unterkategorie als eigene Überschriftszeile, darunter den Inhalt als Fließtext oder kommagetrennte Aufzählung. Beginne nach jeder Unterkategorie eine neue Zeile, aber keine Leerzeile dazwischen.

Aktuell:
Beginne mit dem Vorstellungsgrund. Fasse zusammen: Art der Symptomatik (Schmerz, Sensibilitätsstörung, Paresen, Schwindel, Kopfschmerzen, Krampfanfälle, kognitive Defizite), Lokalisation, Seitenangabe, Ausstrahlung/Dermatomzuordnung, zeitlicher Verlauf (akut/subakut/chronisch, progredient/rezidivierend/konstant), Auslöser, Begleitsymptome (Übelkeit, Erbrechen, Sehstörungen, Sprachstörungen, Gangunsicherheit). Erwähne bisherige neurologische Diagnostik (MRT, CT, EEG, NLG/EMG, Liquorpunktion) und Vorbehandlungen.
Bei Kopfschmerzen: Frequenz, Dauer der Einzelattacke, Aura, Trigger, begleitende Photo-/Phonophobie.
Bei Schwindel: Drehschwindel vs. Schwankschwindel, Dauer, Lageabhängigkeit, Nystagmus.
Bei Anfallsleiden: Anfallstyp, Frequenz, letzte Episode, Prodromi, postiktale Phase.
Nebendiagnosen / Vorerkrankungen:
Nur neurologische Vorerkrankungen (Epilepsie, MS, Schlaganfall, Polyneuropathie), psychiatrische Komorbidität, Schädel-Hirn-Traumata, neurochirurgische Eingriffe, vaskuläre Risikofaktoren, Familienanamnese – kommagetrennt. Kein Verlauf und keine aktuelle Symptomatik (diese gehören unter ""Aktuell"").
Fallback: ""k. A."" wenn nicht erwähnt, ""keine"" wenn Patient explizit keine Nebendiagnosen oder Vorerkrankungen hat.
Dauermedikation:
Jedes Medikament in eine neue Zeile. Format: Name Dosierung Einnahmeschema (z.B. Levetiracetam 500 mg 1-0-1).
Besondere Beachtung von: Antikonvulsiva, Antikoagulantien, Antidepressiva, Neuroleptika, Analgetika inkl. Triptane.
Fallback: ""k. A."" wenn nicht erwähnt, ""keine"" wenn Patient explizit keine Medikamente nimmt.
Allergien / Unverträglichkeiten:
Auslöser und Reaktionstyp sofern genannt.
Fallback: ""k. A."" wenn nicht erwähnt, ""keine bekannt"" wenn Patient explizit verneint.
Vegetative Anamnese:
Nur Schlafstörungen (Ein-/Durchschlaf, Schlafapnoe, REM-Schlaf-Verhaltensstörung), Blasen-/Mastdarmfunktion, Schweißsekretionsstörungen, orthostatische Beschwerden. RR und Gewicht gehören nicht hierher (Vitalparameter/Befund).
Fallback: Wenn im Gespräch nicht thematisiert, diese Kategorie komplett weglassen (auch die Überschrift nicht nennen).
Sozialanamnese:
Beruf (Nacht-/Schichtarbeit, Exposition gegenüber Neurotoxinen), Fahrtauglichkeit, häusliche Versorgungssituation, Pflegebedarf, Mobilität. Nikotinkonsum, Alkoholkonsum (insbesondere bzgl. Polyneuropathie, Anfallsrisiko).
Fallback: ""Sozialanamnese unauffällig."" wenn besprochen aber unauffällig. ""k. A."" wenn nicht erwähnt.";
        }

        private static string GetDefaultBefundPromptAM()
        {
            return @"FACH-MODUL BEFUND: ALLGEMEINMEDIZIN

Erstelle aus dem Transkript den klinischen Untersuchungsbefund. Dokumentiere ausschließlich im Gespräch genannte oder durchgeführte Untersuchungen. Erfinde keine Befunde hinzu.

Formatierung: Für jedes untersuchte Organsystem einen Block. Überschrift: ""Befund [Organsystem] [Seitenangabe wenn zutreffend]:"". Darunter die Einzelbefunde (Inspektion, Palpation, Perkussion, Auskultation, orientierende Funktion), durch Komma getrennt, Block mit Punkt abschließen. Leerzeile zwischen verschiedenen Organsystem-Blöcken.

Vitalparameter:
Wenn genannt, als ersten Block anführen: RR [Wert] mmHg, Puls [Wert]/min, Temperatur [Wert] Grad C, SpO2 [Wert] %, Gewicht [Wert] kg, Größe [Wert] cm.

Relevante Organsysteme (nur dokumentieren wenn im Gespräch untersucht):
Allgemeinzustand und Ernährungszustand.
Haut und Schleimhäute: Kolorit, Turgor, Effloreszenzen, Ikterus, Zyanose.
Kopf/Hals: Lymphknoten, Schilddrüse, Meningismus.
Herz: Auskultation (Herzrhythmus, Herztöne, Geräusche).
Lunge: Auskultation (Atemgeräusch, Rasselgeräusche, Giemen), Perkussion.
Abdomen: Inspektion, Auskultation (Darmgeräusche), Palpation (Druckschmerz, Resistenzen, Organomegalie), Perkussion.
Extremitäten: Ödeme, Pulse, Varikosis, Beweglichkeit.
Orientierende neurologische Untersuchung: Pupillen, Kraft, Sensibilität, Koordination sofern durchgeführt.

Fallback (wenn keine Untersuchung stattfand):
""Keine Untersuchungsergebnisse dokumentiert.""";
        }

        private static string GetDefaultBefundPromptOR()
        {
            return @"FACH-MODUL BEFUND: ORTHOPÄDIE

Erstelle aus dem Transkript den klinischen Untersuchungsbefund. Dokumentiere ausschließlich im Gespräch genannte oder durchgeführte Untersuchungen. Erfinde keine Befunde hinzu. Übernimm genannte Testbezeichnungen wörtlich.

Formatierung: Für jeden untersuchten anatomischen Bereich einen Block. Überschrift: ""Befund [Region] [re./li./bds.]:"". Darunter die Einzelbefunde, durch Komma getrennt, Block mit Punkt abschließen. Leerzeile zwischen verschiedenen Regions-Blöcken.

Vitalparameter:
Wenn genannt: RR [Wert] mmHg, Puls [Wert]/min.

Relevante Untersuchungsinhalte pro Region (nur dokumentieren wenn durchgeführt):
Inspektion: Haltung, Gangbild (Hinken, Schonhaltung), Achsfehlstellung, Schwellung, Rötung, Muskelatrophie, Narben.
Palpation: Druckschmerz (exakte Lokalisation), Krepitation, Erguss, Überwärmung, Muskelhartspann, Triggerpunkte, myofasziale Tonuserhöhung.
Bewegungsausmaß (ROM): Neutral-Null-Methode sofern dokumentiert (z.B. Flex/Ext 130/0/0 Grad). Endgefühl, Bewegungsschmerz.
Kraft: Kraftgrade nach Janda (0-5) sofern geprüft, Seitenvergleich.
Stabilitätstests: Testbezeichnung und Ergebnis (positiv/negativ), z.B. Lachman-Test negativ, vordere Schublade negativ, Meniskustests (McMurray, Steinmann, Apley), Impingement-Tests (Neer, Hawkins, Jobe), Bandstabilität (Aufklappbarkeit, Pivot-Shift).
Neurologie orientierend: Sensibilität, Motorik, Reflexe der betroffenen Extremität sofern geprüft.
Wirbelsäule: Schober-Zeichen, Ott-Zeichen, Finger-Boden-Abstand, Lasegue, Bragard, Federungstest, ISG-Provokation sofern durchgeführt.

Fallback (wenn keine Untersuchung stattfand):
""Keine Untersuchungsergebnisse dokumentiert.""";
        }

        private static string GetDefaultBefundPromptNE()
        {
            return @"FACH-MODUL BEFUND: NEUROLOGIE

Erstelle aus dem Transkript den neurologischen Untersuchungsbefund. Dokumentiere ausschließlich im Gespräch genannte oder durchgeführte Untersuchungen. Erfinde keine Befunde hinzu.

Formatierung: Für jedes geprüfte neurologische System einen Block. Überschrift: ""Befund [System]:"". Darunter die Einzelbefunde, durch Komma getrennt, Block mit Punkt abschließen. Leerzeile zwischen verschiedenen System-Blöcken. Seitenvergleich dokumentieren wo relevant (re./li./bds.).

Vitalparameter:
Wenn genannt: RR [Wert] mmHg, Puls [Wert]/min, Temperatur [Wert] Grad C.

Relevante neurologische Untersuchungssysteme (nur dokumentieren wenn geprüft):

Bewusstsein und Orientierung:
Vigilanz (wach, somnolent, soporös, komatös), Orientierung (zeitlich, örtlich, situativ, zur Person), GCS sofern erhoben.

Hirnnerven:
I: Riechprüfung.
II: Visus orientierend, Gesichtsfeld (Fingerperimetrie), Pupillen (isokor/anisokor, Weite, direkte/konsensuelle Lichtreaktion, Konvergenz).
III/IV/VI: Augenmotilität (Blickfolge, Doppelbilder, Nystagmus: Richtung, erschöpflich/nicht erschöpflich).
V: Sensibilität Gesicht (alle drei Äste), Masseterreflex, Kornealreflex.
VII: Mimische Muskulatur (Stirnrunzeln, Lidschluss, Nasolabialfalte, Zähnezeigen), Geschmack vordere 2/3 der Zunge sofern geprüft.
VIII: Hörprüfung orientierend (Fingerreiben), Weber, Rinne sofern durchgeführt.
IX/X: Gaumensegelinnervation, Würgereflex, Schluckakt.
XI: Kopfwendung, Schulterhebung (M. trapezius, M. sternocleidomastoideus).
XII: Zungenmotilität (Deviation, Atrophie, Faszikulationen).

Motorik:
Kraftgrade nach MRC (0-5) pro Kennmuskel oder Muskelgruppe, Seitenvergleich. Muskeltonus (normoton, spastisch, rigide, schlaff). Trophik (Atrophie, Faszikulationen).

Sensibilität:
Berührung (Pallästhesie, Graphästhesie), Schmerz (Spitz-Stumpf-Diskrimination), Temperatur, Vibration (Stimmgabel mit Wertangabe /8), Lagesinn. Angabe der betroffenen Dermatome oder Verteilung (strumpf-/handschuhförmig, halbseitig, dissoziiert).

Reflexe:
Muskeleigenreflexe: BSR, TSR, RPR, PSR, ASR (Seitenvergleich, Abschwächung/Steigerung/Kloni). Pathologische Reflexe: Babinski, Gordon, Oppenheim, Troemner.

Koordination:
Finger-Nase-Versuch, Knie-Hacke-Versuch, Dysdiadochokinese, Rebound-Phänomen, Romberg-Stehversuch, Unterberger-Tretversuch.

Gang:
Gangbild (normal, breitbasig, kleinschrittig, ataktisch, spastisch, hinkend), Seiltänzergang, Einbeinstand, Fersengang, Zehenspitzengang.

Sprache und Kognition:
Dysarthrie, Aphasie (Typ sofern differenzierbar), Neglect, Apraxie, orientierende kognitive Prüfung (MMSE, MoCA, Uhrentest) mit Ergebnis sofern durchgeführt.

Meningismus:
Nackensteife, Brudzinski, Kernig, Lasegue sofern geprüft.

Fallback (wenn keine Untersuchung stattfand):
""Keine Untersuchungsergebnisse dokumentiert.""";
        }

        private static string GetDefaultDiagnosenPrompt()
        {
            return @"Leite aus der dokumentierten Anamnese und dem Befund die wahrscheinlichsten Diagnosen ab.
Gib die Diagnosen als Liste aus, eine Diagnose pro Zeile. Verwende Fachterminologie.
Sortierung: Hauptdiagnose (aktueller Vorstellungsgrund) zuerst, dann Nebendiagnosen nach klinischer Relevanz absteigend.
Kennzeichne den Sicherheitsgrad:
Gesicherte Diagnose: nur Diagnosetext (z.B. ""Gonarthrose re."")
Verdachtsdiagnose: mit Präfix ""V.a."" (z.B. ""V.a. Meniskusläsion re."")
Ausschlussdiagnose: mit Präfix ""Ausschluss"" (z.B. ""Ausschluss Bandruptur"")
Zustand nach: mit Präfix ""Z.n."" (z.B. ""Z.n. Knie-TEP li. 2019"")
Wenn aus dem Gespräch keine Diagnose ableitbar: ""Keine Diagnose aus den vorliegenden Informationen ableitbar.""";
        }

        private static string GetDefaultIcd10Prompt(string fachrichtung)
        {
            string fachPrio = fachrichtung switch
            {
                "Orthopädie" => "Bei Fachrichtung Orthopädie: priorisiere M-Codes (Muskel-Skelett) und S/T-Codes (Verletzungen), ergänze Begleitdiagnosen aus anderen Kapiteln.",
                "Neurologie" => "Bei Fachrichtung Neurologie: priorisiere G-Codes (Nervensystem), ergänze I-Codes (zerebrovaskulär), R-Codes (Symptome) und Begleitdiagnosen.",
                "Kardiologie" => "Bei Fachrichtung Kardiologie: priorisiere I-Codes (Herz-Kreislauf), insbesondere I05-I09 (rheumatische Klappenfehler), I34-I37 (nichtrheumatische Klappenfehler), I42 (Kardiomyopathien), I50 (Herzinsuffizienz), I31 (Perikarderkrankungen).",
                _ => "Bei Fachrichtung Allgemeinmedizin: gesamtes ICD-10-Spektrum, häufig Kapitel I-XIV."
            };

            return $@"Gib zu jeder Diagnose den passenden ICD-10-GM-Code an. Eine Zeile pro Diagnose. Format: [ICD-10-Code] [Diagnosentext]
Verwende die höchste sinnvolle Spezifität (4- oder 5-stellig). Ergänze die Seitenkennzeichnung bei paarigen Organen: R (rechts), L (links), B (beidseits).
Verwende die Diagnosesicherheit gemäß ICD-10-Kodierrichtlinien: G (gesichert), V (Verdacht), A (Ausschluss), Z (Zustand nach).
{fachPrio}";
        }

        private static string GetDefaultSectionBe() => GetDefaultBefundPromptAM();
        private static string GetDefaultSectionN() => GetDefaultDiagnosenPrompt();
        private static string GetDefaultSectionT() => string.Empty;

        /// <summary>Mode-specific prompt module (Neupatient / Kontrolltermin).</summary>
        public static string GetModePrompt(string recordingMode)
        {
            if (string.Equals(recordingMode, "Kontrolltermin", StringComparison.OrdinalIgnoreCase))
            {
                return @"
MODUS: KONTROLLTERMIN / WIEDERVORSTELLUNG

Für die Anamnese gilt: Beschränke dich auf das aktuelle Vorstellungsanliegen. Beginne mit ""Aktuell:"" und dem Verlauf seit dem letzten Termin (Veränderungen der Symptomatik, Therapieansprechen, aktuelle/neue Beschwerden).";
            }

            return @"
MODUS: NEUPATIENT / ERSTVORSTELLUNG

Für die Anamnese gilt: Erfasse alle verfügbaren Informationen vollständig. Die Anamnese enthält die Unterkategorien wie im Fach-Modul angegeben. Wurde eine Kategorie im Gespräch nicht thematisiert, verwende den im Fach-Modul definierten Fallback-Satz.";
        }

        private static string GetDefaultUniversalPrompt()
        {
            return @"Du bist ein präziser medizinischer Dokumentations-Assistent für Einträge in die Patientenakte.

GLOBALE REGELN:

Sprache: Nutze durchgehend ärztliche Fachsprache. Verwende gängige medizinische Abkürzungen: re. (rechts), li. (links), bds. (beidseits), o.B. (ohne Befund), Z.n. (Zustand nach), V.a. (Verdacht auf), ED (Erstdiagnose), DD (Differentialdiagnose), ggf. (gegebenenfalls), bzgl. (bezüglich), ca. (circa), Pat. (Patient/in).

Format: Nutze für den Inhalt reinen Text. Kein Markdown, keine Sternchen, kein Fettdruck. Keine automatischen Nummerierungen (1., 2., 3.).

Marker-System: Trenne die Hauptabschnitte ausschließlich mit folgenden Markern. Schreibe keinen Text vor dem ersten Marker.
**A** = Anamnese
**Be** = Befund
**N** = Diagnosen
**ICD-10** = ICD-10-Codierung
Gib nur die Marker aus, die vom Nutzer angefordert wurden.

Negativbefunde: Wenn eine Kategorie im Gespräch nicht thematisiert wurde, nutze den jeweils vorgesehenen Fallback-Satz. Erfinde niemals Informationen hinzu.

Sprecher-Erkennung: Analysiere das Gesprächstranskript. Der Sprecher, der Fragen stellt, Anweisungen gibt oder Untersuchungen durchführt, ist der Arzt. Der andere Sprecher ist der Patient. Tausche die Rollen logisch, falls die Labels vertauscht erscheinen.";
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
                    A = GetDefaultAnamnesePromptAM(),
                    Be = GetDefaultBefundPromptAM(),
                    N = GetDefaultDiagnosenPrompt(),
                    T = string.Empty,
                    Icd10 = GetDefaultIcd10Prompt("Allgemeinmedizin")
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
                    A = GetDefaultAnamnesePromptOR(),
                    Be = GetDefaultBefundPromptOR(),
                    N = GetDefaultDiagnosenPrompt(),
                    T = string.Empty,
                    Icd10 = GetDefaultIcd10Prompt("Orthopädie")
                }
            };
        }

        private static SubjectForm CreateDefaultNeurologieForm()
        {
            return new SubjectForm
            {
                Id = "neurologie",
                DisplayName = "Neurologie",
                Description = "Standard-ABeNT für neurologische Befunde.",
                SectionPrompts = new AbentSectionPrompts
                {
                    A = GetDefaultAnamnesePromptNE(),
                    Be = GetDefaultBefundPromptNE(),
                    N = GetDefaultDiagnosenPrompt(),
                    T = string.Empty,
                    Icd10 = GetDefaultIcd10Prompt("Neurologie")
                }
            };
        }

        private static SubjectForm CreateDefaultEchokardiographieForm()
        {
            return new SubjectForm
            {
                Id = "echokardiographie",
                DisplayName = "Echokardiographie",
                Description = "Befundbericht für transthorakale/transösophageale Echokardiographie (kein Anamnese-Block).",
                SectionPrompts = new AbentSectionPrompts
                {
                    A = string.Empty,
                    Be = GetDefaultBefundPromptEcho(),
                    N = GetDefaultDiagnosenPromptEcho(),
                    T = string.Empty,
                    Icd10 = GetDefaultIcd10Prompt("Kardiologie")
                }
            };
        }

        private static string GetDefaultBefundPromptEcho()
        {
            return @"FACH-MODUL BEFUND: ECHOKARDIOGRAPHIE

Erstelle aus dem Transkript einen strukturierten echokardiographischen Befundbericht. Dokumentiere ausschließlich im Gespräch genannte oder diktierte Messwerte und Beurteilungen. Erfinde keine Befunde hinzu.

Formatierung: Für jede untersuchte Struktur einen Block. Überschrift als eigene Zeile, darunter die Einzelbefunde durch Komma getrennt, Block mit Punkt abschließen. Leerzeile zwischen verschiedenen Struktur-Blöcken.

Bildqualität:
Wenn kommentiert: Beurteilung der Schallbedingungen (gut/eingeschränkt/schlecht), ggf. Grund (Adipositas, Emphysem, Lagerung).

Linker Ventrikel (LV):
Dimensionen (LVEDD, LVESD in mm), Wanddicken (IVSd, LVPWd in mm), LV-Masse/Index sofern angegeben. Wandbewegungsanalyse: global normokinetisch oder regionale Wandbewegungsstörungen (Segmentzuordnung nach 16/17-Segment-Modell sofern genannt). Systolische Funktion: LVEF (Simpson biplan, ggf. visuell geschätzt), GLS sofern gemessen. Diastolische Funktion: E/A-Verhältnis, E/e' (septal, lateral, Mittelwert), Dezelerationszeit, LAVI, Trikuspidalinsuffizienz-Geschwindigkeit, Einteilung Grad I-III sofern möglich.

Rechter Ventrikel (RV):
Dimensionen (RVEDD, RV basaler Durchmesser), TAPSE, S' (Gewebedoppler), FAC sofern gemessen. RV-Funktion: normal/eingeschränkt.

Vorhöfe:
LA-Diameter (parasternal), LA-Volumenindex (LAVI), RA-Fläche/Volumen sofern angegeben.

Aortenklappe (AV):
Morphologie (trikuspidalisch/bikuspidalisch, Verkalkungsgrad), Öffnungsverhalten. Bei Stenose: Vmax, mittlerer Gradient, KÖF (Kontinuitätsgleichung), Dimensionsloser Index. Bei Insuffizienz: Grad (I-III), Jet-Richtung, Vena contracta, PISA sofern gemessen.

Mitralklappe (MV):
Morphologie (Segel, Anulus), Bewegungsmuster. Bei Insuffizienz: Grad (I-III), Jet-Richtung, Vena contracta, EROA, Regurgitationsvolumen sofern gemessen. Bei Stenose: MÖF (PHT, Planimetrie), mittlerer Gradient.

Trikuspidalklappe (TV):
Morphologie. Bei Insuffizienz: Grad, Vmax (zur Abschätzung des sPAP), geschätzter sPAP (= 4 x Vmax² + RAP).

Pulmonalklappe (PV):
AT (Akzelerationszeit), sofern untersucht. Insuffizienz: Grad, sofern vorhanden.

Perikard:
Erguss (keiner/gering/moderat/ausgeprägt), Lokalisation, diastolische Kompression von RA/RV sofern beurteilbar.

Aorta:
Aortenwurzel-Diameter, Aorta ascendens-Diameter sofern gemessen.

Vena cava inferior (VCI):
Durchmesser, Atemvariabilität (>50% / <50%), geschätzter RAP.

Fallback (wenn keine Untersuchung stattfand):
""Keine echokardiographischen Befunde dokumentiert.""";
        }

        private static string GetDefaultDiagnosenPromptEcho()
        {
            return @"Leite aus dem dokumentierten echokardiographischen Befund die Beurteilung ab.
Gib die Diagnosen/Beurteilungen als Liste aus, eine pro Zeile. Verwende Fachterminologie.
Sortierung: Hauptbefund zuerst, dann Nebenbefunde nach klinischer Relevanz absteigend.
Kennzeichne den Sicherheitsgrad:
Gesicherter Befund: nur Diagnosetext (z.B. ""Mitralinsuffizienz Grad II"")
Verdacht: mit Präfix ""V.a.""
Normalbefund: ""Strukturell und funktionell unauffälliges Echokardiogramm"" wenn alle Parameter normwertig.
Wenn aus dem Diktat keine Beurteilung ableitbar: ""Keine Beurteilung aus den vorliegenden Informationen ableitbar.""";
        }

        /// <summary>Für app-config.json (nur UniversalPrompt).</summary>
        private class AppConfig
        {
            public string UniversalPrompt { get; set; } = string.Empty;
        }
    }
}
