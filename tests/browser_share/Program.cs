using System;
using System.IO;
using System.Text.Json;
using Vidi_Studio_Windows.Services;

var rows = JsonSerializer.Deserialize<string?[][]>(File.ReadAllText(args[0]))!;
foreach (var row in rows)
{
    if (SharedYouTubeLink.VideoId(row[0]) != row[1])
        throw new Exception("Unexpected video ID for " + row[0]);
}
Console.WriteLine($"{rows.Length} browser share URLs passed.");
