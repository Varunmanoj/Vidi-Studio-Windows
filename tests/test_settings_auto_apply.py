"""Exercise the production preference merge independently of a native Windows host."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]


class AutomaticSettings(unittest.TestCase):
    def test_pending_edits_merge_persist_and_retry(self):
        dotnet = shutil.which("dotnet")
        if not dotnet:
            self.skipTest("The settings harness requires the .NET SDK.")
        with tempfile.TemporaryDirectory(prefix="vidistudio-settings-auto-") as folder:
            temp = Path(folder)
            sources = [ROOT / "Vidi Studio Windows/Services/SettingsAutoApply.cs",
                       ROOT / "tests/settings_auto_apply_harness.cs"]
            includes = "".join(f'<Compile Include="{escape(str(path))}" />' for path in sources)
            (temp / "AutoApply.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><RollForward>Major</RollForward><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
            env = os.environ | {"DOTNET_CLI_HOME": str(temp / "dotnet-home"), "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"}
            result = subprocess.run([dotnet, "run", "--project", str(temp / "AutoApply.csproj"), "--verbosity", "quiet"],
                                    env=env, text=True, capture_output=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("PASS:", result.stdout)

    def test_window_flushes_without_save_cancel_footer(self):
        dialog = (ROOT / "Vidi Studio Windows/Views/SettingsDialog.cs").read_text()
        apply = (ROOT / "Vidi Studio Windows/Views/SettingsDialog.AutoApply.cs").read_text()
        self.assertNotIn('Content = "Save settings"', dialog)
        self.assertNotIn('Content = "Cancel"', dialog)
        self.assertNotIn("SaveAndClose", dialog)
        self.assertIn("AppWindow.Closing += (_, args) => args.Cancel = !ApplyPreferences();", apply)
        self.assertIn("WindowActivationState.Deactivated", apply)
        self.assertIn("public void Hide() { if (ApplyPreferences()) Close(); }", dialog)
        self.assertIn("autoApplyTimer.Stop();", dialog)
        self.assertIn("VerifyAutomaticSettingsForSmokeTestAsync", (ROOT / "Vidi Studio Windows/MainWindow.SmokeTest.cs").read_text())
