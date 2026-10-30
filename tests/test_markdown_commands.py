import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COMMANDS = ROOT / 'Vidi Studio Windows' / 'Models' / 'MarkdownCommands.cs'


class MarkdownCommands(unittest.TestCase):
    def test_native_transformations_match_the_documented_behaviour(self):
        if shutil.which('dotnet') is None:
            self.skipTest('dotnet is required to compile the Windows Markdown transformer')
        project = """<Project Sdk=\"Microsoft.NET.Sdk\">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><Compile Include=\"%s\" Link=\"MarkdownCommands.cs\"/></ItemGroup>
</Project>""" % COMMANDS
        program = r'''
using Vidi_Studio_Windows.Models;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var edit = MarkdownCommands.Apply("hello", 0, 5, "bold");
Check(edit.Text == "**hello**", edit.Text);
Check(MarkdownCommands.Apply(edit.Text, edit.Start, edit.Length, "bold").Text == "hello", "bold toggle");
Check(MarkdownCommands.Apply("Title", 0, 0, "heading2").Text == "## Title", "heading");
Check(MarkdownCommands.Apply("a\nb", 0, 3, "bulletedList").Text == "- a\n- b", "bullets");
Check(MarkdownCommands.Apply("a\nb", 0, 3, "numberedList").Text == "1. a\n2. b", "numbers");
Check(MarkdownCommands.Apply("a", 0, 1, "taskList").Text == "- [ ] a", "task");
Check(MarkdownCommands.Apply("text", 0, 4, "link").Text == "[text](url)", "link");
Check(MarkdownCommands.Apply("alt", 0, 3, "image").Text == "![alt](url)", "image");
Check(MarkdownCommands.Apply("", 0, 0, "table").Text.Contains("| Column 1 |"), "table");
Check(MarkdownCommands.ListContinuation("- item") == "- ", "list continuation");
Check(MarkdownCommands.ListContinuation("1. item") == "2. ", "number continuation");
Check(MarkdownCommands.Apply("", 0, 0, "heading1").Text == "# ", "empty heading");
Check(MarkdownCommands.Apply("\nnext", 0, 0, "heading1").Text == "# \nnext", "first empty line");
Check(MarkdownCommands.Apply("first\nsecond", 0, 6, "bulletedList").Text == "- first\nsecond", "selection excludes next line");
Check(MarkdownCommands.Apply("first\n", 6, 0, "heading2").Text == "first\n## ", "last empty line");
Check(MarkdownCommands.Apply("a\n\nb", 0, 4, "bulletedList").Text == "- a\n- \n- b", "blank lines retained");
Check(MarkdownCommands.ListContinuation("  + item") == "  + ", "nested marker retained");
Check(MarkdownCommands.ListContinuation("  - [x] done") == "  - [ ] ", "new task unchecked");
Check(MarkdownCommands.IsEmptyListItem("3. "), "empty numbered item exits list");
Check(MarkdownCommands.ListContinuation("999999999999999999999. item") == null, "huge number does not crash");
Console.WriteLine("PASS: Windows Markdown transformations");
'''
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            (folder / 'markdown-test.csproj').write_text(project, encoding='utf-8')
            (folder / 'Program.cs').write_text(program, encoding='utf-8')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'markdown-test.csproj')],
                                    capture_output=True, text=True, timeout=180)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: Windows Markdown transformations', result.stdout)


if __name__ == '__main__':
    unittest.main()
