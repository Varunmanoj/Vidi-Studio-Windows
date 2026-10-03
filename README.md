# Vidi Studio for Windows

Native WinUI 3 / .NET 8 front end for the VidiStudio portable media engine. The Windows application targets **ARM64 and x64**. Project folders remain compatible with the macOS engine. The Mac checkout has not been modified.

## Build and run in Visual Studio on Windows ARM64

1. Install Visual Studio with WinUI application development support, .NET desktop tools, and the Windows SDK. This project retains the template's Microsoft.WindowsAppSDK 2.5.1 and SDK BuildTools 10.0.28000.2705 references. NuGet restore needs network access.
2. Install Python 3.11 or later. FFmpeg and FFprobe can use your existing Winget installation. Restart Visual Studio after installing tools so its PATH is refreshed.
3. Open PowerShell in this repository and run:

   ```powershell
   .\script\setup-windows.ps1 -Platform ARM64 -Python 'C:\path\to\python.exe'
   ```

   This creates `.venv-windows`, installs the Google GenAI SDK, and publishes a self-contained ARM64 speech helper to `runtime/speech/ARM64`. The script does not install FFmpeg or change system speech registrations.
4. Open `Vidi Studio Windows.slnx` in Visual Studio. Select **Debug / ARM64**, set **Vidi Studio Windows** as the startup project, and start **Vidi Studio Windows (Package)**. Use Visual Studio's development signing/deployment settings when prompted. Enable Windows Developer Mode for local packaged development if required by Visual Studio.
5. In **Settings → Storage and tools**, set **Python executable** to the full path of `.venv-windows\Scripts\python.exe` in this repository. FFmpeg and FFprobe use PATH by default; if needed, use the **Browse…** button beside each path to select its `.exe`. Save, then reopen Settings and run the engine check.
6. In **Settings → Narration**, choose **Windows OneCore voices**, **SAPI 5 voices**, or **Gemini voices**. The voice dropdown follows the selected speech engine; refresh installed voices and adjust rate, pitch, and volume with sliders. Play a sample with the Play button, which becomes Pause during playback and Resume when paused; Stop cancels generation or resets playback. OneCore samples use Windows speech directly in the app; SAPI 5 samples use the selected local SAPI host. SAPI sample and live playback apply rate and volume; pitch is applied when generating saved narration. Gemini requires your personal API key. In **Settings → Gemini API**, load the model catalog, then choose models from dropdowns for local and YouTube jobs or use the latest available choices. Google controls which models and voices are available to each key.
7. Import a video, or open an existing folder containing `project.json`. Existing projects are edited in place, so use a copy if you want to compare platforms independently.

For x64, rerun setup with `-Platform x64`, then select **x64** in Visual Studio. A command-line build is also available:

```powershell
.\script\build-windows.ps1 -Platform ARM64
.\script\build-windows.ps1 -Platform x64 -Configuration Release
```

## Engine distribution and CPU architecture

The WinUI process and default speech helper use the selected platform. Python and FFmpeg are separate processes: x64 builds can run under Windows ARM64 emulation without loading x64 DLLs into the ARM64 UI. For an entirely native ARM64 distribution, use ARM64 Python and ARM64 media tools and verify dependency wheels on that machine.

For a frozen Python engine, run setup with `-FreezeEngine`. The engine is built using the architecture of the Python executable supplied, then copied into `runtime/engine`. Build the WinUI app after setup so the engine and speech helper files are included. Frozen builds take precedence over source Python in the app. Validate this mode with `.\script\test-windows-ui.ps1 -Platform ARM64 -FrozenEngine`; the test deliberately disables the external Python path. Do not mix stale frozen engine files with newer engine source.

Legacy third-party SAPI voices may be registered only for x86 or x64. Run setup with `-LegacySapiHosts` and select the matching `runtime/speech/x86/VidiStudio.SpeechHost.exe` or x64 helper in Narration settings. Keep the helper's entire directory together. The application itself remains ARM64 or x64. Voices are never copied between registry views or silently substituted.

## Workflow

- Library: **New Project** offers Video File and YouTube Video sources in one light/dark-aware dialog. Choose a local file to preview it and inspect FFprobe duration, resolution, frame rate, codecs, and size before creating the project. For a public YouTube URL, select **Fetch Details** to review its title, channel, and thumbnail; Create stays disabled until details load. The engine checks Gemini availability when the YouTube project is created. All Projects switches between cards and a table with name, duration, AD segment count, transcript, summary, narration, video exports, and audio exports. Open portable project folders, search/filter, rename, or move a project to the Recycle Bin.
- Analyze: complete analysis or individual transcript, summary, and description generation. Gemini preparation and availability checks use the original engine.
- Editor: Transcript, Summary, Descriptions, AD Overview, combined AD Transcript, and Created Exports. Click or drag across the timeline tracks to seek and begin playback, or drag the position slider; a red triangle marks the playhead above the tracks. The transport has one button that changes between Play and Pause, with the Playback menu reflecting the same state. Transcript, Descriptions, and AD Transcript scroll to and highlight the active row when **Follow** is enabled. The top-right **Open folder** button reveals the current project in File Explorer. Add/edit/select/delete rows, undo/redo, re-analyze a selected range, extract frame text, or rewrite an AD cue to fit.
- Narration: generate all, changed, or selected narration; preview a cue; select Core, SAPI 5, or Gemini voices. Generated narration is synchronized with video playback. Summary readback uses one Play/Pause/Resume button plus Stop, in both the Summary view and Playback menu. The Summary view keeps those controls visible while its text scrolls. The spoken sentence and word are highlighted in the summary itself; their colors and visibility are set under **Settings → Appearance**. OneCore supplies exact word boundaries when available; SAPI and Gemini use playback-position estimates.
- Export: Video offers MKV (default), MP4, MOV, and M4V, with original, selectable, or mixed Audio Description soundtracks and closed, open, or dual captions. Choose captions or Audio Description before saving a video. Audio offers WAV (default), MP3, and M4A for original audio, described audio, or narration alone. YouTube projects offer YouTube audio only, narration only, and YouTube audio with narration, plus text exports. The downloader retrieves the original public audio track temporarily using bundled yt-dlp and Deno; it uses no browser credentials and downloads no video. Text offers SRT, VTT, TXT, and JSON, plus Markdown for summaries. Web Playback creates an HLS folder. Narration choices are disabled until every description has current, nonempty saved audio and valid timing; AD Transcript captions require both dialogue and descriptions. Save applies the same checks. An existing matching export requires selecting **Replace the existing export**; a failed export preserves the previous file. A different format creates a separate export. Open captions display a picture-quality/HDR notice. Created Exports recognizes every supported format, including M4V.
- Progress: Processing and active exports appear in a centered card in every library/editor window and Settings, with larger status text and Cancel buttons. Export cards show the project name, current step, percentage and progress bar when known. The card closes automatically when work finishes; final status and errors remain in the footer. Video/audio progress follows FFmpeg media time; HLS combines all rendition/audio stages. Unknown download sizes use indeterminate progress. Progress remains below 100% through checking, MOV accessibility tagging, and saving. Windows requests cooperative export cancellation, with a process-tree termination fallback after three seconds for an unresponsive engine.
- Menus: Every menu-bar action, project context action, and segment/editor **More options** action has a Fluent icon alongside its text. Segment menus include seek, edit, delete, and description-specific narration preview, generation, rewrite, and deletion. Playback menu icons follow Play/Pause/Resume state.

Ctrl+N opens New Project; Ctrl+Shift+O opens a project folder; Ctrl+Shift+E exports; Ctrl+Shift+Space toggles playback; Ctrl+1 through Ctrl+5 choose editor sections. Native controls provide keyboard focus and accessible names.

## Storage and credentials

Project JSON and referenced media are portable. The engine is the sole writer of project data; it retains path confinement, OS locks, atomic JSON replacement, edit validation, bounded history, and generated-file cleanup.

User settings live in `%APPDATA%\VidiStudio\config.json`, outside projects. As in the Mac application, the API key is stored in local plaintext in the user profile. Do not distribute that file. Keys are not passed on process command lines and are redacted from diagnostics. Diagnostic details go to `%APPDATA%\VidiStudio\diagnostics.log`.

**Settings → Gemini API** follows the Mac layout: the Local video group contains speech transcription and the shared summary, description, on-screen text, rewrite, and narration models. YouTube has only its own transcription model, which must be a video model such as Gemini Flash rather than a dedicated speech transcription or TTS model. Older separate YouTube overrides for shared jobs are ignored and removed when settings are saved; the existing Local video selections take precedence. Model pickers follow the Mac eligibility rules: local transcription prefers dedicated transcription models and falls back to general models only when none are available; narration lists TTS models; other actions exclude specialized speech, image, embedding, live, computer-use, robotics, and research models. Saved choices cannot bypass the loaded catalog. API version, processing mode, and bulk model selection are under Advanced. This aligns the settings and processing behavior across platforms; preferences and API keys remain local to each computer.

Settings opens as a native window with standard minimize and close controls and follows the selected light, dark, or system appearance. The overview's **Voice and Narration Settings** button opens Narration; the Help menu opens the separate Help and About sections directly. If Settings is already open, those entry points switch its page and bring that window forward. **Help** lists Windows keyboard shortcuts; **About** reads the app version from package metadata and reports the configured FFmpeg build's version and license flags. FFmpeg is supplied by the user's installation, not bundled by this repository. The detected license depends on that build's configuration; see [FFmpeg's licensing page](https://ffmpeg.org/legal.html).

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
