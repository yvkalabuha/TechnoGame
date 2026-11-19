using System;
using System.Linq;
using Godot;

namespace Singularity.Client.Spike;

/// <summary>Credits (DEBT-0050, ADR-0009): music and sounds from assets/credits.json, the single list the tests check against the audio folders.</summary>
public sealed partial class CreditsSheet : Control
{
    public event Action Closed;
    readonly PanelContainer _sheet;
    readonly Action<Vector2> _fit;
    readonly VBoxContainer _body = new();
    readonly CaptionButton _back;
    System.Collections.Generic.IReadOnlyList<CreditEntry> _entries;

    public CreditsSheet()
    {
        SetAnchorsPreset(LayoutPreset.FullRect); Visible = false; MouseFilter = MouseFilterEnum.Ignore;
        AddChild(MenuUi.Scrim());
        _sheet = MenuUi.CenteredSheet(new Vector2(880, 560), out _fit);
        AddChild(_sheet);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 10);
        _sheet.AddChild(root);
        root.AddChild(Ui.Lbl("credits.title", 22, null, FontKind.Draft));
        root.AddChild(new ColorRect { Color = Ui.InkLine[Ui.Olive], CustomMinimumSize = new Vector2(0, 3) });
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill; _body.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_body);
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(scroll);
        _back = MenuUi.Primary("credits.back", Close);
        MenuUi.FocusRing(_back);
        var foot = new HBoxContainer();
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }); foot.AddChild(_back);
        root.AddChild(foot);
        Loc.Changed += OnLang; TreeExiting += () => Loc.Changed -= OnLang;
        Resized += () => _fit(Size);
    }

    void OnLang() { if (Visible) Rebuild(); }

    public void Open()
    {
        _entries ??= CreditsData.Parse(Godot.FileAccess.GetFileAsString("res://assets/credits.json"));
        Visible = true; _fit(Size);
        Rebuild();
        _back.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() { Visible = false; Closed?.Invoke(); }

    void Rebuild()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        _body.AddChild(Ui.Wrap(Loc.T("credits.intro"), 14, Ui.Ink2));
        Section("credits.music", CreditsData.Music);
        Section("credits.sound", CreditsData.Sound);
    }

    void Section(string key, string kind)
    {
        _body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _body.AddChild(Ui.Lbl(key, 18, Ui.InkText[Ui.Olive], FontKind.Draft));
        _body.AddChild(new ColorRect { Color = Ui.Rule, CustomMinimumSize = new Vector2(0, 1) });
        foreach (var e in _entries.Where(x => x.Kind == kind))
        {
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 2);
            box.AddChild(Ui.Wrap(e.Credit != "" ? e.Credit : $"{e.Title} · {Loc.T("credits.by")} {e.Author}", 16));
            string line = $"{Loc.T("credits.licence")}: {e.Licence} · {e.LicenceUrl}";
            box.AddChild(Ui.Wrap(line, 14, Ui.Ink2));
            box.AddChild(Ui.Wrap(e.Url, 14, Ui.Ink2));
            string ch = e.ChangesIn(Loc.Lang);
            if (ch != "") box.AddChild(Ui.Wrap($"{Loc.T("credits.changes")}: {ch}", 14, Ui.Ink2));
            if (e.Files.Count == 0) box.AddChild(Ui.Lbl("credits.soon", 14, Ui.Ink3));
            var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_bottom", 8); margin.AddChild(box);
            _body.AddChild(margin);
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        Close();
    }
}
