using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace Vidi_Studio_Windows.Services;

internal static class LocalBrowserExtension
{
    internal static void Refresh()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VidiStudio", "BrowserExtension");
        Directory.CreateDirectory(root);
        using var gate = new Mutex(false, @"Local\VidiStudioLocalBrowserExtension");
        try { if (!gate.WaitOne(TimeSpan.FromSeconds(5))) return; }
        catch (AbandonedMutexException) { /* The next publication repairs an interrupted update. */ }
        try
        {
            using var resource = typeof(LocalBrowserExtension).Assembly.GetManifestResourceStream("VidiStudio.BrowserExtension.zip")
                ?? throw new InvalidDataException("The local browser extension is missing.");
            using var archive = new ZipArchive(resource);
            // Publish the manifest last. The running browser uses it as the ready signal.
            foreach (var entry in archive.Entries.OrderBy(e => e.FullName == "manifest.json"))
            {
                var path = Path.Combine(root, entry.FullName);
                using var content = entry.Open();
                using var buffer = new MemoryStream();
                content.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (File.Exists(path) && bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path))) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".updating";
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
        }
        finally { gate.ReleaseMutex(); }
    }
}
