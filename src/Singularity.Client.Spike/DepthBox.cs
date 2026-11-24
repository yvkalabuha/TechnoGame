using Godot;

namespace Singularity.Client.Spike;

/// <summary>Sheet with thickness (VISUAL_DIRECTION v0.6 3.1, TASK-0136 spec 2.10): a soft shadow outside, a 3 du edge strip at the right and bottom,
/// a 1 du light rim at the top-left. Static (no animation). The body style supplies fill, frame and content margins; the edge strip is added to the right and bottom margins.</summary>
public partial class DepthBox : StyleBox
{
    public const float Edge = 3;
    static readonly Color EdgeColor = new("DCDACF"), Rim = new("FFFFFA");
    StyleBoxFlat _edge, _body;

    public static DepthBox Make(StyleBoxFlat body)
    {
        var edge = Ui.Box(EdgeColor, Ui.Rule, 1, 0, body.CornerRadiusTopLeft);
        edge.ShadowSize = 14; edge.ShadowOffset = new Vector2(4, 6); edge.ShadowColor = new Color(0, 0, 0, 0.22f);
        var d = new DepthBox { _edge = edge, _body = body };
        d.SetContentMargin(Side.Left, body.ContentMarginLeft); d.SetContentMargin(Side.Top, body.ContentMarginTop);
        d.SetContentMargin(Side.Right, body.ContentMarginRight + Edge); d.SetContentMargin(Side.Bottom, body.ContentMarginBottom + Edge);
        return d;
    }

    public override void _Draw(Rid toCanvasItem, Rect2 rect)
    {
        var bodyR = new Rect2(rect.Position, rect.Size - new Vector2(Edge, Edge));
        _edge.Draw(toCanvasItem, new Rect2(rect.Position + new Vector2(Edge, Edge), bodyR.Size));
        _body.Draw(toCanvasItem, bodyR);
        float b = _body.BorderWidthLeft;
        RenderingServer.CanvasItemAddRect(toCanvasItem, new Rect2(bodyR.Position + new Vector2(b, b), new Vector2(bodyR.Size.X - 2 * b, 1)), Rim);
        RenderingServer.CanvasItemAddRect(toCanvasItem, new Rect2(bodyR.Position + new Vector2(b, b + 1), new Vector2(1, bodyR.Size.Y - 2 * b - 1)), Rim);
    }

    /// <summary>Draws a sheet by hand (legend, stamp): the same style at a rectangle of the current item.</summary>
    public static readonly DepthBox Plain = Make(Ui.Box(Ui.Sheet, Ui.Ink, 1, 0, Ui.R3));
}
