"""Run the portable project-list persistence harness with isolated settings."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / 'build/project-list-visibility-test'
BUILD.mkdir(parents=True, exist_ok=True)
sources = [ROOT / 'Vidi Studio Windows/Models/StudioProject.cs',
           ROOT / 'Vidi Studio Windows/Models/ProjectIntegrity.cs',
           ROOT / 'Vidi Studio Windows/Services/SettingsStore.cs',
           ROOT / 'Vidi Studio Windows/Services/ProjectListVisibility.cs',
           ROOT / 'tests/project_list_visibility_harness.cs']
project = BUILD / 'ProjectListVisibility.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                   '<TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                   '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
                   + ''.join(f'<Compile Include="{escape(str(path))}" />' for path in sources)
                   + '</ItemGroup></Project>')
subprocess.run(['dotnet', 'run', '--project', str(project)], check=True)
