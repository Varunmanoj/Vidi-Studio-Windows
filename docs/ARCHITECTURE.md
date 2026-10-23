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
| Apple voice catalog / speech | `SpeechHost`: Windows.Media.SpeechSynthesis and SAPI COM |
| Summary readback | `Services/SummaryReadback`, selected speech provider, temporary audio |
| Python engine | Preserved `engine/vidistudio_engine.py`, `parity.py`, `gemini_adapter.py` |
| macOS share extension | Windows activation integration still outstanding |

The editor is native, not an embedded web application. WebView2 is limited to URL-backed video playback. Google API access stays in the Python engine. Neither the UI nor the speech helper receives a Google SDK dependency.

Settings uses its own WinUI window so native title-bar controls and theme colors follow the selected appearance. Sidebar sections include separate Help and About pages. FFmpeg and FFprobe paths are editable or chosen with a Windows executable picker. About reads package version and probes the configured FFmpeg executable instead of assuming a bundled version or license.

## Process contract

`EngineClient` invokes either `engine-bin/vidistudio-engine.exe` or the configured Python with the copied engine script. Arguments use `ProcessStartInfo.ArgumentList`, never shell interpolation. JSON line events on stdout supply status, progress, project paths, OCR results, and model catalogs. stderr and error events are diagnostics. An engine semaphore serializes work within each window; the engine's OS project lock prevents concurrent writers across processes.

Config path and speech helper path are passed through `VIDISTUDIO_CONFIG` and `VIDISTUDIO_SPEECH_HOST`. Settings are outside projects. Source engine execution sets `PYTHONUTF8=1` for multilingual project data.

VidiStudio operates on a Bring Your Own Key (BYOK) architecture. Users provide their own personal Google Gemini API key directly from Google AI Studio. Configuring this key in Settings is an enforced prerequisite before any new local or YouTube projects can be created. The Gemini API key is owned by Windows Credential Manager (`Services/CredentialStore`), never written to `config.json` or project files, and handed to the engine per process through `VIDISTUDIO_API_KEY`. This keeps the key out of the config file, command lines, logs, and project JSON, and lets it survive app updates and reinstalls. macOS stores the same key in the login Keychain through `KeychainStore`.

The speech helper accepts exactly one JSON request on stdin and returns JSON on stdout. `voices` enumerates provider voices; `synthesize` writes a WAV. Core uses WinRT; SAPI uses a synchronous STA COM host with explicit voice token selection and plain-text flags. The `windows-natural` engine lists Narrator/Magnifier natural voice packages (`MicrosoftWindows.Voice.<locale>.<speaker>.<n>`, found through `PackageManager` with a package-repository registry fallback) and matches each to a SAPI 5 bridge token by package name, or by speaker and locale; unmatched packages are returned with `Available=false` and an `natural-package:` ID that synthesis refuses with a plain explanation. Bridged online tokens are returned in a separate `bridge-online` group. Synthesis for natural voices reuses the SAPI path. Vidi Studio never extracts or uses Narrator model keys itself. The engine applies pitch and volume once during audio normalization and rejects narration that overruns its cue. Provider identity forms part of the narration signature so changed-voice regeneration detects provider changes.

Azure online voices run in the Python engine (`engine/azure_speech.py`, shared byte-for-byte with Mac) through the official Azure AI Speech REST API: `list-azure-voices` fetches the regional neural voice list, and `preview-tts`/`synthesize-tts` POST escaped SSML (`riff-24khz-16bit-mono-pcm`) with rate in `<prosody>`; pitch and volume are applied by the shared FFmpeg normalization. The key comes only from `VIDISTUDIO_AZURE_SPEECH_KEY`, is redacted from errors, and region/voice IDs are validated before use. In Settings the downloaded and online voices share the **Natural Voices** picker with group headings; picker IDs prefixed `online:` store `ttsMode=azure` plus `azureVoice`, and downloaded voices store `ttsMode=windows-natural` plus `systemVoice`, so narration signatures and voice reports distinguish them.

The Gemini settings page requests model and voice catalogs through engine commands. Separate model keys cover local and YouTube stages; the engine chooses source-specific keys when processing a YouTube project. The UI displays friendly voice names while storing provider IDs in settings. The catalog requires the user's key and network access, and the featured Gemini voices remain selectable if listing fails.

The Export dialog maps Video, Audio, Text, and Web Playback selections to engine commands and arguments. It offers only text and narration-only audio for YouTube URL projects. The engine also supports an unchanged local-video copy for the no-caption, original-audio choice; it verifies the copied video stream before committing the export.

## Models and compatibility

The C# models read the portable JSON schema and preserve unknown segment metadata when editing. The UI does not rewrite `project.json` directly. All mutations go through engine commands, including save-segments, histories, deletion, range generation, and exports.

`StudioProject.Resolve` rejects rooted or escaping project paths. Engine validation remains authoritative. User selections are retained as voice IDs rather than display labels.

## Build assets

The app project copies engine source and defaults into `engine/`, architecture-matched speech files into `speech/`, and an optional frozen engine into `engine-bin/`. `script/setup-windows.ps1` prepares those runtime directories; `script/build-windows.ps1` locates Visual Studio MSBuild. Runtime binaries and Python environments are ignored by Git. The copied Mac source under `reference/macOS` is an ignored local reference, not a shipped dependency.

Windows app version: 1.0.1 (manifest 1.0.1.0). The Mac project and its version remain unchanged.

## Export metadata parity limit
The Mac application additionally runs AppleAccessibilityMetadataWriter on closed-description MOV output. It writes AVFoundation tagged characteristics and alternate-group metadata, then repairs handler names. Windows now writes the documented track-level udta/tagc characteristics with engine/mov_accessibility.py, groups audio and subtitle alternatives, and retains FFmpeg handler names/default selections. It appends an updated movie header to a temporary export and replaces the original header with free space, preserving encoded media and chunk offsets. This leaves the movie header at the end of the file. Engine validation runs before committing the export. Specification: https://developer.apple.com/documentation/quicktime-file-format/user_data_atoms . Atom contents and actual FFmpeg/frozen-engine exports are verified; recognition by an Apple player still requires Mac-side validation.
