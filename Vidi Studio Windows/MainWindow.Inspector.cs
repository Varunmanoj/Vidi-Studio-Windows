using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System.Linq;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
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
            string fallback = section == AudioSourcePanel ? "Audio sources" : "Inspector section";
            string Title() => textHeading?.Text ?? (heading as Grid)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? fallback;
            var expander = new Expander { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            // Preserve the named/dynamic header and the project integrity action.
            if (heading is TextBlock or Grid) { section.Children.Remove(heading); expander.Header = heading; }
            else expander.Header = fallback;
            container.Children.RemoveAt(index);
            expander.Content = section;
            expander.SetBinding(UIElement.VisibilityProperty, new Binding { Source = section, Path = new PropertyPath("Visibility"), Mode = BindingMode.OneWay });
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
            if (textHeading != null) textHeading.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Restore());
            container.Children.Insert(index, expander);
        }
    }
}
