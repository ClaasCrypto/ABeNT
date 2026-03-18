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
        /// <summary>Fixe Standardformulare: werden bei fehlendem Eintrag im Manifest wieder angelegt; nur für diese wird "Standard wiederherstellen" angeboten.</summary>
        private static readonly string[] StandardFormIds = { "allgemeinmedizin", "orthopaedie" };

        /// <summary>Bei jeder Prompt-Änderung im Code erhöhen. Standardformulare mit niedrigerer Version werden automatisch aktualisiert.</summary>
        public const int CurrentPromptVersion = 9;

        /// <summary>Version des Code-Standards für den Universal-Prompt. Bei Änderung erhöhen.</summary>
        public const int CurrentUniversalPromptVersion = 1;

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

        /// <summary>Id für Dateinamen bereinigen: Deutsche Umlaute transliterieren, dann nur a-z, 0-9, _, -.</summary>
        public static string SanitizeIdForFile(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "form";
            id = id.Trim().ToLowerInvariant();
            id = id.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
            id = Regex.Replace(id, @"[^a-z0-9_\-]", "_");
            id = Regex.Replace(id, @"_+", "_").Trim('_');
            return string.IsNullOrEmpty(id) ? "form" : id;
        }

        private static string GetFormFilePath(string id)
        {
            return Path.Combine(GetFormsFolder(), SanitizeIdForFile(id) + ".json");
        }

        /// <summary>True, wenn es sich um ein fixes Standardformular (Allgemeinmedizin oder Orthopädie) handelt.</summary>
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

                        // Veraltete Formulare entfernen (Upgrade-Migration)
                        var deprecatedIds = new[] { "neurologie", "echokardiographie" };
                        foreach (string depId in deprecatedIds)
                        {
                            if (list.RemoveAll(f => string.Equals(f, depId, StringComparison.OrdinalIgnoreCase)) > 0)
                            {
                                string depPath = GetFormFilePath(depId);
                                try { if (File.Exists(depPath)) File.Delete(depPath); } catch { /* ignore */ }
                                changed = true;
                            }
                        }

                        // Fehlende Standardformulare ergänzen
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
                            SaveAppConfig(appConfig);
                        }
                        SaveManifest(ids);
                        return ids;
                    }
                }
                catch { /* ignore */ }
            }

            // Neuer Start: nur die beiden Standardformulare anlegen
            var defaultIds = new List<string>(StandardFormIds);
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

                if (IsStandardForm(id))
                    ApplyStandardDefaults(form, id);

                return form;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gleicht ein Standardformular mit den Code-Defaults ab.
        /// Pre-Flag-Migration: Erkennt Nutzer-Anpassungen durch Textvergleich.
        /// Post-Flag: Vertraut den *Customized-Flags und aktualisiert nur nicht-angepasste Sektionen.
        /// </summary>
        private static void ApplyStandardDefaults(SubjectForm form, string id)
        {
            var codeDef = GetDefaultFormById(id);
            if (codeDef == null) return;

            var p = form.SectionPrompts ?? new AbentSectionPrompts();
            var d = codeDef.SectionPrompts;
            bool changed = false;

            // Einmalige Migration von Formularen ohne *Customized-Flags (PromptVersion < 9).
            // Textvergleich mit aktuellem Default: Abweichung → als Nutzer-Anpassung bewerten.
            if (p.PromptVersion < 9)
            {
                p.ACustomized = !string.IsNullOrEmpty(p.A?.Trim()) &&
                                !string.Equals(p.A?.Trim(), d.A?.Trim(), StringComparison.Ordinal);
                p.BeCustomized = !string.IsNullOrEmpty(p.Be?.Trim()) &&
                                 !string.Equals(p.Be?.Trim(), d.Be?.Trim(), StringComparison.Ordinal);
                p.TCustomized = !string.IsNullOrEmpty(p.T?.Trim()) &&
                                !string.Equals(p.T?.Trim(), d.T?.Trim(), StringComparison.Ordinal);
                p.NCustomized = !string.IsNullOrEmpty(p.N?.Trim()) &&
                                !string.Equals(p.N?.Trim(), d.N?.Trim(), StringComparison.Ordinal);
                changed = true;
            }

            if (!p.ACustomized && !string.Equals(p.A, d.A ?? string.Empty))
            { p.A = d.A ?? string.Empty; changed = true; }
            if (!p.BeCustomized && !string.Equals(p.Be, d.Be ?? string.Empty))
            { p.Be = d.Be ?? string.Empty; changed = true; }
            if (!p.TCustomized && !string.Equals(p.T, d.T ?? string.Empty))
            { p.T = d.T ?? string.Empty; changed = true; }
            if (!p.NCustomized && !string.Equals(p.N, d.N ?? string.Empty))
            { p.N = d.N ?? string.Empty; changed = true; }

            if (p.PromptVersion != CurrentPromptVersion)
            {
                p.PromptVersion = CurrentPromptVersion;
                changed = true;
            }

            form.SectionPrompts = p;
            if (changed) SaveFormToFile(form);
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

        /// <summary>Setzt das aktuell geöffnete Formular auf die Code-Vorlage zurück. Nur für fixe Standardformulare (AM, OR) erlaubt.</summary>
        public static void RestoreDefaultForm(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            id = id.Trim();
            if (!IsStandardForm(id))
                throw new InvalidOperationException("Nur die Standardformulare Allgemeinmedizin und Orthopädie können wiederhergestellt werden.");
            var form = GetDefaultFormById(id);
            if (form == null)
                throw new InvalidOperationException($"Form '{id}' kann nicht wiederhergestellt werden.");
            SaveFormToFile(form);
        }

        public static string GetUniversalPrompt()
        {
            var config = LoadAppConfig();

            // Migration: alte app-config.json ohne UniversalPromptVersion
            if (config.UniversalPromptVersion == 0 && !string.IsNullOrWhiteSpace(config.UniversalPrompt))
            {
                bool hasOldMarkers = ContainsOldMarkers(config.UniversalPrompt);
                string defaultText = GetDefaultUniversalPrompt();
                bool differs = !hasOldMarkers &&
                               !string.Equals(config.UniversalPrompt.Trim(), defaultText.Trim(), StringComparison.Ordinal);
                config.UniversalPromptCustomized = differs;
                config.UniversalPromptVersion = CurrentUniversalPromptVersion;
                SaveAppConfig(config);
                return differs ? config.UniversalPrompt : defaultText;
            }

            // Reparatur: bereits migrierte Prompts mit alten Markern (**A**, **Be** etc.)
            // sind inkompatibel mit dem neuen [ABSCHNITT:X]-System.
            if (config.UniversalPromptCustomized && !string.IsNullOrWhiteSpace(config.UniversalPrompt)
                && ContainsOldMarkers(config.UniversalPrompt))
            {
                config.UniversalPromptCustomized = false;
                config.UniversalPrompt = string.Empty;
                config.UniversalPromptVersion = CurrentUniversalPromptVersion;
                SaveAppConfig(config);
                return GetDefaultUniversalPrompt();
            }

            if (config.UniversalPromptCustomized && !string.IsNullOrWhiteSpace(config.UniversalPrompt))
                return config.UniversalPrompt;

            return GetDefaultUniversalPrompt();
        }

        public static void SetUniversalPrompt(string universalPrompt)
        {
            var config = LoadAppConfig();
            config.UniversalPrompt = universalPrompt ?? string.Empty;
            config.UniversalPromptCustomized = true;
            config.UniversalPromptVersion = CurrentUniversalPromptVersion;
            SaveAppConfig(config);
        }

        /// <summary>Setzt den Universal-Prompt auf den Code-Standard zurück.</summary>
        public static void RestoreDefaultUniversalPrompt()
        {
            var config = LoadAppConfig();
            config.UniversalPrompt = string.Empty;
            config.UniversalPromptCustomized = false;
            config.UniversalPromptVersion = CurrentUniversalPromptVersion;
            SaveAppConfig(config);
        }

        /// <summary>Gibt true zurück, wenn der Nutzer den Universal-Prompt individuell angepasst hat.</summary>
        public static bool IsUniversalPromptCustomized()
        {
            var config = LoadAppConfig();
            if (config.UniversalPromptVersion == 0 && !string.IsNullOrWhiteSpace(config.UniversalPrompt))
            {
                if (ContainsOldMarkers(config.UniversalPrompt)) return false;
                string defaultText = GetDefaultUniversalPrompt();
                return !string.Equals(config.UniversalPrompt.Trim(), defaultText.Trim(), StringComparison.Ordinal);
            }
            if (config.UniversalPromptCustomized && !string.IsNullOrWhiteSpace(config.UniversalPrompt)
                && ContainsOldMarkers(config.UniversalPrompt))
                return false;
            return config.UniversalPromptCustomized;
        }

        /// <summary>Erkennt alte Markdown-basierte Sektionsmarker im Prompt-Text.</summary>
        private static bool ContainsOldMarkers(string text)
        {
            return text.Contains("**A**") || text.Contains("**Be**")
                || text.Contains("**N**") || text.Contains("**ICD");
        }

        /// <summary>Gibt den Code-Standard des Universal-Prompts zurück (öffentlich für UI-Vergleich).</summary>
        public static string GetDefaultUniversalPromptText() => GetDefaultUniversalPrompt();

        /// <summary>Gibt die Code-Standard-SectionPrompts eines Standardformulars zurück (null für Nicht-Standard).</summary>
        public static AbentSectionPrompts? GetDefaultSectionPrompts(string formId)
        {
            var form = GetDefaultFormById(formId);
            return form?.SectionPrompts;
        }

        private static AppConfig LoadAppConfig()
        {
            string path = GetAppConfigPath();
            if (!File.Exists(path))
                return new AppConfig();
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch { return new AppConfig(); }
        }

        private static void SaveAppConfig(AppConfig config)
        {
            string path = GetAppConfigPath();
            File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        public static string BuildSystemPromptFromConfig(string? formId, string gender, bool includeBefund, bool includeDiagnosen, bool includeTherapie, bool includeIcd10, string recordingMode = "Neupatient")
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
            bool includeN = includeDiagnosen;
            sb.AppendLine("\nOutput-Struktur (trenne die Abschnitte mit den exakten Markern):");
            if (includeA) sb.AppendLine("- [ABSCHNITT:A] (Anamnese)");
            if (includeBefund) sb.AppendLine("- [ABSCHNITT:Be] (Befund)");
            if (includeTherapie) sb.AppendLine("- [ABSCHNITT:T] (Therapie)");
            if (includeN) sb.AppendLine("- [ABSCHNITT:N] (Diagnosen" + (includeIcd10 ? " inkl. ICD-10-Codierung" : "") + ")");

            sb.AppendLine("\nSTRUKTURVORGABEN:");

            if (includeA)
            {
                string promptA = form?.SectionPrompts?.A?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptA)) promptA = GetDefaultSectionA();
                sb.AppendLine("\n[ABSCHNITT:A]\n" + promptA);
            }

            if (includeBefund)
            {
                string promptBe = form?.SectionPrompts?.Be?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptBe)) promptBe = GetDefaultSectionBe();
                sb.AppendLine("\n[ABSCHNITT:Be]\n" + promptBe);
            }
            if (includeTherapie)
            {
                string promptT = form?.SectionPrompts?.T?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptT)) promptT = GetDefaultSectionT();
                if (!string.IsNullOrEmpty(promptT))
                    sb.AppendLine("\n[ABSCHNITT:T]\n" + promptT);
            }
            if (includeN)
            {
                string promptN = form?.SectionPrompts?.N?.Trim() ?? "";
                if (string.IsNullOrEmpty(promptN)) promptN = GetDefaultSectionN();
                sb.AppendLine("\n[ABSCHNITT:N]\n" + promptN);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Baut den System-Prompt für die Diktat-Funktion.
        /// Enthält Universal-Basis (ohne Sprecher-Erkennung) + Diktat-Kontext + Sektions-Prompt.
        /// </summary>
        public static string BuildDictationPrompt(string sectionPrompt)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("Du bist ein präziser medizinischer Dokumentations-Assistent für Einträge in die Patientenakte.");
            sb.AppendLine();
            sb.AppendLine("Format: Reiner Text, kein Markdown. Keine Formatierungszeichen.");
            sb.AppendLine();
            sb.AppendLine("Sprache: Nutze durchgehend ärztliche Fachsprache. Verwende gängige medizinische Abkürzungen: re. (rechts), li. (links), bds. (beidseits), o.B. (ohne Befund), Z.n. (Zustand nach), V.a. (Verdacht auf), ED (Erstdiagnose), DD (Differentialdiagnose), ggf. (gegebenenfalls), bzgl. (bezüglich), ca. (circa), Pat. (Patient/in).");
            sb.AppendLine();
            sb.AppendLine("Kontext: Ein Arzt diktiert direkt. Strukturiere und formuliere das Diktat gemäß den folgenden Anweisungen. Ergänze oder erfinde keine Informationen.");

            if (!string.IsNullOrWhiteSpace(sectionPrompt))
            {
                sb.AppendLine();
                sb.AppendLine(sectionPrompt);
            }

            return sb.ToString();
        }

        private static string GetDefaultSectionA() => GetDefaultAnamnesePromptAM();

        internal static string GetDefaultAnamnesePromptAM()
        {
            return @"FACH-MODUL ANAMNESE: ALLGEMEINMEDIZIN

<system_instruktion>
Du bist ein medizinischer Dokumentationsassistent. Deine Aufgabe: Extrahiere aus dem folgenden Arzt-Patienten-Gespräch die allgemeinmedizinische Anamnese.

Stilregeln:
- Zwingend Nominalstil oder sehr kurze, objektive Sätze (z. B. ""Hustenangabe seit fünf Tagen, produktiv, gelblicher Auswurf"", nicht ""Der Patient hustet seit fünf Tagen und hat gelben Auswurf"").
- Medizinisch irrelevante Gesprächsanteile (Smalltalk, Begrüßung, Verabschiedung) rigoros herausfiltern.
- Keine eigenen Interpretationen, Diagnosen oder Ergänzungen hinzufügen – nur das wiedergeben, was im Gespräch tatsächlich gesagt wurde.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown (keine **, keine #, keine ``` usw.).
2. Platzhalter-Logik: Die Begriffe in den runden Klammern im Template definieren den Suchraum – sie beschreiben, wonach zu suchen ist. Sie dürfen nicht als fester Text in die Ausgabe übernommen werden. Ersetze den gesamten Klammerausdruck durch die extrahierte Information.
3. Nicht erfragt vs. verneint: Wurde ein Thema im Gespräch nicht angesprochen, schreibe ""nicht erfragt"". Wurde es aktiv verneint, schreibe die Verneinung (z. B. ""keine bekannt"", ""kein Nikotinkonsum""). Felder nie stillschweigend weglassen.
4. Nebendiagnosen: Kommagetrennte Aufzählung. Unter ""Nebendiagnosen / Vorerkrankungen"" nur Diagnosen, Voroperationen, chronische Erkrankungen, familiäre Belastung und bekannte Vordiagnosen aufführen – keinen aktuellen Verlauf und keine aktuelle Symptomatik (diese gehören unter ""Aktuell"").
5. Allergien: Format [Auslöser] – [Reaktionstyp]. Falls verneint: ""keine bekannt"". Falls nicht besprochen: ""nicht erfragt"".
6. Sozialanamnese: Beruf (inkl. körperlicher Belastung, z. B. sitzende Tätigkeit, Schichtarbeit), Sport, häusliche Situation, Pflegebedarf, Alkohol (keiner / gelegentlich / regelmäßig), Nikotin (Nichtraucher / Raucher, ggf. Menge in pack years). Nicht erfragte Teilbereiche mit ""nicht erfragt"" kennzeichnen.
7. Dauermedikation: Ein Listenpunkt je Medikament. Format: [Medikamentenname] [Dosierung], [Schema X-X-X oder X-X-X-X]. Nur die im Gespräch tatsächlich genannten Angaben ausgeben – fehlende Teile nicht ergänzen oder mit ""nicht erfragt"" auffüllen.

   Beispiel:
   Patient sagt: ""Ich nehme morgens eine Metformin 1000 und abends nochmal eine.""
   Ausgabe:       - Metformin 1000 mg, 1-0-1

   Patient sagt: ""Und dann noch was für die Schilddrüse, L-Thyroxin.""
   Ausgabe:       - L-Thyroxin

   Falls keine Dauermedikation: ""keine"". Falls nicht erfragt: ""nicht erfragt"".
8. Vegetative Anamnese: Alle genannten Angaben dokumentieren. Nicht erfragte Teilbereiche mit ""nicht erfragt"" kennzeichnen. Falls der gesamte Bereich nicht zur Sprache kam: ""nicht erfragt"". Hinweis: Gewicht als Messwert gehört in die Vitalparameter, nicht hierher – nur subjektive Gewichtsveränderung dokumentieren.
</formatierungs_regeln>

<ausgabe_template>
Aktuell: (Lokalisation, Charakter, Dauer, Auslöser, zeitlicher Verlauf, Begleitsymptome, Selbstmedikation/Akutbehandlung)
Nebendiagnosen / Vorerkrankungen: (chronische Erkrankungen, relevante Vordiagnosen, Operationen, familiäre Belastung)
Dauermedikation:
- (ein Listenpunkt je Medikament)
Allergien / Unverträglichkeiten: (Auslöser – Reaktionstyp)
Vegetative Anamnese: (B-Symptomatik: Fieber, Nachtschweiß, Gewichtsveränderung; Appetit, Schlaf, Miktion, Stuhlgang)
Sozialanamnese: (Beruf inkl. Belastung, sportliche Aktivität, häusliche Situation, Pflegebedarf, Nikotinkonsum, Alkoholkonsum)
</ausgabe_template>

<transkript>
{{TRANSKRIPT HIER EINFÜGEN}}
</transkript>";
        }

        private static string GetDefaultAnamnesePromptOR()
        {
            return @"FACH-MODUL ANAMNESE: ORTHOPÄDIE

<system_instruktion>
Du bist ein medizinischer Dokumentationsassistent. Deine Aufgabe: Extrahiere aus dem folgenden Arzt-Patienten-Gespräch die orthopädische Anamnese.

Stilregeln:
- Zwingend Nominalstil (z. B. ""Schmerzangabe seit drei Wochen"", nicht ""Der Patient hat seit drei Wochen Schmerzen"").
- Medizinisch irrelevante Gesprächsanteile (Smalltalk, Begrüßung, Verabschiedung) rigoros herausfiltern.
- Keine eigenen Interpretationen, Diagnosen oder Ergänzungen hinzufügen – nur das wiedergeben, was im Gespräch tatsächlich gesagt wurde.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown (keine **, keine #, keine ``` usw.).
2. Platzhalter-Logik: Die Begriffe in den runden Klammern im Template definieren den Suchraum – sie beschreiben, wonach zu suchen ist. Sie dürfen nicht als fester Text in die Ausgabe übernommen werden. Ersetze den gesamten Klammerausdruck durch die extrahierte Information.
3. Nicht erfragt vs. verneint: Wurde ein Thema im Gespräch nicht angesprochen, schreibe ""nicht erfragt"". Wurde es aktiv verneint, schreibe die Verneinung (z. B. ""keine bekannt"", ""kein Nikotinkonsum""). Felder nie stillschweigend weglassen.
4. Nebendiagnosen: Kommagetrennte Aufzählung. Unter ""Nebendiagnosen / Vorerkrankungen"" nur Diagnosen, Voroperationen, Frakturen und bekannte degenerative Veränderungen aufführen – keinen aktuellen Verlauf und keine aktuelle Symptomatik (diese gehören unter ""Aktuell"").
5. Allergien: Kommagetrennte Liste. Falls verneint: ""keine bekannt"". Falls nicht besprochen: ""nicht erfragt"".
6. Sozialanamnese: Beruf (inkl. körperlicher Belastung, z. B. Überkopfarbeit, sitzende Tätigkeit), Sport, Alkohol (keiner / gelegentlich / regelmäßig), Nikotin (Nichtraucher / Raucher, ggf. Menge). Nicht erfragte Teilbereiche mit ""nicht erfragt"" kennzeichnen.
7. Dauermedikation: Ein Listenpunkt je Medikament. Format: [Medikamentenname] [Dosierung], [Schema X-X-X oder X-X-X-X]. Nur die im Gespräch tatsächlich genannten Angaben ausgeben – fehlende Teile nicht ergänzen oder mit ""k. A."" auffüllen.

   Beispiel:
   Patient sagt: ""Ich nehme morgens und abends Ibuprofen 600.""
   Ausgabe:       - Ibuprofen 600 mg, 1-0-1

   Patient sagt: ""Dann nehme ich noch so eine Blutdrucktablette, Ramipril glaube ich.""
   Ausgabe:       - Ramipril

   Falls keine Dauermedikation: ""keine"". Falls nicht erfragt: ""nicht erfragt"".
8. Vegetative Anamnese: Nur aufführen, was im Gespräch tatsächlich angesprochen wurde. Nicht erfragte Teilbereiche mit ""nicht erfragt"" kennzeichnen. Falls der gesamte Bereich nicht zur Sprache kam: ""nicht erfragt"".
</formatierungs_regeln>

<ausgabe_template>
Aktuell: (Lokalisation, Seitenangabe, Ausstrahlung, Charakter, Auslöser, zeitlicher Verlauf, Begleitsymptome, Selbstmedikation/bisherige Behandlung)
Nebendiagnosen / Vorerkrankungen: (internistische oder andere chronische Erkrankungen, Voroperationen oder Frakturen, bekannte degenerative Veränderungen, sonstige relevante Begleitdiagnosen)
Dauermedikation:
- (ein Listenpunkt je Medikament)
Allergien / Unverträglichkeiten: (Auslöser, Substanz)
Vegetative Anamnese: (Schlaf, Verdauung, Miktion, B-Symptomatik)
Sozialanamnese: (Beruf inkl. körperlicher Belastung, sportliche Aktivität, häusliche Situation, Pflegebedarf, Nikotinkonsum, Alkoholkonsum)
</ausgabe_template>

<transkript>
{{TRANSKRIPT HIER EINFÜGEN}}
</transkript>";
        }

        internal static string GetDefaultBefundPromptAM()
        {
            return @"FACH-MODUL BEFUND: ALLGEMEINMEDIZIN

<system_instruktion>
Extrahiere aus dem Transkript ausschließlich durchgeführte körperliche Untersuchungen und deren Ergebnisse.
Oberste Direktive: Generiere keine False Positives. Es gilt striktes Zero-Shot-Verhalten bezüglich medizinischer Befunde. Was nicht explizit im Transkript genannt oder gemessen wurde, existiert nicht.
Explizit erhobene Negativbefunde (z.B. ""kein Druckschmerz"", ""keine Rasselgeräusche"") sind reale Untersuchungsergebnisse und werden dokumentiert.
</system_instruktion>

<extraktions_parameter>
Prüfe das Transkript auf Entitäten aus folgenden Kategorien. Diese Liste dient NUR der Definition des Suchraums, nicht als Vorlage für die Ausgabe:
- Vitalparameter (RR, Puls, Temperatur, SpO2, Gewicht, Größe)
- Allgemein- und Ernährungszustand
- Haut/Schleimhäute (Kolorit, Turgor, Effloreszenzen, Ikterus, Zyanose)
- Kopf/Hals (Lymphknoten, Schilddrüse, Meningismus)
- Herz (Rhythmus, Herztöne, Geräusche)
- Lunge (Atemgeräusch, Rasselgeräusche, Giemen, Perkussion)
- Abdomen (Inspektion, Auskultation, Palpation, Perkussion)
- Extremitäten (Ödeme, Pulse, Varikosis, Beweglichkeit)
- Neurologie (Pupillen, Kraft, Sensibilität, Koordination)
</extraktions_parameter>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown.
2. Dynamische Blöcke: Erstelle nur dann einen Block für ein Organsystem, wenn im Transkript tatsächliche Befunde dazu vorliegen.
3. Struktur pro Block: [Organsystem] [Seitenangabe, falls zutreffend]: [Einzelbefunde durch Komma getrennt]. Jeden Block mit Punkt abschließen.
4. Vitalparameter: Wenn Vitalparameter extrahiert wurden, müssen diese zwingend als allererster Block unter der Überschrift ""Vitalparameter:"" stehen. Format: [Parameter] [Wert] [Einheit], kommagetrennt.
5. Globaler Fallback: Wenn das Transkript keinerlei klinische Untersuchungsergebnisse enthält, gib exakt und ausschließlich diesen Satz aus: ""Keine Untersuchungsergebnisse dokumentiert.""
</formatierungs_regeln>";
        }

        private static string GetDefaultBefundPromptOR()
        {
            return @"FACH-MODUL BEFUND: ORTHOPÄDIE

<system_instruktion>
Extrahiere aus dem Transkript ausschließlich durchgeführte körperliche Untersuchungen und deren Ergebnisse.
Oberste Direktive: Generiere keine False Positives. Es gilt striktes Zero-Shot-Verhalten bezüglich medizinischer Befunde. Was nicht explizit im Transkript genannt oder gemessen wurde, existiert nicht.
Explizit erhobene Negativbefunde (z.B. ""Lachman-Test negativ"", ""kein Erguss"") sind reale Untersuchungsergebnisse und werden dokumentiert.
Übernimm genannte Testbezeichnungen wörtlich.
</system_instruktion>

<extraktions_parameter>
Prüfe das Transkript auf Entitäten aus folgenden Kategorien. Diese Liste dient NUR der Definition des Suchraums, nicht als Vorlage für die Ausgabe:
- Vitalparameter (RR, Puls)
- Inspektion (Haltung, Gangbild, Achsfehlstellung, Schwellung, Rötung, Muskelatrophie, Narben)
- Palpation (Druckschmerz mit exakter Lokalisation, Krepitation, Erguss, Überwärmung, Muskelhartspann, Triggerpunkte, myofasziale Tonuserhöhung)
- Bewegungsausmaß / ROM (Neutral-Null-Methode, Endgefühl, Bewegungsschmerz)
- Kraft (Kraftgrade nach Janda 0-5, Seitenvergleich)
- Stabilitätstests (Lachman, vordere Schublade, McMurray, Steinmann, Apley, Neer, Hawkins, Jobe, Aufklappbarkeit, Pivot-Shift – Testbezeichnung und Ergebnis positiv/negativ)
- Neurologie orientierend (Sensibilität, Motorik, Reflexe der betroffenen Extremität)
- Wirbelsäule (Schober-Zeichen, Ott-Zeichen, Finger-Boden-Abstand, Lasegue, Bragard, Federungstest, ISG-Provokation)
</extraktions_parameter>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown.
2. Dynamische Blöcke: Erstelle nur dann einen Block für eine anatomische Region, wenn im Transkript tatsächliche Befunde dazu vorliegen.
3. Struktur pro Block: [Region] [re./li./bds.]: [Einzelbefunde durch Komma getrennt]. Jeden Block mit Punkt abschließen.
4. Vitalparameter: Wenn Vitalparameter extrahiert wurden, müssen diese zwingend als allererster Block unter der Überschrift ""Vitalparameter:"" stehen. Format: [Parameter] [Wert] [Einheit], kommagetrennt.
5. Globaler Fallback: Wenn das Transkript keinerlei klinische Untersuchungsergebnisse enthält, gib exakt und ausschließlich diesen Satz aus: ""Keine Untersuchungsergebnisse dokumentiert.""
</formatierungs_regeln>";
        }

        internal static string GetDefaultDiagnosenPrompt(string fachrichtung = "Allgemeinmedizin", bool includeIcd10 = true)
        {
            if (!includeIcd10)
            {
                return $@"FACH-MODUL DIAGNOSEN: {fachrichtung.ToUpperInvariant()}

<system_instruktion>
Leite aus der dokumentierten Anamnese und dem Befund die wahrscheinlichsten Diagnosen ab.
Oberste Direktive: Nur Diagnosen ableiten, die sich aus den vorliegenden Informationen begründen lassen. Keine spekulativen Diagnosen.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Eine Diagnose pro Zeile.
2. Sortierung: Hauptdiagnose (aktueller Vorstellungsgrund) zuerst, dann Nebendiagnosen nach klinischer Relevanz absteigend.
3. Sicherheitsgrad-Kennzeichnung: Gesicherte Diagnose nur als Text (z.B. ""Gonarthrose re.""). Verdacht mit Präfix ""V.a."" (z.B. ""V.a. Meniskusläsion re.""). Ausschluss mit Präfix ""Ausschluss"", Zustand nach mit Präfix ""Z.n.""
4. Globaler Fallback: Wenn aus dem Gespräch keine Diagnose ableitbar ist, gib exakt aus: ""Keine Diagnose aus den vorliegenden Informationen ableitbar.""
</formatierungs_regeln>";
            }

            string fachPrio = fachrichtung switch
            {
                "Orthopädie" => "Priorisiere M-Codes (Muskel-Skelett) und S/T-Codes (Verletzungen), ergänze Begleitdiagnosen aus anderen Kapiteln.",
                "Neurologie" => "Priorisiere G-Codes (Nervensystem), ergänze I-Codes (zerebrovaskulär), R-Codes (Symptome) und Begleitdiagnosen.",
                "Kardiologie" => "Priorisiere I-Codes (Herz-Kreislauf), insbesondere I05-I09 (rheumatische Klappenfehler), I34-I37 (nichtrheumatische Klappenfehler), I42 (Kardiomyopathien), I50 (Herzinsuffizienz), I31 (Perikarderkrankungen).",
                _ => "Gesamtes ICD-10-Spektrum, häufig Kapitel I-XIV."
            };

            return $@"FACH-MODUL DIAGNOSEN: {fachrichtung.ToUpperInvariant()}

<system_instruktion>
Leite aus der dokumentierten Anamnese und dem Befund die wahrscheinlichsten Diagnosen ab und ordne jeder Diagnose den passenden ICD-10-GM-Code zu.
Oberste Direktive: Nur Diagnosen ableiten, die sich aus den vorliegenden Informationen begründen lassen. Keine spekulativen Diagnosen.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Eine Diagnose pro Zeile gemäß <ausgabe_template>.
2. Sortierung: Hauptdiagnose (aktueller Vorstellungsgrund) zuerst, dann Nebendiagnosen nach klinischer Relevanz absteigend.
3. Diagnosesicherheit: Kennzeichne jede Diagnose gemäß ICD-10-Kodierrichtlinien mit G (gesichert), V (Verdacht), A (Ausschluss) oder Z (Zustand nach).
4. ICD-10-Spezifität: Verwende die höchste sinnvolle Spezifität (4- oder 5-stellig). Ergänze die Seitenkennzeichnung bei paarigen Organen: R (rechts), L (links), B (beidseits).
5. Fachspezifische Priorisierung: {fachPrio}
6. Globaler Fallback: Wenn aus dem Gespräch keine Diagnose ableitbar ist, gib exakt aus: ""Keine Diagnose aus den vorliegenden Informationen ableitbar.""
</formatierungs_regeln>

<ausgabe_template>
[Diagnosetext] - [ICD-10-Code][Sicherheit]
[Diagnosetext] - [ICD-10-Code][Sicherheit]
</ausgabe_template>";
        }

        private static string GetDefaultSectionBe() => GetDefaultBefundPromptAM();
        private static string GetDefaultSectionN() => GetDefaultDiagnosenPrompt("Allgemeinmedizin");
        private static string GetDefaultSectionT() => GetDefaultTherapiePromptAM();

        internal static string GetDefaultTherapiePromptAM()
        {
            return @"FACH-MODUL THERAPIE: ALLGEMEINMEDIZIN

<system_instruktion>
Extrahiere aus dem Transkript alle vom Arzt genannten oder mit dem Patienten besprochenen therapeutischen Maßnahmen, Empfehlungen und Verordnungen.
Dokumentiere nur explizit genannte Maßnahmen. Keine eigenen Therapievorschläge generieren.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown. Stichpunktartige Auflistung, eine Maßnahme pro Zeile.
2. Kategorien (nur aufführen, wenn im Gespräch thematisiert):
   - Medikation (neue Verordnungen, Dosisänderungen, Absetzungen; Format: [Medikament] [Dosierung], [Schema/Dauer])
   - Nicht-medikamentöse Therapie (Physiotherapie, Krankengymnastik, Ergotherapie, Logopädie, Reha)
   - Diagnostik (Überweisung, Labor, Bildgebung, Konsiliaruntersuchung)
   - Hilfsmittel (Orthesen, Bandagen, Einlagen, Gehhilfen)
   - Beratung (Verhaltensempfehlungen, Ernährung, Bewegung, Nikotinkarenz)
   - Arbeitsunfähigkeit (Dauer falls genannt)
   - Wiedervorstellung (Termin/Intervall falls genannt)
3. Globaler Fallback: Wenn keine therapeutischen Maßnahmen im Gespräch dokumentiert sind, gib exakt aus: ""Keine Therapiemaßnahmen dokumentiert.""
</formatierungs_regeln>";
        }

        private static string GetDefaultTherapiePromptOR()
        {
            return @"FACH-MODUL THERAPIE: ORTHOPÄDIE

<system_instruktion>
Extrahiere aus dem Transkript alle vom Arzt genannten oder mit dem Patienten besprochenen therapeutischen Maßnahmen, Empfehlungen und Verordnungen.
Dokumentiere nur explizit genannte Maßnahmen. Keine eigenen Therapievorschläge generieren.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown. Stichpunktartige Auflistung, eine Maßnahme pro Zeile.
2. Kategorien (nur aufführen, wenn im Gespräch thematisiert):
   - Medikation (NSAR, Analgetika, Muskelrelaxantien, Cortison-Infiltrationen; Format: [Medikament] [Dosierung], [Schema/Dauer])
   - Physikalische Therapie (Krankengymnastik, manuelle Therapie, Elektrotherapie, Wärme-/Kältetherapie, Reha)
   - Hilfsmittel (Orthesen, Bandagen, Einlagen, Schienen, Gehhilfen, Pufferabsatz)
   - Operative Maßnahmen (OP-Aufklärung, geplanter Eingriff, Arthroskopie, Gelenkersatz)
   - Diagnostik (Bildgebung, MRT, Röntgen, Überweisung, Konsiliaruntersuchung)
   - Belastungsempfehlung (Sportpause, Teilbelastung, Vollbelastung, Schonung)
   - Arbeitsunfähigkeit (Dauer falls genannt)
   - Wiedervorstellung (Termin/Intervall falls genannt)
3. Globaler Fallback: Wenn keine therapeutischen Maßnahmen im Gespräch dokumentiert sind, gib exakt aus: ""Keine Therapiemaßnahmen dokumentiert.""
</formatierungs_regeln>";
        }

        // -----------------------------------------------------------------------
        // Diktat-Prompts – direkte ärztliche Diktat-Eingabe in eine Karte
        // -----------------------------------------------------------------------

        /// <summary>Gibt den Diktat-Sektions-Prompt zurück: formular- und sektionsspezifisch.
        /// Fallback auf den Allgemeinmedizin-Prompt wenn das Formular unbekannt ist.</summary>
        public static string GetDictationSectionPrompt(string? formId, string section)
        {
            bool isOrthopaedie = string.Equals(formId, "orthopaedie", StringComparison.OrdinalIgnoreCase);

            return section switch
            {
                "A"  => isOrthopaedie ? GetDictationAnamnesePromptOR() : GetDictationAnamnesePromptAM(),
                "Be" => isOrthopaedie ? GetDictationBefundPromptOR()   : GetDictationBefundPromptAM(),
                "T"  => isOrthopaedie ? GetDictationTherapiePromptOR() : GetDictationTherapiePromptAM(),
                "N"  => isOrthopaedie ? GetDictationDiagnosenPromptOR() : GetDictationDiagnosenPromptAM(),
                _    => string.Empty
            };
        }

        private static string GetDictationAnamnesePromptAM()
        {
            return @"DIKTAT-MODUL ANAMNESE: ALLGEMEINMEDIZIN

<system_instruktion>
Ein Arzt diktiert direkt. Strukturiere das Diktat als Anamnese-Eintrag.
Wende Nominalstil oder sehr kurze, objektive Sätze an.
Keine Informationen ergänzen oder erfinden.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown.
2. Platzhalter-Logik: Ersetze [...] durch das Diktierte. Nicht diktierte Felder: ""k. A."". Explizit verneintes: ""keine"" bzw. ""keine bekannt"".
3. Ausnahme Vegetative Anamnese: Wenn nicht diktiert, Kategorie inkl. Überschrift komplett weglassen.
4. Ausnahme Sozialanamnese: Wenn diktiert aber unauffällig: ""Sozialanamnese unauffällig.""
5. Format Dauermedikation: Eine Zeile pro Medikament. Format: [Name] [Dosierung], [Schema X-X-X]. Nur diktierte Angaben – fehlende Dosierung/Schema nicht mit ""k. A."" auffüllen.
6. Format Allergien: [Auslöser] - [Reaktionstyp].
</formatierungs_regeln>

<ausgabe_template>
Aktuell:
[Zusammenfassung: Lokalisation, Charakter, Dauer, Auslöser, zeitlicher Verlauf, Begleitsymptome, Selbstmedikation/Akutbehandlung]
Nebendiagnosen / Vorerkrankungen: [Kommagetrennte Liste: chronische Erkrankungen, relevante Vordiagnosen, Operationen, familiäre Belastung]
Dauermedikation: [Medikament A] [Dosierung], [Schema]; [Medikament B] [Dosierung], [Schema];
Allergien / Unverträglichkeiten: [Auslöser] - [Reaktionstyp]
Vegetative Anamnese: [Angaben zu B-Symptomatik (Fieber, Nachtschweiß, Gewichtsveränderung), Appetit, Schlaf, Miktion, Stuhlgang]
Sozialanamnese: [Beruf inkl. Belastung, sportliche Aktivität, häusliche Situation, Pflegebedarf, Nikotinkonsum, Alkoholkonsum]
</ausgabe_template>";
        }

        private static string GetDictationAnamnesePromptOR()
        {
            return @"DIKTAT-MODUL ANAMNESE: ORTHOPÄDIE

<system_instruktion>
Ein Arzt diktiert direkt. Strukturiere das Diktat als orthopädischen Anamnese-Eintrag.
Wende Nominalstil oder sehr kurze, objektive Sätze an.
Keine Informationen ergänzen oder erfinden.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown.
2. Platzhalter-Logik: Ersetze [...] durch das Diktierte. Nicht diktierte Felder: ""k. A."". Explizit verneintes: ""keine"" bzw. ""keine bekannt"".
3. Ausnahme Vegetative Anamnese: Wenn nicht diktiert, Kategorie inkl. Überschrift komplett weglassen.
4. Ausnahme Sozialanamnese: Wenn diktiert aber unauffällig: ""Sozialanamnese unauffällig.""
5. Format Dauermedikation: Eine Zeile pro Medikament. Format: [Name] [Dosierung], [Schema X-X-X]. Nur diktierte Angaben – fehlende Dosierung/Schema nicht mit ""k. A."" auffüllen.
6. Format Allergien: [Auslöser] - [Reaktionstyp].
</formatierungs_regeln>

<ausgabe_template>
Aktuell:
[Zusammenfassung: Lokalisation, Seitenangabe, Ausstrahlung, Charakter, Auslöser, zeitlicher Verlauf, Begleitsymptome, Selbstmedikation/bisherige Behandlung]
Nebendiagnosen / Vorerkrankungen: [Kommagetrennte Liste: Orthopädische Voroperationen, Frakturen, bekannte degenerative Veränderungen, rheumatologische Grunderkrankungen, sonstige relevante Begleitdiagnosen]
Dauermedikation: [Medikamentenname] [Dosierung], [Schema]; [Medikamentenname] [Dosierung], [Schema];
Allergien / Unverträglichkeiten: [Auslöser] - [Reaktionstyp]
Vegetative Anamnese: [Schlafstörungen durch Schmerzen, B-Symptomatik (Fieber, Nachtschweiß, Gewichtsveränderung)]
Sozialanamnese: [Beruf inkl. körperlicher Belastung, sportliche Aktivität, häusliche Situation, Pflegebedarf, Nikotinkonsum, Alkoholkonsum]
</ausgabe_template>";
        }

        private static string GetDictationBefundPromptAM()
        {
            return @"DIKTAT-MODUL BEFUND: ALLGEMEINMEDIZIN

<system_instruktion>
Ein Arzt diktiert direkt Untersuchungsbefunde. Strukturiere das Diktat als klinischen Befundeintrag.
Oberste Direktive: Nur explizit diktierte Befunde dokumentieren. Nichts ergänzen oder erfinden.
Diktierte Negativbefunde (z.B. ""kein Druckschmerz"") werden dokumentiert.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown.
2. Dynamische Blöcke: Einen Block pro Organsystem, nur wenn tatsächlich diktiert.
3. Struktur pro Block: [Organsystem] [Seitenangabe, falls zutreffend]: [Einzelbefunde durch Komma getrennt]. Jeden Block mit Punkt abschließen.
4. Vitalparameter: Wenn diktiert, als allerersten Block unter ""Vitalparameter:"" ausgeben. Format: [Parameter] [Wert] [Einheit], kommagetrennt.
5. Globaler Fallback: Wenn kein Befund diktiert wurde: ""Keine Untersuchungsergebnisse dokumentiert.""
</formatierungs_regeln>";
        }

        private static string GetDictationBefundPromptOR()
        {
            return @"DIKTAT-MODUL BEFUND: ORTHOPÄDIE

<system_instruktion>
Ein Arzt diktiert direkt orthopädische Untersuchungsbefunde. Strukturiere das Diktat als klinischen Befundeintrag.
Oberste Direktive: Nur explizit diktierte Befunde dokumentieren. Nichts ergänzen oder erfinden.
Diktierte Negativbefunde (z.B. ""Lachman-Test negativ"", ""kein Erguss"") werden dokumentiert. Testbezeichnungen wörtlich übernehmen.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown.
2. Dynamische Blöcke: Einen Block pro anatomischer Region, nur wenn tatsächlich diktiert.
3. Struktur pro Block: [Region] [re./li./bds.]: [Einzelbefunde durch Komma getrennt]. Jeden Block mit Punkt abschließen.
4. Vitalparameter: Wenn diktiert, als allerersten Block unter ""Vitalparameter:"" ausgeben. Format: [Parameter] [Wert] [Einheit], kommagetrennt.
5. Globaler Fallback: Wenn kein Befund diktiert wurde: ""Keine Untersuchungsergebnisse dokumentiert.""
</formatierungs_regeln>";
        }

        private static string GetDictationTherapiePromptAM()
        {
            return @"DIKTAT-MODUL THERAPIE: ALLGEMEINMEDIZIN

<system_instruktion>
Ein Arzt diktiert direkt therapeutische Maßnahmen. Strukturiere das Diktat als Therapie-Eintrag.
Nur explizit diktierte Maßnahmen dokumentieren. Keine eigenen Therapievorschläge generieren.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown. Stichpunktartige Auflistung, eine Maßnahme pro Zeile.
2. Kategorien (nur aufführen, wenn diktiert):
   - Medikation (neue Verordnungen, Dosisänderungen, Absetzungen; Format: [Medikament] [Dosierung], [Schema/Dauer])
   - Nicht-medikamentöse Therapie (Physiotherapie, Krankengymnastik, Ergotherapie, Logopädie, Reha)
   - Diagnostik (Überweisung, Labor, Bildgebung, Konsiliaruntersuchung)
   - Hilfsmittel (Orthesen, Bandagen, Einlagen, Gehhilfen)
   - Beratung (Verhaltensempfehlungen, Ernährung, Bewegung, Nikotinkarenz)
   - Arbeitsunfähigkeit (Dauer falls genannt)
   - Wiedervorstellung (Termin/Intervall falls genannt)
3. Globaler Fallback: Wenn nichts diktiert wurde: ""Keine Therapiemaßnahmen dokumentiert.""
</formatierungs_regeln>";
        }

        private static string GetDictationTherapiePromptOR()
        {
            return @"DIKTAT-MODUL THERAPIE: ORTHOPÄDIE

<system_instruktion>
Ein Arzt diktiert direkt orthopädische Therapiemaßnahmen. Strukturiere das Diktat als Therapie-Eintrag.
Nur explizit diktierte Maßnahmen dokumentieren. Keine eigenen Therapievorschläge generieren.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Reiner Text, kein Markdown. Stichpunktartige Auflistung, eine Maßnahme pro Zeile.
2. Kategorien (nur aufführen, wenn diktiert):
   - Medikation (NSAR, Analgetika, Muskelrelaxantien, Cortison-Infiltrationen; Format: [Medikament] [Dosierung], [Schema/Dauer])
   - Physikalische Therapie (Krankengymnastik, manuelle Therapie, Elektrotherapie, Wärme-/Kältetherapie, Reha)
   - Hilfsmittel (Orthesen, Bandagen, Einlagen, Schienen, Gehhilfen, Pufferabsatz)
   - Operative Maßnahmen (OP-Aufklärung, geplanter Eingriff, Arthroskopie, Gelenkersatz)
   - Diagnostik (Bildgebung, MRT, Röntgen, Überweisung, Konsiliaruntersuchung)
   - Belastungsempfehlung (Sportpause, Teilbelastung, Vollbelastung, Schonung)
   - Arbeitsunfähigkeit (Dauer falls genannt)
   - Wiedervorstellung (Termin/Intervall falls genannt)
3. Globaler Fallback: Wenn nichts diktiert wurde: ""Keine Therapiemaßnahmen dokumentiert.""
</formatierungs_regeln>";
        }

        private static string GetDictationDiagnosenPromptAM()
        {
            string fachPrio = "Gesamtes ICD-10-Spektrum, häufig Kapitel I-XIV.";
            return $@"DIKTAT-MODUL DIAGNOSEN: ALLGEMEINMEDIZIN

<system_instruktion>
Ein Arzt diktiert direkt Diagnosen. Strukturiere das Diktat als Diagnosen-Eintrag mit ICD-10-Codierung.
Nur explizit diktierte Diagnosen dokumentieren. Keine spekulativen Diagnosen ergänzen.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Eine Diagnose pro Zeile gemäß <ausgabe_template>.
2. Sortierung: Hauptdiagnose zuerst, dann Nebendiagnosen nach klinischer Relevanz absteigend.
3. Diagnosesicherheit: Kennzeichne gemäß ICD-10-Kodierrichtlinien mit G (gesichert), V (Verdacht), A (Ausschluss) oder Z (Zustand nach).
4. ICD-10-Spezifität: Höchste sinnvolle Spezifität (4- oder 5-stellig). Seitenkennzeichnung: R (rechts), L (links), B (beidseits).
5. Fachspezifische Priorisierung: {fachPrio}
6. Globaler Fallback: Wenn keine Diagnose diktiert: ""Keine Diagnose aus den vorliegenden Informationen ableitbar.""
</formatierungs_regeln>

<ausgabe_template>
[Diagnosetext] - [ICD-10-Code][Sicherheit]
[Diagnosetext] - [ICD-10-Code][Sicherheit]
</ausgabe_template>";
        }

        private static string GetDictationDiagnosenPromptOR()
        {
            string fachPrio = "Priorisiere M-Codes (Muskel-Skelett) und S/T-Codes (Verletzungen), ergänze Begleitdiagnosen aus anderen Kapiteln.";
            return $@"DIKTAT-MODUL DIAGNOSEN: ORTHOPÄDIE

<system_instruktion>
Ein Arzt diktiert direkt orthopädische Diagnosen. Strukturiere das Diktat als Diagnosen-Eintrag mit ICD-10-Codierung.
Nur explizit diktierte Diagnosen dokumentieren. Keine spekulativen Diagnosen ergänzen.
</system_instruktion>

<formatierungs_regeln>
1. Ausgabe-Format: Eine Diagnose pro Zeile gemäß <ausgabe_template>.
2. Sortierung: Hauptdiagnose zuerst, dann Nebendiagnosen nach klinischer Relevanz absteigend.
3. Diagnosesicherheit: Kennzeichne gemäß ICD-10-Kodierrichtlinien mit G (gesichert), V (Verdacht), A (Ausschluss) oder Z (Zustand nach).
4. ICD-10-Spezifität: Höchste sinnvolle Spezifität (4- oder 5-stellig). Seitenkennzeichnung: R (rechts), L (links), B (beidseits).
5. Fachspezifische Priorisierung: {fachPrio}
6. Globaler Fallback: Wenn keine Diagnose diktiert: ""Keine Diagnose aus den vorliegenden Informationen ableitbar.""
</formatierungs_regeln>

<ausgabe_template>
[Diagnosetext] - [ICD-10-Code][Sicherheit]
[Diagnosetext] - [ICD-10-Code][Sicherheit]
</ausgabe_template>";
        }

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

Format: Reiner Text, kein Markdown. Alle Abschnitte in Reintext ohne Formatierungszeichen ausgeben.

Sprache: Nutze durchgehend ärztliche Fachsprache. Verwende gängige medizinische Abkürzungen: re. (rechts), li. (links), bds. (beidseits), o.B. (ohne Befund), Z.n. (Zustand nach), V.a. (Verdacht auf), ED (Erstdiagnose), DD (Differentialdiagnose), ggf. (gegebenenfalls), bzgl. (bezüglich), ca. (circa), Pat. (Patient/in).

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
                    N = GetDefaultDiagnosenPrompt("Allgemeinmedizin", includeIcd10: true),
                    T = GetDefaultTherapiePromptAM(),
                    Icd10 = string.Empty,
                    PromptVersion = CurrentPromptVersion
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
                    N = GetDefaultDiagnosenPrompt("Orthopädie", includeIcd10: true),
                    T = GetDefaultTherapiePromptOR(),
                    Icd10 = string.Empty,
                    PromptVersion = CurrentPromptVersion
                }
            };
        }

        /// <summary>Für app-config.json: Universal-Prompt mit Standard/User-Trennung.</summary>
        private class AppConfig
        {
            public string UniversalPrompt { get; set; } = string.Empty;
            public bool UniversalPromptCustomized { get; set; }
            public int UniversalPromptVersion { get; set; }
        }
    }
}
