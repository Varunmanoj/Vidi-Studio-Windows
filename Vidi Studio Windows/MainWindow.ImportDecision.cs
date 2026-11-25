using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    internal static ContentDialog BuildImportDecision(string title, string message, bool canContinue, string? alternative,
        out Func<ContentDialogResult> contextChoice)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = Message(message)
            },
            CloseButtonText = "Okay",
            PrimaryButtonText = canContinue ? "Import Anyway" : "",
            SecondaryButtonText = alternative ?? "",
            DefaultButton = ContentDialogButton.Close
        };
        var choice = ContentDialogResult.None;
        var actions = new MenuFlyout();
        void Add(string title, Symbol icon, ContentDialogResult result)
        {
            var item = new MenuFlyoutItem { Text = title, Tag = result, Icon = new SymbolIcon(icon) };
            item.Click += (_, _) => { choice = result; dialog.Hide(); };
            actions.Items.Add(item);
        }
        Add("Okay", Symbol.Cancel, ContentDialogResult.None);
        if (canContinue) Add("Import Anyway", Symbol.Download, ContentDialogResult.Primary);
        if (alternative != null) Add(alternative, Symbol.Folder, ContentDialogResult.Secondary);
        dialog.ContextFlyout = actions;
        contextChoice = () => choice;
        return dialog;
    }

    private async Task<ContentDialogResult> ShowImportDecisionAsync(string title, string message, bool canContinue, string? alternative = null)
    {
        var dialog = BuildImportDecision(title, message, canContinue, alternative, out var contextChoice);
        var result = await ShowDialogAsync(dialog, 580, 460);
        return result == ContentDialogResult.None ? contextChoice() : result;
    }

    private async Task<List<EngineEvent>> RunImportWithWarningsAsync(string command, IEnumerable<string> arguments,
        bool needsProject = true, Func<Task<string?>>? reviewTiming = null)
    {
        var request = arguments.ToList();
        var folder = needsProject ? Model.Project?.Folder : null;
        while (!windowClosed && !Model.Busy && (!needsProject || Model.Project?.Folder == folder))
        {
            try
            {
                var result = await Model.RunAsync(command, request, needsProject: needsProject, throwOnProcessingError: true);
                if (result.Count == 0 && !windowClosed && !Model.ConnectionFailure &&
                    !Model.Status.StartsWith("Cancelled", StringComparison.Ordinal))
                    await ShowImportDecisionAsync("Import Could Not Finish", Model.Status, false);
                return result;
            }
            catch (ProcessingException ex) when (ex.ImportIssue is { } issue)
            {
                var token = issue.Token is { } candidate && !request.Contains(candidate) ? candidate : null;
                var canReview = token != null && reviewTiming != null && issue.Title == "Timing Warnings" &&
                    !request.Contains("--timing-input");
                var result = await ShowImportDecisionAsync(issue.Title, issue.Message, token != null, canReview ? "Review Timing…" : null);
                if (result == ContentDialogResult.Secondary && canReview)
                {
                    var timing = await reviewTiming!();
                    if (timing == null) break;
                    request.AddRange(new[] { "--timing-input", timing });
                }
                else if (result == ContentDialogResult.Primary && token != null)
                    request.AddRange(new[] { "--accept-import-warning", token });
                else
                {
                    Model.ReportStatus(token == null ? issue.Title : "Import cancelled.");
                    break;
                }
            }
            catch (ProcessingException ex)
            {
                await ShowImportDecisionAsync("Import Could Not Finish", ex.Message, false);
                break;
            }
        }
        return new();
    }
}
