using System;
using Godot;
using Singularity.Kernel;

namespace Singularity.Client.Spike;

/// <summary>Values of the settings screen (spec §4). A plain record so that the sheet can keep «at open» and «working» copies.</summary>
public sealed class SettingsData
{
    public static readonly int[] FpsLimits = { 30, 60, 120, 0 };
    public static readonly double[] TipDelays = { 0.3, 0.6, 1.0 };
    public static readonly string[] BusNames = { "Master", "Music", "Ambient", "UI", "Sfx", "Alert" };

    // video
    public int WindowMode = 1;           // 0 borderless full window, 1 fullscreen (a framed window exists only behind the --window flag, #424)
    public int Quality = 2;              // 0 low, 1 medium, 2 high, 3 custom
    public int Upscaler;                 // 0 off, 1 FSR 1.0, 2 FSR 2.2 (DLSS is shown disabled)
    public int UpscaleMode;              // 0 quality, 1 balance, 2 performance
    public bool Vsync = true;
    public int FpsLimit = 1;             // index of FpsLimits
    public double UiScale = 100;         // percent
    // sound, percent 0..100 in the order of BusNames
    public double[] Volume = { 100, 80, 80, 90, 100, 100 };
    public bool SoundInBackground = true;
    // general
    public string Lang = "ru";
    public int DateFormat;               // 0 by language, 1 ISO
    public bool PauseOnAlarm = true;
    public int TipDelay = 1;             // index of TipDelays
    public bool Animations = true;
    public int Textures = 2;             // paper texture preset: 0 off, 1 weak, 2 normal (#360)
    public int DeskInstruments = 2;      // layer 1, objects on the desk: 0 off, 1 weak, 2 normal (#404; drawn from a later task)
    public int OrganInstruments = 2;     // layer 2, instrument-style controls: 0 off (the plain «before» look), 1 weak (ink only), 2 normal (#404)
    public bool RailWide;                // area rail: false narrow (icon + letter), true wide (with names)
    public int GlobeLook = (int)GlobeLookLogic.Default;   // 0 prior flat look, 1 matte, 2 matte + haze, 3 physical atmosphere (owner #444: the default)

    public SettingsData Clone()
    {
        var c = (SettingsData)MemberwiseClone();
        c.Volume = (double[])Volume.Clone();
        return c;
    }

    /// <summary>True when a change needs the «keep these settings?» dialog (a bad value could leave the screen unusable).</summary>
    public bool NeedsConfirm(SettingsData old) => WindowMode != old.WindowMode || Upscaler != old.Upscaler || UpscaleMode != old.UpscaleMode;

    public void ResetVideo() { var d = new SettingsData(); WindowMode = d.WindowMode; Quality = d.Quality; Upscaler = d.Upscaler; UpscaleMode = d.UpscaleMode; Vsync = d.Vsync; FpsLimit = d.FpsLimit; UiScale = d.UiScale; }
    public void ResetSound() { var d = new SettingsData(); Volume = d.Volume; SoundInBackground = d.SoundInBackground; }
    public void ResetGeneral() { var d = new SettingsData(); DateFormat = d.DateFormat; PauseOnAlarm = d.PauseOnAlarm; TipDelay = d.TipDelay; Animations = d.Animations; Textures = d.Textures; DeskInstruments = d.DeskInstruments; OrganInstruments = d.OrganInstruments; RailWide = d.RailWide; GlobeLook = d.GlobeLook; }
}

/// <summary>Current settings, `user://settings.cfg`, and their effect on the engine.</summary>
public static class Settings
{
    const string Path = "user://settings.cfg";
    public static SettingsData Current = new();
    public static bool Loaded { get; private set; }
    /// <summary>Screenshot and bench runs must not touch the owner's saved settings.</summary>
    public static bool ReadOnly { get; set; }
    /// <summary>--window: a framed 1920x1080 window for tests (the game itself is fullscreen or borderless, #424).</summary>
    public static bool Framed { get; set; }
    /// <summary>Set by the client: the globe look changed (the settings sheet previews it live).</summary>
    public static Action<int> OnGlobeLook;
    static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public static void Load()
    {
        var cf = new ConfigFile();
        if (cf.Load(Path) != Error.Ok) return;
        Loaded = true;
        var s = new SettingsData();
        if (cf.HasSectionKey("video", "display_mode")) s.WindowMode = Math.Clamp((int)cf.GetValue("video", "display_mode", s.WindowMode), 0, 1);
        else if (cf.HasSectionKey("video", "window_mode")) s.WindowMode = SettingsLogic.MigrateWindowMode((int)cf.GetValue("video", "window_mode", 2));   // preference of the build before #424
        s.Quality = Math.Clamp((int)cf.GetValue("video", "quality", s.Quality), 0, 3);
        s.Upscaler = Math.Clamp((int)cf.GetValue("video", "upscaler", s.Upscaler), 0, 2);
        s.UpscaleMode = Math.Clamp((int)cf.GetValue("video", "upscale_mode", s.UpscaleMode), 0, 2);
        s.Vsync = (bool)cf.GetValue("video", "vsync", s.Vsync);
        s.FpsLimit = Math.Clamp((int)cf.GetValue("video", "fps_limit", s.FpsLimit), 0, SettingsData.FpsLimits.Length - 1);
        s.UiScale = SettingsLogic.SnapUiScale((double)cf.GetValue("video", "ui_scale", s.UiScale));
        for (int i = 0; i < s.Volume.Length; i++) s.Volume[i] = Math.Clamp((double)cf.GetValue("sound", SettingsData.BusNames[i], s.Volume[i]), 0, 100);
        s.Volume[5] = Math.Max(s.Volume[5], AudioSettings.AlertFloor * 100);
        s.SoundInBackground = (bool)cf.GetValue("sound", "in_background", s.SoundInBackground);
        s.Lang = (string)cf.GetValue("general", "lang", s.Lang);
        if (Array.IndexOf(Loc.Languages, s.Lang) < 0) s.Lang = "ru";
        s.DateFormat = Math.Clamp((int)cf.GetValue("general", "date_format", s.DateFormat), 0, 1);
        s.PauseOnAlarm = (bool)cf.GetValue("general", "pause_on_alarm", s.PauseOnAlarm);
        s.TipDelay = Math.Clamp((int)cf.GetValue("general", "tip_delay", s.TipDelay), 0, 2);
        s.Animations = (bool)cf.GetValue("general", "animations", s.Animations);
        s.Textures = Math.Clamp((int)cf.GetValue("general", "textures", s.Textures), 0, 2);
        s.DeskInstruments = Math.Clamp((int)cf.GetValue("general", "desk_instruments", s.DeskInstruments), 0, 2);
        s.OrganInstruments = Math.Clamp((int)cf.GetValue("general", "organ_instruments", s.OrganInstruments), 0, 2);
        s.RailWide = (bool)cf.GetValue("general", "rail_wide", s.RailWide);
        s.GlobeLook = Math.Clamp((int)cf.GetValue("general", "globe_look", s.GlobeLook), 0, 3);
        Current = s;
    }

    public static void Save()
    {
        if (ReadOnly) return;
        var s = Current; var cf = new ConfigFile();
        cf.SetValue("video", "display_mode", s.WindowMode); cf.SetValue("video", "quality", s.Quality);
        cf.SetValue("video", "upscaler", s.Upscaler); cf.SetValue("video", "upscale_mode", s.UpscaleMode); cf.SetValue("video", "vsync", s.Vsync);
        cf.SetValue("video", "fps_limit", s.FpsLimit); cf.SetValue("video", "ui_scale", s.UiScale);
        for (int i = 0; i < s.Volume.Length; i++) cf.SetValue("sound", SettingsData.BusNames[i], s.Volume[i]);
        cf.SetValue("sound", "in_background", s.SoundInBackground);
        cf.SetValue("general", "lang", s.Lang); cf.SetValue("general", "date_format", s.DateFormat); cf.SetValue("general", "pause_on_alarm", s.PauseOnAlarm);
        cf.SetValue("general", "tip_delay", s.TipDelay); cf.SetValue("general", "animations", s.Animations);
        cf.SetValue("general", "textures", s.Textures); cf.SetValue("general", "rail_wide", s.RailWide);
        cf.SetValue("general", "desk_instruments", s.DeskInstruments); cf.SetValue("general", "organ_instruments", s.OrganInstruments); cf.SetValue("general", "globe_look", s.GlobeLook);
        cf.Save(Path);
    }

    static bool Headless => DisplayServer.GetName() == "headless";

    /// <summary>Logs the real window size and px/du; a real game on a display of 1920x1080 or more that renders below the scale of its setting is put back into fullscreen once.</summary>
    static void ReportScale(SettingsData s, Window win)
    {
        var size = DisplayServer.WindowGetSize(); var screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
        double k = SettingsLogic.PixelsPerDu(size.X, size.Y, s.UiScale);
        Godot.GD.Print($"window {size.X}x{size.Y} on screen {screen.X}x{screen.Y}: {k:F3} px/du at UI scale {s.UiScale} % ({(Framed ? "framed test window, no visual PASS" : "game")})");
        if (!Framed && screen.X >= SettingsLogic.BaseWidth && screen.Y >= SettingsLogic.BaseHeight && !SettingsLogic.ScaleFloorOk(size.X, size.Y, s.UiScale))
        {
            Godot.GD.PushWarning("render below 1 px/du on a Full HD display: forcing fullscreen");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetMode(s.WindowMode == 1 ? DisplayServer.WindowMode.ExclusiveFullscreen : DisplayServer.WindowMode.Fullscreen);   // O-034, as in Apply
        }
    }

    /// <summary>Applies a settings set to the engine. <paramref name="window"/> false leaves the window size and mode alone (screenshot runs keep a fixed window).</summary>
    public static void Apply(SettingsData s, Window win, bool window = true)
    {
        if (!Headless && window)
        {
            if (Framed) { DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false); DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetSize(new Vector2I(SettingsLogic.BaseWidth, SettingsLogic.BaseHeight)); }
            else
            {
                // O-034: "fullscreen" is exclusive, "borderless" is Godot's non-exclusive Fullscreen (a borderless full window). A Windowed window of the monitor size is not used:
                // a window manager with a panel clamps it below the screen height and the UI would scale under 1 px/du (A1).
                DisplayServer.WindowSetMode(s.WindowMode == 1 ? DisplayServer.WindowMode.ExclusiveFullscreen : DisplayServer.WindowMode.Fullscreen);
            }
            win.GetTree().CreateTimer(1.0).Timeout += () => ReportScale(s, win);   // the mode change lands a few frames later
        }
        DisplayServer.WindowSetVsyncMode(s.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = SettingsData.FpsLimits[s.FpsLimit];
        // Scale policy (UI_DESIGN_SYSTEM 4.3): base 1920x1080 du, larger screens scale up by min(w/1920, h/1080), the setting multiplies on top.
        win.ContentScaleSize = new Vector2I(SettingsLogic.BaseWidth, SettingsLogic.BaseHeight);
        win.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        win.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        win.ContentScaleFactor = (float)(s.UiScale / 100.0);
        var vp = win.GetViewport();
        vp.Msaa3D = s.Quality switch { 0 => Viewport.Msaa.Disabled, 1 => Viewport.Msaa.Msaa2X, 3 => vp.Msaa3D, _ => Viewport.Msaa.Msaa4X };
        // Upscaling acts on the 3D view only (the UI is drawn at native resolution).
        vp.Scaling3DMode = s.Upscaler switch { 1 => Viewport.Scaling3DModeEnum.Fsr, 2 => Viewport.Scaling3DModeEnum.Fsr2, _ => Viewport.Scaling3DModeEnum.Bilinear };
        vp.Scaling3DScale = s.Upscaler == 0 ? 1f : (float)SettingsLogic.RenderScale(s.UpscaleMode);
        ProjectSettings.SetSetting("gui/timers/tooltip_delay_sec", SettingsData.TipDelays[s.TipDelay]);
        Loc.IsoNumbers = s.DateFormat == 1;
        OnGlobeLook?.Invoke(s.GlobeLook);
        PaperSurface.SetPreset(s.Textures);
        InstrumentLogic.OrganLevel = s.OrganInstruments;
        AudioServer.SetBusVolumeDb(0, (float)(s.Volume[0] < 0.1 ? -80 : 20 * Math.Log10(s.Volume[0] / 100)));
    }

    /// <summary>Date text by the format setting: by language (RU 02.09.1945, EN 2 Sep 1945) or ISO.</summary>
    public static string Date(CivilDate d) =>
        Current.DateFormat == 1 ? d.ToString() : Loc.Lang == "ru" ? $"{d.Day:D2}.{d.Month:D2}.{d.Year:D4}" : $"{d.Day} {Months[d.Month - 1]} {d.Year}";
}
