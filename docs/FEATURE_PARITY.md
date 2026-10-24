# macOS to Windows parity tracking

This is a work record, not a declaration that the full migration is finished.

| Area | Current Windows status |
| --- | --- |
| Portable schema, atomic writes, locking, validation, cleanup | Existing engine retained; automated tests pass |
| Local import, metadata, eight thumbnails | Connected to native UI; New Project previews selected media with FFprobe before creation; real engine tests pass |
| YouTube import, canonical URLs, AI availability validation | New Project fetches title, channel, and thumbnail before enabling Create; engine validation and WebView2 player connected; live service/player validation outstanding |
| Full and individual AI stages | Connected; mocked engine coverage; live Gemini unverified |
| Targeted transcript/AD reanalysis | Connected through segment dialog; engine tests pass |
| Add/edit/delete, multiselect, undo/redo | Connected; timing/history engine tests and native save/undo/redo workflows pass on both targets |
| Project search/filter/rename/recycle | Rename/search native workflow passes; recycle acceptance outstanding |
| Transcript, summary, AD, overview, combined transcript | Implemented with formatted summary headings, paragraphs, lists, and emphasis; native view checks pass |
| Core, SAPI 5, Piper, Gemini narration | All providers connected; Core/SAPI actual synthesis validated through both native app targets; Piper routing, catalog, download verification, and packaging tested on macOS; Gemini mocked only; Windows build and listening validation outstanding |
| Piper voices (Windows) | Implemented: **Piper Voices** is a bundled offline neural engine with an in-app catalog of the public Rhasspy voices. Users can filter by language and quality, sort by language/name/quality/size, read plain-English language and dialect labels, and play a hosted sample before downloading. Only selected models are downloaded, verified by size and MD5, into `%APPDATA%\VidiStudio\piper\voices`; `SpeechHost` runs the bundled `piper.exe` for samples, saved narration, live preview, and summary read-aloud. The runtime is fetched from the verified official Rhasspy release at build time and packaged with the app. Mac has no Piper generation; legacy `piper` settings migrate to Apple Voices. |
| Azure and voice-bridge removal | **Azure Voices** and the NaturalVoiceSAPIAdapter promotion were removed from Windows and macOS, including engine pickers, credential fields, engine modules, tests, Help, and website docs. Legacy `azure` settings migrate to Windows OneCore on Windows and Apple Voices on macOS; legacy `windows-natural` migrates to SAPI 5. Summary read-aloud always uses the narration voice. |
| SAPI architecture isolation | ARM64/x64/x86 helpers built; x86 Microsoft SAPI discovery and WAV synthesis pass; third-party voice validation outstanding |
| Generated narration synchronization | Native muted-media checks pass on ARM64/x64 for cue alignment, pause, and seeking beyond a cue; listening review remains |
| Summary readback | Single Play/Pause/Resume control plus Stop in view and menu; sentence and word highlighting in the summary uses Appearance colors; Core word boundaries and fallback playback-position estimates implemented |
| Voice filtering, samples, settings | Implemented; native settings navigation and visual checks pass; screen-reader validation outstanding |
| Transcript/AD labeled timeline tracks | Mac-style full-width thumbnail strip with centered crop, equal local-frame cells, synchronized thumbnail playhead, click/drag seeking, and empty state; timed rows follow playback in Transcript, Descriptions, and AD Transcript |
| Captions in editor preview | Implemented with separate dialogue/AD colors, opacity, position and size settings |
| On-device narration without saved project audio | Implemented through Core/SAPI/Piper; synchronization requires listening validation |
| Exports, caption variants, audio, HLS | Synced to Mac d38a3b1: MKV/MP4/MOV/M4V video; WAV/MP3/M4A audio; replacement matching and content/narration gating; actual media and native ARM64/x64 checks pass |
| Public YouTube soundtrack exports | Original audio, narration only, and downloaded soundtrack with narration; live source and packaged soundtrack download/export plus packaged Windows narration/M4A mix pass. Windows 1.0.3 adds one fresh retry for temporary failures and safe specific worker errors; this extends the Mac d38a3b1 downloader. |
| Export progress and cancellation | Centered progress cards across library/editor/Settings windows for active processing and exports; media-time progress, aggregate HLS progress, indeterminate downloads, per-export cancellation; native placement, cross-window, completion, and engine cancellation checks pass |
| Menu and segment action icons | Fluent icons on every menu-bar action, project context action, and segment/editor More menu; native icon assertions pass |
| Future export drift detection | Read-only script compares shared Mac/Windows export logic and reports changes requiring review; synchronization remains an explicit port/validation step |
| Created export open/delete | Connected; interactive validation outstanding |
| Generated-content deletion dependencies | Connected |
| Grid/list library presentation, context menus | Grid/list, thumbnail cards, search/filter, and native context menus implemented; view checks pass |
| Multiple independent editor windows | Native workflow verifies a second editor with independent view selection |
| File Explorer context menu & OS share activation | Implemented with Package.appxmanifest fileTypeAssociation + verbs, deep link protocol, and registry shell context menu for all videos; direct new project creation & duplicate matching in full parity with macOS Finder |
| Full Mac shortcut and menu coverage | Native File/View/Analysis/Segments/Playback/Help menus and shortcuts implemented; keyboard accessibility audit remains |
| Accessibility metadata conventions | Portable MOV tagc characteristics and alternate groups implemented; real exports validated; Apple-player recognition unverified |
| ARM64/x64 builds | Debug and Release builds plus native UI smoke tests pass for both targets; ARM64 development registration/activation and packaged workflow pass; signed MSIX installation remains unverified |

Complete these gaps and record interactive results before declaring full functionality parity.
