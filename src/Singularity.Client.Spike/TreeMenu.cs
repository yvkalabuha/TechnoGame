using Godot;

namespace Singularity.Client.Spike;

/// <summary>Full-tree layer of the node menu (#473): it is drawn above the map, the panel and the buttons, and while the menu is open it takes every click so that one outside closes it. The tree owns state and drawing.</summary>
public sealed partial class MenuLayer : Control
{
    readonly TechTree _tree;
    public MenuLayer(TechTree tree)
    {
        _tree = tree; MouseFilter = MouseFilterEnum.Ignore; FocusMode = FocusModeEnum.All; Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }
    public override void _Draw() => _tree.PaintMenu(this);
    public override void _GuiInput(InputEvent e) => _tree.MenuInput(e);
}
