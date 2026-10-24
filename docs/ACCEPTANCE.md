# Windows acceptance checks

This checklist records remaining end-to-end verification. Passing builds and engine tests do not close these items automatically. Use disposable copies of projects.

| Check | Procedure and expected evidence |
| --- | --- |
| Deployment and restart | ARM64 development registration and identity-verified activation passed. Still check Start launch, import, restart, and reopening a saved project. Signed MSIX installation requires an appropriate signing certificate and remains unverified. |
| Windows Share target | From an app that shares files/links, share one supported video and a public YouTube URL to Vidi Studio. Each should import once and report completion; unsupported inputs should show a useful error. Requires package deployment. |
| Live Gemini analysis | Configure a personal key in Settings, select valid stage models, import an approved short test clip, and run transcript, summary, descriptions, range regeneration, and OCR. Inspect resulting text/timestamps and retry/cancel behavior. Never include the key in logs or reports. |
| Gemini narration | Select Gemini and a valid voice, preview text, synthesize a cue, then export it. Confirm intelligible audio and correct timing. |
| YouTube | Provide a public embeddable test URL. Confirm title/duration populate, native timeline seeking and playback controls work, gallery navigation pauses playback, and URL-backed video export remains unavailable. |
| Speech listening | For Core and SAPI, preview a voice, generate narration, play through cue boundaries, pause/resume, seek backwards/forwards, and test on-device playback. Confirm no stale or doubled speech. Automated tests currently verify muted player timing. |
| Natural voices and Azure Voices | Install NaturalVoiceSAPIAdapter v0.2.10 or later (64-bit version on ARM64), choose Windows SAPI 5 and select Refresh voices. Confirm the add-on voices appear under their headings and headings cannot be picked by mouse or keyboard. Sample, generate, export, read the summary and use on-device playback with an online add-on voice; then disconnect the internet and confirm a clear "needs the internet" style message appears instead of a general error, and live narration reports it in the status bar. Choose **Azure Voices**, confirm the key/region boxes appear only for that engine, enter a valid key/region, sample and generate an Azure voice, set **Summary read-aloud voice → Azure online voice** with another engine selected and read a summary, then remove the key and confirm the Azure summary option disappears. Check headings and help text with Narrator. |
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
