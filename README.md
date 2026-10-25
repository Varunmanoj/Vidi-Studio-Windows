# Vidi Studio for Windows

Native WinUI 3 / .NET 8 front end for the VidiStudio portable media engine. The Windows application targets **ARM64 and x64**. Project folders remain compatible with the macOS engine. The Mac checkout has not been modified.

## Bring Your Own Key (BYOK) Architecture

Vidi Studio is built on a **Bring Your Own Key (BYOK)** system. **A personal Google Gemini API key must be configured in Settings before utilizing this application to create projects or perform AI processing.**

### Why BYOK is a Key Feature:
- **Zero Subscriptions & Middlemen:** Vidi Studio has no monthly platform fees, centralized server proxies, or account registrations. Requests travel directly from your Windows workstation to Google's official Gemini API endpoints.
- **Direct Quota & Pricing Control:** Use your own [Google AI Studio](https://aistudio.google.com/) account. You can utilize the generous Free tier or pay-as-you-go pricing, paying Google directly with zero markups or token resale fees.
- **Enterprise-Grade Credential Security:** Your Gemini API key is stored securely in **Windows Credential Manager** (generic credential target `VidiStudio/GeminiApiKey`). It is never written to plaintext configuration files (`config.json`), never committed to project folders, and never logged or exposed on command lines.
- **Prerequisite Enforcement:** Project creation (via File → New Project, `Ctrl-N`, or the All Projects gallery cards) is strictly gated on having a valid Gemini API key configured. If no key is configured, Vidi Studio immediately opens **Settings → Gemini API** with an explanation so you can enter your key before proceeding. Automated speech transcription, scene summaries, Audio Description generation, cue rewriting, frame OCR ("Read Frame Text"), YouTube video accessibility validation, and Gemini TTS narration all use your configured BYOK key.

## Official Repositories & Live Website

VidiStudio is maintained across dedicated desktop repositories and an online showcase:

| Component | Platform / Role | Repository URL | Links |
| :--- | :--- | :--- | :--- |
| **Windows Application** | Native WinUI 3 / .NET 8 (ARM64 & x64) | `https://github.com/Varunmanoj/Vidi-Studio-Windows.git` | [GitHub Repo](https://github.com/Varunmanoj/Vidi-Studio-Windows) |
| **macOS Application** | Native SwiftUI / AVKit (macOS 26+) | `https://github.com/Varunmanoj/VidiStudio-Mac.git` | [GitHub Repo](https://github.com/Varunmanoj/VidiStudio-Mac) |
| **Online Showcase & Web** | React / Vite Landing Page & Documentation | `https://github.com/Varunmanoj/VidiStudio.git` | [GitHub Repo](https://github.com/Varunmanoj/VidiStudio) |
| **Official Website** | Live Product Showcase & Feature Guide | — | [accessivideo.pages.dev](https://accessivideo.pages.dev/) |

## Build and run in Visual Studio on Windows ARM64

1. Install Visual Studio with WinUI application development support, .NET desktop tools, and the Windows SDK. The native Windows 11 File Explorer context-menu extension is optional: when the **Desktop development with C++** workload and its matching ARM64 or x64 build tools are installed, the app builds `ShellExtension/VidiStudio.ShellExtension.vcxproj` through its project reference and packages the DLL. Without that workload the app still builds and runs with the legacy **Show more options** context menu; install it and rebuild to enable the modern menu. The extension inherits `<DefaultPlatformToolset>` rather than pinning v143, so VS 2022 and newer releases use their installed toolset. This project retains the template's Microsoft.WindowsAppSDK 2.5.1 and SDK BuildTools 10.0.28000.2705 references. NuGet restore needs network access.
2. Install Python 3.11 or later. FFmpeg and FFprobe can use your existing Winget installation. Restart Visual Studio after installing tools so its PATH is refreshed.
3. Open PowerShell in this repository and run:

   ```powershell
   .\script\setup-windows.ps1 -Platform ARM64 -Python 'C:\path\to\python.exe'
   ```

   This creates `.venv-windows`, installs the Google GenAI SDK, publishes a self-contained ARM64 speech helper to `runtime/speech/ARM64`, and fetches the verified Piper runtime to `runtime/piper`. The script does not install FFmpeg or change system speech registrations.
4. Open `Vidi Studio Windows.slnx` in Visual Studio. Select **Debug / ARM64**, set **Vidi Studio Windows** as the startup project, and start **Vidi Studio Windows (Package)**. Use Visual Studio's development signing/deployment settings when prompted. Enable Windows Developer Mode for local packaged development if required by Visual Studio.
5. In **Settings → Storage**, set **Python executable** to the full path of `.venv-windows\Scripts\python.exe` in this repository. FFmpeg and FFprobe use PATH by default; if needed, use the **Browse…** button beside each path to select its `.exe`. Save, then reopen Settings and run the engine check.
6. In **Settings → Narration**, choose **Windows OneCore**, **Windows SAPI 5**, **Piper Voices**, or **Gemini Voices**. **Piper Voices** runs the bundled Piper neural speech engine entirely on this PC; download only the voices you need from the in-app catalog (see [Piper voices](#piper-voices)). The voice dropdown follows the selected speech engine; refresh installed voices and adjust rate, pitch, and volume with sliders. Play a sample with the Play button, which becomes Pause during playback and Resume when paused; Stop cancels generation or resets playback. OneCore samples use Windows speech directly in the app; SAPI 5 samples use the selected local SAPI host; Piper samples use the bundled runtime and a downloaded voice. Sample and live playback apply rate and volume; pitch is applied when generating saved narration. Gemini requires your personal API key. In **Settings → Gemini API**, load the model catalog, then choose models from dropdowns for local and YouTube jobs or use the latest available choices. Google controls which models and voices are available to each key.
7. Import a video, or open an existing folder containing `project.json`. Existing projects are edited in place, so use a copy if you want to compare platforms independently.

For x64, rerun setup with `-Platform x64`, then select **x64** in Visual Studio. A command-line build is also available:

```powershell
.\script\build-windows.ps1 -Platform ARM64
.\script\build-windows.ps1 -Platform x64 -Configuration Release
```

If Visual Studio reports missing controls that were removed from XAML (for example,
`InspectorRegenerateAllNarrationButton` or `InspectorNarrateSelectedButton`), run this
diagnostic inside the checkout being built:

```powershell
python script\check_xaml_references.py --deep
```

It scans every file — including ignored build output such as `obj/` and `bin/` — and
lists the exact file and line still containing a retired name. Delete the folders it
reports (MSBuild regenerates them), or update the checkout so every listed file matches
the current commit. A clean scan result means the failing project lives in a different
folder than the one you scanned.

```powershell
.\script\build-windows.ps1 -Platform ARM64 -Configuration Debug -Rebuild
```

Use `-Platform x64` or `-Configuration Release` when appropriate. `-Rebuild` invokes
MSBuild's standard Rebuild target and clears the persistent XAML compiler cache
(`%LOCALAPPDATA%\Temp\XamlCompiler`) instead of reusing incremental XAML output; it
does not restore the removed duplicate buttons. The build script also checks
production inspector field references against the current XAML before building.
If the error persists after a current-checkout rebuild, report the exact file path
and line number from the build output to distinguish stale generated code from an
out-of-date source partial class.

## Engine distribution and CPU architecture

The WinUI process and default speech helper use the selected platform. Python and FFmpeg are separate processes: x64 builds can run under Windows ARM64 emulation without loading x64 DLLs into the ARM64 UI. For an entirely native ARM64 distribution, use ARM64 Python and ARM64 media tools and verify dependency wheels on that machine.

For a frozen Python engine, run setup with `-FreezeEngine`. The engine is built using the architecture of the Python executable supplied, then copied into `runtime/engine`. Build the WinUI app after setup so the engine and speech helper files are included. Frozen builds take precedence over source Python in the app. Validate this mode with `.\script\test-windows-ui.ps1 -Platform ARM64 -FrozenEngine`; the test deliberately disables the external Python path. Do not mix stale frozen engine files with newer engine source.

Legacy third-party SAPI voices may be registered only for x86 or x64. Run setup with `-LegacySapiHosts` and select the matching `runtime/speech/x86/VidiStudio.SpeechHost.exe` or x64 helper in Narration settings. Keep the helper's entire directory together. The application itself remains ARM64 or x64. Voices are never copied between registry views or silently substituted.

## Piper voices

**Piper Voices** is the built-in neural speech engine for natural-sounding offline narration. The Piper runtime (`piper.exe` and its dependencies) ships inside Vidi Studio, so there is nothing to install separately. The much larger voice models are optional and downloaded only when you choose them.

1. In **Settings → Narration**, choose **Piper Voices**, then choose **Download or remove voices**.
2. Search or filter the public Piper voice collection by language and quality, then sort by language, name, quality, or size. Each entry shows the voice name, quality, size, and the language and dialect in plain English, such as **English (United States)**. Use **Play sample** to hear a short published sample before downloading; samples need the internet, while a downloaded voice works offline. Voices range from small extra-low-quality models to larger high-quality models. Some catalog voices contain multiple speakers; Vidi Studio currently uses the model's first speaker.
3. Choose **Download**. A progress bar under the row shows the percentage and downloaded size while Vidi Studio verifies the published size and MD5 checksum, then the voice becomes available in the voice list. Use **Delete** in the same dialog to remove a downloaded voice; the row shows a removal indicator while the model and its cached sample are deleted.
4. Select a Piper voice and play a sample. Rate and volume apply to samples and live playback; pitch is applied when generating saved narration.

Piper voices work entirely on this PC. Voice samples, saved narration files and exports, live **Speak with an on-device voice** preview, and summary read-aloud all use the selected Piper voice without sending narration text anywhere. The summary is read with the same engine and voice chosen for narration and never creates project files.

Downloaded voices live in `%APPDATA%\VidiStudio\piper\voices` as `<voice-id>.onnx` plus `<voice-id>.onnx.json`. The bundled runtime lives in the app's `piper` folder. If the app reports that the Piper engine is missing, reinstall Vidi Studio or run `script\fetch-piper.ps1` and build again. The build scripts fetch the verified official Rhasspy Piper Windows x64 release automatically.

Windows SAPI 5 remains available for classic desktop voices and compatible third-party SAPI engines. The former NaturalVoiceSAPIAdapter voice-bridge guidance and the Azure Voices engine were removed because the bridge add-on did not work reliably for live narration and summary read-aloud. Settings saved with the earlier **Natural Voices** engine move to **Windows SAPI 5** automatically; saved **Azure Voices** settings move to **Windows OneCore**.

When a voice cannot speak, Vidi Studio says why instead of showing a general error, for example that the Piper engine or selected voice is missing, or that the voice produced no audio. Live narration reports the same message in the status bar. A voice that stalls is stopped after two minutes, and a SAPI voice that fails or produces no audio is retried up to three times. **Settings → Storage → Open diagnostics folder** opens `diagnostics.log`, which records the speech helper's full error, including retry notes.

## Workflow

- Initial Setup (BYOK): Configure your personal Google Gemini API key in **Settings → Gemini API** before utilizing the application. Both local and YouTube project creation require a configured key. Use **Check API Key & Plan** to verify the key, automatically detect your Free or Paid plan, and populate the compatible model catalog.
- Library: **New Project** offers Video File and YouTube Video sources in one light/dark-aware dialog. Choose a local file to preview it and inspect FFprobe duration, resolution, frame rate, codecs, and size before creating the project. For a public YouTube URL, select **Fetch Details** to review its title, channel, and thumbnail; Create stays disabled until details load. The engine checks Gemini availability when the YouTube project is created. All Projects switches between cards and a table with name, duration, AD segment count, transcript, summary, narration, video exports, and audio exports. Open portable project folders, search/filter, rename, or move a project to the Recycle Bin. System speech narration works offline once projects exist, but project creation and AI stages require the BYOK key.
- Analyze: complete analysis or individual transcript, summary, and description generation. Gemini preparation and availability checks use the original engine.
- Timeline pictures: Local frames and YouTube cover images fill the complete timeline strip with centered cropping and resize with the editor. A YouTube cover fills one full-width cell; local frames divide the strip evenly. Click or drag anywhere on the strip to seek and play, or use the video-position slider for keyboard seeking. The playhead follows playback, and missing pictures show a placeholder.
- Editor: Transcript, Summary, Descriptions, AD Overview, combined AD Transcript, and Created Exports. Click or drag across the timeline tracks to seek and begin playback, or drag the position slider; a red triangle marks the playhead above the tracks. The transport has one button that changes between Play and Pause, with the Playback menu reflecting the same state. Transcript, Descriptions, and AD Transcript scroll to and highlight the active row when **Follow** is enabled. The top-right **Open folder** button reveals the current project in File Explorer. Add/edit/select/delete rows, undo/redo, re-analyze a selected range, extract frame text, or rewrite an AD cue to fit.
- Narration: generate all, changed, or selected narration; preview a cue; select Windows OneCore, Windows SAPI 5, Piper, or Gemini voices. Generated narration is synchronized with video playback. Summary readback uses centered Previous/Next Sentence and Paragraph buttons around one Play/Pause/Resume button, plus Stop, in both the Summary view and Playback menu. The Summary view keeps those controls visible while its text scrolls. The spoken sentence and word are highlighted in the summary itself; their colors and visibility are set under **Settings → Appearance**. OneCore supplies exact word boundaries when available; SAPI, Piper, and Gemini use playback-position estimates.
- Export: Video offers MKV (default), MP4, MOV, and M4V, with original, selectable, or mixed Audio Description soundtracks and closed, open, or dual captions. Choose captions or Audio Description before saving a video. Audio offers WAV (default), MP3, and M4A for original audio, described audio, or narration alone. YouTube projects offer YouTube audio only, narration only, and YouTube audio with narration, plus text exports. The downloader retrieves the original public audio track temporarily using bundled yt-dlp and Deno; it uses no browser credentials and downloads no video. Text offers SRT, VTT, TXT, and JSON, plus Markdown for summaries. Web Playback creates an HLS folder. Narration choices are disabled until every description has current, nonempty saved audio and valid timing; AD Transcript captions require both dialogue and descriptions. Save applies the same checks. An existing matching export requires selecting **Replace the existing export**; a failed export preserves the previous file. A different format creates a separate export. Open captions display a picture-quality/HDR notice. Created Exports recognizes every supported format, including M4V.
- Progress: Processing and active exports appear in a centered card in every library/editor window and Settings, with larger status text and Cancel buttons. Export cards show the project name, current step, percentage and progress bar when known. The card closes automatically when work finishes; final status and errors remain in the footer. Video/audio progress follows FFmpeg media time; HLS combines all rendition/audio stages. Unknown download sizes use indeterminate progress. Progress remains below 100% through checking, MOV accessibility tagging, and saving. Windows requests cooperative export cancellation, with a process-tree termination fallback after three seconds for an unresponsive engine.
- Menus: Every menu-bar action, project context action, and segment/editor **More options** action has a Fluent icon alongside its text. Segment menus include seek, edit, delete, and description-specific narration preview, generation, rewrite, and deletion. Playback menu icons follow Play/Pause/Resume state.

Ctrl+N opens New Project; Ctrl+Shift+O opens a project folder; Ctrl+Shift+E exports; Ctrl+Shift+Space toggles playback; Ctrl+1 through Ctrl+5 choose editor sections. Native controls provide keyboard focus and accessible names.

## Storage and credentials

Project JSON and referenced media are portable. The engine is the sole writer of project data; it retains path confinement, OS locks, atomic JSON replacement, edit validation, bounded history, and generated-file cleanup.

User settings live in `%APPDATA%\VidiStudio\config.json`, outside projects. As in the Mac application, the Gemini API key is stored in the OS credential store — Windows Credential Manager (generic credential target `VidiStudio/GeminiApiKey`) — instead of a plaintext file, so it survives app updates and reinstalls. The engine receives the key through the `VIDISTUDIO_API_KEY` environment variable; keys are not passed on process command lines and are redacted from diagnostics. Diagnostic details go to `%APPDATA%\VidiStudio\diagnostics.log`.

**Settings → Gemini API** follows the Mac layout: the Local video group contains speech transcription and the shared summary, description, on-screen text, rewrite, and narration models. YouTube has only its own transcription model, which must be a video model such as Gemini Flash rather than a dedicated speech transcription or TTS model. Older separate YouTube overrides for shared jobs are ignored and removed when settings are saved; the existing Local video selections take precedence. Model pickers follow the Mac eligibility rules: local transcription prefers dedicated transcription models and falls back to general models only when none are available; narration lists TTS models; other actions exclude specialized speech, image, embedding, live, computer-use, robotics, and research models. Saved choices cannot bypass the loaded catalog. API version, processing mode, and bulk model selection are under Advanced. This aligns the settings and processing behavior across platforms; preferences and API keys remain local to each computer.

Settings opens as a native window with standard minimize and close controls and follows the selected light, dark, or system appearance. **Appearance** includes an **Advanced** card with **Developer mode**, which is off by default; enabling it shows **Developer prompts** in the Settings sidebar plus the optional Python, FFmpeg, and FFprobe executable paths in **Storage**, the optional SAPI host executable in **Narration**, and the legacy File Explorer context-menu repair action. Disabling it hides those controls while keeping saved prompt edits and paths. The Windows 11 modern **Describe with Vidi Studio** context menu is registered automatically by the package through its native `IExplorerCommand` COM surrogate; the repair action only restores the legacy **Show more options** entry. The overview's **Voice and Narration Settings** button opens Narration; the Help menu opens the separate Help and About sections directly. If Settings is already open, those entry points switch its page and bring that window forward. **Help** lists Windows keyboard shortcuts; **About** reads the app version from package metadata, reports the configured FFmpeg build's version and license flags, and identifies the bundled Piper speech engine with its MIT license, copyright notice, source link, and the bundled Piper notice. Piper voice models are downloaded on demand and keep the license and terms published with each voice. FFmpeg is supplied by the user's installation, not bundled by this repository. The detected license depends on that build's configuration; see [FFmpeg's licensing page](https://ffmpeg.org/legal.html).

Cancelling processing lets export workers stop and clean up, then terminates an unresponsive engine process tree after three seconds. Completed stages remain saved; forced Windows process termination can leave temporary work files until project cleanup. Do not manually delete source files or change a project while it is processing.

## Verification and status

Windows 1.0.3 retries a temporary YouTube audio download failure once with a fresh worker and removes partial downloads before retrying. Connection, YouTube refusal/restriction, JavaScript runtime, and missing media-tool errors have specific messages. Worker diagnostics carry only fixed error codes, so temporary signed media URLs stay out of saved project errors and logs. FFmpeg and FFprobe are checked before downloading. This Windows reliability update extends the Mac d38a3b1 downloader; the export parity script recognizes this exact reviewed difference and flags any subsequent downloader change on either platform.

Both ARM64 and x64 WinUI builds and native smoke tests have passed. The smoke test covers Settings button and menu routing, OneCore and SAPI 5 sample playback, appearance changes, the gallery/editor, timeline position, Transcript and AD Transcript synchronization, summary highlight colors and readback controls, local-video inspection, YouTube preview gating, saved narration, format arguments, disabled export choices, replacement gating, shared export progress, and menu icons. **116 engine regression tests passed**, including actual Core and SAPI WAV generation and real FFmpeg import/export, HLS, caption, HDR, format, progress, and cancellation tests, with bundled CLI paths exercised. Live public YouTube original-audio and described-audio exports passed through the frozen engine. Live Gemini model/voice listings and a live YouTube import preview have not been checked against a live account in the smoke test.

Run `.\script\test-windows-ui.ps1 -Platform ARM64` (or `x64`) to rebuild and check the native views, persisted edits, undo/redo, Core/SAPI narration, caption timing, synchronized playback pause/seek, and combined subtitle export against an isolated synthetic project. It saves screen renders and a report under `build/ui-test-<platform>` without changing your library.

The full port remains in progress. See [feature parity](docs/FEATURE_PARITY.md), [architecture](docs/ARCHITECTURE.md), and [validation record](PROGRESS.md) for implemented workflows and outstanding work. Do not treat a passing build as proof of complete Mac feature parity.

The Windows 1.0.2 export engine was compared with Mac commit `d38a3b1`. Run `.venv-windows\Scripts\python.exe script/check-mac-export-parity.py --mac-root '<Mac checkout>'` after Mac export changes. It compares 53 shared export/progress functions, formats, ducking, and the YouTube downloader while allowing documented Windows cancellation and MOV-finalization hooks. A nonzero exit flags code requiring review; the script never rewrites either checkout. Keep checking the native UI policies and run the engine/native tests when porting a new Mac change. This is a drift check, not automatic synchronization. `script/bundle-windows-engine.ps1` refreshes the frozen engine after source changes; setup also calls it when `-FreezeEngine` is selected.

## Inspect a distribution package

Run `.\script\build-windows.ps1 -Platform ARM64 -Configuration Release -Package` (or `x64`) to produce an unsigned MSIX under `Vidi Studio Windows/AppPackages`. The command does not install the app or modify certificate trust. To install outside Visual Studio, sign using your own development/distribution certificate whose publisher matches the manifest.

Verify the package with `.venv-windows\Scripts\python.exe script/verify-windows-package.py '<path-to-msix>' --platform arm64` (or `x64`). This checks the manifest, native UI and speech-host CPU types, engine files, and player asset. It does not prove installed-package activation. A bundled x64 engine is supported as a separate process on Windows ARM64.

See [remaining acceptance checks](docs/ACCEPTANCE.md) for live-service, installed-package, listening, accessibility, and cross-platform verification steps.

The ARM64 development registration and Windows launch contract have been tested. After registering a DEBUG build from Visual Studio or `Add-AppxPackage -Register <AppxManifest.xml>`, run `script/test-windows-ui.ps1 -Platform ARM64 -FrozenEngine -AppDirectory <registered-build-directory> -RequirePackage` to verify actual package identity and the isolated workflow. This mode activates the registered app and closes its test windows afterward; Release builds do not contain the test entry point.

## Stale XAML build errors

Stale generated XAML can report CS0123 for the old `RightTappedEventHandler`, or CS0029 converting `ItemsControl` to `ExportProgressView` together with CS1061 for `MainWindow.CancelExport_Click`. The current source uses `ContextRequested` and the reusable `ExportProgressView`; the export cancel handler belongs to that control. These errors occur when `MainWindow.g.cs` still contains bindings from an earlier layout while `MainWindow.g.i.cs` has the current field types.

Close Visual Studio, remove only the affected generated `Vidi Studio Windows\obj\ARM64\Debug` directory (or the corresponding platform/configuration), reopen the solution, and rebuild. Do not edit generated `.g.cs` files or add a duplicate handler to MainWindow. Clearing that cache and rebuilding resolved both reproduced Debug ARM64 failures.
