import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


class TestFileExplorerActivation(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(__file__).resolve().parents[1]
        cls.repo_root = cls.root.parent if cls.root.name == "Vidi-Studio-Windows" else cls.root
        cls.manifest_path = cls.root / "Vidi Studio Windows" / "Package.appxmanifest"
        cls.app_xaml_cs_path = cls.root / "Vidi Studio Windows" / "App.xaml.cs"
        cls.main_window_cs_path = cls.root / "Vidi Studio Windows" / "MainWindow.xaml.cs"
        cls.main_window_commands_cs_path = cls.root / "Vidi Studio Windows" / "MainWindow.Commands.cs"
        cls.view_model_cs_path = cls.root / "Vidi Studio Windows" / "ViewModels" / "StudioViewModel.cs"
        cls.shell_integration_cs_path = cls.root / "Vidi Studio Windows" / "Services" / "ShellIntegration.cs"

        # macOS paths
        cls.mac_root = cls.repo_root / "VidiStudio Mac"
        cls.mac_share_vc_path = cls.mac_root / "VidiStudioShareExtension" / "ShareViewController.swift"
        cls.mac_content_view_path = cls.mac_root / "VidiStudio" / "ContentView.swift"

    def test_app_display_name_is_vidi_studio_without_windows(self):
        manifest_text = self.manifest_path.read_text(encoding="utf-8")
        self.assertNotIn("Vidi Studio Windows", manifest_text, "Manifest should not name the app 'Vidi Studio Windows'")

        tree = ET.parse(self.manifest_path)
        root = tree.getroot()
        ns = {
            "p": "http://schemas.microsoft.com/appx/manifest/foundation/windows10",
            "uap": "http://schemas.microsoft.com/appx/manifest/uap/windows10",
        }
        props_display_name = root.find(".//p:Properties/p:DisplayName", ns)
        self.assertIsNotNone(props_display_name)
        self.assertEqual(props_display_name.text, "Vidi Studio")

        visual_elements = root.find(".//uap:VisualElements", ns)
        self.assertIsNotNone(visual_elements)
        self.assertEqual(visual_elements.attrib.get("DisplayName"), "Vidi Studio")
        self.assertEqual(visual_elements.attrib.get("Description"), "Vidi Studio")

    def test_appxmanifest_declares_file_type_association_and_verbs(self):
        self.assertTrue(self.manifest_path.is_file(), "Package.appxmanifest must exist")
        tree = ET.parse(self.manifest_path)
        root = tree.getroot()

        # Check namespaces
        namespaces = dict([node for _, node in ET.iterparse(str(self.manifest_path), events=['start-ns'])])
        self.assertIn("uap2", namespaces)
        self.assertEqual(namespaces["uap2"], "http://schemas.microsoft.com/appx/manifest/uap/windows10/2")
        self.assertIn("uap3", namespaces)
        self.assertEqual(namespaces["uap3"], "http://schemas.microsoft.com/appx/manifest/uap/windows10/3")

        # Check extensions
        ns = {
            "p": "http://schemas.microsoft.com/appx/manifest/foundation/windows10",
            "uap": "http://schemas.microsoft.com/appx/manifest/uap/windows10",
            "uap2": "http://schemas.microsoft.com/appx/manifest/uap/windows10/2",
            "uap3": "http://schemas.microsoft.com/appx/manifest/uap/windows10/3",
            "desktop6": "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6",
            "rescap": "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities",
        }

        extensions = root.findall(".//uap:Extension", ns) + root.findall(".//uap3:Extension", ns)
        categories = [ext.attrib.get("Category") for ext in extensions]
        self.assertIn("windows.fileTypeAssociation", categories)
        self.assertIn("windows.shareTarget", categories)
        self.assertIn("windows.protocol", categories)

        fta = root.find(".//uap3:Extension[@Category='windows.fileTypeAssociation']/uap3:FileTypeAssociation", ns)
        if fta is None:
            fta = root.find(".//uap:Extension[@Category='windows.fileTypeAssociation']/uap:FileTypeAssociation", ns)
        self.assertIsNotNone(fta, "FileTypeAssociation must be declared")
        self.assertEqual(fta.attrib.get("Name"), "video")

        supported_types = [ft.text for ft in fta.findall("uap:SupportedFileTypes/uap:FileType", ns)]
        expected_types = [".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv"]
        for ext in expected_types:
            self.assertIn(ext, supported_types, f"Extension {ext} must be in SupportedFileTypes")

        # Check verb definition
        verb = fta.find("uap2:SupportedVerbs/uap3:Verb", ns)
        self.assertIsNotNone(verb, "Verb must be declared under uap2:SupportedVerbs")
        self.assertEqual(verb.attrib.get("Id"), "DescribeWithVidiStudio")
        self.assertIn('--video "%1"', verb.attrib.get("Parameters", ""))
        self.assertEqual(verb.text, "Describe with Vidi Studio")

        # Check unvirtualized resources capability and disabled write virtualization
        reg_virt = root.find(".//desktop6:RegistryWriteVirtualization", ns)
        self.assertIsNotNone(reg_virt)
        self.assertEqual(reg_virt.text, "disabled")
        unvirt = root.find(".//rescap:Capability[@Name='unvirtualizedResources']", ns)
        self.assertIsNotNone(unvirt)

        # Check protocol definition
        protocol = root.find(".//uap:Extension[@Category='windows.protocol']/uap:Protocol", ns)
        self.assertIsNotNone(protocol, "Protocol must be declared")
        self.assertEqual(protocol.attrib.get("Name"), "vidistudio")

    def test_app_handles_file_and_command_line_activation(self):
        source = self.app_xaml_cs_path.read_text(encoding="utf-8")
        self.assertIn("ExtendedActivationKind.File", source)
        self.assertIn("ExtendedActivationKind.CommandLineLaunch", source)
        self.assertIn("ExtendedActivationKind.Protocol", source)
        self.assertIn("ParseActivationTarget", source)
        self.assertIn("ParseTokens", source)
        self.assertIn("SplitCommandLine", source)
        self.assertIn("ParseProtocolUri", source)
        self.assertIn("OpenVideoFileAsync", source)
        self.assertIn("OpenYouTubeUrlAsync", source)
        self.assertIn("RedirectActivationToAsync", source)

    def test_main_window_has_open_video_and_foreground_helpers(self):
        source = self.main_window_cs_path.read_text(encoding="utf-8")
        self.assertIn("public async Task OpenVideoFileAsync(string videoPath)", source)
        self.assertIn("public async Task OpenYouTubeUrlAsync(string url)", source)
        self.assertIn("public void BringToForeground()", source)
        self.assertIn("ExistingProjectForVideo", source)
        self.assertIn("ExistingProjectForYouTube", source)
        self.assertIn("import-video", source)
        self.assertIn("import-youtube", source)
        # Project creation should be permitted without an API key
        self.assertNotIn("!Model.HasApiKey", source.split("OpenVideoFileAsync")[1].split("ExistingProjectForVideo")[0])
        self.assertIn("ShellIntegration.UpdateContextMenuState", source)

    def test_viewmodel_has_existing_project_matchers(self):
        source = self.view_model_cs_path.read_text(encoding="utf-8")
        self.assertIn("public StudioProject? ExistingProjectForVideo(string filePath)", source)
        self.assertIn("public StudioProject? ExistingProjectForYouTube(string rawUrl)", source)

    def test_shell_integration_service_implements_registry_context_menu_and_unrestricted_explorer(self):
        source = self.shell_integration_cs_path.read_text(encoding="utf-8")
        self.assertIn("public static class ShellIntegration", source)
        self.assertIn("public static void RegisterContextMenu(string? exePath = null)", source)
        self.assertIn("public static void UnregisterContextMenu()", source)
        self.assertIn("public static bool IsContextMenuRegistered()", source)
        self.assertIn("public static void UpdateContextMenuState", source)
        self.assertIn(r"Software\Classes\SystemFileAssociations\video\shell", source)
        self.assertIn("DescribeWithVidiStudio", source)
        self.assertIn("Describe with Vidi Studio", source)
        self.assertIn("SHChangeNotify", source)
        self.assertIn("AppliesTo", source)
        self.assertIn("VidiStudio.Video", source)
        self.assertIn("--video", source)
        self.assertIn('DeleteValue("LegacyDisable"', source)

    def test_powershell_registration_script_exists_and_implements_describe_menu(self):
        script_path = self.root / "script" / "register-context-menu.ps1"
        self.assertTrue(script_path.is_file(), "register-context-menu.ps1 must exist in script/")
        source = script_path.read_text(encoding="utf-8")
        self.assertIn("DescribeWithVidiStudio", source)
        self.assertIn("Describe with Vidi Studio", source)
        self.assertIn("SHChangeNotify", source)
        self.assertIn("AppliesTo", source)
        self.assertIn("VidiStudio.Video", source)
        self.assertIn("OpenWithProgids", source)
        self.assertIn("OpenWithList", source)
        self.assertIn("$Unregister", source)

    def test_in_app_context_menu_and_ai_gating(self):
        source = self.main_window_commands_cs_path.read_text(encoding="utf-8")
        # Empty space context menu in project browser allows project creation without an API key
        self.assertIn("New project from video…", source)
        self.assertIn("New project from YouTube…", source)
        self.assertIn("IsEnabled = Model.Idle", source)

        # In-app project AI actions are gated on activeApiKey and on the
        # project still having a readable manifest and an available folder.
        self.assertIn("activeApiKey = Model.HasApiKey && !Model.IsApiKeyInvalid", source)
        self.assertIn("usable = !project.IntegrityPlaceholder", source)
        self.assertIn('Item("Export", Export_Click, activeApiKey && usable)', source)
        self.assertIn('Item("Rename", Rename_Click, usable)', source)
        self.assertIn('project.Integrity?.IsDamaged == true ? "Delete damaged project…"', source)

        # In-app segment actions
        self.assertIn("canSynthesize = Model.CurrentNarrationEngine() != \"gemini\" || (Model.HasApiKey && !Model.IsApiKeyInvalid)", source)
        self.assertIn('Item("Rewrite description to fit", Rewrite_Click, Model.HasApiKey && !Model.IsApiKeyInvalid)', source)
        self.assertIn('Item("Re-Analyze this segment", async (_, _) => await EditSegment(segment), Model.HasApiKey && !Model.IsApiKeyInvalid)', source)

    def test_startup_logs_launch_milestones_for_silent_failures(self):
        source = self.app_xaml_cs_path.read_text(encoding="utf-8")
        self.assertIn("LogStartup", source)
        self.assertIn("App constructed", source)
        self.assertIn("OnLaunched", source)
        self.assertIn("Main window created.", source)
        self.assertIn("Startup failed: ", source)
        self.assertIn("diagnostics.log", (self.root / "Vidi Studio Windows" / "Services" / "EngineClient.cs").read_text(encoding="utf-8"))

    def test_macos_share_extension_and_content_view_allow_project_creation_without_api_key(self):
        if self.mac_share_vc_path.is_file():
            share_src = self.mac_share_vc_path.read_text(encoding="utf-8")
            self.assertNotIn("apiKeyRequired", share_src)

        if self.mac_content_view_path.is_file():
            content_src = self.mac_content_view_path.read_text(encoding="utf-8")
            self.assertNotIn("guard studio.hasValidApiKey else", content_src)


if __name__ == "__main__":
    unittest.main()

