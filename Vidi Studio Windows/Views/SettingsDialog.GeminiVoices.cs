using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private StackPanel geminiVoiceControls = new() { Spacing = 8 };
    private TextBlock geminiConnectionLabel = new();
    private TextBlock geminiVoiceLabel = new();
    private StackPanel geminiCatalogControls = new();
    private TextBlock geminiExpressStatus = new();
    private Grid? installedVoiceRow;
    private Button? refreshVoiceList;
    private TextBox? narrationSampleText;
    private Action? updateGeminiBrowserSamples;

    private void BuildGeminiVoiceControls(StackPanel parent)
    {
        geminiVoiceControls = new StackPanel { Spacing = 8 };
        geminiConnectionLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };
        geminiVoiceLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };
        geminiCatalogControls = new StackPanel { Spacing = 8 };
        geminiExpressStatus = new TextBlock { Text = "Gemini narration requires a different AI connection.", TextWrapping = TextWrapping.Wrap };
        CompactRow(geminiVoiceControls, "AI connection", geminiConnectionLabel, true);
        geminiVoiceControls.Children.Add(geminiExpressStatus);
        BuildGeminiNarrationCatalog(geminiCatalogControls);
        geminiVoiceControls.Children.Add(geminiCatalogControls);
        parent.Children.Add(geminiVoiceControls);
    }

    private void UpdateGeminiVoiceControls(string provider)
    {
        var gemini = provider == "gemini";
        geminiVoiceControls.Visibility = gemini ? Visibility.Visible : Visibility.Collapsed;
        if (installedVoiceRow != null) installedVoiceRow.Visibility = gemini ? Visibility.Collapsed : Visibility.Visible;
        if (refreshVoiceList != null) refreshVoiceList.Visibility = gemini ? Visibility.Collapsed : Visibility.Visible;
        if (!gemini) return;
        geminiConnectionLabel.Text = OnlineRequirements.Title(DraftAIConnection);
        geminiVoiceLabel.Text = "Selected: " + GeminiVoices.Selected(draft).Name;
        geminiCatalogControls.Visibility = DraftAIConnection == AiConnection.Express ? Visibility.Collapsed : Visibility.Visible;
        geminiExpressStatus.Visibility = DraftAIConnection == AiConnection.Express ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GeminiVoiceLanguage(Voice voice)
    {
        if (voice.Language.Length == 0) return GeminiVoices.IsPrebuilt(voice.Id) ? "Multilingual" : "Library voice";
        try { return CultureInfo.GetCultureInfo(voice.Language).DisplayName; }
        catch (CultureNotFoundException) { return voice.Language; }
    }

    private void BuildGeminiNarrationCatalog(StackPanel panel)
    {
        var connection = DraftAIConnection;
        var search = new TextBox { PlaceholderText = "Search by voice, style or language" };
        AutomationProperties.SetName(search, "Search voices");
        AutomationProperties.SetAutomationId(search, "gemini-voice-search");
        var languagesPanel = new StackPanel();
        panel.Children.Add(languagesPanel);
        FullWidthVoiceSearch(search);
        panel.Children.Add(search);
        var filters = new StackPanel();
        panel.Children.Add(filters);
        var libraryStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(libraryStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var rows = new StackPanel { Spacing = 8 };
        var rowActions = new List<Action>();
        var library = new List<Voice>();
        var searched = "";
        var language = "";
        var style = "";
        var sort = "language";
        string? playingId = null;
        CancellationTokenSource? activeSearch = null;
        bool busy = false;
        int searchRevision = 0;
        bool Ready() => !closed && SettingsStore.GetString(draft["ttsMode"]) == "gemini" && DraftAIConnection == connection && DraftVoiceConnection == connection &&
            ConnectivityService.Shared.Available(OnlineService.Gemini);
        Button searchLibrary = null!;
        searchLibrary = SpeechAction(panel, "Search Voice Library", "gemini-voice-library-search", () => _ = SearchLibraryAsync());
        panel.Children.Add(libraryStatus);
        panel.Children.Add(geminiVoiceLabel);
        panel.Children.Add(new ScrollViewer { Content = rows, MinHeight = 180, MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        SpeechAction(panel, "Reset Voice Filters", "gemini-voice-reset", () =>
        {
            searchRevision++;
            search.Text = ""; language = ""; style = ""; sort = "language"; library.Clear(); searched = "";
            libraryStatus.Text = ""; RebuildFilters(); Render();
        });
        search.TextChanged += (_, _) => Render();
        search.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter) { _ = SearchLibraryAsync(); args.Handled = true; }
        };
        var searchMenu = MenuAccessKeys.Create();
        var focus = new MenuFlyoutItem { Text = "Search voices", Icon = MenuIcons.Create("Search") };
        focus.Click += (_, _) => search.Focus(FocusState.Programmatic);
        var clear = new MenuFlyoutItem { Text = "Clear voice search", Icon = MenuIcons.Create("Clear Search") };
        clear.Click += (_, _) => search.Text = "";
        searchMenu.Opening += (_, _) => clear.IsEnabled = search.Text.Length > 0;
        searchMenu.Items.Add(focus); searchMenu.Items.Add(clear); search.ContextFlyout = searchMenu;
        Action updateSamples = () => { foreach (var update in rowActions) update(); };
        panel.Loaded += (_, _) => { updateGeminiBrowserSamples = updateSamples; Render(); };
        panel.Unloaded += (_, _) =>
        {
            activeSearch?.Cancel();
            if (updateGeminiBrowserSamples == updateSamples) updateGeminiBrowserSamples = null;
        };
        RebuildFilters(); Render();

        IEnumerable<Voice> Options() => GeminiVoices.Prebuilt.Concat(library).Append(GeminiVoices.Selected(draft)).DistinctBy(v => v.Id);

        void RebuildFilters()
        {
            var languages = Options().Select(GeminiVoiceLanguage).Distinct().OrderBy(value => value).Select(value => (value, value)).ToArray();
            if (!languages.Any(option => option.Item1 == language)) language = "";
            filters.Children.Clear();
            languagesPanel.Children.Clear();
            SpeechChoiceControl(languagesPanel, "Language and region", "gemini-voice-language", language,
                new[] { ("", "All languages") }.Concat(languages), value => { language = value; Render(); });
            var styles = GeminiVoices.Prebuilt.Select(voice => voice.Note).Distinct().OrderBy(value => value).Select(value => (value, value));
            SpeechChoiceControl(filters, "Style", "gemini-voice-style", style,
                new[] { ("", "All styles") }.Concat(styles), value => { style = value; Render(); });
            SpeechChoiceControl(filters, "Sort by", "gemini-voice-sort", sort, new[] {
                ("name", "Voice name (A–Z)"), ("descending", "Voice name (Z–A)"), ("language", "Language (A–Z)")
            }, value => { sort = value; Render(); });
        }

        void Render()
        {
            var selected = GeminiVoices.Selected(draft).Id;
            var query = search.Text.Trim();
            searchLibrary.IsEnabled = !busy && Ready() && query.Length > 0;
            var filtered = Options().Where(voice =>
                (language.Length == 0 || language == GeminiVoiceLanguage(voice)) &&
                (style.Length == 0 || GeminiVoices.IsPrebuilt(voice.Id) && voice.Note == style) &&
                (query.Length == 0 || (voice.Name + " " + GeminiVoiceLanguage(voice) + " " + voice.Note).Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || query == searched && library.Any(option => option.Id == voice.Id)));
            var visible = sort switch
            {
                "language" => filtered.OrderBy(GeminiVoiceLanguage).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase),
                "descending" => filtered.OrderByDescending(v => v.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => filtered.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            };
            rows.Children.Clear(); rowActions.Clear();
            string? currentLanguage = null;
            foreach (var voice in visible)
            {
                if (sort == "language" && currentLanguage != GeminiVoiceLanguage(voice))
                {
                    currentLanguage = GeminiVoiceLanguage(voice);
                    rows.Children.Add(ScreenReaderHeading.Mark(new TextBlock { Text = currentLanguage, FontSize = 16 }));
                }
                var details = GeminiVoiceLanguage(voice) + (GeminiVoices.IsPrebuilt(voice.Id) ? ", " + voice.Note : "");
                var choice = new RadioButton { Content = new TextBlock { Text = voice.Name + " · " + details, TextWrapping = TextWrapping.Wrap },
                    GroupName = "gemini-voice-choices", IsChecked = selected == voice.Id };
                AutomationProperties.SetName(choice, $"Select {voice.Name}, {details}");
                void SelectVoice()
                {
                    if (GeminiVoices.Selected(draft).Id == voice.Id) return;
                    GeminiVoices.Select(draft, voice);
                    choice.IsChecked = true;
                    UpdateGeminiVoiceControls("gemini");
                }
                choice.Checked += (_, _) => SelectVoice();
                var menu = MenuAccessKeys.Create();
                var select = new ToggleMenuFlyoutItem { Text = "Select " + voice.Name, Icon = MenuIcons.Create("Select") };
                select.Click += (_, _) => SelectVoice();
                menu.Opening += (_, _) => select.IsChecked = choice.IsChecked == true;
                menu.Items.Add(select); choice.ContextFlyout = menu;
                var play = new Button();
                var ring = new ProgressRing { Width = 18, Height = 18 };
                AutomationProperties.SetName(ring, "Preparing sample of " + voice.Name);
                bool Active() => playingId == voice.Id && sampleState != SampleState.Stopped;
                bool CanPlay() => !VoiceSampleSession.Busy && Ready() && !string.IsNullOrWhiteSpace(narrationSampleText?.Text);
                void Play()
                {
                    if (!CanPlay()) return;
                    playingId = voice.Id;
                    _ = PlaySampleAsync(voice);
                }
                void Update()
                {
                    var active = Active();
                    play.Content = new SymbolIcon(active ? Symbol.Stop : Symbol.Play);
                    ToolTipService.SetToolTip(play, active ? "Stop Sample" : "Play Sample");
                    AutomationProperties.SetName(play, (active ? "Stop sample of " : "Play sample of ") + voice.Name);
                    play.IsEnabled = active || CanPlay();
                    ring.IsActive = active && sampleState == SampleState.Generating;
                    ring.Visibility = ring.IsActive ? Visibility.Visible : Visibility.Collapsed;
                }
                play.Click += (_, _) => { if (Active()) StopSample(false); else Play(); };
                SetVoiceSampleMenu(play, "gemini-row-sample-" + voice.Id, CanPlay, Play, Active, () => StopSample(false));
                rowActions.Add(Update); Update();
                var layout = new Grid { ColumnSpacing = 8 };
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.Children.Add(choice);
                var actions = CompactActions(play, ring); Grid.SetColumn(actions, 1); layout.Children.Add(actions);
                rows.Children.Add(layout);
            }
            if (rows.Children.Count == 0) rows.Children.Add(new TextBlock { Text = "No matching voices" });
        }

        async Task PlaySampleAsync(Voice voice)
        {
            await Preview(narrationSampleText!.Text, voice);
            if (!closed) libraryStatus.Text = status.Text;
        }

        async Task SearchLibraryAsync()
        {
            var query = search.Text.Trim();
            if (busy || !Ready() || query.Length == 0) return;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            activeSearch = cancellation;
            busy = true;
            int revision = ++searchRevision;
            searchLibrary.IsEnabled = false;
            libraryStatus.Text = "Searching voice library…";
            try
            {
                var result = await SearchGeminiVoicesAsync(query, cancellation.Token);
                if (cancellation.IsCancellationRequested || searchRevision != revision) return;
                library = result.Voices;
                searched = query;
                libraryStatus.Text = result.HasMore ? "More matches available. Refine your search." : $"{library.Count} library matches";
                RebuildFilters();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!cancellation.IsCancellationRequested && searchRevision == revision) libraryStatus.Text = "Could not search the voice library.";
            }
            finally { activeSearch = null; busy = false; if (!cancellation.IsCancellationRequested) Render(); }
        }
    }
}
