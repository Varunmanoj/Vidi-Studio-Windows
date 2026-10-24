# Windows architecture

## Layers

| macOS implementation | Windows implementation |
| --- | --- |
| SwiftUI app and windows | WinUI 3 `App`, `MainWindow`, native Settings window and export dialog |
| `StudioModel` | `ViewModels/StudioViewModel`, `Models/StudioProject`, `Models/Segment` |
| Process invocation / settings | `Services/EngineClient`, `SettingsStore` |
| Keychain credential store | `Services/CredentialStore` (Windows Credential Manager) |
| AVKit player | `MediaPlayerElement` / `MediaPlayer` |
| YouTube WebKit player | WebView2 hosting only the YouTube player |
| Apple voice catalog / speech | `SpeechHost`: Windows.Media.SpeechSynthesis, SAPI COM, and bundled Piper runtime |
| Summary readback | `Services/SummaryReadback`, selected speech provider (including Piper), temporary audio |
| Python engine | Preserved `engine/vidistudio_engine.py`, `parity.py`, `gemini_adapter.py` |
| macOS share extension | Windows activation integration still outstanding |

The editor is native, not an embedded web application. WebView2 is limited to URL-backed video playback. Google API access stays in the Python engine. Neither the UI nor the speech helper receives a Google SDK dependency.

Settings uses its own WinUI window so native title-bar controls and theme colors follow the selected appearance. Sidebar sections include separate Help and About pages. FFmpeg and FFprobe paths are editable or chosen with a Windows executable picker. About reads package version and probes the configured FFmpeg executable instead of assuming a bundled version or license.

## Process contract

`EngineClient` invokes either `engine-bin/vidistudio-engine.exe` or the configured Python with the copied engine script. Arguments use `ProcessStartInfo.ArgumentList`, never shell interpolation. JSON line events on stdout supply status, progress, project paths, OCR results, and model catalogs. stderr and error events are diagnostics. An engine semaphore serializes work within each window; the engine's OS project lock prevents concurrent writers across processes.

Config path and speech helper path are passed through `VIDISTUDIO_CONFIG` and `VIDISTUDIO_SPEECH_HOST`. Settings are outside projects. Source engine execution sets `PYTHONUTF8=1` for multilingual project data.

VidiStudio operates on a Bring Your Own Key (BYOK) architecture. Users provide their own personal Google Gemini API key directly from Google AI Studio. Configuring this key in Settings is an enforced prerequisite before any new local or YouTube projects can be created. The Gemini API key is owned by Windows Credential Manager (`Services/CredentialStore`), never written to `config.json` or project files, and handed to the engine per process through `VIDISTUDIO_API_KEY`. This keeps the key out of the config file, command lines, logs, and project JSON, and lets it survive app updates and reinstalls. macOS stores the same key in the login Keychain through `KeychainStore`.

The speech helper accepts exactly one JSON request on stdin and returns JSON on stdout. `voices` enumerates Core and SAPI voices; Piper voices are enumerated by the app from downloaded models. `synthesize` writes a WAV. Core uses WinRT; SAPI uses a synchronous STA COM host with explicit voice token selection and plain-text flags. SAPI speech is spoken asynchronously with `WaitUntilDone` (two-minute limit), and output of 64 bytes or less is rejected, so a stalled or silent voice fails clearly. COM errors are reported as a plain first line plus a technical second line; the app shows only the first line (`SpeechService.FriendlyMessage`). The earlier `windows-natural` engine name is accepted everywhere as SAPI 5 and migrated in settings. Vidi Studio never extracts or uses Narrator model keys itself. The engine applies pitch and volume once during audio normalization and rejects narration that overruns its cue. Provider identity forms part of the narration signature so changed-voice regeneration detects provider changes.

Piper is the bundled offline neural engine. `Services/PiperService` parses the packaged `Assets/piper-voices.json` catalog, enumerates downloaded models under `%APPDATA%\VidiStudio\piper\voices`, and downloads only the selected model and config from the public Rhasspy Piper collection on Hugging Face after verifying the published size and MD5 checksum. The catalog keeps the plain-English language name and dialect (`English (United States)`) alongside the regional code, so the voice browser shows readable labels instead of `en-US`. `Views/PiperVoicesDialog` filters the catalog by language family and quality, sorts by language, name, quality, or size, and can preview the short `samples/speaker_0.mp3` published beside each model. Samples download on demand into `%APPDATA%\VidiStudio\piper\samples` and are removed with the voice; playing a sample needs the internet, while an installed voice synthesizes offline. `SpeechHost` runs the bundled `piper.exe` with `--model`, `--config`, `--output_file`, and `--length_scale` (the inverse of speaking rate), writes the narration text to stdin as UTF-8, and waits with a timeout. The runtime is x64 and runs on ARM64 Windows through emulation; it is fetched once from the verified official Rhasspy release at build time. The Python engine resolves `VIDISTUDIO_PIPER_PATH` and `VIDISTUDIO_PIPER_VOICES`, routes `piper` synthesis through `SpeechHost`, and includes the voice in the narration signature. Piper serves voice samples, saved narration, live preview, and summary read-aloud; narration text never leaves the PC.

Azure Voices and the NaturalVoiceSAPIAdapter promotion were removed from both platforms. Settings saved with `ttsMode=azure` migrate to Windows OneCore on Windows and Apple Voices on macOS; saved `windows-natural` settings migrate to Windows SAPI 5. The legacy Azure credential slots and engine modules are no longer present, and the summary always uses the narration voice.

The Gemini settings page requests model and voice catalogs through engine commands. Separate model keys cover local and YouTube stages; the engine chooses source-specific keys when processing a YouTube project. The UI displays friendly voice names while storing provider IDs in settings. The catalog requires the user's key and network access, and the featured Gemini voices remain selectable if listing fails.

The Export dialog maps Video, Audio, Text, and Web Playback selections to engine commands and arguments. It offers only text and narration-only audio for YouTube URL projects. The engine also supports an unchanged local-video copy for the no-caption, original-audio choice; it verifies the copied video stream before committing the export.

## Models and compatibility

The C# models read the portable JSON schema and preserve unknown segment metadata when editing. The UI does not rewrite `project.json` directly. All mutations go through engine commands, including save-segments, histories, deletion, range generation, and exports.

`StudioProject.Resolve` rejects rooted or escaping project paths. Engine validation remains authoritative. User selections are retained as voice IDs rather than display labels.

## Build assets

The app project copies engine source and defaults into `engine/`, architecture-matched speech files into `speech/`, and an optional frozen engine into `engine-bin/`. `script/setup-windows.ps1` prepares those runtime directories; `script/build-windows.ps1` locates Visual Studio MSBuild. Runtime binaries and Python environments are ignored by Git. The copied Mac source under `reference/macOS` is an ignored local reference, not a shipped dependency.

Windows app version: 1.0.109 (manifest 1.0.109.0), synchronized with the Mac project.

## Export metadata parity limit
The Mac application additionally runs AppleAccessibilityMetadataWriter on closed-description MOV output. It writes AVFoundation tagged characteristics and alternate-group metadata, then repairs handler names. Windows now writes the documented track-level udta/tagc characteristics with engine/mov_accessibility.py, groups audio and subtitle alternatives, and retains FFmpeg handler names/default selections. It appends an updated movie header to a temporary export and replaces the original header with free space, preserving encoded media and chunk offsets. This leaves the movie header at the end of the file. Engine validation runs before committing the export. Specification: https://developer.apple.com/documentation/quicktime-file-format/user_data_atoms . Atom contents and actual FFmpeg/frozen-engine exports are verified; recognition by an Apple player still requires Mac-side validation.
