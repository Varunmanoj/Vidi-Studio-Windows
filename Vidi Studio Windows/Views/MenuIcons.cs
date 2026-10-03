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
            "Show / hide preview" or "Play video" or "Seek to segment" => "\uE714",
            "Show / hide timeline" => "\uE823",
            "Full-screen video" => "\uE740",
            "Transcript" or "Generate transcript" or "AD Transcript" => "\uE8A5",
            "Summary" or "Generate summary" => "\uE8A4",
            "Descriptions" or "Generate descriptions" => "\uE767",
            "AD Overview" => "\uE9D9",
            "Analyze video" => "\uE721",
            "Refresh video details" or "Refresh video details and timeline pictures" => "\uE72C",
            "Read frame text" => "\uE8D2",
            "Add segment" => "\uE710",
            "Edit selected" or "Edit segment" or "Rename" => "\uE70F",
            "Select all" => "\uE8B3",
            "Deselect all" => "\uE8B4",
            "Delete selected" or "Delete segment" or "Delete narration" or "Move to Recycle Bin" => "\uE74D",
            "Undo" => "\uE7A7",
            "Redo" => "\uE7A6",
            "Rewrite description to fit" => "\uE8AC",
            "Generate all narration" or "Generate changed narration" or "Generate narration" or "Narration on / off" => "\uE767",
            "Preview narration" or "Play summary" or "Resume summary" => "\uE768",
            "Pause video" or "Pause summary" => "\uE769",
            "Stop summary" => "\uE71A",
            "Rewind five seconds" => "\uE892",
            "Forward five seconds" => "\uE893",
            "Follow active segment" => "\uE81D",
            "Preparing summary…" => "\uE895",
            _ => "\uE712"
        }
    };
}
