using System.Collections.Generic;

namespace ABeNT.Model
{
    /// <summary>
    /// Root config for output forms (Fachrichtungen) and universal prompt.
    /// Stored in AppData/ABeNT/output-forms.json.
    /// </summary>
    public class OutputFormsConfig
    {
        /// <summary>Base instructions applied to all forms. Editable only with double confirmation.</summary>
        public string UniversalPrompt { get; set; } = string.Empty;

        /// <summary>List of subject-specific forms (e.g. Orthopädie).</summary>
        public List<SubjectForm> Forms { get; set; } = new List<SubjectForm>();
    }

    /// <summary>
    /// One output form (Fachrichtung) with per-ABeNT-section prompts.
    /// </summary>
    public class SubjectForm
    {
        /// <summary>Unique id, e.g. "orthopaedie".</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Display name, e.g. "Orthopädie".</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Optional description.</summary>
        public string? Description { get; set; }

        /// <summary>Per-section prompt texts for ABeNT (and optional ICD-10).</summary>
        public AbentSectionPrompts SectionPrompts { get; set; } = new AbentSectionPrompts();
    }

    /// <summary>
    /// Prompt text for each ABeNT section. Empty string = use built-in default when building the full prompt.
    /// </summary>
    public class AbentSectionPrompts
    {
        /// <summary>Anamnese (A).</summary>
        public string A { get; set; } = string.Empty;

        /// <summary>Befund (Be).</summary>
        public string Be { get; set; } = string.Empty;

        /// <summary>Diagnose / Name (N).</summary>
        public string N { get; set; } = string.Empty;

        /// <summary>Therapie (T).</summary>
        public string T { get; set; } = string.Empty;

        /// <summary>ICD-10 (optional section).</summary>
        public string Icd10 { get; set; } = string.Empty;
    }
}
