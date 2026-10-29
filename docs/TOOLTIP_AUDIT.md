# Tooltip and UI Automation audit

The existing-asset, provenance, Markdown, narration-source, and export work keeps `ToolTipService.ToolTip` and `AutomationProperties.Name` on custom controls.

## Checked areas

- Project → Import Existing Assets…, editor Import assets button, import dialog file rows, AD transcript/audio mode pickers, replacement confirmation, and report actions.
- TXT timing dialog: Distribute Evenly, Auto-time with Gemini, start/end fields, Save Timing.
- Summary toolbar: history, inline, headings, blocks, lists, insert, clear, preview, Markdown Help, provenance picker, undo/redo.
- Description rows: provenance badge, overlap badge, narration preview, More options/context menu, Import narration audio.
- AD Overview narration sources/warnings and Voice review dialogs.
- Export dialog source picker and described-export controls.
- Disabled controls explain prerequisites in their tooltip.

## Static checks

`tests/test_import_assets_ui.py` verifies the import, Markdown, and export source controls and their tooltips/automation names. `script/check_xaml_references.py` verifies production XAML references.

## Exceptions

Native menu-bar items and system ContentDialog buttons do not expose custom hover tooltips; standard labels and UIA names are used.
