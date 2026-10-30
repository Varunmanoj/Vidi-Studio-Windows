using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Vidi_Studio_Windows.Services;

internal static class ProjectListVisibilityHarness
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "vidistudio-list-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var previousConfig = Environment.GetEnvironmentVariable("VIDISTUDIO_CONFIG");
        try
        {
            var libraryProject = Path.Combine(root, "library", "project");
            var externalProject = Path.Combine(root, "external", "project");
            foreach (var folder in new[] { libraryProject, externalProject })
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "project.json"), "{\"id\":\"fixture\"}");
                File.WriteAllBytes(Path.Combine(folder, "source.mp4"), new byte[] { 1, 2, 3 });
                File.WriteAllText(Path.Combine(folder, "narration.wav"), "narration fixture");
                File.WriteAllText(Path.Combine(folder, "export.srt"), "subtitle fixture");
            }
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path,
                path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path)));
            var config = Path.Combine(root, "config.json");
            Environment.SetEnvironmentVariable("VIDISTUDIO_CONFIG", config);
            var settings = new SettingsStore();
            settings.Set("libraryPath", Path.GetDirectoryName(libraryProject)!);
            settings.Data["projectLocations"] = new JsonArray(externalProject);
            settings.Save();
            var visibility = new ProjectListVisibility(settings);
            Require(!visibility.IsRemoved(libraryProject), "A new project starts hidden.");
            visibility.Remove(libraryProject);
            visibility.Remove(externalProject);
            visibility.Remove(libraryProject + Path.DirectorySeparatorChar);
            Require(settings.Data["removedProjectFolders"]!.AsArray().Count == 2, "Path aliases created duplicate entries.");
            settings = new SettingsStore();
            visibility = new ProjectListVisibility(settings);
            Require(visibility.IsRemoved(libraryProject) && visibility.IsRemoved(externalProject), "Removal did not survive settings reload/restart.");
            Require(visibility.IsRemoved(libraryProject.ToUpperInvariant()), "Windows path casing changed visibility.");
            Require(!visibility.IsRemoved(Path.Combine(root, "other", "project")), "Removing one project hid a different same-name project.");
            visibility.Restore(libraryProject);
            visibility = new ProjectListVisibility(new SettingsStore());
            Require(!visibility.IsRemoved(libraryProject) && visibility.IsRemoved(externalProject), "Reopening did not restore only the selected project.");
            Require(settings.Data["projectLocations"]![0]!.GetValue<string>() == externalProject, "External project location was lost.");
            foreach (var entry in files)
                Require(File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value.Bytes) && File.GetLastWriteTimeUtc(entry.Key) == entry.Value.Modified,
                    "Project files changed during remove/restore: " + entry.Key);
            Require(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == files.Count + 1, "Project contents changed.");
            settings = new SettingsStore();
            visibility = new ProjectListVisibility(settings);
            var saved = File.ReadAllBytes(config);
            Environment.SetEnvironmentVariable("VIDISTUDIO_CONFIG", root); // Force a settings-write failure.
            var failed = false;
            try { visibility.Remove(libraryProject); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Require(failed && !visibility.IsRemoved(libraryProject), "A failed save changed in-memory visibility.");
            Require(File.ReadAllBytes(config).SequenceEqual(saved), "A failed save changed persisted preferences.");
            Console.WriteLine("PASS: library/external visibility persists, reopening restores one folder, path aliases are handled, project files stay byte-for-byte unchanged, and failed saves roll back.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VIDISTUDIO_CONFIG", previousConfig);
            Directory.Delete(root, true);
        }
    }
}

// The harness never accesses Windows Credential Manager or real user credentials.
namespace Vidi_Studio_Windows.Services
{
    public static class CredentialStore
    {
        public static string? Read() => null;
        public static bool Write(string value) => true;
    }
}
