using Godot;

namespace Singularity.Client.Spike;

/// <summary>Slider of the lever window with its own two-tone focus ring (A7): the theme "focus" box of an HSlider did not show in a frame (TASK-0156 round 2),
/// so the ring is drawn here, like the one of CaptionButton: 2 px ink outside, 2 px paper inside, so that one of them reads on every fill.</summary>
public sealed partial class LeverSlider : HSlider
{
    static readonly StyleBoxFlat RingOuter = Ring(Ui.Ink), RingInner = Ring(Ui.Paper);
    static StyleBoxFlat Ring(Color c) { var s = new StyleBoxFlat { DrawCenter = false, BorderColor = c }; s.SetBorderWidthAll(2); s.SetCornerRadiusAll(3); return s; }

    public override void _Draw()
    {
        if (!HasFocus()) return;
        DrawStyleBox(RingOuter, new Rect2(Vector2.Zero, Size));
        DrawStyleBox(RingInner, new Rect2(new Vector2(2, 2), Size - new Vector2(4, 4)));
    }
}
