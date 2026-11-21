using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    internal static Border CompactCard(UIElement content)
    {
        var card = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{ThemeResource CardBackgroundFillColorDefaultBrush}\" Padding=\"12,10\" CornerRadius=\"12\"/>");
        card.Child = content; card.Tag = "settings-card"; return card;
    }
    internal static StackPanel CompactGroup(StackPanel parent, string title)
    {
        parent.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0,12,0,4) }));
        var rows = new StackPanel { Spacing = 0 }; parent.Children.Add(CompactCard(new AccessibilityGroup(title + " settings") { Child = rows })); return rows;
    }
    internal static Grid CompactRow(StackPanel rows, string label, FrameworkElement control, bool stretch = false)
    {
        if (rows.Children.Count > 0) rows.Children.Add(GeminiRowDivider());
        return AddSettingsRow(rows,label,control,stretch);
    }
    internal static CompactActionsPanel CompactActions(params FrameworkElement[] controls)
    {
        var row = new CompactActionsPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var control in controls) { control.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(control); }
        return row;
    }
    private static string PromptTitle(string stage) => stage switch {
        "multilingual_media" => "Multilingual Media", "audio_descriptions" => "Audio Descriptions",
        "screen_text" => "Screen Text", "youtube_screen_text" => "YouTube Screen Text",
        "text_rewrite" => "Text Rewrite", "tts_audio" => "Narration Audio", "youtube_transcript" => "YouTube Transcript",
        _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(stage.Replace('_',' '))
    };
    private void BuildDeveloperPrompts(StackPanel panel)
    {
        var rows = CompactGroup(panel,"Developer prompts");
        var order = new[] { "multilingual_media","transcript","youtube_transcript","summary","audio_descriptions","screen_text","youtube_screen_text","text_rewrite","tts_audio" };
        if (draft["prompts"] is not JsonObject prompts) return;
        foreach (var stage in prompts.OrderBy(p => Array.IndexOf(order,p.Key) < 0 ? int.MaxValue : Array.IndexOf(order,p.Key)).ThenBy(p => p.Key)) {
            var fields = new StackPanel { Spacing = 8 };
            if (stage.Value is JsonObject pair) foreach (var entry in pair.ToList()) {
                var label = entry.Key switch { "system_instruction" => "System instruction", "user_prompt" => "User prompt", _ => PromptTitle(entry.Key) };
                var field = new TextBox { Header = label, Text = Services.SettingsStore.GetString(entry.Value), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = entry.Key == "system_instruction" ? 90 : 130 };
                AutomationProperties.SetName(field,PromptTitle(stage.Key)+" "+label);
                var name=entry.Key; field.TextChanged += (_,_) => pair[name]=field.Text; fields.Children.Add(field);
            }
            if (rows.Children.Count > 0) rows.Children.Add(GeminiRowDivider());
            rows.Children.Add(new Expander { Header = PromptTitle(stage.Key), Content = fields, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
    }
    private void BuildStorage(StackPanel panel)
    {
        var location = CompactGroup(panel,"Project storage");
        AddText(location,"Default folder for new projects","libraryPath");
        storageAdvancedControls.Children.Clear();
        var tools = CompactGroup(storageAdvancedControls,"Advanced");
        AddText(tools,"Python executable","pythonPath","python.exe");
        AddExecutablePicker(tools,"FFmpeg executable","ffmpegPath");
        AddExecutablePicker(tools,"FFprobe executable","ffprobePath");
        var repair = new Button { Content = "Repair legacy File Explorer context menu" };
        repair.Click += (_,_) => {
            try { Services.ShellIntegration.RegisterContextMenu(); status.Text="Legacy File Explorer context menu registered for video files."; }
            catch (Exception ex) { status.Text="Could not register the legacy context menu: "+ex.Message; }
        };
        tools.Children.Add(GeminiRowDivider()); tools.Children.Add(repair);
        panel.Children.Add(storageAdvancedControls); UpdateAdvancedControlsVisibility();
        var diagnostics = CompactGroup(panel,"Diagnostics");
        var doctor = new Button { Content = "Check engine and media tools" };
        doctor.Click += async (_,_) => { await model.RunAsync("doctor",needsProject:false); status.Text=model.Status; };
        var logs = new Button { Content = "Open diagnostics folder" };
        logs.Click += async (_,_) => { System.IO.Directory.CreateDirectory(Services.SettingsStore.DirectoryPath); await Windows.System.Launcher.LaunchFolderPathAsync(Services.SettingsStore.DirectoryPath); };
        diagnostics.Children.Add(CompactActions(doctor,logs));
    }
#if DEBUG
    internal async Task VerifyCompactPagesForSmokeTestAsync(Func<int,string,Task> capture)
    {
        var developer = draft["developerMode"]?.DeepClone();
        var originalSection=SelectedSection; var originalTarget=projectTarget; var originalAllow=allowClosedProjectTarget;
        try {
            draft["developerMode"]=true; UpdateDeveloperPromptsVisibility(); SetProjectTarget(model.Project);
            foreach (var section in new[] {0,1,2,4,5,6,7}) {
                NavigateForSmokeTest(section); await Task.Delay(150);
                foreach (var theme in new[] {"dark","light"}) {
                    ApplyWindowTheme(theme); await Task.Delay(100); layout.UpdateLayout();
                    var cards=SpeechItems<Border>(currentSectionPanel!).Where(b => b.Tag as string == "settings-card").ToArray();
                    if (section != 2 && cards.Length == 0) throw new InvalidOperationException("Settings page lacks compact groups: "+section);
                    foreach (var card in cards) {
                        if (card.Background is not SolidColorBrush brush) continue;
                        var c=brush.Color;
                        if (theme=="dark" && (c.R+c.G+c.B)/3.0*c.A/255.0+32*(1-c.A/255.0)>120) throw new InvalidOperationException("Settings card uses a light fill in dark mode: "+section);
                    }
                    foreach (var row in SpeechItems<Grid>(currentSectionPanel!).Where(g => g.Tag as string == "settings-row" && g.Visibility==Visibility.Visible && g.ActualWidth>=480 && g.ActualHeight>0)) {
                        if (row.Children.Count < 2 || row.Children[0] is not TextBlock label || row.Children[1] is not FrameworkElement control || control.Visibility != Visibility.Visible) continue;
                        var parent=control as FrameworkElement; var visible=true;
                        while(parent!=null) { if(parent.Visibility==Visibility.Collapsed) {visible=false;break;} parent=parent.Parent as FrameworkElement; }
                        if(!visible)continue;
                        var l=label.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
                        var f=control.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
                        if(f.X<=l.X || Math.Abs((l.Y+label.ActualHeight/2)-(f.Y+control.ActualHeight/2))>2) throw new InvalidOperationException("Settings label and field are not beside one another: "+section+" "+label.Text);
                    }
                    sectionHost.ChangeView(null,0,null,true); await Task.Delay(80); await capture(section,theme);
                }
            }
        } finally { draft["developerMode"]=developer; UpdateDeveloperPromptsVisibility(); projectTarget=originalTarget; allowClosedProjectTarget=originalAllow; NavigateForSmokeTest(originalSection); }
    }
#endif
}
