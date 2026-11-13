using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Vidi_Studio_Windows.Views;

/// <summary>A layout-neutral, named UIA group that preserves its child controls.</summary>
public sealed class AccessibilityGroup : ContentControl
{
    public AccessibilityGroup()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Template = (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContentControl'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}' Padding='{TemplateBinding Padding}'><ContentPresenter Content='{TemplateBinding Content}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border></ControlTemplate>");
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Control);
    }
    public UIElement? Child { get => Content as UIElement; set => Content = value; }
    public AccessibilityGroup(string name) : this() { AutomationProperties.SetName(this, name); }
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    private sealed class GroupPeer : FrameworkElementAutomationPeer
    {
        public GroupPeer(AccessibilityGroup owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(AccessibilityGroup);
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        // Keep the framework's child traversal; never merge or replace the controls.
    }
}
