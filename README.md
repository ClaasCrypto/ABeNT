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

- **API-Keys:** In der Anwendung unter **Einstellungen** Deepgram- und LLM-API-Keys eintragen (z. B. Claude, ChatGPT, Gemini). Die Keys werden nur unter `%AppData%\ABeNT` gespeichert, nicht im Projektordner.
- **Berichtsformulare:** Es stehen u. a. Vorlagen für Allgemeinmedizin und Orthopädie zur Verfügung; unter „Berichtsformulare verwalten“ können Formulare angepasst oder wiederhergestellt werden.

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
