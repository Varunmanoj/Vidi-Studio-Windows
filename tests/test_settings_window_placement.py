"""Run the production placement calculation without requiring a Windows UI host."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]

class SettingsPlacement(unittest.TestCase):
    def test_restored_windows_fit_the_owner_display_at_each_scale(self):
        dotnet = shutil.which("dotnet")
        if not dotnet:
            self.skipTest("The placement harness requires the .NET SDK.")
        with tempfile.TemporaryDirectory(prefix="vidistudio-settings-placement-") as folder:
            temp = Path(folder)
            sources = [ROOT / "Vidi Studio Windows/Views/SettingsWindowPlacement.cs", ROOT / "tests/settings_window_placement_harness.cs"]
            includes = "".join(f'<Compile Include="{escape(str(path))}" />' for path in sources)
            (temp / "Placement.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><RollForward>Major</RollForward><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
            env = os.environ | {"DOTNET_CLI_HOME": str(temp / "dotnet-home"), "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"}
            result = subprocess.run([dotnet, "run", "--project", str(temp / "Placement.csproj"), "--verbosity", "quiet"], env=env, text=True, capture_output=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("PASS:", result.stdout)
            print(result.stdout.strip())
