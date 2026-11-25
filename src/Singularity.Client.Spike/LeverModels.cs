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

    /// <summary>The command for the edited percents: the hidden items (tooling, quality, security) stay exactly as in force, so the player never changes what the window does not show.</summary>
    public static BudgetShares ToShares(BudgetShares inForce, int[] pct) =>
        new(Fx6.FromRaw(pct[Science] * Pct1), inForce.Capability, Fx6.FromRaw(pct[Mastery] * Pct1), Fx6.FromRaw(pct[Procurement] * Pct1), inForce.Quality, inForce.Security);

    /// <summary>Largest percent of item <paramref name="i"/> given the others: the sum with the hidden items may not pass 100 %.</summary>
    public static int MaxFor(BudgetShares inForce, int[] pct, int i)
    {
        long others = Hidden(inForce);
        for (int k = 0; k < pct.Length; k++) if (k != i) others += pct[k] * Pct1;
        return (int)Math.Clamp((Whole - others) / Pct1, 0, 100);
    }

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

/// <summary>One research goal the player can give to an institute: a node of the frontier with what it still needs and what this institute adds in its area.</summary>
public sealed record GoalOption(int Node, string Name, int Discipline, double Remaining, bool Threshold, int BonusPct);

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

public sealed record InstitutesModel(bool Enabled, ImmutableArray<InstituteRow> Standing, ImmutableArray<InstituteRow> Candidates, int SlotsTotal, int SlotsBusy, double ResearchYearMillion);

public static class InstitutesWindowModel
{
    /// <summary>The changeable rule of the core: one change of a goal per this many months (InstituteParams.ReassignMonths, shown after a refusal).</summary>
    public static int ReassignMonths => new InstituteParams().ReassignMonths;

    /// <param name="actor">The view of the player's actor; without institutes (default array) the model is disabled, not empty.</param>
    /// <param name="researchIdxMonth">Research money per month in budget indices (research share of the budget in force).</param>
    /// <param name="nodeName">Plain name of a node by its number.</param>
    /// <param name="nodeDiscipline">Discipline index of a node by its number, -1 unknown.</param>
    public static InstitutesModel Build(ActorView actor, int year, long month, double researchIdxMonth, MoneyFormat.Scale scale,
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
                    opts.Add(new GoalOption(r.Node, nodeName(r.Node), d, Math.Max(0, (r.Need - r.Have).ToDouble()), r.Threshold, BonusPct(level, p)));
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
                int ahead = Math.Max(0, inst.RefYear - year);
                int months = InstituteMath.BuildMonths(inst.TotalLevels, p);
                Fx6 rate = InstituteMath.BuildRate(inst.TotalLevels, ahead, p);
                double perMonth = MoneyFormat.Million1990(rate.ToDouble() * researchIdxMonth, scale);
                string why = ""; int arg = 0;
                if (building) why = "why.inst.building";
                else if (year < inst.RefYear - p.WindowYears) { why = "why.inst.early"; arg = inst.RefYear - p.WindowYears; }
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
