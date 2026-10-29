# Windows acceptance checks

This checklist records remaining end-to-end verification. Passing builds and engine tests do not close these items automatically. Use disposable copies of projects.

| Check | Procedure and expected evidence |
| --- | --- |
| Deployment and restart | ARM64 development registration and identity-verified activation passed. Still check Start launch, import, restart, and reopening a saved project. Signed MSIX installation requires an appropriate signing certificate and remains unverified. |
| Windows Share target | From an app that shares files/links, share one supported video and a public YouTube URL to Vidi Studio. Each should import once and report completion; unsupported inputs should show a useful error. Requires package deployment. |
| Live Gemini analysis | Configure a personal key in Settings, select valid stage models, import an approved short test clip, and run transcript, summary, descriptions, range regeneration, and OCR. Inspect resulting text/timestamps and retry/cancel behavior. Never include the key in logs or reports. |
| Gemini narration | Select Gemini and a valid voice, preview text, synthesize a cue, then export it. Confirm intelligible audio and correct timing. |
| YouTube | Provide a public embeddable test URL. Confirm title/duration populate, native timeline seeking and playback controls work, gallery navigation pauses playback, and URL-backed video export remains unavailable. |
| Speech listening | For Core, SAPI, and Piper, preview a voice, generate narration, play through cue boundaries, pause/resume, seek backwards/forwards, and test on-device playback. Confirm no stale or doubled speech. Automated tests currently verify muted player timing. |
| Piper voices | In Settings → Narration, choose **Piper Voices**, open **Download or remove voices**, filter by Language and Quality, sort by each of the four orders, and confirm every row shows a plain-English language and dialect rather than a code. Play a sample for an undownloaded voice, download a small voice, and confirm that row shows a live percentage, downloaded size, aggregate progress bar, and disabled Download button while it transfers. Play the installed sample, generate narration for one cue and for all cues, play it in the editor, read the summary aloud, and export narration-only audio. Disconnect the internet and confirm the downloaded voice still speaks; then remove the voice and confirm that row shows a removal indicator, the buttons are disabled during removal, and a clear "not downloaded" message appears instead of a general error. Confirm the bundled Piper runtime is present under the app's `piper` folder, cached samples live in `%APPDATA%\VidiStudio\piper\samples`, deleting a voice removes its cached sample, and no separate Piper installation is requested. Check the voice catalog, filters, sort order, sample buttons, both progress indicators, and Narrator announcements. |
| Legacy voice settings | Open a settings file saved with `ttsMode=windows-natural` and confirm it becomes **Windows SAPI 5**; open one saved with `ttsMode=azure` and confirm it becomes **Windows OneCore** with Azure credential fields absent. |
| Third-party SAPI | Install/use a licensed test engine, choose the helper matching its x86/x64 architecture, refresh voices, sample, generate, and export. Microsoft voices have been tested through the x86 helper; this does not prove vendor-engine compatibility. |
| Keyboard and screen reader | With Narrator or NVDA, navigate gallery, menus, editor, settings, segment editing and export without a mouse. Verify names, focus order, selected states, errors, and status announcements. Check 200% scaling and both themes. |
| Project recycle/export open | On a disposable imported project, open an export with its default app, remove that export, then recycle the project. Verify source data in unrelated projects is untouched and the editor returns to gallery. |
| Apple MOV recognition | Open a Windows closed-description MOV with captions on a Mac. Inspect both audio choices, subtitle choices, and AVFoundation accessibility characteristics. Windows tests verify atoms and FFmpeg streams, not Apple player interpretation. |

## Evidence already produced

- ARM64/x64 native builds and native workflow harness, including persisted edits, undo/redo, Core/SAPI output, subtitle export, captions and muted synchronized playback.
- 97 Python tests with real FFmpeg media operations and speech synthesis.
- Frozen engine workflow with external Python deliberately unavailable.
- Unsigned ARM64/x64 MSIX structure and PE-architecture checks.
- ARM64 development package activation with verified package identity and native workflow.
- Native rename/search, independent editor window, and summary replacement/cancellation checks.

Record date, platform, app version, input fixture, expected/actual result, and relevant diagnostic excerpt for each additional check. Keep credentials and private media out of shared reports.


## Existing assets, provenance, and Markdown summary (1.0.149)

Acceptance includes existing-asset import validation (tolerant rows, duration, overlap flags, zero-valid preservation), two-track combine with correct metadata and defaults, open single-mix export, per-segment narration fit validation, provenance transitions and pickers, AD Overview source grouping and mixed-source warnings, Markdown summary transformations/toolbar/shortcuts, and described-export blocking while timing is unresolved. On Windows these require a Windows host for native build, package, and UI verification; the shared engine and Python suites run on the current host and are recorded in PROGRESS.md.
