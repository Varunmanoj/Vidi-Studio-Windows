"""Inspect an unsigned or signed MSIX without installing it."""
import argparse
import struct
import re
import urllib.parse
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("package")
parser.add_argument("--platform", choices=["arm64", "x64"], required=True)
args = parser.parse_args()
with zipfile.ZipFile(args.package) as package:
    names = {urllib.parse.unquote(name).replace("\\", "/"): name for name in package.namelist()}
    def read(name):
        if name not in names:
            raise SystemExit("Missing packaged file: " + name)
        return package.read(names[name])
    manifest = ET.fromstring(read("AppxManifest.xml"))
    ns = {"p": "http://schemas.microsoft.com/appx/manifest/foundation/windows10"}
    identity = manifest.find("p:Identity", ns)
    assert identity.attrib["ProcessorArchitecture"] == args.platform, "Wrong MSIX architecture"
    app = manifest.find("p:Applications/p:Application", ns)
    expected = {"arm64": 0xaa64, "x64": 0x8664}[args.platform]
    def machine(name):
        data = read(name)
        assert data[:2] == b"MZ", name + " is not a PE executable"
        offset = struct.unpack_from("<I", data, 0x3c)[0]
        assert data[offset:offset+4] == b"PE\0\0", "Invalid PE header"
        return struct.unpack_from("<H", data, offset+4)[0]
    assert machine(app.attrib["Executable"]) == expected, "Wrong UI architecture"
    assert machine("speech/VidiStudio.SpeechHost.exe") == expected, "Wrong speech helper architecture"
    for file in ["speech/VidiStudio.SpeechHost.runtimeconfig.json", "engine/vidistudio_engine.py", "engine/windows_speech.py", "engine/mov_accessibility.py", "engine/youtube_audio.py", "engine/ai-defaults.json", "Assets/youtube.html"]:
        read(file)
    if "engine-bin/vidistudio-engine.exe" in names:
        engine_machine = machine("engine-bin/vidistudio-engine.exe")
        assert engine_machine in (expected, 0x8664), "Engine is incompatible with target"
        read("engine-bin/_internal/ai-defaults.json")
        assert machine("engine-bin/media/deno.exe") in (expected, 0x8664), "YouTube JavaScript runtime is incompatible with target"
        read("engine-bin/_internal/yt_dlp_ejs/yt/solver/core.min.js")
        assert any(name.startswith("engine-bin/") and re.search(r"/python3\d+\.dll$", name) for name in names), "Missing bundled Python runtime"
        print("Bundled engine PE machine:", hex(engine_machine))
    print("PASS:", args.platform, "MSIX manifest, executable architectures, speech host, engine, and player assets")
