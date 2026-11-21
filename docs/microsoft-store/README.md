# Vidi Studio Microsoft Store assets

## Store packages

Use a staged copy of the desktop workspace. In the Windows copy, run `script/set-store-manifest.ps1` with the staged manifest path, package name, publisher and publisher display name from Partner Center's Product Identity page. Then run `script/build-windows.ps1 -Package -Configuration Release -Store -Platform ARM64` and repeat for x64.

The Store packages use Windows' default registry and AppData isolation. They declare the Windows 11 context menu, Open with, Share and browser handoff in the manifest. Edge and Chrome extensions update through their browser stores. Legacy registry repair and the unpacked browser updater run in the direct-download and development builds. Inspect the final manifests before uploading and confirm that Partner Center shows both architectures with no request for `unvirtualizedResources`.

## Desktop screenshots

### All Projects · Dark mode

![All Projects shows local and YouTube video thumbnails, a search box, and filters for language and project type.](all-projects-dark.png)

### Local video editor · Dark mode

![The local video editor shows a preview, separate transcript and audio description timeline tracks, and a timed transcript segment.](local-editor-dark.png)

### YouTube project editor · Light mode

![The YouTube project editor shows an embedded video preview, transcript and audio description tracks, and a timed Hindi transcript segment.](youtube-editor-light.png)

### Appearance settings · Light mode

![Appearance settings show a caption text-size slider and separate foreground and background colors for dialogue and audio description captions.](settings-light.png)

### Help · Light mode

![The Help guide shows a searchable table of contents beside the welcome page and its Windows system requirements.](help-light.png)

## App icons

| 300 × 300 | 150 × 150 | 71 × 71 |
| --- | --- | --- |
| ![Vidi Studio icon with a purple video camera, headphones, and an orange-red wand, 300 pixels square.](app-tile-300.png) | ![Vidi Studio icon, 150 pixels square.](app-tile-150.png) | ![Vidi Studio icon, 71 pixels square.](app-tile-71.png) |
