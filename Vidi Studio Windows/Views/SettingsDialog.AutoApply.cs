using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Text.Json.Nodes;
using Vidi_Studio_Windows.Services;

namespace Vidi_Studio_Windows.Views;

public sealed partial class SettingsDialog
{
    private readonly DispatcherTimer autoApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly TextBlock autoApplyError = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly SettingsAutoApply autoApply;
    private JsonObject? pendingPreferences;
    private long retryAfter;

    private void StartAutoApply()
    {
        AutomationProperties.SetLiveSetting(autoApplyError, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        // One observer covers nested model/prompt changes and asynchronous key/voice validation too.
        autoApplyTimer.Tick += (_, _) =>
        {
            if (!autoApply.HasChanges(draft)) { pendingPreferences = null; return; }
            if (Environment.TickCount64 < retryAfter) return;
            if (JsonNode.DeepEquals(pendingPreferences, draft)) ApplyPreferences();
            else pendingPreferences = draft.DeepClone().AsObject();
        };
        autoApplyTimer.Start();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && !closed) ApplyPreferences();
        };
        AppWindow.Closing += (_, args) => args.Cancel = !ApplyPreferences();
    }

    private bool ApplyPreferences()
    {
        try
        {
            autoApply.Apply(draft, () => { model.Settings.Reload(); return model.Settings.Data; }, (preferences, apiKey, changed) =>
            {
                if (apiKey != null && !CredentialStore.Write(apiKey))
                    throw new IOException("Credential storage failed.");
                model.Settings.Data.Clear();
                foreach (var item in preferences) model.Settings.Data[item.Key] = item.Value?.DeepClone();
                model.Settings.Save();
                App.ApplySettingsChanges(changed);
            });
            pendingPreferences = null;
            retryAfter = 0;
            autoApplyError.Text = "";
            autoApplyError.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Never log preferences or credential contents. Keep the edits pending and retry.
            retryAfter = Environment.TickCount64 + 2000;
            autoApplyError.Text = "Could not save settings. Check access to app storage and try again.";
            autoApplyError.Visibility = Visibility.Visible;
            return false;
        }
    }

#if DEBUG
    internal void StageFinalSettingsEditForSmokeTest() => draft["captionFontSize"] = 33;

    internal async System.Threading.Tasks.Task VerifyAutomaticSettingsForSmokeTestAsync()
    {
        var before = draft.DeepClone().AsObject();
        try
        {
            draft["captionFontSize"] = 31;
            (draft["prompts"]?["multilingual_media"] as JsonObject ?? throw new InvalidOperationException("Missing prompts."))["system_instruction"] = "Automatic settings smoke fixture";
            await System.Threading.Tasks.Task.Delay(650);
            var persisted = JsonNode.Parse(File.ReadAllText(SettingsStore.ConfigPath))!.AsObject();
            if (persisted["captionFontSize"]?.GetValue<int>() != 31 || persisted["prompts"]?["multilingual_media"]?["system_instruction"]?.GetValue<string>() != "Automatic settings smoke fixture")
                throw new InvalidOperationException("Settings did not save automatically while open.");
            if (persisted.ContainsKey("apiKey")) throw new InvalidOperationException("A credential entered settings JSON.");
        }
        finally
        {
            draft.Clear();
            foreach (var item in before) draft[item.Key] = item.Value?.DeepClone();
            if (!ApplyPreferences()) throw new InvalidOperationException("Could not restore settings after smoke test.");
        }
    }
#endif
}
