using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private string sarvamVoiceSearch = "", sarvamVoiceStyle = "all", sarvamVoiceSort = "name";
    private void BuildSarvamVoiceBrowser(JsonObject profile, string modelId)
    {
        var language = SettingsStore.GetString(profile["language"],"en-IN");
        var voices = SarvamSpeech.Catalog.Voices.Where(v => v.Model == modelId && v.Languages.Contains(language)).ToArray();
        var browser = new StackPanel { Spacing = 10 };
        AutomationProperties.SetAutomationId(browser,"sarvam-voice-browser"); sarvamControls.Children.Add(browser);
        browser.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = "Voices", FontSize = 16 }));
        var styles = new[] {("all","All styles")}.Concat(voices.GroupBy(v => v.Style).Select(group => (group.Key,group.First().StyleLabel.Length > 0 ? group.First().StyleLabel : "Standard")).OrderBy(option => option.Item2));
        var rows = new StackPanel { Spacing = 10 };
        var selected = new TextBlock();
        void Render()
        {
            var selectedId = SettingsStore.GetString(profile["voice"]);
            selected.Text = "Selected: " + (voices.FirstOrDefault(v => v.Id == selectedId)?.Label ?? "Selected voice unavailable");
            var query = sarvamVoiceSearch.Trim();
            var filtered = voices.Where(v => (sarvamVoiceStyle == "all" || v.Style == sarvamVoiceStyle) &&
                (query.Length == 0 || (v.Label + " " + string.Join(" ",v.Languages.Select(code => SarvamSpeech.Catalog.Languages.GetValueOrDefault(code,code)))).Contains(query,StringComparison.CurrentCultureIgnoreCase)));
            var families = filtered.GroupBy(v => v.Name);
            var ordered = sarvamVoiceSort == "descending" ? families.OrderByDescending(group => group.Key,StringComparer.CurrentCultureIgnoreCase)
                : sarvamVoiceSort == "style" ? families.OrderBy(group => group.OrderBy(v => v.StyleLabel).First().StyleLabel,StringComparer.CurrentCultureIgnoreCase).ThenBy(group => group.Key,StringComparer.CurrentCultureIgnoreCase)
                : families.OrderBy(group => group.Key,StringComparer.CurrentCultureIgnoreCase);
            rows.Children.Clear(); sarvamSampleRings.Clear();
            foreach (var family in ordered)
            {
                rows.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = family.Key, FontSize = 16 }));
                foreach (var voice in family.OrderBy(v => v.Label,StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = new StackPanel { Spacing = 6 };
                    var layout = new Grid { ColumnSpacing = 8 };
                    layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
                    layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(layout);
                    var label = (voice.StyleLabel.Length == 0 ? "Standard" : voice.StyleLabel) + " — " + string.Join(", ",voice.Languages.Select(code => SarvamSpeech.Catalog.Languages.GetValueOrDefault(code,code)));
                    var choice = new RadioButton { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, IsChecked = selectedId == voice.Id, GroupName = "sarvam-voice-variants" };
                    AutomationProperties.SetName(choice,"Select " + voice.Label + ", " + string.Join(", ",voice.Languages.Select(code => SarvamSpeech.Catalog.Languages.GetValueOrDefault(code,code))));
                    AutomationProperties.SetAutomationId(choice,"sarvam-voice-" + voice.Id);
                    void SelectVoice() {
                        if (SettingsStore.GetString(profile["voice"]) == voice.Id) return;
                        StopSarvamDemo(); profile["voice"] = voice.Id;
                        if (!voice.Languages.Contains(SettingsStore.GetString(profile["language"]))) profile["language"] = voice.Languages[0];
                        BuildSarvamSettings();
                    }
                    choice.Checked += (_,_) => SelectVoice();
                    var menu = MenuAccessKeys.Create();
                    var select = new ToggleMenuFlyoutItem { Text = "Select " + voice.Label, IsChecked = selectedId == voice.Id };
                    select.Click += (_,_) => SelectVoice(); menu.Items.Add(select); choice.ContextFlyout = menu;
                    layout.Children.Add(choice);
                    var actions = new StackPanel { Spacing = 4 };
                    Grid.SetColumn(actions,1); layout.Children.Add(actions);
                    var ring = new ProgressRing { Width = 18, Height = 18, IsActive = false, Visibility = Visibility.Collapsed };
                    AutomationProperties.SetName(ring,"Preparing sample of " + voice.Label); sarvamSampleRings[voice.Id] = ring; actions.Children.Add(ring);
                    var status = new TextBlock();
                    var playSample = new Button { Content = "Play Sample", IsEnabled = CanPlaySarvamSample };
                    AutomationProperties.SetAutomationId(playSample,"sarvam-row-sample-" + voice.Id);
                    playSample.Click += (_,_) => {
                        if (sarvamDemoActive && sarvamSampleId == voice.Id) StopSarvamDemo();
                        else if (CanPlaySarvamSample) _ = PlaySarvamDemoAsync(voice,language,status);
                    };
                    SetVoiceSampleMenu(playSample,"sarvam-row-sample-" + voice.Id,() => CanPlaySarvamSample,() => _ = PlaySarvamDemoAsync(voice,language,status),() => sarvamDemoActive && sarvamSampleId == voice.Id,StopSarvamDemo);
                    IconButtonContent.Set(playSample,"Play Sample",Symbol.Play);
                    actions.Children.Add(playSample);
                    actions.Children.Add(status); rows.Children.Add(row);
                }
            }
            UpdateSarvamSampleControls();
            if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "No matching voices" });
        }
        SpeechChoiceControl(browser,"Style","sarvam-style-filter",sarvamVoiceStyle,styles,value => { sarvamVoiceStyle = value; Render(); });
        browser.Children.Add(new TextBlock { Text = "Search voices" });
        var searchBox = new Grid { ColumnSpacing = 8 };
        AutomationProperties.SetAutomationId(searchBox,"sarvam-search-box");
        searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        searchBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var searchIcon = new FontIcon { Glyph = "\uE721", FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(searchIcon,Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        searchBox.Children.Add(searchIcon);
        var search = new TextBox { PlaceholderText = "Search by voice, style or language", Text = sarvamVoiceSearch };
        Grid.SetColumn(search,1); searchBox.Children.Add(search);
        AutomationProperties.SetName(search,"Search voices");
        AutomationProperties.SetAutomationId(search,"sarvam-voice-search");
        Button? clearButton = null;
        search.TextChanged += (_,_) => { sarvamVoiceSearch = search.Text; if (clearButton != null) clearButton.IsEnabled = search.Text.Length > 0; Render(); };
        search.KeyDown += (_,args) => { if (args.Key == Windows.System.VirtualKey.Escape) { search.Text = ""; args.Handled = true; } };
        var searchMenu = MenuAccessKeys.Create();
        var focus = new MenuFlyoutItem { Text = "Search voices", Tag = "sarvam-search-focus" }; focus.Click += (_,_) => search.Focus(FocusState.Programmatic); searchMenu.Items.Add(focus);
        var clear = new MenuFlyoutItem { Text = "Clear voice search", Tag = "sarvam-search-clear" }; clear.Click += (_,_) => search.Text = ""; searchMenu.Items.Add(clear); search.ContextFlyout = searchMenu;
        var clearHost = new StackPanel(); Grid.SetColumn(clearHost,2); searchBox.Children.Add(clearHost);
        clearButton = SpeechAction(clearHost,"Clear voice search","sarvam-search-clear-icon",() => search.Text = "",search.Text.Length > 0);
        clearButton.Content = new FontIcon { Glyph = "\uE711", FontSize = 14 };
        AutomationProperties.SetName(clearButton,"Clear voice search"); ToolTipService.SetToolTip(clearButton,"Clear voice search");
        browser.Children.Add(searchBox);
        SpeechChoiceControl(browser,"Sort by","sarvam-voice-sort",sarvamVoiceSort,new[] {("name","Voice name (A–Z)"),("descending","Voice name (Z–A)"),("style","Style (A–Z)")},value => { sarvamVoiceSort = value; Render(); });
        browser.Children.Add(selected);
        var scroll = new ScrollViewer { Content = rows, MaxHeight = 320, MinHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(scroll,"sarvam-voice"); browser.Children.Add(scroll);
        SpeechAction(browser,"Reset Voice Filters","sarvam-filters-reset",() => { sarvamVoiceSearch = ""; sarvamVoiceStyle = "all"; sarvamVoiceSort = "name"; BuildSarvamSettings(); });
        Render();
    }
}
