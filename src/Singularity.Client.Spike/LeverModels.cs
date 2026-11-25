#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Singularity.Core.Commands;
using Singularity.Core.Institutes;
using Singularity.Core.Policy;
using Singularity.Kernel;
using Singularity.Session;

namespace Singularity.Client.Spike;

// Pure models of the lever windows (TASK-0156, docs/ui/specs/levers-windows.md §7): one function picks what to show, the fixture frames and the live window call the same one.
// Godot-free so that unit tests and a run-time check on a real session state can use it. Texts are keys plus arguments; money is formatted through the function passed in.

/// <summary>One visible budget item: its percent, the largest percent the 100 % limit allows, the yearly money, the change against the shares in force.</summary>
public sealed record BudgetRow(string Key, int Pct, int InForcePct, int Max, double YearMillion, string Money, int DeltaPp, string DeltaMoney, bool AtLimit, bool Stop);

/// <summary>The budget window («Бюджет и резерв»). Money is in million 1990 dollars; a field the view does not carry is absent (HasData false), never zero.</summary>
public sealed record BudgetModel(
    bool HasData, double TotalYearMillion, string TotalMoney,
    ImmutableArray<BudgetRow> Rows, int OtherPct, string OtherMoney,
    int ReservePct, string ReserveYearMoney,
    bool ReserveOn, double BalanceMillion, string BalanceMoney, string LeakYearMoney, ImmutableArray<string> Forecast,
    bool ReserveEmpty, bool ReserveShrinks, bool EffortStop, int EffortStopPercent,
    bool Changed, string ApplyDisabledKey, bool Editable);

public static class BudgetWindowModel
{
    public const int Science = 0, Mastery = 1, Procurement = 2;
    public static readonly string[] Keys = { "science", "mastery", "procurement" };
    const long Pct1 = 10_000, Whole = 1_000_000;

    static long Hidden(BudgetShares s) => s.Capability.Raw + s.Quality.Raw + s.Security.Raw;
    static int PctOf(Fx6 x) => (int)((x.Raw + Pct1 / 2) / Pct1);

    /// <summary>The visible shares in force as whole percents (science, mastery, procurement).</summary>
    public static int[] InForcePct(BudgetShares s) => new[] { PctOf(s.Research), PctOf(s.Effort), PctOf(s.Procurement) };

    static Fx6[] Visible(BudgetShares s) => new[] { s.Research, s.Effort, s.Procurement };

    /// <summary>Raw share of item k for the edited percents: an item whose percent did not change keeps its exact value in force (a share of 28.4 % is not rounded to 28 % by a click elsewhere).</summary>
    static long RawOf(BudgetShares inForce, int[] pct, int k)
    {
        Fx6 f = Visible(inForce)[k];
        return pct[k] == PctOf(f) ? f.Raw : pct[k] * Pct1;
    }

    /// <summary>The command for the edited percents: the hidden items (tooling, quality, security) and every unchanged item stay exactly as in force, so the player never changes what the window does not show or did not touch.</summary>
    public static BudgetShares ToShares(BudgetShares inForce, int[] pct) =>
        new(Fx6.FromRaw(RawOf(inForce, pct, Science)), inForce.Capability, Fx6.FromRaw(RawOf(inForce, pct, Mastery)), Fx6.FromRaw(RawOf(inForce, pct, Procurement)), inForce.Quality, inForce.Security);

    /// <summary>Largest percent of item <paramref name="i"/> given the others: the sum with the hidden items may not pass 100 %.</summary>
    public static int MaxFor(BudgetShares inForce, int[] pct, int i)
    {
        long others = Hidden(inForce);
        for (int k = 0; k < pct.Length; k++) if (k != i) others += RawOf(inForce, pct, k);
        return (int)Math.Clamp((Whole - others) / Pct1, 0, 100);
    }

    /// <summary>The one line «no more: 100 % distributed» (shown once under the items, not under each slider): some item stands at its limit and the reserve holds nothing.</summary>
    public static bool ShowLimitNote(BudgetModel m) => m.HasData && m.ReservePct == 0 && m.Rows.Any(r => r.AtLimit);

    /// <summary>A «+» at the limit and a «−» at zero are dimmed like every inactive button (and leave the Tab order); the slider keeps the keyboard.</summary>
    public static bool PlusEnabled(BudgetRow r, bool editable) => editable && !r.AtLimit;
    public static bool MinusEnabled(BudgetRow r, bool editable) => editable && r.Pct > 0;

    /// <summary>«п. п.» is explained once, when a change is on screen.</summary>
    public static bool ShowPpNote(BudgetModel m) => m.HasData && m.Rows.Any(r => r.DeltaPp != 0);

    /// <summary>Footer when nothing can be applied: «Изменений нет» alone for «nothing changed» (the reason would only repeat it), the reason after it for any other cause.</summary>
    public static string FooterNone(string noChangeWord, string reasonKey, string reasonWord) =>
        reasonKey == "" || reasonKey == "why.budget.nochange" ? noChangeWord : LeverText.Join(noChangeWord, reasonWord);

    /// <param name="inputs">Inputs of the actor in the last monthly step; null before the first step (no data).</param>
    /// <param name="edit">The percents being edited (science, mastery, procurement); null = the shares in force.</param>
    public static BudgetModel Build(ActorInputsView inputs, int[] edit, bool foreign, MoneyFormat.Scale scale, Func<double, string> fmt)
    {
        if (inputs == null || scale == null)
            return new BudgetModel(false, 0, "", ImmutableArray<BudgetRow>.Empty, 0, "", 0, "", false, 0, "", "", ImmutableArray<string>.Empty, false, false, false, 0, false, "why.budget.nodata", false);
        var s = inputs.Shares;
        int[] force = InForcePct(s);
        int[] cur = edit ?? force;
        double yearIdx = inputs.Budget.ToDouble() * 12;
        double total = MoneyFormat.Million1990(yearIdx, scale);
        var rows = ImmutableArray.CreateBuilder<BudgetRow>(3);
        for (int i = 0; i < 3; i++)
        {
            int max = MaxFor(s, cur, i);
            double money = total * cur[i] / 100.0;
            double delta = total * (cur[i] - force[i]) / 100.0;
            rows.Add(new BudgetRow(Keys[i], cur[i], force[i], max, money, fmt(money), cur[i] - force[i], fmt(delta), cur[i] >= max, i == Mastery && cur[i] < scale.EffortStopPercent));
        }
        int other = (int)Math.Round(Hidden(s) / (double)Pct1, MidpointRounding.AwayFromZero);
        int reserve = Math.Max(0, 100 - other - cur.Sum());
        bool changed = !cur.SequenceEqual(force);
        double reserveYear = total * reserve / 100.0;
        bool on = inputs.Reserve is not null;
        double balance = 0, leakYear = 0; var forecast = ImmutableArray.CreateBuilder<string>(3);
        bool shrinks = false;
        if (on)
        {
            double b = inputs.Reserve!.Value.Balance.ToDouble(), leak = inputs.Reserve!.Value.LeakNextMonth.ToDouble();
            balance = MoneyFormat.Million1990(b, scale);
            double m = b > 0 ? Math.Clamp(leak / b, 0, 1) : 0;   // monthly leak fraction as the core pays it next month
            double keep = Math.Pow(1 - m, 12);
            leakYear = balance * (1 - keep);
            foreach (int years in new[] { 1, 3, 5 }) forecast.Add(fmt(balance * Math.Pow(1 - m, 12 * years)));
            shrinks = reserve > 0 ? leakYear > reserveYear : leakYear > 0;
        }
        string disabled = foreign ? "why.budget.foreign" : !changed ? "why.budget.nochange" : "";
        return new BudgetModel(true, total, fmt(total), rows.MoveToImmutable(), other, fmt(total * other / 100.0), reserve, fmt(reserveYear),
            on, balance, fmt(balance), fmt(leakYear), forecast.ToImmutable(), on && balance <= 0, on && shrinks, cur[Mastery] < scale.EffortStopPercent, scale.EffortStopPercent,
            changed, disabled, !foreign);
    }
}

/// <summary>One research goal the player can give to an institute: a node of the frontier with what it still needs and what this institute adds in its area (O-082: a bonus of 0 comes with the standing institute that does have a level there).</summary>
public sealed record GoalOption(int Node, string Name, int Discipline, double Remaining, bool Threshold, int BonusPct, string BetterInstituteId);

public enum InstituteState { Free, Goal, Building, Candidate }

/// <summary>A row of the institute table (standing institutes) or of the build list (candidates).</summary>
public sealed record InstituteRow(
    int Slot, string Id, InstituteState State, ImmutableArray<(int Discipline, int Level)> Levels,
    int GoalNode, string GoalName, int GoalBonusPct, double GoalRemaining,
    int ProposedGoal, string ProposedName, bool ProposesBuild,
    int RefYear, int MonthsLeft, int BuildMonths, bool BuildStartKnown,
    // price of a candidate, known before the click
    double CostPerYearMillion, double CostTotalMillion, int RateTenthPercent, int AheadYears, double AheadMultiplier,
    string BuildDisabledKey, int BuildDisabledArg,
    ImmutableArray<GoalOption> Options);

/// <summary>What the model needs of the scenario: the calendar of the month index and whether the actor is a small country (the same rule as InstituteLayout).</summary>
public sealed record InstituteContext(int StartYear, int StartMonth, bool SmallCountry);

public sealed record InstitutesModel(bool Enabled, ImmutableArray<InstituteRow> Standing, ImmutableArray<InstituteRow> Candidates, int SlotsTotal, int SlotsBusy, double ResearchYearMillion);

public static class InstitutesWindowModel
{
    /// <summary>The changeable rule of the core: one change of a goal per this many months (InstituteParams.ReassignMonths, shown after a refusal).</summary>
    public static int ReassignMonths => new InstituteParams().ReassignMonths;

    /// <param name="actor">The view of the player's actor; without institutes (default array) the model is disabled, not empty.</param>
    /// <param name="researchIdxMonth">Research money per month in budget indices (research share of the budget in force).</param>
    /// <param name="nodeName">Plain name of a node by its number.</param>
    /// <param name="nodeDiscipline">Discipline index of a node by its number, -1 unknown.</param>
    public static int YearOf(InstituteContext c, long month) => c.StartYear + (int)((c.StartMonth - 1 + month) / 12);

    /// <summary>Same rule as InstituteLayout: small = budget index not above SmallCountryShare of the largest.</summary>
    public static bool IsSmallCountry(Singularity.Core.Data.ScenarioData sc, int actor)
    {
        Fx6 largest = Fx6.Zero;
        foreach (Fx6 b in sc.BudgetIndex) largest = b > largest ? b : largest;
        return sc.BudgetIndex[actor] <= largest * new InstituteParams().SmallCountryShare;
    }

    /// <summary>The order of the core to start a project is applied in the month after the one the player sees (verified against the project start in the view, test InstituteQuote).</summary>
    public const int StartLag = 1;

    /// <summary>The check of the core (InstituteSystem.CanBuild) on what the view carries; the month is the one the order will be checked in.</summary>
    public static BuildCheck CheckBuild(InstituteView i, InstituteContext c, long month, InstituteParams p)
    {
        if (i.Standing) return BuildCheck.AlreadyStanding;
        if (i.BuildStart >= 0) return BuildCheck.AlreadyBuilding;
        if (YearOf(c, month) < i.RefYear - p.WindowYears) return BuildCheck.TooEarly;
        if (c.SmallCountry && i.TotalLevels > p.SmallCountryLevelCap) return BuildCheck.SmallCountryCap;
        return BuildCheck.Ok;
    }

    /// <summary>Locale key of the reason a project cannot start (null = it can); the argument of the key is given by <see cref="BuildWhyArg"/>.</summary>
    public static string BuildWhyKey(BuildCheck b) => b switch
    {
        BuildCheck.AlreadyStanding => "why.inst.standing",
        BuildCheck.AlreadyBuilding => "why.inst.building",
        BuildCheck.TooEarly => "why.inst.early",
        BuildCheck.SmallCountryCap => "why.inst.cap",
        _ => "",
    };

    static int BuildWhyArg(BuildCheck b, InstituteView i, InstituteParams p) => b == BuildCheck.TooEarly ? i.RefYear - p.WindowYears : b == BuildCheck.SmallCountryCap ? p.SmallCountryLevelCap : 0;

    public static InstitutesModel Build(ActorView actor, long month, double researchIdxMonth, MoneyFormat.Scale scale, InstituteContext ctx,
        Func<int, string> nodeName, Func<int, int> nodeDiscipline)
    {
        if (actor == null || actor.Institutes.IsDefault || scale == null)
            return new InstitutesModel(false, ImmutableArray<InstituteRow>.Empty, ImmutableArray<InstituteRow>.Empty, 0, 0, 0);
        var p = new InstituteParams();
        var research = actor.Research.IsDefault ? ImmutableArray<ResearchView>.Empty : actor.Research;
        var taken = new HashSet<int>();   // lookup only, never enumerated
        foreach (var i in actor.Institutes) if (i.Standing && i.Goal > 0) taken.Add(i.Goal);
        var standing = ImmutableArray.CreateBuilder<InstituteRow>();
        var cands = ImmutableArray.CreateBuilder<InstituteRow>();
        double resYearMillion = MoneyFormat.Million1990PerYear(researchIdxMonth, scale);
        int busy = 0;
        foreach (var inst in actor.Institutes)
        {
            var levels = ImmutableArray.CreateBuilder<(int, int)>();
            for (int d = 0; d < inst.Levels.Length; d++) if (inst.Levels[d] > 0) levels.Add((d, inst.Levels[d]));
            var lv = levels.OrderByDescending(x => x.Item2).ThenBy(x => x.Item1).ToImmutableArray();
            if (inst.Standing)
            {
                if (inst.Goal > 0) busy++;
                var opts = ImmutableArray.CreateBuilder<GoalOption>();
                foreach (var r in research)
                {
                    if (taken.Contains(r.Node) && r.Node != inst.Goal) continue;
                    int d = nodeDiscipline(r.Node);
                    int level = d >= 0 && d < inst.Levels.Length ? inst.Levels[d] : 0;
                    string better = "";
                    if (level == 0 && d >= 0)
                    {
                        int best = 0;
                        foreach (var o in actor.Institutes)
                            if (o.Standing && o.Slot != inst.Slot && d < o.Levels.Length && o.Levels[d] > best && (o.Goal == 0 || o.Goal == r.Node)) { best = o.Levels[d]; better = o.Id; }
                    }
                    opts.Add(new GoalOption(r.Node, nodeName(r.Node), d, Math.Max(0, (r.Need - r.Have).ToDouble()), r.Threshold, BonusPct(level, p), better));
                }
                int gd = inst.Goal > 0 ? nodeDiscipline(inst.Goal) : -1;
                int gl = gd >= 0 && gd < inst.Levels.Length ? inst.Levels[gd] : 0;
                double rem = 0; foreach (var r in research) if (r.Node == inst.Goal) rem = Math.Max(0, (r.Need - r.Have).ToDouble());
                standing.Add(new InstituteRow(inst.Slot, inst.Id, inst.Goal > 0 ? InstituteState.Goal : InstituteState.Free, lv,
                    inst.Goal, inst.Goal > 0 ? nodeName(inst.Goal) : "", BonusPct(gl, p), rem,
                    inst.ProposedGoal, inst.ProposedGoal > 0 ? nodeName(inst.ProposedGoal) : "", false, inst.RefYear, 0, 0, false, 0, 0, 0, 0, 1, "", 0, opts.ToImmutable()));
            }
            else
            {
                bool building = inst.BuildStart >= 0;
                int months = building ? inst.BuildMonths : InstituteMath.BuildMonths(inst.TotalLevels, p);
                // the share of the view: for a candidate it is the core's price if started now (TASK-0175)
                Fx6 rate = inst.BuildRate;
                int ahead = building ? Math.Max(0, inst.RefYear - YearOf(ctx, inst.BuildStart + inst.BuildMonths - 1)) : Math.Max(0, inst.RefYear - YearOf(ctx, month + StartLag + months - 1));
                double perMonth = MoneyFormat.Million1990(rate.ToDouble() * researchIdxMonth, scale);
                BuildCheck chk = CheckBuild(inst, ctx, month + StartLag, p);
                string why = BuildWhyKey(chk); int arg = BuildWhyArg(chk, inst, p);
                int left = building ? Math.Max(0, (int)(inst.BuildStart + inst.BuildMonths - month)) : 0;
                cands.Add(new InstituteRow(inst.Slot, inst.Id, building ? InstituteState.Building : InstituteState.Candidate, lv, 0, "", 0, 0, 0, "", inst.ProposesBuild,
                    inst.RefYear, left, building ? inst.BuildMonths : months, true,
                    perMonth * 12, perMonth * months, (int)((rate.Raw + 500) / 1000), ahead, InstituteMath.AheadMultiplier(ahead, p).ToDouble(), why, arg, ImmutableArray<GoalOption>.Empty));
            }
        }
        return new InstitutesModel(true, standing.ToImmutable(), cands.ToImmutable(), standing.Count, busy, resYearMillion);
    }

    /// <summary>Speed bonus of an institute level in the area of a goal (core: LevelMultiplier - 1), whole percent.</summary>
    public static int BonusPct(int level, InstituteParams p) => (int)((InstituteMath.LevelMultiplier(level, p).Raw - Fx6.One.Raw + 5_000) / 10_000);

    /// <summary>Words of a refusal the session gave back (CommandRefusal name): a key of the locale or null when only the generic text fits.</summary>
    public static string RefusalKey(string reason) => reason switch
    {
        "PriorityNotAvailable" => "refusal.PriorityNotAvailable",
        "PriorityTooSoon" => "refusal.PriorityTooSoon",
        "BuildRefused" => "refusal.BuildRefused",
        "InstitutesOff" => "refusal.InstitutesOff",
        "ReserveOff" => "refusal.ReserveOff",
        "BadShares" => "refusal.BadShares",
        "LineLimit" => "refusal.LineLimit",
        "NoProposal" => "refusal.NoProposal",
        "StaleHandle" => "refusal.StaleHandle",
        "LicenceRefused" => "refusal.LicenceRefused",
        "LicenceNotAllowed" => "refusal.LicenceNotAllowed",
        "BadOffer" => "refusal.BadOffer",
        "NeedsConfirmation" => "refusal.NeedsConfirmation",
        _ => null,
    };
}

/// <summary>What a why_not link points the player at.</summary>
public static class WhyLines
{
    /// <summary>Node a link asks the player to study: the subject of a knowledge link, the process of a capability link, and for an engine-generation limit the node whose study lifts it (DEBT-0095). -1 = none.</summary>
    public static int SubjectNode(Singularity.Core.WhyNot.Link l) =>
        l.Layer == Singularity.Core.WhyNot.WhyNotLayer.Knowledge ? l.SubjectId
        : l.Layer == Singularity.Core.WhyNot.WhyNotLayer.Capability ? l.Process
        : l.Cause == Singularity.Core.WhyNot.WhyNotCause.EngineGenLimit ? l.SubjectId : -1;
}

/// <summary>Auto-pause rule (#196, #481): a signal that turns on (a deficit, a refusal, a licence event) pauses a running game once; nothing pauses a paused game.</summary>
public static class AutoPauseLogic
{
    static readonly string[] Keys = { "sig.alloy", "sig.refusal", "sig.licence" };
    public static bool ShouldPause(string signalKey, bool on, int speed, bool enabled) => enabled && on && speed > 0 && Array.IndexOf(Keys, signalKey) >= 0;
}
