using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System.Linq;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private readonly StackPanel inspectorAudioReadiness = new() { Spacing = 6 };
    private readonly FontIcon inspectorActiveAudioReadiness = new() { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };

    private void InitializeADAudioReadiness()
    {
        if (InspectorExportButton.Parent is not StackPanel parent) return;
        var position = parent.Children.IndexOf(InspectorExportButton);
        parent.Children.Remove(InspectorExportButton);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(InspectorExportButton); row.Children.Add(inspectorActiveAudioReadiness);
        parent.Children.Insert(position, row); parent.Children.Insert(position + 1, inspectorAudioReadiness);
    }
    private static void SetADAudioIcon(FontIcon icon, Models.ADAudioReadiness status)
    {
        SetInspectorStatusIcon(icon, status.Complete, status.AccessibleName);
    }
    private static void SetInspectorStatusIcon(FontIcon icon, bool complete, string accessibleName)
    {
        icon.Glyph = complete ? "\uE73E" : "\uE711";
        icon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(complete ? Windows.UI.Color.FromArgb(255, 32, 148, 71) : Windows.UI.Color.FromArgb(255, 210, 38, 48));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(icon, accessibleName);
        ToolTipService.SetToolTip(icon, accessibleName);
    }
    private static FrameworkElement ADAudioReadinessRow(Models.ADAudioReadiness status)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var icon = new FontIcon { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        SetADAudioIcon(icon, status);
        row.Children.Add(icon); row.Children.Add(new TextBlock { Text = status.Title, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = icon.Foreground });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, status.AccessibleName);
        ToolTipService.SetToolTip(row, status.AccessibleName);
        return row;
    }
    private void UpdateADAudioReadiness(Models.StudioProject project)
    {
        var section = Section.SelectedIndex;
        var view = section == 3 ? project.OverviewProject : project;
        var active = view.ContentStatus(section, view.SelectedLanguage);
        inspectorActiveAudioReadiness.Visibility = active == null ? Visibility.Collapsed : Visibility.Visible;
        if (active != null) SetInspectorStatusIcon(inspectorActiveAudioReadiness, active.Complete, active.AccessibleName);
        inspectorAudioReadiness.Children.Clear();
        foreach (var status in view.ContentStatuses(section)) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var icon = new FontIcon { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            SetInspectorStatusIcon(icon, status.Complete, status.AccessibleName);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = status.Title, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = icon.Foreground });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, status.AccessibleName);
            ToolTipService.SetToolTip(row, status.AccessibleName);
            inspectorAudioReadiness.Children.Add(row);
        }
    }

    private string inspectorVoiceRequest = "";
    private string inspectorVoiceLanguage = "Unknown";

    private void UpdateInspectorSpeechInfo()
    {
        if (Model.Project is null) return;
        var engineID = Model.CurrentNarrationEngine();
        var voiceID = Model.Settings.Get("systemVoice", "").Trim();
        var host = Model.Settings.Get("sapiHostPath");
        if (string.IsNullOrWhiteSpace(host)) host = Model.Engine.SpeechHost;
        var request = engineID + "|" + host + "|" + voiceID;
        if (request != inspectorVoiceRequest)
        {
            inspectorVoiceRequest = request;
            inspectorVoiceLanguage = "Unknown";
            if (engineID is not ("piper" or "gemini")) _ = LoadInspectorVoiceLanguageAsync(request, host, engineID, voiceID);
        }
        var language = engineID == "gemini" ? "Multilingual"
            : engineID == "piper" ? Services.PiperService.Catalog.FirstOrDefault(v => v.Id == Model.CurrentNarrationVoice())?.LanguageLabel ?? "Unknown"
            : inspectorVoiceLanguage;
        var engine = Models.NarrationVoiceFormatting.EngineDisplayName(engineID);
        var voice = Models.NarrationVoiceFormatting.VoiceDisplayName(Model.CurrentNarrationVoice());
        InspectorSpeechEngine.Text = "Engine: " + engine;
        InspectorSpeechLanguage.Text = "Language: " + language;
        InspectorSpeechVoice.Text = "Voice: " + voice;
        OverviewVoice.Text = $"Speech engine: {engine}\nLanguage: {language}\nSelected voice: {voice}";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(OverviewVoice, $"Engine: {engine}, Language: {language}, Voice: {voice}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(InspectorSpeechInfo, $"Engine: {engine}, Language: {language}, Voice: {voice}");
    }

    private async System.Threading.Tasks.Task LoadInspectorVoiceLanguageAsync(string request, string host, string engine, string voiceID)
    {
        try
        {
            var voices = await Services.SpeechService.VoicesAsync(host, engine);
            if (windowClosed || request != inspectorVoiceRequest) return;
            var voice = string.IsNullOrEmpty(voiceID) ? voices.FirstOrDefault(v => v.IsDefault)
                : voices.FirstOrDefault(v => v.Id == voiceID);
            if (!string.IsNullOrWhiteSpace(voice?.Language))
            {
                try { inspectorVoiceLanguage = System.Globalization.CultureInfo.GetCultureInfo(voice.Language).DisplayName; }
                catch (System.Globalization.CultureNotFoundException) { inspectorVoiceLanguage = voice.Language; }
            }
            UpdateInspectorSpeechInfo();
        }
        catch (System.Exception ex) { Model.Engine.Log(ex.ToString()); }
    }

    private void InitializeInspectorSections()
    {
        if (InspectorScrollViewer.Content is not StackPanel container) return;
        foreach (var section in container.Children.OfType<StackPanel>().ToArray())
        {
            var index = container.Children.IndexOf(section);
            FrameworkElement? heading = section.Children.FirstOrDefault() as FrameworkElement;
            var textHeading = heading as TextBlock;
            string fallback = section == LanguageResultsPanel ? "Language and Saved Results" : section == AudioSourcePanel ? "Audio sources" : "Inspector section";
            string Title() => textHeading?.Text ?? (heading as Grid)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? fallback;
            var expander = new Expander { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            // Preserve the named/dynamic header and the project integrity action.
            if (heading is TextBlock or Grid) { section.Children.Remove(heading); expander.Header = heading; }
            else expander.Header = fallback;
            container.Children.RemoveAt(index);
            expander.Content = section;
            expander.SetBinding(UIElement.VisibilityProperty, new Binding { Source = section, Path = new PropertyPath("Visibility"), Mode = BindingMode.OneWay });
            void UpdateAccessibleHeader()
            {
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(expander, Title());
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(expander, "inspector-section-" + (section.Name.Length > 0 ? section.Name : Title()));
            }
            UpdateAccessibleHeader();
            bool restoring = false;
            void Restore()
            {
                restoring = true;
                try { expander.IsExpanded = Model.Settings.GetBool("inspectorSection." + Title() + ".expanded", true); }
                finally { restoring = false; }
            }
            void Save(bool expanded)
            {
                if (restoring) return;
                Model.Settings.Set("inspectorSection." + Title() + ".expanded", expanded); Model.Settings.Save();
            }
            Restore();
            expander.Expanding += (_, _) => Save(true);
            expander.Collapsed += (_, _) => Save(false);
            if (textHeading != null) textHeading.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => { UpdateAccessibleHeader(); Restore(); });
            container.Children.Insert(index, expander);
        }
    }
}
