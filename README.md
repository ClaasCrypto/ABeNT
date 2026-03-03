# ABeNT

ABeNT – Aufzeichnung und KI-gestützte Auswertung von Arzt-Patienten-Gesprächen zu Berichten.

## Anforderungen

- **.NET 8 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Windows** (WPF-Anwendung)

## Build & Start

```bash
dotnet build
dotnet run
```

Alternativ nach dem Build: `bin\Debug\net8.0-windows\ABeNT.exe` starten.

## Ersteinrichtung

### API-Keys

ABeNT benötigt **zwei Arten von API-Zugängen**: einen Anbieter für **Sprache-zu-Text (STT)** und einen für die **Berichtserstellung (LLM)**. Es wird jeweils genau ein Anbieter pro Kategorie genutzt. Die Keys werden nur unter `%AppData%\ABeNT` gespeichert, nicht im Projektordner.

**Sprache-zu-Text (einer nötig):**

| Anbieter | Beschreibung | API-Key / Anleitung |
|----------|--------------|---------------------|
| **Deepgram** | STT per Cloud-API | [Deepgram Console – API Key erstellen](https://console.deepgram.com/) |
| **Azure Speech Service** | Microsoft Speech-to-Text (Key + Region) | [Azure Portal – Speech-Ressource anlegen](https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices). Region z. B. `westeurope` oder `germanywestcentral` (aus der URL `https://germanywestcentral.api.cognitive.microsoft.com/` nur den Teil vor `.api…` eintragen). |
| **Custom API** | Eigener STT-Endpoint | Endpoint-URL und optional API-Key in den Einstellungen eintragen. |

**Berichtserstellung / LLM (einer nötig):**

| Anbieter | Beschreibung | API-Key / Anleitung |
|----------|--------------|---------------------|
| **ChatGPT (OpenAI)** | GPT-Modelle für Berichtstext | [OpenAI API Keys](https://platform.openai.com/api-keys) |
| **Gemini (Google)** | Google LLM | [Google AI Studio – API Key](https://aistudio.google.com/apikey) |
| **Claude (Anthropic)** | Claude-Modelle | [Anthropic Console – API Keys](https://console.anthropic.com/) |
| **Mistral (Mistral AI)** | Mistral Large u. a. | [Mistral AI Console – API Keys](https://console.mistral.ai/) |

**Hinweis:** Keys nur in den Einstellungen der Anwendung eintragen. Keine API-Keys im Projektordner oder Repository speichern.

### Berichtsformulare

Es stehen u. a. Vorlagen für Allgemeinmedizin und Orthopädie zur Verfügung; unter „Berichtsformulare verwalten“ können Formulare angepasst oder wiederhergestellt werden.

## Repository auf GitHub veröffentlichen

Falls noch nicht geschehen: Git installieren, dann im Projektordner:

```bash
git init
git add .
git commit -m "Initial commit"
```

Auf GitHub ein neues Repository anlegen (ohne README/Lizenz/.gitignore). Dann:

```bash
git remote add origin https://github.com/DEIN-USERNAME/ABeNT.git
git branch -M main
git push -u origin main
```

Für versionierte Releases: `git tag v0.1.0` und `git push --tags`.

## Lizenz

Siehe [LICENSE](LICENSE).
