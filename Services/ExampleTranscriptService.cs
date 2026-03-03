using System.IO;

namespace ABeNT.Services
{
    /// <summary>
    /// Provides the default example transcript for prompt testing and supports loading from file.
    /// </summary>
    public static class ExampleTranscriptService
    {
        /// <summary>
        /// Default example transcript: male patient, 86 years, back pain, knee TEP right,
        /// Parkinson, diabetes mellitus, obesity, elevated blood pressure. Format: "Sprecher: Text" per block.
        /// </summary>
        public static string GetDefaultTranscript()
        {
            return @"Arzt: Guten Tag, Herr Müller. Wie geht es Ihnen heute?

Patient: Guten Tag. Also, mit dem Rücken geht's wieder los. Seit etwa zwei Wochen, vor allem morgens beim Aufstehen und wenn ich länger sitze.

Arzt: Wo genau tut es weh – eher unten im Lendenbereich oder höher?

Patient: So im unteren Rücken, und manchmal strahlt es ein bisschen ins rechte Bein runter. Nicht so stark wie damals vor der Knie-OP.

Arzt: Verstehe. Das rechte Knie – wie ist das seit der TEP? Läuft das stabil?

Patient: Das Knie ist eigentlich gut. Ich humple kaum noch, Treppen gehen besser. Nur beim Wetterumschwung spüre ich es manchmal.

Arzt: Gut. Nehmen Sie Ihre Medikamente wie besprochen – also für den Parkinson und den Diabetes?

Patient: Ja, die nehme ich. Metformin morgens und abends, und die Parkinson-Tabletten wie Sie es eingestellt haben. Den Blutdruck messe ich zu Hause, der ist oft so um 150 zu 88.

Arzt: 150 zu 88 – das ist etwas hoch. Messen Sie regelmäßig, immer zur gleichen Zeit?

Patient: Meistens morgens nach dem Aufstehen. Manchmal vergesse ich's. Beim letzten Mal in der Apotheke war er 148 zu 85.

Arzt: Notieren Sie die Werte am besten in einem Heft, dann können wir beim nächsten Mal genauer schauen. Wie steht es mit dem Gewicht – haben Sie versucht, etwas abzunehmen?

Patient: Ich wiege mich nicht jeden Tag, aber ungefähr gleich. Ich weiß, ich bin zu schwer. Ich bewege mich schon, spazieren mit dem Rollator, aber das rechte Knie und der Rücken bremsen mich.

Arzt: Das verstehe ich. Haben Sie in letzter Zeit Stürze gehabt oder Unsicherheit beim Gehen?

Patient: Einmal bin ich im Bad fast ausgerutscht, aber nicht hingefallen. Sonst fühle ich mich beim Gehen manchmal etwas steif, besonders wenn die Parkinson-Medikamente nachlassen.

Arzt: Wir schauen uns gleich den Rücken und die Beweglichkeit an. Machen Sie bitte den Oberkörper frei, dann teste ich die Wirbelsäule und die Beinreflexe.

Patient: In Ordnung.

Arzt: Bitte einmal nach vorne beugen – soweit es geht. Tut das weh? Gut. Jetzt aufrichten. Ich taste jetzt den Lendenbereich ab. Hier neben der Wirbelsäule – ist das der schmerzhafte Punkt? Ja? Und das rechte Knie – ich prüfe die Beweglichkeit. Beugung und Streckung in Ordnung, keine Schwellung. Puls an den Füßen ist beidseits gut tastbar.

Patient: Passt.

Arzt: Zusammenfassung: Wir behalten Ihre bisherigen Medikamente bei. Für den Rücken empfehle ich leichte Mobilisation und Wärme. Wenn die Schmerzen zunehmen oder Sie Taubheit oder Kribbeln im Bein spüren, bitte bald wieder melden. Blutdruck weiter messen und Werte notieren. Beim nächsten Termin besprechen wir, ob wir die Blutdrucktherapie anpassen. Passt das so für Sie?

Patient: Ja, passt. Danke.";
        }

        /// <summary>
        /// Load transcript text from a file. Returns null if file cannot be read.
        /// </summary>
        public static string? LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;
            try
            {
                return File.ReadAllText(filePath);
            }
            catch
            {
                return null;
            }
        }
    }
}
