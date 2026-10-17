# Windows migration progress

## 2026-10-17 — In-project processing indicators for Transcript, Summary, and Descriptions (1.0.63 / 1.0.63.0)

Mirrors the macOS change. The centered progress card is no longer used for section analysis: `ProgressOverlay` keeps its card for narration and export runs (so those paths and their smoke checks are unchanged) and stays hidden while an analysis stage runs. Each editor section now shows progress in place. `StudioViewModel` tracks the active `ProcessingSection`, a `VideoPreparation` upload state, and an ordered `ProcessingPlan` derived from the command and `--start-stage`; `IsAnalyzing` and a short `ProcessingStageTitle` drive the chrome. A `SectionProcessingOverlay` in the editor shows a spinner with the stage title over the running section and a "Waiting to generate…" spinner over sections still queued, and the footer status bar (ring, stage title, live message, Cancel processing) appears only for analysis. Section keys are derived from the editor selector: Transcript = transcript, Summary = summary, Descriptions and AD Overview = descriptions, and AD Transcript = transcript + descriptions. The editor stays navigable during analysis (`EditorWorkspace.IsEnabled = Model.Idle || analyzing`) while other long operations keep the previous workspace lock. The footer Cancel button reuses the existing `Cancel_Click`.

Validation: source review only. The C#/XAML changes were reviewed against the WinUI controls and existing handlers. No Windows build, UI smoke test, or engine run was performed in this macOS environment, so compilation and the ARM64/x64 native workflows remain outstanding and no parity claim is made. `git diff --check` passed and `python3 script/sync_version.py --check` reports 1.0.63 / 1.0.63.0.

## 2026-10-17 — Red playhead styling and playback synchronization (1.0.62 / 1.0.62.0)

Enhanced timeline playhead visibility and video playback synchronization across timeline tracks:
- High-contrast red playhead:
  - Updated `ThumbnailCursor` to 12px width with a 3px red `#E52B32` border and 12×12 ellipse handle with a 2.5px red stroke, matching the Mac thumbnail playhead.
  - Aligned `TimelineCursor` visibility with `ThumbnailCursor` visibility in `MainWindow.xaml.cs` so both cursor elements stay in lockstep with video duration readiness.
  - Added XAML resource overrides to `Slider x:Name="Playhead"` (`SliderThumbBackground`, `SliderThumbBackgroundPointerOver`, `SliderThumbBackgroundPressed`, `SliderTrackValueFill`, etc.) in `MainWindow.xaml` so the timeline scrubber thumb and active progress track render in matching high-contrast red `#E52B32`, `#FF3B42`, and `#C02026`.
- Smooth playback synchronization:
  - In `Timer_Tick`, dynamically sets `timer.Interval = playing ? TimeSpan.FromMilliseconds(40) : TimeSpan.FromMilliseconds(150);` so that video playback updates the playhead position smoothly at 25 fps without lag or stutter, while dropping back to 150ms when idle or paused.
- App and package versions synchronized to macOS version: 1.0.62 / 1.0.62.0.

Validation: Visual and XAML review against Mac implementation; `git diff --check` passed.

## 2026-10-04 — Timeline thumbnail parity (1.0.5 / 1.0.5.0)

Compared the actual Mac `thumbnailTrack` in ContentView.swift with Windows. Windows used fixed 88×50 thumbnail buttons in a scrolling row, leaving a YouTube project's single cover image at the left. Replaced that row with a full-width 42-pixel strip, equal-width cells, 2-pixel gaps, and centered image-fill cropping, matching Mac. Image brushes keep the crop inside each cell rather than letting WinUI Image content overflow its viewport. The strip resizes with the editor/inspector and aligns with transcript/description tracks. Added the synchronized thumbnail playhead and pointer click/drag seeking across the entire strip; the existing video-position slider remains available for keyboard seeking. Empty or missing thumbnails show a placeholder.

ARM64 and x64 native frozen-engine workflows passed with a copy of the reported video's actual thumbnail in isolated fixtures. Checks cover a single YouTube cover, eight local thumbnail cells, inspector resizing, empty state, and synchronized playheads. The actual Visual Studio ARM64 Debug build also passed. Visually reviewed the resulting crop against the Mac screenshot. Mac source and user project files were read only. Existing PRI249 packaging warnings remain.

## 2026-10-03 — Centered progress display (1.0.4 / 1.0.4.0)

Moved active processing and shared export progress from the bottom status strip into a centered, theme-aware card in each application window, including Settings. Status text is larger, export bars fill the card width, and each task retains its Cancel button. The card appears only while work is active and automatically closes on completion, failure, or cancellation. Long lists of simultaneous exports scroll within the available window height. The footer now shows idle/completion/error status without active progress controls. Observers are detached when windows close. Existing task execution, export percentages, and cancellation behavior remain intact.

ARM64 Debug build and native frozen-engine workflows on ARM64 and x64 passed. Native checks verify centered placement in the main window and Settings, processing visibility during actual Core/SAPI narration, shared export visibility in a second editor, and automatic dismissal after completion. Captured and visually reviewed light/dark screenshots. Existing PRI249 packaging warnings remain.

## 2026-10-03 — YouTube audio download recovery (1.0.3 / 1.0.3.0)

Investigated the reported failure for public video `YUMl5LUlSRk`. The existing bundled engine downloaded the original soundtrack, completed a full MP3 export using the saved app settings in an isolated project, and downloaded successfully under the actual Windows package identity. The original failure could not be reproduced; the running app already had FFmpeg on PATH and bundled Deno was present. The old parent worker discarded all error reasons, including missing tools, and replaced them with the same generic download error.

Added a temporary worker protocol carrying only allowlisted error codes, specific messages for connection failures, HTTP refusal, restricted/live videos, missing runtime/tools, and invalid audio, plus one fresh worker/download retry for temporary failures. Partial downloads are removed between attempts; both attempts share the original total timeout and retain cancellation cleanup. FFmpeg/FFprobe are checked before downloading. No browser credentials or signed stream URLs are saved or displayed. These reliability changes intentionally extend the Mac d38a3b1 downloader; the parity checker recognizes the exact reviewed pair of downloader ASTs and flags subsequent changes on either platform. Mac source remains unchanged.

Validation: all 120 engine tests passed with the rebuilt bundled CLI and actual ARM64 speech host; ARM64 Debug build and native frozen-engine workflow passed. The rebuilt engine exported the reported video to MP3 under Windows package identity and returned the specific FFmpeg error with a fixed worker code when PATH was deliberately empty. Test media/configuration remained in isolated workspace fixtures. Existing PRI249 warnings remain.

## 2026-10-03 — Visual Studio ARM64 Debug XAML cache repair

Reproduced the reported CS0029 and CS1061 directly in the generated ARM64 Debug files: `MainWindow.g.i.cs` correctly declared `ActiveExports` as `ExportProgressView`, while `MainWindow.g.cs` still cast the element to the previous `ItemsControl` and referenced the removed `MainWindow.CancelExport_Click`. Source XAML and the cancel handler in `ExportProgressView` were correct. Removed only the verified workspace `obj/ARM64/Debug` cache and rebuilt the actual packaged Debug configuration successfully. Regenerated code now casts to `ExportProgressView` and contains no obsolete MainWindow cancel binding. Updated troubleshooting documentation. App version remains 1.0.2 / 1.0.2.0 because this repaired generated build state without changing app behavior. Existing PRI249 packaging warnings remain.

## 2026-10-03 — Mac export update sync (1.0.2 / 1.0.2.0)

Compared the current Mac checkout at `d38a3b1` with Windows. Ported configurable video formats (MKV default, MP4, MOV, M4V) and audio formats (WAV default, MP3, M4A), original public YouTube soundtrack downloads and described soundtrack exports, and FFmpeg/HLS progress events. Preserved Windows OneCore/SAPI routing, model selection, UTF-8 project/config reads, and the source-copy compatibility command. UI defaults and disabled options follow Mac content/narration readiness: every description must have current nonempty audio with valid timing; combined captions need both transcript and descriptions. Matching exports can be explicitly replaced while failure leaves the previous output intact; changing formats keeps both. Open-caption quality/HDR guidance is visible. M4V is classified as video in the library and Created Exports.

Active export progress and per-export cancellation appear across library/editor windows and Settings. Percentages stay monotonic and below completion until media validation, portable MOV track tagging, and saving finish. Windows uses a cancellation marker to let media/download workers unwind temporary files, with a three-second termination fallback. MOV tagging now handles a single mixed described-audio track and descriptive transcript characteristics as well as alternate audio tracks. Bundled yt-dlp/EJS and Deno are included in the refreshed frozen engine and checked by package inspection.

Added Fluent icons to every menu-bar action and project context action, plus segment/editor More menus; Play/Pause/Resume icons follow playback. Added a read-only Mac export drift checker comparing 53 shared functions, formats, ducking, and the downloader, excluding the documented Windows cancellation/MOV hooks. Both app and package versions advanced to 1.0.2 / 1.0.2.0. Mac files and its pre-existing local changes were left untouched.

Validation completed:

- 115 source-engine tests passed before the final cancellation-marker test; the final 116-test regression suite passed with `VIDISTUDIO_TEST_ENGINE` pointing at the rebuilt frozen executable, exercising bundled CLI format dispatch, progress, and media exports alongside source API tests and actual OneCore/SAPI speech.
- ARM64 and x64 native frozen-engine smoke workflows passed. New assertions cover missing/current/outdated narration, invalid duration, YouTube audio command routing, media-format arguments, replacement gating, shared export visibility in another editor, monotonic percentages and cleared completion state, and icons on menu/segment actions. Rendered export controls show the disabled AD choices.
- Real public YouTube `jNQXAC9IVRw` soundtrack download/export passed through source (WAV) and frozen (MP3) engines in an isolated fixture. The frozen engine also generated Windows narration and downloaded/mixed the actual soundtrack into a validated described M4A. No user project or credential was changed.
- ARM64 and x64 Release unsigned MSIX builds succeeded. Package inspection passed for app/speech/engine CPU architecture, source modules, bundled Python, Deno, EJS solver, defaults, and player assets.
- Shared export drift check and `git diff --check` passed. Existing PRI249 Python-extension qualifier and missing symbol-tool warnings remain. Packages were built/inspected, not signed or installed. Listening, screen-reader behavior, Apple-player recognition, and unrelated live Gemini acceptance remain in `docs/ACCEPTANCE.md`; this update does not assert complete platform parity.


## 2026-09-26 — 1.0.1 / 1.0.1.0

Reviewed the Mac README, feature map, Swift models, command invocation, and portable engine. Copied the engine and its tests into the Windows project without modifying the Mac checkout. Retained local portable projects and all existing processing/export command implementations.

Added WinUI models/view model, engine/settings/speech services, native library/editor/settings/export controls, local and YouTube playback adapters, segment edits and range regeneration, OCR, narration, summary readback, generated-content controls, and export actions. Added an isolated .NET speech host supporting Windows Core and SAPI 5 voices, including an alternate architecture host path for legacy voices.

Validation completed:

- ARM64 Debug WinUI build passed using Visual Studio MSBuild.
- x64 Debug WinUI build passed using Visual Studio MSBuild.
- Self-contained ARM64 and x64 speech helpers published successfully.
- Actual ARM64 host discovery returned three Windows Core voices and two SAPI desktop voices.
- Python suite: 94 tests passed in 35.397 seconds. Includes actual WAV synthesis through Core and SAPI, FFmpeg video import/export, caption streams/rendering, HDR and HLS behavior, histories, storage safety, and mocked AI integration.
- Initial Core voice enumeration failed under the restricted sandbox but succeeded with normal user speech-service access. This was an execution-environment restriction, not a missing voice fallback.

Native UI validation subsequently passed on ARM64 and on x64 under Windows ARM emulation. The harness opens all six editor views, switches library layouts and panels, navigates every settings section including repeated voice selection, opens export options, and invokes the engine doctor. It renders the gallery, transcript editor, descriptions, and appearance settings for visual inspection. Hardware video surfaces are not captured by RenderTargetBitmap.

Adapted the UI to the supplied Mac screenshots: gallery cards, compact preview and labeled timeline tracks, a contextual right inspector, timestamped rows with origin/readiness labels, a narration overview, formatted summaries, and grouped caption appearance controls. Fixed DPI clipping, settings control reparenting, window cleanup, stale thumbnail refresh, and JSON origin preservation. A smoke assertion checks that AI origins survive project loading.

Core word highlighting, live on-device narration, preview captions, native menus/context menus, multiple project windows, and Share target handlers are implemented. Actual Windows Share activation, third-party SAPI engines, live Gemini/YouTube, listening checks, screen-reader behavior, and signed package installation remain unverified. Full parity remains active; see docs/FEATURE_PARITY.md.

Final Release compilation passed for both ARM64 and x64 with package signing disabled. This validates compilation, not signed MSIX installation.

Playback follow-up: preview dialogue and AD now render in separate caption boxes using their respective configured colors. ARM64 native checks verify dialogue timing, AD timing/color, and expiration. Cue transitions clear prior narration sources so a cue without generated audio cannot continue the previous cue's recording. These changes were compiled and exercised by the ARM64 UI harness; listening validation remains outstanding.

Native workflow validation now passes for ARM64 and x64 (x64 runs under ARM emulation): save an edited AI description, verify ai_modified provenance, undo and redo through the engine, synthesize narration with Windows Core then SAPI 5, export combined SRT, and confirm its text matches the edited cue. Both runs also include caption timing/style assertions and the existing view/settings checks. These use an isolated synthetic project, not the user library.

Distribution validation: setup-windows.ps1 -FreezeEngine -LegacySapiHosts completed, publishing ARM64/x64/x86 speech hosts and a standalone x64 Python engine. The ARM64 UI workflow passed with -FrozenEngine and an intentionally nonexistent external Python path. This exposed and fixed locale-dependent JSON reads: configuration, projects, and edit payloads now explicitly use UTF-8. The fixture includes Hindi text. The x86 SAPI helper enumerated two Microsoft desktop voices and produced a valid PCM 22050 Hz WAV. Third-party voice engines remain unverified. The first frozen build emitted PRI qualifier warnings for Python extension filenames; runtime validation passed, but signed MSIX packaging remains to be checked.

Settings lifecycle follow-up: installed-voice discovery now cancels earlier requests, rejects stale provider results, and stops when the dialog closes. Model discovery is tied to dialog lifetime, and cancelled samples cannot start playback after closing. ARM64 frozen-engine workflow passed with a rapid SAPI/Core/Gemini switching assertion in addition to edits, narration, and export checks.

Unsigned ARM64 and x64 MSIX generation succeeded. Package inspection verifies the manifest architecture, matching UI and speech executable PE types, bundled engine/runtime/defaults, and YouTube asset. Added script/verify-windows-package.py and build-windows.ps1 -Package. Packages were not signed or installed. PRI249 warnings for Python extension filenames and missing mspdbcmf.exe symbol-package tooling remain; these did not prevent either MSIX from building.

Project lifecycle follow-up: gallery navigation now pauses the YouTube player as well as native video and narration. Project deletion releases playback sources and clears the editor, inspector, timeline, captions, and summary before returning to the gallery. ARM64 frozen-engine workflow passed with an assertion that ClearProject leaves no active project, playback source, or inspector details. Actual YouTube playback remains pending a live test URL.

YouTube parity follow-up: bridge polling now returns duration and title, and the native editor persists player duration changes through update-metadata, matching Mac behavior for URL imports without initial duration. Poll completion ignores a project that was replaced or closed. ARM64 frozen-engine regression passed; node tests/test_youtube_bridge.cjs verifies bridge metadata and controls with a stub player. Live YouTube playback is still unverified.

Summary playback follow-up: readback requests serialize access to their MediaPlayer so cancellation/cleanup from an old request cannot clear the next request's source. ARM64 native workflow passes with immediate replacement, pause/resume, and cancellation. Accessibility export review confirms portable track titles and HLS attributes in the engine, but the Mac AVFoundation tagged-characteristic MOV postprocessing has no Windows equivalent implemented yet.

MOV metadata gap addressed: implemented the Apple-documented ASCII tagc user-data atoms for described audio and dialogue subtitles, with audio/subtitle alternate groups. Writer modifies only temporary exports, preserves chunk/media offsets, rejects malformed or fragmented headers, and is idempotent. All 97 engine tests passed (37.252 seconds), including real media exports. Rebuilt frozen engine and exported an actual two-audio-track MOV with captions; independently parsed correct per-track characteristics. Earlier notes that this writer was unimplemented are superseded. Apple-player recognition remains unverified.

Synchronized native playback validation passes for both ARM64 and x64 using the bundled engine: start real video inside an AD cue, check narration offset within 0.5 seconds, pause both, then seek past the cue and verify narration is inactive. Media is muted during this test; it verifies player state/timing, not perceived audio quality. Both full native regression runs also pass the latest settings, close-state, summary-cancellation, edit, and export checks.

ARM64 native workflow now also passes project rename/search filtering and opening a second editor with independent view selection. The debug harness runs once per process to permit child-window validation. Added docs/ACCEPTANCE.md with concrete remaining live, installation, listening, screen-reader, and Apple-player checks. Read-only environment check found no user Gemini settings/key and no installed package for this app identity.

Packaged startup verified: normal-user queries revealed the existing 1.0.0.0 Visual Studio development registration and required Windows App Runtime; the earlier sandbox-only no-package observation was incomplete. Updated that development registration to ARM64 1.0.1.0 using existing Developer Mode, without certificate changes. Activated through IApplicationActivationManager, verified GetPackageFullName returned the expected app identity, and passed the full native workflow. Direct executable launch correctly failed the identity requirement. Added DEBUG-only activation payload support and test-windows-ui.ps1 -AppDirectory/-RequirePackage. Signed MSIX installation and actual Share-target dispatch remain unverified. Gemini key absence was rechecked with normal user access and confirmed.

Accessibility implementation review: project menus now use WinUI ContextRequested rather than mouse-only RightTapped, supporting keyboard context requests with focused/selected-project fallback. Segment seek/preview names include timing and text, artifact status icons have accessible names, and unavailable narration previews are disabled. Screen-reader/physical-keyboard acceptance remains to be performed.

Navigation and appearance follow-up: the All Projects page now presents only File, View, and Help menus, while project editor windows present File, View, Analysis, Segments, Playback, and Help. Appearance selection previews immediately, Save persists it, Cancel restores the saved choice, and the native title bar follows the resolved light/dark theme. Removed a fixed Settings background brush that could remain light. ARM64 native regression verifies both menu sets, resolved themes, and darker/lighter rendered page backgrounds; ARM64 and x64 Debug builds pass.

Final offline audit (2026-09-26): rebuilt current ARM64 and x64 Release unsigned MSIX packages. Both pass architecture/content inspection, now including the MOV accessibility writer. The updated x64 frozen-engine native workflow passes, including the latest accessibility changes. Existing PRI qualifier and missing symbol-tool warnings remain. Documentation now records development activation and rename/search/multiple-window evidence accurately. Live Gemini credentials are still absent; live-service and external acceptance checks in docs/ACCEPTANCE.md remain prerequisites for declaring full parity. No complete-parity claim is made.

User-reported Debug ARM64 CS0123 reproduced: four stale MainWindow.g.cs bindings still used RightTapped while source XAML used ContextRequested. Removed only the generated obj/ARM64/Debug cache and rebuilt successfully. Verified all four regenerated bindings and passed registered-package activation plus the isolated native workflow. Added README troubleshooting; no handler workaround or generated-source edit was needed.
