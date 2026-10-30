#!/usr/bin/env python3
"""Build the Windows Help book from the macOS Help sources.

The generated Help folder is committed so a standalone Windows checkout builds
without the macOS checkout. Re-run this after changing docs/help on macOS.
"""
from __future__ import annotations
import argparse
import os
import html
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--mac-root', type=Path)
args = parser.parse_args()
workspace = Path(os.environ.get('VIDISTUDIO_DESKTOP_WORKSPACE', ROOT.parent))
mac = args.mac_root or workspace / 'VidiStudio Mac'
MAC_HELP = mac / 'docs' / 'help'
BUILT = mac / 'build' / 'help' / 'VidiStudio.help' / 'Contents' / 'Resources' / 'en.lproj'
TARGET = ROOT / 'Vidi Studio Windows' / 'Help'
if not MAC_HELP.is_dir():
    parser.error('The Mac Help sources are required; pass --mac-root or VIDISTUDIO_DESKTOP_WORKSPACE.')
ORDER = ['welcome', 'first-project', 'projects', 'editor', 'descriptions', 'analysis',
         'narration', 'exports', 'settings', 'keyboard', 'troubleshooting', 'privacy']

def plain(value):
    return html.unescape(re.sub(r'<[^>]+>', '', value)).strip()

def slugify(value):
    value = re.sub(r'[^a-z0-9]+', '-', plain(value).lower()).strip('-')
    return value or 'section'

def add_ids(fragment):
    sections = []
    seen = set()
    def heading(match):
        level, attrs, body = match.group(1), match.group(2), match.group(3)
        title = plain(body)
        existing = re.search(r'id=["\']([^"\']+)["\']', attrs)
        anchor = existing.group(1) if existing else slugify(title)
        while anchor in seen:
            anchor += '-2'
        seen.add(anchor)
        if existing:
            sections.append((anchor, title))
            return match.group(0)
        sections.append((anchor, title))
        return f'<h{level}{attrs} id="{anchor}">{body}</h{level}>'
    fragment = re.sub(r'<h([23])([^>]*)>(.*?)</h\1>', heading, fragment, flags=re.S)
    return fragment, sections


def windowsize(fragment):
    replacements = [
        ('in the macOS Keychain on Mac, and Windows Credential Manager on Windows', 'in Windows Credential Manager'),
        ('On Mac, voices are stored in Application Support; on Windows, they are stored in your app data folder.', 'On Windows, voices are stored in your app data folder.'),
        ('moves the complete project folder to the Mac Trash', 'moves the complete project folder to the Recycle Bin'),
        ('On this Mac the system choices are Apple Voices; on Windows they are Windows OneCore and Windows SAPI 5.', 'On Windows the system choices are Windows OneCore and Windows SAPI 5.'),
        ('native Mac application', 'native Windows application'),
        ('on this Mac', 'on this PC'),
        ('on your Mac', 'on your PC'),
        ('this Mac', 'this PC'),
        ('your Mac', 'your PC'),
        ('macOS Keychain', 'Windows Credential Manager'),
        ('macOS Finder', 'Windows File Explorer'),
        ('Mac Help book', 'Vidi Studio Help book'),
        ('Help Viewer', 'the Help window'),
        ('Finder', 'File Explorer'),
        ('Apple Voices', 'Windows OneCore and SAPI 5 voices'),
        ('Apple voices', 'Windows OneCore and SAPI 5 voices'),
        ('Apple on-device voices', 'Windows on-device voices'),
        ('Apple voice', 'Windows voice'),
        ('Apple speech', 'Windows speech'),
        ('Apple system icons', 'Windows system icons'),
        ('Apple or Piper', 'Windows or Piper'),
        ('Apple accessibility metadata', 'Windows accessibility metadata'),
        ('Apple and Piper', 'Windows and Piper'),
        ('on Mac and to Windows OneCore or SAPI 5 on Windows', 'on Windows with OneCore or SAPI 5'),
        ('on Mac', 'on Windows'),
        ('Keychain', 'Credential Manager'),
        ('macOS', 'Windows'),
    ]
    for old, new in replacements:
        fragment = fragment.replace(old, new)
    # Keep only the Windows shortcut column in the generated shared table.
    def windows_table(match):
        table = match.group(0)
        if 'Mac shortcut' not in table: return table
        def row(match):
            cells = list(re.finditer(r'<t[dh]\b[^>]*>.*?</t[dh]>', match.group(0), re.S))
            if len(cells) != 3: return match.group(0)
            return '<tr>' + cells[0].group(0) + cells[2].group(0) + '</tr>'
        return re.sub(r'<tr\b[^>]*>.*?</tr>', row, table, flags=re.S)
    fragment = re.sub(r'<table\b[^>]*>.*?</table>', windows_table, fragment, flags=re.S)
    fragment = re.sub(r'<strong>Command[^<]*</strong> on Mac / (<strong>.*?</strong>) on Windows', r'\1', fragment)
    fragment = re.sub(r'in Windows File Explorer, right-click.*?; in Windows File Explorer,', 'in Windows File Explorer,', fragment, flags=re.S)
    fragment = fragment.replace('VidiStudio &gt; Settings', 'File &gt; Settings')
    fragment = fragment.replace('Command-Shift-Z', 'Ctrl-Y').replace('Command-', 'Ctrl-').replace('Command +', 'Ctrl +').replace('Option-', 'Alt-')
    fragment = fragment.replace('VoiceOver', 'Narrator').replace('Control-Option', 'Caps Lock')
    fragment = fragment.replace('Trash', 'Recycle Bin').replace('both desktops', 'Windows').replace('both platforms', 'Windows')
    fragment = fragment.replace('Mac shortcut', 'Windows shortcut')
    fragment = re.sub(r'(<th scope=[\'\"]row[\'\"]>New Project(?:…)?</th><td><strong>).*?(</strong></td>)', r'\g<1>Ctrl+N\2', fragment)
    fragment = re.sub(r'<strong>[^<]+</strong> \(Mac\) / (<strong>[^<]+</strong>) \(Windows\)', r'\1', fragment)
    fragment = re.sub(r'(<strong>[^<]+</strong> and <strong>[^<]+</strong>) on Windows / <strong>[^<]+</strong> and <strong>[^<]+</strong> on Windows', r'\1', fragment)
    fragment = re.sub(r'(<strong>[^<]+</strong>) on Windows / <strong>[^<]+</strong> on Windows', r'\1', fragment)
    fragment = re.sub(r'<li><strong>On Windows:</strong> In File Explorer, right-click.*?</li>(?=<li><strong>On Windows:</strong>)', '', fragment, count=1, flags=re.S)
    fragment = fragment.replace('Shortcuts below show the Mac combination first with the Windows equivalent beside it.', 'Shortcuts below are the Windows combinations.')
    fragment = fragment.replace('Application shortcuts for Mac and Windows', 'Application shortcuts')
    fragment = fragment.replace('Enable Keyboard navigation in Windows System Settings &gt; Keyboard to reach all controls using', 'Reach controls using')
    fragment = fragment.replace('Windows System Settings &gt; Accessibility &gt; Read &amp; Speak', 'Windows Settings &gt; Time &amp; language &gt; Speech')
    fragment = fragment.replace('Choose Windows OneCore and SAPI 5 voices', 'Choose a Windows voice')
    fragment = fragment.replace('Windows OneCore and SAPI 5 voices on Windows with OneCore or SAPI 5', 'Windows OneCore or SAPI 5 voices')
    fragment = fragment.replace('moves it to the Mac Recycle Bin', 'moves it to the Recycle Bin')
    fragment = fragment.replace('Ctrl-Shift-Z', 'Ctrl-Y').replace('standard red Close button', 'standard Close (×) button')
    fragment = fragment.replace('On Windows, Command maps to Ctrl and Option maps to Alt.', 'Use the Ctrl and Alt shortcuts shown above.')
    fragment = fragment.replace('Use standard Caps Lock navigation and interaction commands.', 'Use standard Narrator navigation and interaction commands with the Narrator modifier (Caps Lock or Insert).')
    if 'id="choose-a-source"' in fragment:
        fragment = fragment.replace('<h2 id="bring-existing-assets">', '<p><strong>Project location.</strong> New Project shows the parent folder where a new portable project folder will be created. Choose Project Location to use another folder or create a parent folder in the Windows folder chooser. Projects opened outside the default library are remembered on this PC.</p>\n<h2 id="bring-existing-assets">')
    return fragment

def keyboard_fragment():
    path = BUILT / 'keyboard.html'
    if not path.is_file():
        path = TARGET / 'keyboard.html'
    if not path.is_file():
        raise FileNotFoundError('Build Mac Help or retain the committed Windows keyboard chapter.')
    text = path.read_text(encoding='utf-8')
    match = re.search(r'<main[^>]*>(.*?)</main>', text, flags=re.S)
    if not match:
        return None
    body = re.sub(r'<a name="[^"]+" id="[^"]+"></a>', '', match.group(1))
    return body

TARGET.mkdir(parents=True, exist_ok=True)
css = (BUILT / 'style.css').read_text(encoding='utf-8') if (BUILT / 'style.css').is_file() else ''
css += """
html { font-family: "Segoe UI", -apple-system, BlinkMacSystemFont, sans-serif; }
main.topic { max-width: 900px; margin: 0 auto; padding: 2.25rem 2.6rem 4rem; }
main.topic a, main.topic a:visited { color: light-dark(#005bbb, #80baff); text-decoration: underline; }
main.topic a:hover { background: transparent; }
main.topic table { display: block; overflow-x: auto; }
main.topic pre { overflow-x: auto; padding: 1rem; background: light-dark(#f4f4f6, #29292c); border-radius: 8px; }
main.topic img { max-width: 100%; height: auto; }
"""
(TARGET / 'style.css').write_text(css, encoding='utf-8')

pages = []
for slug in ORDER:
    source = MAC_HELP / f'{slug}.html'
    if slug == 'keyboard':
        fragment = keyboard_fragment()
    else:
        fragment = source.read_text(encoding='utf-8') if source.is_file() else None
    if fragment is None:
        continue
    fragment = windowsize(fragment)
    fragment, sections = add_ids(fragment)
    title_match = re.search(r'<h1[^>]*>(.*?)</h1>', fragment, flags=re.S)
    title = plain(title_match.group(1)) if title_match else slug.replace('-', ' ').title()
    page = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{html.escape(title)} — Vidi Studio Help</title><link rel="stylesheet" href="style.css">
</head><body><main class="topic" id="topic">{fragment}</main></body></html>"""
    (TARGET / f'{slug}.html').write_text(page, encoding='utf-8')
    pages.append({'slug': slug, 'title': title, 'sections': [{'id': a, 'title': t} for a, t in sections]})

images = MAC_HELP / 'images'
if images.is_dir():
    shutil.copytree(images, TARGET / 'images', dirs_exist_ok=True)
(TARGET / 'manifest.json').write_text(json.dumps({'pages': pages}, indent=2), encoding='utf-8')
print(f'Wrote {len(pages)} Help pages to {TARGET}')
