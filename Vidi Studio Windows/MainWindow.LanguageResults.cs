using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Models;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    internal async Task<bool?> ConfirmExportReplacementAsync(IReadOnlyList<string> paths) {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new ScrollViewer { MaxHeight = 240, Content = new TextBlock { Text = string.Join("\n", paths), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } });
        var result = await ShowDialogAsync(new ContentDialog {
            Title = paths.Count == 1 ? "Replace Existing Export?" : "Replace Existing Exports?", Content = content,
            PrimaryButtonText = "Replace Existing", SecondaryButtonText = "Skip Existing", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        }, 650, 480);
        return result == ContentDialogResult.Primary ? true : result == ContentDialogResult.Secondary ? false : null;
    }

    private ContentControl? activeLanguagePanelHost;
    private readonly List<(Control control, bool requiresEdit)> languageControls = new();
    private bool LanguageEditMode => Section.SelectedIndex == 1 ? summaryEditMode : Section.SelectedIndex is 0 or 2 or 4 && editMode;
    private bool HasUnsavedLanguageSummary => summaryEditMode && (NormalizeSummaryText(SummaryEditorBox.Text) != NormalizeSummaryText(Model.Project?.Summary)
        || (SummaryProvenance.SelectedIndex switch { 0 => "ai", 1 => "imported", 2 => "manual", _ => Model.Project?.SummaryOrigin }) != Model.Project?.SummaryOrigin);
    private string[] CurrentLanguageKinds => Section.SelectedIndex switch {
        0 => new[] { "transcript" }, 1 => new[] { "summary" },
        2 or 3 => new[] { "descriptions", "adAudio" }, 4 => new[] { "transcript", "descriptions" }, _ => Array.Empty<string>() };
    private void RegisterLanguageControl(Control control, bool requiresEdit) {
        languageControls.Add((control, requiresEdit)); UpdateLanguageControlStates();
    }
    private void UpdateLanguageControlStates() {
        foreach (var (control, requiresEdit) in languageControls) {
            var noViewingData = control is ComboBox picker && picker.Header as string == "View Project Language" && picker.ItemsSource is IEnumerable<OutputLanguage> choices && choices.All(v => v.Id == "unavailable");
            control.IsEnabled = !noViewingData && !Model.Busy && !HasUnsavedLanguageSummary && (!requiresEdit || LanguageEditMode);
        }
    }
    private void RefreshLanguagePanel() {
        if (Model.Project is not { } project) return;
        LanguageResultsPanel.Children.Clear(); LanguageResultsPanel.Children.Add(BuildLanguageResultsPanel(project));
        if (activeLanguagePanelHost != null) activeLanguagePanelHost.Content = BuildLanguageResultsPanel(project);
    }
    private async Task ShowLanguageResultsDialogAsync() {
        if (Model.Project is not { } project) return;
        activeLanguagePanelHost = new ScrollViewer { Content = BuildLanguageResultsPanel(project) };
        try { await ShowDialogAsync(new ContentDialog { Title = "Language and Saved Results", Content = activeLanguagePanelHost, CloseButtonText = "Close" }, 650, 700); }
        finally { activeLanguagePanelHost = null; RefreshLanguagePanel(); }
    }
    private async Task RunLanguageActionAsync(string command, string[]? arguments = null, bool settingsEditing = false) {
        if (Model.Busy || HasUnsavedLanguageSummary) return;
        var changesLanguage = command is "select-section-language" or "sync-section-languages" or "assign-result-language";
        if (changesLanguage && !LanguageEditMode && !settingsEditing) return;
        StopSegmentPreview(); narrationPlayer.Pause(); readback.Stop();
        SummaryEditorBox.IsEnabled = false; SummaryProvenance.IsEnabled = false;
        try {
            await Model.RunAsync(command, arguments ?? Array.Empty<string>());
            if (summaryEditMode) SetSummaryEditMode(true);
        } finally {
            SummaryEditorBox.IsEnabled = true; SummaryProvenance.IsEnabled = true;
            RefreshLanguagePanel();
        }
    }
    private string exportResultFilter = "all";
    private string reviewLanguageFilter = "all";
    private string exportLanguageFilter = "all";
    private void InitializeLanguageFilters() {
        var options = new[] { new OutputLanguage("all", "All Languages") }.Concat(OutputLanguage.Options).ToList();
        GalleryLanguageFilter.ItemsSource = options; ProjectLanguageFilter.ItemsSource = options;
        GalleryLanguageFilter.SelectedIndex = 0; ProjectLanguageFilter.SelectedIndex = 0;
    }
    private void LanguageFilter_Changed(object sender, SelectionChangedEventArgs e) {
        if (!ready || sender is not ComboBox picker || picker.SelectedItem is not OutputLanguage value) return;
        if (Model.LanguageFilter == value.Id) return;
        Model.LanguageFilter = value.Id;
        GalleryLanguageFilter.SelectedItem = (GalleryLanguageFilter.ItemsSource as IEnumerable<OutputLanguage>)?.FirstOrDefault(v => v.Id == value.Id);
        ProjectLanguageFilter.SelectedItem = (ProjectLanguageFilter.ItemsSource as IEnumerable<OutputLanguage>)?.FirstOrDefault(v => v.Id == value.Id);
        if (ReferenceEquals(sender, GalleryLanguageFilter)) FilterGallery(); else Filter();
    }
    private ComboBox LanguagePicker(string title, string selected, bool all = false)
    {
        var options = OutputLanguage.Options.ToList();
        if (all) options.Insert(0, new OutputLanguage("all", "All Languages"));
        var field = new ComboBox { Header = title, ItemsSource = options, DisplayMemberPath = "Title", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !Model.Busy };
        field.SelectedItem = options.FirstOrDefault(v => v.Id == selected);
        AutomationProperties.SetName(field, title);
        return field;
    }

    private StackPanel BuildLanguageResultsPanel(StudioProject project, bool settingsContext = false, bool editingSettings = false, Action<bool>? setSettingsEditing = null, Action? settingsChanged = null)
    {
        if (!settingsContext) languageControls.Clear();
        var editing = settingsContext ? editingSettings : LanguageEditMode;
        var kinds = settingsContext ? new[] { "transcript", "summary", "descriptions", "adAudio" } : CurrentLanguageKinds;
        void Register(Control control, bool needsEdit) {
            if (!settingsContext) RegisterLanguageControl(control, needsEdit);
            else control.IsEnabled = !Model.Busy && !HasUnsavedLanguageSummary && (!needsEdit || editing);
        }
        async Task Run(string command, string[]? arguments = null) {
            await RunLanguageActionAsync(command, arguments, settingsContext && editing);
            settingsChanged?.Invoke();
        }
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Language and Saved Results", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!settingsContext) {
            var copyAll = new Button { Content = "Copy to…", IsEnabled = CanCopyLanguageResults };
            copyAll.Click += async (_, _) => await ShowCopyToAsync(); panel.Children.Add(copyAll);
        }
        if (settingsContext) {
            var edit = new Button { Content = editing ? "Finish Editing Languages" : "Edit Languages", IsEnabled = !Model.Busy && !HasUnsavedLanguageSummary };
            edit.Click += (_, _) => setSettingsEditing?.Invoke(!editing); panel.Children.Add(edit);
        }
        if (HasUnsavedLanguageSummary) panel.Children.Add(new TextBlock { Text = "Save or cancel summary edits first.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var choices = OutputLanguage.Options.Where(v => editing || project.AvailableViewingLanguages.Contains(v.Id)).Select(v => new OutputLanguage(v.Id, project.ViewingLanguageTitle(v.Id))).ToList();
        if (!editing && !choices.Any(v => v.Id == project.SelectedLanguage)) choices.Insert(0, new OutputLanguage("unavailable", "No saved content selected"));
        var language = new ComboBox { Header = editing ? "Edit Project Language" : "View Project Language", ItemsSource = choices, DisplayMemberPath = "Title", SelectedItem = choices.FirstOrDefault(v => v.Id == project.SelectedLanguage) ?? choices.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(language, editing ? "Edit Project Language" : "View Project Language");
        Register(language, false);
        language.SelectionChanged += async (_, _) => {
            if (language.SelectedItem is OutputLanguage value && value.Id != "unavailable" && value.Id != project.SelectedLanguage) {
                StopSegmentPreview(); narrationPlayer.Pause(); readback.Stop();
                await Run("select-language", new[] { "--language", value.Id });
            }
        };
        if (!editing && project.AvailableViewingLanguages.Count == 0) language.IsEnabled = false;
        panel.Children.Add(language);
        var synchronize = new Button { Content = "Keep All Sections in Sync", IsEnabled = !Model.Busy };
        synchronize.Click += async (_, _) => { StopSegmentPreview(); narrationPlayer.Pause(); readback.Stop(); await Run("sync-section-languages"); }; Register(synchronize, true); panel.Children.Add(synchronize);
        foreach (var kind in kinds) {
            var sectionOptions = new[] { new OutputLanguage("project", "Follow Project Language") }.Concat(OutputLanguage.Options).ToList();
            var selectedLanguage = project.SectionLanguages.TryGetValue(kind, out var custom) ? custom : "project";
            var sectionLanguage = new ComboBox { Header = LanguageResult.KindTitle(kind) + " Generation Language", ItemsSource = sectionOptions, DisplayMemberPath = "Title", SelectedItem = sectionOptions.First(v => v.Id == selectedLanguage), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !Model.Busy };
            AutomationProperties.SetName(sectionLanguage, LanguageResult.KindTitle(kind) + " language");
            sectionLanguage.SelectionChanged += async (_, _) => {
                if (sectionLanguage.SelectedItem is not OutputLanguage choice) return;
                var arguments = choice.Id == "project" ? new[] { "--section", kind, "--follow-project" } : new[] { "--section", kind, "--language", choice.Id };
                StopSegmentPreview(); narrationPlayer.Pause(); readback.Stop();
                await Run("select-section-language", arguments);
            };
            Register(sectionLanguage, true);
            panel.Children.Add(sectionLanguage);

            var viewingOverview = !settingsContext && Section.SelectedIndex == 3;
            var code = viewingOverview ? project.OverviewLanguageCode : project.SelectedLanguage;
            var rows = project.SavedResults.Where(r => r.Kind == kind && r.Language == code).OrderByDescending(r => r.CreatedAt).ToList();
            if (rows.Count == 0) {
                panel.Children.Add(new TextBlock { Text = "No saved " + LanguageResult.KindTitle(kind).ToLowerInvariant() + " in " + OutputLanguage.Label(code) + ".", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
                continue;
            }
            var picker = new ComboBox { Header = LanguageResult.SavedResultsTitle(kind), ItemsSource = rows, DisplayMemberPath = "Title", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !Model.Busy };
            SavedResultOptions.Populate(picker, rows, project, project.ActiveResultID(kind, code));
            AutomationProperties.SetName(picker, LanguageResult.SavedResultsTitle(kind));
            picker.SelectionChanged += async (_, _) => {
                if (SavedResultOptions.Selected(picker) is LanguageResult result && result.Id != project.ActiveResultID(kind, code)) {
                    StopSegmentPreview(); narrationPlayer.Pause(); readback.Stop();
                    await Run("select-result", new[] { "--result-id", result.Id });
                }
            };
            Register(picker, false);
            panel.Children.Add(picker);
            if (!settingsContext && kind is "transcript" or "summary" or "descriptions") {
                var copy = new Button { Content = "Copy " + LanguageResult.KindTitle(kind) + " to…", IsEnabled = CanCopyLanguageResults };
                copy.Click += async (_, _) => await ShowCopyToAsync(new[] { kind }); panel.Children.Add(copy);
            }
            var assign = new Button { Content = "Assign Language…", Visibility = editing ? Visibility.Visible : Visibility.Collapsed };
            Register(assign, true);
            assign.Click += async (_, _) => {
                if (SavedResultOptions.Selected(picker) is not LanguageResult result) return;
                var choice = LanguagePicker("Language", result.Language);
                var dialog = new ContentDialog { Title = "Assign Language", Content = choice, PrimaryButtonText = "Assign Language", CloseButtonText = "Cancel" };
                if (await ShowDialogAsync(dialog, 550, 320) == ContentDialogResult.Primary && choice.SelectedItem is OutputLanguage value)
                    await Run("assign-result-language", new[] { "--result-id", result.Id, "--language", value.Id });
            };
            panel.Children.Add(assign);
        }
        if (project.SavedResults.Any(r => kinds.Contains(r.Kind) && r.State == "draft")) {
            var filter = LanguagePicker("Review language filter", reviewLanguageFilter, true);
            var drafts = new StackPanel { Spacing = 6 };
            void RefreshDrafts() {
                drafts.Children.Clear();
                foreach (var draft in project.SavedResults.Where(r => kinds.Contains(r.Kind) && r.State == "draft" && (reviewLanguageFilter == "all" || r.Language == reviewLanguageFilter))) {
                    var button = new Button { Content = "Review " + OutputLanguage.Label(draft.Language) + " " + LanguageResult.KindTitle(draft.Kind) + "…" };
                    Register(button, false);
                    button.Click += async (_, _) => { await ReviewLanguageResultAsync(project, draft); settingsChanged?.Invoke(); }; drafts.Children.Add(button);
                }
            }
            filter.SelectionChanged += (_, _) => { reviewLanguageFilter = (filter.SelectedItem as OutputLanguage)?.Id ?? "all"; RefreshDrafts(); };
            panel.Children.Add(filter); panel.Children.Add(drafts); RefreshDrafts();
        }
        return panel;
    }

    private async Task ReviewLanguageResultAsync(StudioProject project, LanguageResult draft)
    {
        var body = new StackPanel { Spacing = 12 };
        var old = project.SavedResults.FirstOrDefault(r => r.Id == draft.ReplacesResultID);
        body.Children.Add(new TextBlock { Text = "Selected saved result", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        body.Children.Add(new TextBox { Text = old?.Preview ?? "No previous result", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 });
        body.Children.Add(new TextBlock { Text = "New result", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        body.Children.Add(new TextBox { Text = draft.Preview, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 });
        void AddPreview(string title, LanguageResult item) {
            string? path = null;
            if (item.Payload.TryGetProperty("importedAudioDescription", out var imported) && imported.TryGetProperty("path", out var file)) path = file.GetString();
            if (path == null && item.Payload.TryGetProperty("cues", out var cues))
                foreach (var cue in cues.EnumerateArray()) if (cue.TryGetProperty("audio", out var audio)) { path = audio.GetString(); break; }
            if (path == null || !File.Exists(project.Resolve(path))) return;
            var relative = path;
            var preview = new Button { Content = title };
            preview.Click += (_, _) => { narrationPlayer.Pause(); narrationPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(project.Resolve(relative))); narrationPlayer.Play(); };
            body.Children.Add(preview);
        }
        if (old != null) AddPreview("Preview Saved Narration", old);
        AddPreview("Preview New Narration", draft);
        var choice = new ComboBox { Header = "Retain results", SelectedIndex = 0 };
        choice.Items.Add("Keep Both");
        if (old != null) choice.Items.Add("Replace Selected");
        choice.Items.Add("Discard New"); body.Children.Add(choice);
        var dialog = new ContentDialog { Title = "Review " + OutputLanguage.Label(draft.Language) + " " + LanguageResult.KindTitle(draft.Kind), Content = body, PrimaryButtonText = "Apply Choice", CloseButtonText = "Review Later", DefaultButton = ContentDialogButton.Primary };
        var reviewChoice = await ShowDialogAsync(dialog, 800, 760);
        narrationPlayer.Pause();
        if (reviewChoice == ContentDialogResult.Primary) {
            var disposition = choice.SelectedItem?.ToString() switch { "Replace Selected" => "replace", "Discard New" => "discard", _ => "keep" };
            await RunLanguageActionAsync("review-result", new[] { "--result-id", draft.Id, "--disposition", disposition });
        }
    }

    private async Task<List<string>?> ChooseLanguageSourcesAsync(string command)
    {
        var args = new List<string>();
        if (Model.Project is not { } project) return args;
        var kinds = command == "generate-ad-cues" ? new[] { "transcript", "summary" } : command == "generate-summary" ? new[] { "transcript" } : Array.Empty<string>();
        foreach (var kind in kinds) {
            if (!string.IsNullOrEmpty(project.ActiveResultID(kind))) continue;
            var rows = project.SavedResults.Where(r => r.Kind == kind && r.State == "saved").ToList();
            if (rows.Count == 0) { await ShowError(new InvalidOperationException("Create or import a source " + kind + " first.")); return null; }
            var picker = new ComboBox { Header = "Source " + kind, ItemsSource = rows, DisplayMemberPath = "Title", SelectedIndex = 0 };
            var dialog = new ContentDialog { Title = "Choose source " + kind, Content = picker, PrimaryButtonText = "Continue", CloseButtonText = "Cancel" };
            if (await ShowDialogAsync(dialog, 650, 350) != ContentDialogResult.Primary || picker.SelectedItem is not LanguageResult result) return null;
            args.AddRange(new[] { "--source-" + kind + "-id", result.Id });
        }
        return args;
    }

    internal async Task RunResultRequestAsync(string command, object request)
    {
        var path = Path.Combine(Path.GetTempPath(), "vidistudio-results-" + Guid.NewGuid() + ".json");
        try { File.WriteAllText(path, JsonSerializer.Serialize(request)); await Model.RunAsync(command, new[] { "--input", path }); }
        finally { File.Delete(path); }
    }

    private bool CanCopyLanguageResults => Model.Idle && !HasUnsavedLanguageSummary && Model.Project is { } p &&
        p.SavedResults.Any(r => r.State == "saved" && r.HasContent && r.Kind is "transcript" or "summary" or "descriptions");

    private async Task ShowCopyToAsync(string[]? initialKinds = null, Dictionary<string, string[]>? segmentIDs = null)
    {
        if (!CanCopyLanguageResults || Model.Project is not { } project) return;
        var allKinds = new[] { "transcript", "summary", "descriptions" };
        var source = LanguagePicker("Source Language", project.SelectedLanguage);
        var target = LanguagePicker("Target Language", project.SelectedLanguage == "auto" ? "en" : project.SelectedLanguage);
        var panel = new StackPanel { Spacing = 12 };
        var translate = new CheckBox { Content = "Translate Text", IsChecked = true };
        panel.Children.Add(source); panel.Children.Add(translate); panel.Children.Add(target);
        var selectedOnly = new CheckBox { Content = "Selected Segments Only", IsChecked = segmentIDs?.Count > 0,
            Visibility = segmentIDs?.Count > 0 ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(selectedOnly);
        var selectAll = new Button { Content = "Select All" }; panel.Children.Add(selectAll);
        var content = new StackPanel { Spacing = 8 }; panel.Children.Add(content);
        var boxes = new Dictionary<string, CheckBox>();
        var pickers = new Dictionary<string, ComboBox>();
        var chosenKinds = (initialKinds ?? allKinds).ToHashSet();
        var dialog = new ContentDialog { Title = "Copy to", Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = "Copy", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        var translationStatus = new TextBlock { Text = "Gemini is unavailable. Check connection and API key.", TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(translationStatus);
        void UpdatePrimary() {
            dialog.PrimaryButtonText = translate.IsChecked == true ? "Translate and Copy" : "Copy";
            translationStatus.Visibility = translate.IsChecked == true && !Model.CanUseAI ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = CanCopyLanguageResults && boxes.Any(v => v.Value.IsEnabled && v.Value.IsChecked == true) &&
                (translate.IsChecked != true || Model.CanUseAI && (target.SelectedItem as OutputLanguage)?.Id is not null and not "auto");
        }
        void RefreshTargets() {
            var selected = (target.SelectedItem as OutputLanguage)?.Id ?? "en";
            var choices = OutputLanguage.Options.Where(v => translate.IsChecked != true || v.Id != "auto").ToList();
            target.ItemsSource = choices; target.SelectedItem = choices.FirstOrDefault(v => v.Id == selected) ?? choices.First(v => v.Id == "en");
            UpdatePrimary();
        }
        void Refresh() {
            content.Children.Clear(); boxes.Clear(); pickers.Clear();
            var code = (source.SelectedItem as OutputLanguage)?.Id ?? project.SelectedLanguage;
            source.IsEnabled = selectedOnly.IsChecked != true;
            foreach (var kind in allKinds) {
                var rows = project.SavedResults.Where(r => r.Language == code && r.Kind == kind && r.State == "saved" && r.HasContent).OrderByDescending(r => r.CreatedAt).ToList();
                var box = new CheckBox { Content = LanguageResult.KindTitle(kind), IsChecked = chosenKinds.Contains(kind),
                    IsEnabled = rows.Count > 0 && (selectedOnly.IsChecked != true || segmentIDs?.ContainsKey(kind) == true) };
                boxes[kind] = box; content.Children.Add(box);
                box.Checked += (_, _) => { chosenKinds.Add(kind); UpdatePrimary(); };
                box.Unchecked += (_, _) => { chosenKinds.Remove(kind); UpdatePrimary(); };
                if (rows.Count == 0) continue;
                var picker = new ComboBox { Header = LanguageResult.SavedResultsTitle(kind), ItemsSource = rows, DisplayMemberPath = "Title",
                    SelectedItem = rows.FirstOrDefault(r => r.Id == project.ActiveResultID(kind, code)) ?? rows[0],
                    HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = selectedOnly.IsChecked != true };
                AutomationProperties.SetName(picker, LanguageResult.SavedResultsTitle(kind));
                pickers[kind] = picker; content.Children.Add(picker);
                if (selectedOnly.IsChecked == true && segmentIDs?.TryGetValue(kind, out var ids) == true)
                    content.Children.Add(new TextBlock { Text = ids.Length + " selected segments" });
            }
            UpdatePrimary();
        }
        target.SelectionChanged += (_, _) => UpdatePrimary();
        translate.Checked += (_, _) => RefreshTargets(); translate.Unchecked += (_, _) => RefreshTargets();
        source.SelectionChanged += (_, _) => Refresh();
        selectedOnly.Checked += (_, _) => { source.SelectedItem = ((IEnumerable<OutputLanguage>)source.ItemsSource).First(v => v.Id == project.SelectedLanguage); Refresh(); }; selectedOnly.Unchecked += (_, _) => Refresh();
        selectAll.Click += (_, _) => { chosenKinds = allKinds.ToHashSet(); selectedOnly.IsChecked = false; Refresh(); };
        Refresh(); RefreshTargets();
        if (await ShowDialogAsync(dialog, 650, 700) != ContentDialogResult.Primary || !CanCopyLanguageResults || Model.Project?.Id != project.Id) return;
        if (translate.IsChecked == true && (!Model.CanUseAI || (target.SelectedItem as OutputLanguage)?.Id is null or "auto")) return;
        var kinds = allKinds.Where(k => boxes[k].IsEnabled && boxes[k].IsChecked == true).ToArray();
        if (kinds.Length == 0) return;
        var results = kinds.ToDictionary(k => k, k => ((LanguageResult)pickers[k].SelectedItem).Id);
        var segments = selectedOnly.IsChecked == true ? segmentIDs!.Where(v => kinds.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value) : new Dictionary<string, string[]>();
        StopSegmentPreview(); narrationPlayer.Pause(); readback.Stop();
        await RunResultRequestAsync(translate.IsChecked == true ? "translate-results" : "copy-results", new { sourceLanguage = ((OutputLanguage)source.SelectedItem).Id,
            targetLanguage = ((OutputLanguage)target.SelectedItem).Id, kinds, resultIDs = results, segmentIDs = segments });
    }

    private async Task ShowLanguageDeletionAsync(string? component = null)
    {
        if (Model.Project is not { } project || Model.Busy) return;
        var panel = new StackPanel { Spacing = 10 };
        var deletionKind = component is "narration" or "history" ? "descriptions" : component;
        var languages = new[] { new OutputLanguage("all", "All Languages") }
            .Concat(project.DeletionLanguages.Select(code => new OutputLanguage(code, OutputLanguage.Label(code)))).ToList();
        var initialLanguage = deletionKind == null || deletionKind is "thumbnails" or "exports" ? "all" : project.SelectedLanguage;
        var language = new ComboBox { Header = "Language filter", ItemsSource = languages, DisplayMemberPath = "Title",
            SelectedItem = languages.FirstOrDefault(v => v.Id == initialLanguage) ?? languages[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(language, "Language filter");
        panel.Children.Add(language);
        string DeletionTitle(string kind) => kind switch {
            "descriptions" => "Description Segments and Audio", "narration" => "Description Audio Only (Keep Segments)",
            "history" => "Segment Undo History Only", "thumbnails" => "Thumbnails (Entire Project)", _ => LanguageResult.KindTitle(kind)
        };
        var options = new List<CheckBox>();
        foreach (var kind in new[] { "transcript", "summary", "descriptions", "adAudio", "narration", "history", "thumbnails" }) {
            var box = new CheckBox { Content = DeletionTitle(kind), Tag = kind, IsChecked = component == kind };
            options.Add(box); panel.Children.Add(box);

        }
        var rows = new StackPanel { Spacing = 5 }; panel.Children.Add(rows);
        var selected = new HashSet<string>(); var exports = new HashSet<string>();
        if (component != null) {
            var rid = project.ActiveResultID(component is "narration" or "history" ? "descriptions" : component);
            if (!string.IsNullOrEmpty(rid)) selected.Add(rid);
        }
        bool VisibleResult(LanguageResult r) {
            var code = (language.SelectedItem as OutputLanguage)?.Id ?? "all";
            var kinds = options.Where(v => v.IsChecked == true).Select(v => (string)v.Tag).ToHashSet();
            return (code == "all" || r.Language == code) && (kinds.Contains(r.Kind) || kinds.Contains("narration") && r.Kind == "descriptions" || kinds.Contains("history") && r.Kind is "descriptions" or "transcript");
        }
        void Refresh() {
            selected.IntersectWith(project.SavedResults.Where(VisibleResult).Select(r => r.Id));
            rows.Children.Clear();
            var code = (language.SelectedItem as OutputLanguage)?.Id ?? "all";
            foreach (var result in project.SavedResults.Where(VisibleResult)) {
                var current = result.State == "saved" && project.ActiveResultID(result.Kind, result.Language) == result.Id;
                var prefix = OutputLanguage.Label(result.Language) + " · " + LanguageResult.KindTitle(result.Kind) + " · ";
                var row = SavedResultOptions.Content(result, project, current, titlePrefix: prefix);
                var box = new CheckBox { Content = row, IsChecked = selected.Contains(result.Id) };
                AutomationProperties.SetName(box, prefix + SavedResultOptions.AccessibleName(result, project, current).Replace(", Selected", ", Selected (current saved result)"));
                AutomationProperties.SetAutomationId(box, "delete-saved-result-" + result.Id);
                box.Checked += (_, _) => selected.Add(result.Id); box.Unchecked += (_, _) => selected.Remove(result.Id); rows.Children.Add(box);
            }
            rows.Children.Add(new TextBlock { Text = "Optional exports" });
            foreach (var path in project.Exports.Where(path => code == "all" || (project.ExportResults.TryGetValue(path, out var provenance) ? provenance.Matches(code) : code == "auto"))) {
                var box = new CheckBox { Content = Path.GetFileName(path), IsChecked = exports.Contains(path) };
                box.Checked += (_, _) => exports.Add(path); box.Unchecked += (_, _) => exports.Remove(path); rows.Children.Add(box);
            }
        }
        foreach (var box in options) {
            box.Checked += (_, _) => {
                var kind = (string)box.Tag;
                if (kind is "narration" or "descriptions") foreach (var other in options.Where(v => (string)v.Tag == (kind == "narration" ? "descriptions" : "narration"))) other.IsChecked = false;
                Refresh();
            };
            box.Unchecked += (_, _) => Refresh();
        }
        var selectVisible = new Button { Content = "Select Visible Results" };
        selectVisible.Click += (_, _) => { selected.UnionWith(project.SavedResults.Where(VisibleResult).Select(r => r.Id)); Refresh(); };
        panel.Children.Insert(panel.Children.IndexOf(rows), selectVisible);
        language.SelectionChanged += (_, _) => Refresh(); Refresh();
        var dialog = new ContentDialog { Title = "Delete Content by Language", Content = new ScrollViewer { Content = panel }, PrimaryButtonText = "Review Deletion", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await ShowDialogAsync(dialog, 800, 760) != ContentDialogResult.Primary) return;
        var components = options.Where(v => v.IsChecked == true).Select(v => (string)v.Tag).ToArray();
        if (components.Length == 0 && exports.Count == 0) return;
        var request = new { resultIDs = selected.ToArray(), components, exportPaths = exports.ToArray() };
        var previewInput = Path.Combine(Path.GetTempPath(), "vidistudio-delete-preview-" + Guid.NewGuid() + ".json");
        string previewText;
        try {
            File.WriteAllText(previewInput, JsonSerializer.Serialize(request));
            var events = await Model.RunAsync("preview-delete-results", new[] { "--input", previewInput });
            var preview = events.FirstOrDefault(v => v.Data.TryGetProperty("deletionPreview", out _));
            if (preview == null) return;
            var data = preview.Data.GetProperty("deletionPreview");
            previewText = $"{data.GetProperty("selectedResultCount")} selected results, {data.GetProperty("exportCount")} exports, and {data.GetProperty("fileCount")} files.\n" +
                string.Join("\n", data.GetProperty("files").EnumerateArray().Select(v => v.GetString()));
        } finally { File.Delete(previewInput); }
        if (!await Confirm("Delete selected content permanently?", previewText + "\nContent types: " + string.Join(", ", components.Select(DeletionTitle)) + ". This cannot be undone.", "Delete Selected")) return;
        StopSegmentPreview(); narrationPlayer.Pause();
        await RunResultRequestAsync("delete-results", request);
    }
}
