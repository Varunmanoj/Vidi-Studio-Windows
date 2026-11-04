using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System.Linq;

namespace Vidi_Studio_Windows;

public sealed partial class MainWindow
{
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
