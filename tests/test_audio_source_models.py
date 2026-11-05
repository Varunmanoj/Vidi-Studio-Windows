"""Execute the production portable C# audio-source models without a WinUI host."""
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(shutil.which('dotnet'), 'A .NET SDK is required for portable model checks.')
class AudioSourceModels(unittest.TestCase):
    def test_portable_source_preferences_and_timing_approvals(self):
        sdk = subprocess.check_output(['dotnet', '--version'], text=True).strip().split('.')[0]
        with tempfile.TemporaryDirectory(prefix='vidistudio-audio-models-') as temporary:
            folder = Path(temporary)
            files = ['StudioProject.cs', 'ProjectIntegrity.cs', 'AudioSources.cs', 'LanguageResult.cs']
            includes = ''.join(f'<Compile Include="{escape(str(ROOT / "Vidi Studio Windows/Models" / name))}" />' for name in files)
            (folder/'Models.csproj').write_text(
                f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net{sdk}.0</TargetFramework>'
                '<OutputType>Exe</OutputType><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit>'
                '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
            (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
            (folder/'Program.cs').write_text(r'''
using System;
using System.Text.Json;
using System.IO;
using Vidi_Studio_Windows.Models;
var json = """
{"sourceType":"youtube","duration":4,"originalAudio":{"path":"imports/a.wav","sourceName":"Creator soundtrack.wav","duration":3,"sha256":"abc","timingAccepted":true,"confirmedVideoDuration":4},"audioSources":{"export":"imported","playback":"youtube","transcription":"imported"},"transcriptAudioSource":{"origin":"mixed"},"exportAudioSources":{"exports/a.wav":{"origin":"imported","sourceName":"Creator soundtrack.wav"}}}
""";
var project = JsonSerializer.Deserialize<StudioProject>(json, Json.Options)!;
if (project.OriginalAudio is not { } asset || asset.NeedsConfirmation(4) || !asset.NeedsConfirmation(5)) throw new Exception("Duration approval changed");
if (asset.NeedsConfirmation(3.1)) throw new Exception("0.1-second tolerance changed");
if (project.AudioSources?.Export != "imported" || project.AudioSources.Playback != "youtube" || project.AudioSources.Transcription != "imported") throw new Exception("Source preferences are not independent");
if (project.TranscriptAudioSource?.Label != "Multiple audio sources") throw new Exception("Mixed transcript source lost");
var encoded = JsonSerializer.Serialize(project, Json.Options);
using var doc = JsonDocument.Parse(encoded);
if (doc.RootElement.GetProperty("originalAudio").GetProperty("sha256").GetString() != "abc") throw new Exception("Portable hash field changed");
if (doc.RootElement.GetProperty("exportAudioSources").GetProperty("exports/a.wav").GetProperty("sourceName").GetString() != "Creator soundtrack.wav") throw new Exception("Export source lost");
var legacy = JsonSerializer.Deserialize<StudioProject>("{\"sourceType\":\"youtube\"}", Json.Options)!;
if (legacy.OriginalAudio != null || legacy.AudioSources != null) throw new Exception("Legacy project compatibility changed");
var folder = Path.Combine(Path.GetTempPath(), "vidi-view-model-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
try {
    var manifest = """
    {"id":"view","name":"View","selectedLanguage":"auto","sectionLanguages":{"summary":"hi"},"summary":"Hindi stale projection","languageResults":[{"id":"a","kind":"summary","language":"auto","state":"saved","createdAt":1,"payload":{"summary":"Auto saved","summaryOrigin":"manual"}},{"id":"h","kind":"summary","language":"hi","state":"saved","createdAt":2,"payload":{"summary":"Hindi saved","summaryOrigin":"imported"}}]}
    """;
    File.WriteAllText(Path.Combine(folder, "project.json"), manifest);
    var viewed = StudioProject.Load(folder);
    if (viewed.Summary != "Auto saved" || viewed.SummaryOrigin != "manual" || viewed.SectionLanguage("summary") != "hi") throw new Exception("Viewing projection must preserve target and restore saved provenance");
    File.WriteAllText(Path.Combine(folder, "project.json"), manifest.Replace("\"selectedLanguage\":\"auto\"", "\"selectedLanguage\":\"hi\""));
    if (StudioProject.Load(folder).Summary != "Hindi saved") throw new Exception("Viewing language must switch saved content on reopening");
} finally { Directory.Delete(folder, true); }
var reportProject = new StudioProject { SelectedLanguage = "hi", OverviewLanguage = "auto", NarrationEngine = "old", NarrationVoice = "old voice", DescriptionTimingAudit = new() { Count = 9 }, LanguageResults = new() { new LanguageResult { Id = "hi", Kind = "descriptions", Language = "hi", Payload = JsonSerializer.SerializeToElement(new { cues = new object[0], descriptionTimingAudit = new { count = 2, segments = new object[0] } }) } } };
var overview = reportProject.OverviewProject;
if (overview.DescriptionTimingIssueCount != 2 || overview.NarrationVoice != null) throw new Exception("Overview must use its own timing audit and narration settings");
reportProject.SelectedLanguage = "ta";
if (reportProject.OverviewProject.DescriptionTimingIssueCount != 0 || reportProject.OverviewProject.NarrationEngine != null) throw new Exception("An empty viewing language must not inherit another language's metadata");
Console.WriteLine("PASS: portable C# audio sources, timing approval, legacy decoding, and export provenance");
''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder/'Models.csproj'), '--verbosity', 'quiet'],
                                    capture_output=True, text=True, timeout=90)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: portable C# audio sources', result.stdout)


if __name__ == '__main__':
    unittest.main()
