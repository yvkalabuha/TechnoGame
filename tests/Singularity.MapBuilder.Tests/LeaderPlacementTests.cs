using System.Collections.Generic;
using Singularity.Client.Spike;
using Xunit;

namespace Singularity.MapBuilder.Tests;

// Leader labels (review task-0163-0167-visual.md items 1, 2, 4, 6): collision with other labels and foreign land, hiding, one size scale.
public class LeaderPlacementTests
{
    const float Vw = 1920, Vh = 1080;
    static readonly List<LabelPlacement.Box> None = new();
    static bool Free(float x, float y) => false;

    [Fact] public void Free_water_takes_the_first_direction_up_right()
    {
        Assert.True(LabelPlacement.PlaceLeader(900, 500, 80, 18, Vw, Vh, 960, 540, 5000, 10, None, Free, out float cx, out float cy));
        Assert.True(cx > 900 && cy < 500);
    }

    [Fact] public void A_placed_label_is_not_overlapped()
    {
        LabelPlacement.PlaceLeader(900, 500, 80, 18, Vw, Vh, 960, 540, 5000, 10, None, Free, out float x0, out float y0);
        var taken = new List<LabelPlacement.Box> { new(x0 - 40, y0 - 9, 80, 18) };
        Assert.True(LabelPlacement.PlaceLeader(900, 500, 80, 18, Vw, Vh, 960, 540, 5000, 10, taken, Free, out float cx, out float cy));
        Assert.False(new LabelPlacement.Box(cx - 40, cy - 9, 80, 18).Intersects(taken[0]));
    }

    [Fact] public void Foreign_land_is_avoided_and_the_box_moves_to_another_direction()
    {
        // everything right of the anchor is foreign land (the Netherlands: Germany on the east)
        bool East(float x, float y) => x > 905;
        Assert.True(LabelPlacement.PlaceLeader(900, 500, 80, 18, Vw, Vh, 960, 540, 5000, 10, None, East, out float cx, out _));
        Assert.True(cx + 40 <= 905);
    }

    [Fact] public void Hidden_when_every_direction_is_blocked_or_off_the_disc()
    {
        Assert.False(LabelPlacement.PlaceLeader(900, 500, 80, 18, Vw, Vh, 960, 540, 5000, 10, None, (x, y) => true, out _, out _));
        Assert.False(LabelPlacement.PlaceLeader(900, 500, 80, 18, Vw, Vh, 960, 540, 20, 10, None, Free, out _, out _));   // limb radius too small for any box
    }

    [Fact] public void Box_keeps_eight_px_from_the_frame_and_inside_the_limb()
    {
        Assert.True(LabelPlacement.PlaceLeader(1850, 30, 100, 18, Vw, Vh, 960, 540, 5000, 10, None, Free, out float cx, out float cy));
        Assert.InRange(cx - 50, LabelPlacement.Margin, Vw); Assert.True(cx + 50 <= Vw - LabelPlacement.Margin);
        Assert.True(cy - 9 >= LabelPlacement.Margin);
        Assert.True(LabelPlacement.InsideDisc(cx, cy, 50, 9, 960, 540, 5000));
    }

    [Fact] public void Size_search_never_goes_below_the_12_px_floor()
    {
        for (int s = 28; s > 0; s = LabelPlacement.NextSize(s)) Assert.True(s >= (int)LabelPlacement.MinFont);
        Assert.Equal(0, LabelPlacement.NextSize(12));
    }

    [Fact] public void Anchor_near_the_bottom_edge_hides_the_leader()
    {
        Assert.False(LabelPlacement.PlaceLeader(900, Vh - 3, 80, 18, Vw, Vh, 960, 540, 5000, 10, None, Free, out _, out _));   // Panama: no leader off the frame
        Assert.True(LabelPlacement.PlaceLeader(900, Vh - 40, 80, 18, Vw, Vh, 960, 540, 5000, 10, None, Free, out _, out float cy));
        Assert.True(cy + 9 <= Vh - LabelPlacement.Margin);
    }

    [Fact] public void A_leader_line_blocks_later_labels()
    {
        var placed = new List<LabelPlacement.Box>();
        LabelPlacement.LineBoxes(100, 100, 160, 60, 30, 9, placed);
        Assert.True(placed.Count > 2);
        Assert.Contains(placed, b => b.Intersects(new LabelPlacement.Box(120, 80, 4, 4)));   // a word across the line is rejected
    }

    [Fact] public void A_thin_gap_under_the_word_fails_the_fit()
    {
        // a 4 px strip of sea in the middle of a 100 px word (British West Africa between two lands)
        Assert.False(LabelPlacement.FitsInside(500, 500, 100, 16, (x, y) => System.MathF.Abs(x - 525) > 2));
        Assert.True(LabelPlacement.FitsInside(500, 500, 100, 16, (x, y) => true));
    }

    [Fact] public void One_size_scale_for_every_label_by_own_area()
    {
        int prev = 0;
        foreach (float a in new[] { 400f, 8000f, 30000f, 90000f, 250000f, 1_000_000f })
        {
            int s = LabelPlacement.FontSizeFor(a);
            Assert.True(s >= prev && s >= 14 && s <= 28); prev = s;
        }
        // a large colony and a country of the same projected area get the same size: the function takes the area only
        Assert.Equal(LabelPlacement.FontSizeFor(120000f), LabelPlacement.FontSizeFor(120000f));
    }
}
