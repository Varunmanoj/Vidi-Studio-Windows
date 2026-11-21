#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Vidi_Studio_Windows.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
    private static IEnumerable<FrameworkElement> DialogElements(DependencyObject root)
    {
        if (root is FrameworkElement element) yield return element;
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in DialogElements(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
    private static IEnumerable<FrameworkElement> DialogControls(UIElement root)
    {
        if(root is FrameworkElement element)yield return element;
        IEnumerable<UIElement> children=root switch {
            Panel panel=>panel.Children,
            Border {Child:{} child}=>new[]{child},
            ScrollViewer {Content:UIElement child}=>new[]{child},
            ContentControl {Content:UIElement child}=>new[]{child},
            _=>Array.Empty<UIElement>()
        };
        foreach(var child in children)foreach(var descendant in DialogControls(child))yield return descendant;
    }
    private static void VerifyCompactDialogLayout(ContentDialog dialog)
    {
        dialog.UpdateLayout();
        var root=(FrameworkElement)dialog.XamlRoot.Content;
        void Bounds(FrameworkElement field) {
            if(field.Visibility!=Visibility.Visible || field.ActualWidth<=0)return;
            for(DependencyObject? parent=field;parent!=null;parent=VisualTreeHelper.GetParent(parent)) {
                if(parent is UIElement {Visibility:Visibility.Collapsed})return;
                if(parent is ScrollViewer scroll && scroll.HorizontalScrollMode!=ScrollMode.Disabled && scroll.ScrollableWidth>1)return;
            }
            var point=field.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            if(point.X < -1 || point.X+field.ActualWidth>root.ActualWidth+1)
                throw new InvalidOperationException($"Dialog control clipped: {dialog.Title} / {AutomationProperties.GetName(field)} {field.GetType().Name}: {point.X}+{field.ActualWidth} > {root.ActualWidth}; dialog={dialog.ActualWidth}");
        }
        if(dialog.Content is UIElement content)foreach(var control in DialogControls(content).Where(c=>c is Button or RadioButton or ComboBox or TextBox or NumberBox))Bounds(control);
        if(dialog.Content is UIElement searchContent) foreach(var search in DialogControls(searchContent).Where(c=>c.Tag as string=="voice-search-full-width"))
            Views.SettingsDialog.VerifyFullWidthVoiceSearchForSmokeTest(search);
        foreach(var title in new[] {dialog.PrimaryButtonText,dialog.SecondaryButtonText,dialog.CloseButtonText})
            if(!string.IsNullOrEmpty(title) && FindButton(dialog,title) is {} button)Bounds(button);
        foreach(var row in DialogElements(dialog).OfType<Grid>().Where(g => g.Tag as string == "dialog-form-row" && g.ActualWidth>0 && g.ActualHeight>0)) {
            var label=(TextBlock)row.Children[0]; var field=(FrameworkElement)row.Children[1];
            if(field.Visibility!=Visibility.Visible)continue;
            var l=label.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
            var f=field.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
            var bounds=row.TransformToVisual((UIElement)dialog.XamlRoot.Content).TransformPoint(new Windows.Foundation.Point());
            if(bounds.X < -1 || bounds.X+row.ActualWidth>((FrameworkElement)dialog.XamlRoot.Content).ActualWidth+1 || f.X+field.ActualWidth>row.ActualWidth+1)
                throw new InvalidOperationException("Dialog form clipped horizontally: "+dialog.Title+" / "+label.Text);
            if(row.ActualWidth>=360 && (f.X<=l.X || Math.Abs(l.Y+label.ActualHeight/2-f.Y-field.ActualHeight/2)>2))
                throw new InvalidOperationException("Dialog label/field alignment: "+dialog.Title+" / "+label.Text);
            if(string.IsNullOrWhiteSpace(AutomationProperties.GetName(field))) throw new InvalidOperationException("Dialog field lost its accessible name.");
        }
        foreach(var actions in DialogElements(dialog).OfType<Views.CompactActionsPanel>()) {
            var buttons=actions.Children.OfType<FrameworkElement>().Where(b=>b.Visibility==Visibility.Visible).ToArray();
            if(buttons.Length<2 || actions.ActualWidth<buttons.Sum(b=>b.DesiredSize.Width)+8*(buttons.Length-1))continue;
            var first=buttons[0].TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point());
            foreach(var button in buttons.Skip(1)) {
                var next=button.TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point());
                if(Math.Abs(first.Y-next.Y)>1 || next.X<=first.X)throw new InvalidOperationException("Related dialog buttons stacked when they fit.");
            }
        }
        if(dialog.ActualTheme==ElementTheme.Dark) foreach(var card in DialogElements(dialog).OfType<Border>().Where(b => b.Tag as string is "settings-card" or "dialog-form-card")) {
            if(card.Background is not SolidColorBrush brush) throw new InvalidOperationException("Dialog card lacks a theme fill.");
            var c=brush.Color;
            if((c.R+c.G+c.B)/3.0*c.A/255.0+32*(1-c.A/255.0)>120) throw new InvalidOperationException("Light dialog card in dark mode.");
        }
    }
    private async Task VerifyCompactDialogFormsForSmokeTestAsync(string report)
    {
        var originalTheme=Model.Settings.Get("theme","system");
        var project=Model.Project ?? throw new InvalidOperationException("Dialog fixture missing.");
        var before=await File.ReadAllBytesAsync(Path.Combine(project.Folder,"project.json"));
        Views.ExportDialog Export(int section) { var dialog=new Views.ExportDialog(Model); dialog.RequestForSmokeTest(section,0,0); return dialog; }
        var cases=new (string Name,Func<ContentDialog> Create,int Width,int Height)[] {
            ("import",()=>new Views.ImportAssetsDialog(this,project),800,760),
            ("timing",()=>new Views.TextTimingDialog("dialogue.srt",project.Duration,new[] {new Models.ImportTimingInspector.Row(1,"A sample line.",0,1)},"narration",Model,project),940,720),
            ("languages",()=>new ContentDialog {Title="Language and Saved Results",Content=new ScrollViewer {Content=BuildLanguageResultsPanel(project)},CloseButtonText="Close"},720,800),
            ("export-text",()=>Export(2),1040,760),
            ("export-audio",()=>Export(1),1040,760),
            ("voice-offline-gemini",()=>Views.SettingsDialog.VoiceConnectionDialog(OnlineService.Gemini,"No internet connection. Connect to the internet to use Gemini."),520,300),
            ("voice-offline-elevenlabs",()=>Views.SettingsDialog.VoiceConnectionDialog(OnlineService.ElevenLabs,"No internet connection. Connect to the internet to use ElevenLabs."),520,300),
            ("voice-offline",()=>Views.SettingsDialog.VoiceConnectionDialog(OnlineService.Sarvam,"No internet connection. Connect to the internet to use Sarvam."),520,300),
            ("piper-voices",()=>new Views.PiperVoicesDialog(),780,740),
            ("piper-delete",()=>new Views.PiperDeleteVoicesDialog(),720,700),
            ("sources",()=>new ContentDialog {Title="Audio sources",Content=new Views.AudioSourceControls(this,Model,exportHost:false),CloseButtonText="Done"},740,700)
        };
        try {
            foreach(var theme in new[] {"light","dark"}) foreach(var item in cases) {
                PreviewTheme(theme); var dialog=item.Create(); var showing=ShowDialogAsync(dialog,item.Width,item.Height);
                try {
                    await dialogPresentationVerifier!.WaitUntilReadyAsync(dialog); await Task.Delay(150);
                    VerifyCompactDialogLayout(dialog); dialogPresentationVerifier.Verify(dialog);
                    if(dialog is Views.ImportAssetsDialog import) await import.VerifyGroupedLayoutForSmokeTestAsync();
                    await CaptureSmokeImage((FrameworkElement)dialogPresentationVerifier.WindowFor(dialog).Content,Path.Combine(Path.GetDirectoryName(report)!,"compact-dialog-"+item.Name+"-"+theme+".png"));
                } finally {dialog.Hide();await showing;}
            }
            var size=AppWindow.Size;
            var scale=Root.XamlRoot.RasterizationScale;
            try {
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(640*scale),(int)(720*scale)));
                await Task.Delay(150);
                foreach(var theme in new[] {"light","dark"}) foreach(var item in cases.Where(c=>c.Name is "import" or "export-text")) {
                    PreviewTheme(theme);var dialog=item.Create();var showing=ShowDialogAsync(dialog,item.Width,item.Height);
                    try {
                        await dialogPresentationVerifier!.WaitUntilReadyAsync(dialog);await Task.Delay(150);
                        VerifyCompactDialogLayout(dialog);dialogPresentationVerifier.Verify(dialog);
                        await CaptureSmokeImage((FrameworkElement)dialogPresentationVerifier.WindowFor(dialog).Content,Path.Combine(Path.GetDirectoryName(report)!,"compact-dialog-"+item.Name+"-narrow-"+theme+".png"));
                    } finally {dialog.Hide();await showing;}
                }
            } finally {AppWindow.Resize(size);}
            // Header metadata, values and events must survive repeated preparation and theme changes.
            var picker=new ComboBox {Header="Language",ItemsSource=new[] {"English","Hindi"},SelectedIndex=1};
            var events=0;picker.SelectionChanged+=(_,_)=>events++;
            var form=new StackPanel();form.Children.Add(picker);
            Views.CompactDialogLayout.PrepareContent(form);Views.CompactDialogLayout.PrepareContent(form);
            if(picker.Header as string!="Language" || picker.SelectedIndex!=1 || events!=0)throw new InvalidOperationException("Form presentation changed a selected value or header.");
            picker.SelectedIndex=0;if(events!=1)throw new InvalidOperationException("Form presentation detached its selection action.");
            if(!before.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(project.Folder,"project.json"))))throw new InvalidOperationException("Canceling layout checks changed the fixture project.");
        } finally {PreviewTheme(originalTheme);}
    }
}
#endif
