using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Vidi_Studio_Windows.Views;

public static class MenuIcons
{
    public static FontIcon Create(string action) => new()
    {
        FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16,
        Glyph = action switch
        {
            "Import video" or "New project" => "\uE710",
            "New YouTube project" => "\uE71B",
            "Open project folder" => "\uE8B7",
            "Open editor in new window" => "\uE8A7",
            "Export" => "\uE74E",
            "Settings" => "\uE713",
            "Close window" => "\uE8BB",
            "Help and keyboard shortcuts" => "\uE897",
            "About Vidi Studio" => "\uE946",
            "List / Grid projects" => "\uE80A",
            "Show / hide projects" => "\uE8F1",
            "Show / hide inspector" => "\uE8A0",
            "Show / hide preview" or "Play video" => "\uE714",
            "Seek to segment" or "Skip to segment" or "Forward five seconds" => "\uE893",
            "Show / hide timeline" => "\uE823",
            "Full-screen video" => "\uE740",
            "Transcript" or "Generate transcript" or "AD Transcript" => "\uE8A5",
            "Summary" or "Generate summary" => "\uE8A4",
            "Descriptions" or "Generate descriptions" => "\uE767",
            "AD Overview" => "\uE9D9",
            "Analyze video" or "Re-Analyze this segment" => "\uE721",
            "Refresh video details" or "Refresh video details and timeline pictures" => "\uE72C",
            "Read frame text" => "\uE8D2",
            "Add segment" => "\uE710",
            "Edit selected" or "Edit segment" or "Rename" => "\uE70F",
            "Select all" => "\uE8B3",
            "Deselect all" => "\uE8B4",
            "Delete selected" or "Delete segment" or "Move to Recycle Bin" => "\uE74D",
            "Delete narration" => "\uE74F",
            "Undo" => "\uE7A7",
            "Redo" => "\uE7A6",
            "Rewrite description to fit" => "\uE8AC",
            "Generate all narration" or "Generate changed narration" or "Generate narration" or "Synthesize this segment" or "Regenerate segment narration" or "Narration on / off" or "Narration" => "\uE767",
            "Review Narration Issues…" => "\uE9D9",
            "Review Narration Voices…" => "\uE77B",
            "Generate All AD Audio" or "Regenerate All AD Audio…" or "Generate Missing or Changed AD Audio" => "\uE767",
            "Preview narration" or "Play from segment" or "Play summary" or "Resume summary" => "\uE768",
            "Pause video" or "Pause summary" => "\uE769",
            "Stop summary" => "\uE71A",
            "Previous summary sentence" => "\uE892",
            "Next summary sentence" => "\uE893",
            "Previous summary paragraph" => "\uE70E",
            "Next summary paragraph" => "\uE70D",
            "Rewind five seconds" => "\uE892",
            "Follow active segment" => "\uE81D",
            "Preparing summary…" => "\uE895",
            var s when s.StartsWith("Generating audio", System.StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("Currently generating AD", System.StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("Synthesizing", System.StringComparison.OrdinalIgnoreCase) => "\uE895",
            var s when s.StartsWith("Queued for audio", System.StringComparison.OrdinalIgnoreCase) => "\uE823",
            _ => "\uE712"
        }
    };
}
