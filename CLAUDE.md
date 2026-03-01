# CLAUDE.md

This file provides guidance to Claude Code (and other AI assistants) when working with the **ABeNT** codebase.

## Project Overview

**ABeNT** (Aufzeichnung und KI-gestützte Auswertung von Arzt-Patienten-Gesprächen zu Berichten) is a **Windows desktop application** that records doctor–patient conversations, transcribes them via Deepgram, and generates structured medical reports using LLMs (Claude, ChatGPT, Gemini).

- **Framework:** .NET 8, WPF (Windows-only)
- **Language:** C# with nullable reference types enabled
- **License:** MIT

## Build & Run

```bash
# Build
dotnet build

# Run
dotnet run

# Or launch the built executable directly
bin\Debug\net8.0-windows\ABeNT.exe
```

There are **no tests** in this project currently. There is a single `.csproj` file — no solution file.

## Architecture

The project follows an **MVVM-inspired** structure:

```
ABeNT/
├── Model/                  # Data models / POCOs
│   ├── BaseModel.cs        # INotifyPropertyChanged base class
│   ├── OutputFormsConfig.cs# Report form configuration model
│   ├── RecorderReportOptions.cs # Options passed to recorder for LLM report generation
│   └── TranscriptSegment.cs# Single transcript segment (speaker + text)
├── ViewModel/
│   └── MainViewModel.cs    # Main window view model
├── Services/               # Business logic / external API integrations
│   ├── AudioService.cs     # Microphone recording & audio processing (NAudio)
│   ├── DeepgramService.cs  # Speech-to-text via Deepgram REST API
│   ├── LlmService.cs       # LLM report generation (Claude, ChatGPT, Gemini)
│   ├── OutputFormsService.cs # Report form templates (CRUD, defaults, prompts)
│   └── SettingsService.cs  # App settings persistence (%AppData%\ABeNT)
├── MainWindow.xaml/.cs     # Primary dashboard window
├── RecorderWindow.xaml/.cs # Audio recording window
├── OutputFormsWindow.xaml/.cs # Report form management window
├── SettingsWindow.xaml/.cs # API key & preferences window
├── UniversalPromptWindow.xaml/.cs # Universal prompt editor
├── App.xaml/.cs            # Application entry point, global error handling
├── ABeNT.csproj            # Project file
└── app.manifest            # Windows app manifest
```

## Key Dependencies (NuGet)

| Package | Version | Purpose |
|---------|---------|---------|
| NAudio | 2.2.1 | Audio capture and processing |
| Newtonsoft.Json | 13.0.3 | JSON serialization/deserialization |
| Microsoft.Extensions.Configuration | 8.0.0 | Configuration support |

## Coding Conventions

### Language & Comments
- **Code comments and UI text are in German.** Follow this convention when adding new comments, labels, or user-facing strings.
- XML doc comments (`/// <summary>`) are used on public classes and key methods — continue this practice.

### Code Style
- Use **C# 12** features (file-scoped namespaces are *not* used — the project uses block-scoped `namespace ABeNT { ... }`).
- Use `string.Empty` instead of `""` for default string values.
- Use `var` where the type is obvious; explicit types otherwise.
- Async methods end with `Async` suffix (e.g., `TranscribeAudioAsync`, `GenerateAbentReportAsync`).
- Private fields use `_camelCase` prefix convention.
- Event handlers follow `ElementName_EventName` naming (e.g., `BtnSettings_Click`, `CmbSubjectForm_SelectionChanged`).

### Pattern Conventions
- **Services** are classes with their own `Dispose()` method; they manage `HttpClient` instances internally.
- **Models** that need property change notifications inherit from `BaseModel` (`INotifyPropertyChanged`).
- **XAML code-behind** handles UI events directly — there is no strict command/binding pattern in the code-behind files.
- HTTP calls to external APIs (Deepgram, LLMs) use `HttpClient` directly — no SDK wrappers.

### Error Handling
- Global unhandled exception handlers are registered in `App.xaml.cs`.
- Service methods typically use try/catch with `MessageBox.Show()` for user-facing errors.
- Settings loading falls back to defaults on any deserialization failure.

## External Services & Secrets

The app integrates with three external APIs. **API keys are never stored in the repository** — they live in `%AppData%\ABeNT\settings.json`.

| Service | Purpose | Key field in `AppSettings` |
|---------|---------|--------------------------|
| Deepgram | Audio transcription (speech-to-text) | `DeepgramApiKey` |
| OpenAI (ChatGPT) | LLM report generation | `OpenAiApiKey` |
| Anthropic (Claude) | LLM report generation | `ClaudeApiKey` |
| Google (Gemini) | LLM report generation | `GeminiApiKey` |

> **Important:** Never hardcode API keys. Always use `SettingsService.LoadSettings()` to retrieve them at runtime.

## Data & File Storage

All persistent data is stored under `%AppData%\ABeNT\`:

| Path | Content |
|------|---------|
| `settings.json` | API keys, preferences, last-selected options |
| `Forms\` | Individual report form JSON files |
| `forms-manifest.json` | Ordered list of available report forms |
| `app-config.json` | Universal prompt configuration |
| `recordings\` | Saved documentation/recording data |

Standard report forms (Allgemeinmedizin, Orthopädie) can be restored to code-defined defaults via `OutputFormsService.RestoreDefaultForm()`.

## Common Tasks

### Adding a new LLM provider
1. Add API key property to `AppSettings` and `RecorderReportOptions`.
2. Add a new `Call<Provider>Async()` + `Parse<Provider>Response()` method pair in `LlmService.cs`.
3. Extend the `switch` in `GenerateAbentReportAsync()` and `RecorderReportOptions.GetLlmApiKey()`.
4. Add the option in `SettingsWindow.xaml` and update the ComboBox items.

### Adding a new report section
1. Define the section content helper in `OutputFormsService.cs` (e.g., `GetDefaultSection<X>()`).
2. Add corresponding UI elements in `MainWindow.xaml`.
3. Update `ParseAndDisplayResult()` in `MainWindow.xaml.cs` to handle the new section.
4. Update the system prompt in `LlmService.BuildSystemPrompt()` or `OutputFormsService.BuildSystemPromptFromConfig()`.

### Adding a new window
1. Create `<WindowName>.xaml` and `<WindowName>.xaml.cs` in the project root.
2. Open it from the relevant parent window using `new <WindowName>().ShowDialog()`.

## Design Language

ABeNT follows a **Bauhaus / geometric** design aesthetic — bold shapes, vibrant accent colors, and clean typography on a warm background.

### Visual Identity
- **Style:** Bold geometric illustrations (circles, triangles, rectangles), overlapping shapes, vivid color blocks.
- **Layout:** Clean sections with generous whitespace, alternating left/right content–illustration arrangements.
- **Buttons:** Rounded-border outlined buttons (not filled), simple hover states.
- **Typography:** Clean sans-serif font, `18px` base size, `32px` line-height, `font-weight: 400`.

### Color Palette

| Token | Value | Usage |
|-------|-------|-------|
| Background | `#FFF9F0` (`rgba(255,249,240)`) | Page / window background — warm off-white |
| Text | `#000609` (`rgba(0,6,9)`) | Primary text — near-black |
| Blue | bright blue (primary accent) | Primary CTA, links, key shapes |
| Coral / Red-Orange | vivid coral | Secondary accent, geometric shapes |
| Yellow | bright yellow | Tertiary accent, highlights |
| Pink | soft pink | Supplementary accent |
| Black | `#000000` | Headlines, borders, geometric fills |
| White | `#FFFFFF` | Cards, contrast areas |

### Spacing & Layout Tokens (Responsive)

```css
/* Paragraph spacing */
--para-spacing-xs: 16px;
--para-spacing-sm: 16px;
--para-spacing-lg: 24px;

/* Header horizontal margin */
--header-x-margin-xs: 16px;
--header-x-margin-sm: 16px;
--header-x-margin-md: 32px;
--header-x-margin-lg: 40px;
--header-x-margin-xl: 40px;

/* Header top padding */
--header-top-padding-xs: 26px;
--header-top-padding-sm: 42px;
--header-top-padding-md: 44px;
--header-top-padding-lg: 44px;
--header-top-padding-xl: 44px;

/* Content horizontal margin */
--content-x-margin-xs: 0px;
--content-x-margin-sm: 60px;
--content-x-margin-md: 80px;
--content-x-margin-lg: 104px;
--content-x-margin-xl: 104px;

/* Content vertical padding */
--content-top-padding-xs: 88px;
--content-top-padding-sm: 120px;
--content-top-padding-md: 120px;
--content-top-padding-lg: 120px;
--content-top-padding-xl: 120px;
--content-bottom-padding-xs: 120px;
--content-bottom-padding-sm: 120px;
--content-bottom-padding-md: 120px;
--content-bottom-padding-lg: 120px;
--content-bottom-padding-xl: 120px;

/* Container sizing */
--container-min-width-sm: 624px;
--container-min-width-md: 800px;
--container-min-width-lg: 960px;
--container-min-width-xl: 1680px;
--container-max-width-lg: 1680px;
--container-max-width-xl: 1680px;
--container-x-margin-xs: 16px;
--container-x-margin-sm: 12px;
--container-x-margin-md: 16px;
--container-x-margin-lg: 16px;
--container-x-margin-xl: auto;
```

### Base Element Styles

```css
font-family: inherit;
font-size: 18px;
line-height: 32px;
font-weight: 400;
-webkit-font-smoothing: antialiased;
background-color: #FFF9F0;
color: #000609;
box-sizing: border-box;
```

### Design Principles
- When building any UI (web or WPF), use the **warm off-white background** (`#FFF9F0`) — never pure white.
- Use **geometric accent shapes** (circles, half-circles, triangles) as decorative elements.
- Keep typography **clean and bold** — large headings, generous line spacing.
- Use the **accent colors** (blue, coral, yellow, pink) for illustrations and highlights, not for large background fills (except footer areas, which may use solid blue).
- Maintain **generous whitespace** — the spacing tokens above define the rhythm.

## Things to Avoid

- **Do not** add `CLAUDE.md` or any AI config files to `.gitignore` unless explicitly asked — they are intended to be committed.
- **Do not** switch to SDK-based API clients (e.g., OpenAI SDK) without explicit approval — the project uses raw `HttpClient` calls intentionally.
- **Do not** refactor to file-scoped namespaces — the project consistently uses block-scoped namespaces.
- **Do not** replace `Newtonsoft.Json` with `System.Text.Json` without explicit approval.
- **Do not** store any secrets or API keys in source files.
- **Do not** use pure white (`#FFFFFF`) as a page background — always use the warm off-white (`#FFF9F0`).
