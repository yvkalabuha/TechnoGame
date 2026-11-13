using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Singularity.Core.Commands;
using Singularity.Core.Data;
using Singularity.Core.Industry;
using Singularity.Core.Knowledge;
using Singularity.Core.Policy;
using Singularity.Core.Products;
using Singularity.Core.WhyNot;
using Singularity.Kernel;

namespace Singularity.Session;

/// <summary>
/// The world of the jet slice assembled in one place (ADR-0014 п. 1, SLICE_COMPOSITION §2): one table with a row per actor (a site is an
/// actor, spec §3.5 A27), the monthly systems in the fixed order Knowledge → Industry → Products → Policy (the order is part of the
/// column schema), the input adapters, the RNG streams, the channel switches and the initialisation phase (A8). Nothing here
/// computes domain formulas: the adapters call the public pure functions of Core. Internal: a world is reached through
/// <see cref="SliceSession"/>. The events of T6d (licences, alloy disruption) live in the Policy system (spec A43): they are decided there and act from the next month.
/// </summary>
internal sealed class SliceWorld
{
    /// <summary>Version of the column schema (ADR-0013 п. 2); raised with every change of the table layout (pinned by a fingerprint test).</summary>
    public const int SchemaVersion = 7;

    // Layout constants of the slice (spec §1.4, §3.3, §2): engineer and skilled pools, bureaus, cohort years 1945…1977.
    public const int WorkforceCategories = 2;
    public const int Bureaus = 2;
    public const int Years = 33;
    public const string ProductClass = "jet_fighter";

    public SliceWorld(GameData data, SliceRunConfig config)
    {
        if (config.Layer is not (LayerControl.Full or LayerControl.Lk or LayerControl.Lk0 or LayerControl.Lc0))
            throw new InvalidOperationException($"Layer control {(int)config.Layer} is not supported (L_C with t_known and L_P need Core support that does not exist yet).");
        if ((config.Layer == LayerControl.Lk) != (config.LkTables is not null))
            throw new ArgumentException("LkTables are needed exactly with Layer = Lk.", nameof(config));
        // The tables are copied: a caller changing its arrays later must not change a running world.
        Config = config.LkTables is { } src ? config with { LkTables = CopyTables(src) } : config;
        Data = data;
        Streams = SliceStreams.Create(config);
        ScenarioData scenario = data.Scenario(config.ScenarioId);
        scenario = config.Start switch
        {
            StartVariant.Perturbed => scenario.Perturbed(i => PolicyRng.StartMultiplier(Streams.Start, i), data.Nodes, data.ProductClasses),
            StartVariant.Symmetric => scenario.Symmetrized(data.Nodes, data.ProductClasses),
            _ => scenario,
        };
        Scenario = scenario;

        KnowledgeParams kp = new();
        IndustryParams ip = new();
        ProductsParams pp = new();
        (kp, ip, pp) = ApplyChannels(config.Channels, kp, ip, pp);
        (kp, ip) = ApplyGrid(config.Grid, kp, ip);
        KnowledgeParameters = kp;
        IndustryParameters = ip;
        ProductsParameters = pp;
        pp = ApplySaleArm(config.SaleArm, pp);
        PolicyParams policy = new()
        {
            YProd = pp.YProd, TField = pp.HBuild, NServiceMin = pp.NServiceMin, FleetArm = config.FleetArm, FleetLineCond = config.FleetLineCond,
            LineClose = config.LineClose,
            ToolingTarget = config.ToolingTarget,
            LicOff = config.LicOff, T6dOff = config.T6dOff,
        };
        config.ToolingTarget.Validate();
        ScenarioData baseScenario = data.Scenario(config.ScenarioId);
        // fv_budget: 24 median procurement budgets of the great powers at t0 (spec §5.5); the actors that exist among USA, GBR, SUN.
        var great = new List<Fx6>();
        foreach (string id in (string[])["USA", "GBR", "SUN"])
        {
            if (baseScenario.ActorIndex(id) is int i and >= 0)
                great.Add(baseScenario.BudgetIndex[i]);
        }
        // fv_budget is a constant of the world fixed by the main shares: the EFFORT_SHARE arm changes the shares only.
        PolicyParameters = ApplyEffortShare(config.EffortShare, policy with { FvBudget = policy.FvBudgetOf(great.Count > 0 ? [.. great] : [.. baseScenario.BudgetIndex]) });

        Graph = KnowledgeGraph.FromData(data.Nodes, scenario.Disciplines, kp);
        KnowledgeStart kStart = KnowledgeStart.FromScenario(scenario, Graph);
        // The product layout fixes how many requester ids the capability layer must hold (lines, bureaus, fix teams).
        IndustryLayout probe = IndustryLayout.FromData(data.Nodes, scenario, WorkforceCategories, 64);
        int requesters = ProductsLayout.FromData(data.Nodes, data.ProductClasses, ProductClass, probe, Bureaus, scenario.StartYear, Years, scenario.MaxDesigns).Requesters;
        // One more id above every line, bureau and fix-team id: the quality programme, which the bureaus outrank in the rounding of grants (A41).
        QualityRequester = requesters;
        IndustryLayout = IndustryLayout.FromData(data.Nodes, scenario, WorkforceCategories, requesters + 1);
        IndustryStart iStart = IndustryStart.FromScenario(scenario, data.Nodes, IndustryLayout, ip);
        // S-2 (spec §1.1): a process the base scenario calls mastered must have yield(t0) >= 0.7; checked before START_PERT, which scales culture by design.
        if (!config.SkipStartValidation)
            StartYieldValidator.Validate(baseScenario, data.Nodes, IndustryLayout, ip);
        ProductsLayout = ProductsLayout.FromData(data.Nodes, data.ProductClasses, ProductClass, IndustryLayout, Bureaus, scenario.StartYear, Years, scenario.MaxDesigns);
        ProductsStart pStart = ProductsStart.FromScenario(scenario, data.Nodes, data.ProductClasses, ProductsLayout);

        Table = new Table(scenario.ActorCount);
        Calendar = new TickCalendar(new CivilDate(scenario.StartYear, scenario.StartMonth, 1));
        int chunks = TickRunner.ChunkCountFor(Table.RowCount, config.ChunkSize);

        Knowledge = NewKnowledge(kStart, kp, chunks);
        ICultureRule rule = config.Channels.HasFlag(SliceChannels.CultureR0) ? new V23CultureRule() : new CbCultureRule();
        IndustryInputs = new IndustryInputsAdapter(this);
        Industry = new IndustrySystem(Table, IndustryLayout, iStart, ip, IndustryInputs, chunks, rule,
            config.Layer == LayerControl.Lc0 ? IndustryMode.ConstLc0 : IndustryMode.Full);
        ProductsInputs = new ProductsInputsAdapter(this);
        Products = new ProductsSystem(Table, ProductsLayout, pStart, pp, ProductsInputs, Industry.CreateMailbox(chunks, Cadence.Monthly),
            startMonth: scenario.StartMonth);

        StartVectors(scenario, out var actorNumber, out var budget0, out var growth, out var forced);
        BudgetAtStart = budget0;
        // The liveness of the give-up rule (spec A40, L-C1…L-C3) is a property of the manifest, not of a seed: checked on the scenario before START_PERT.
        PolicyLiveness.Validate(pp, PolicyParameters, ProductsLayout, baseScenario.BudgetIndex.AsSpan(), baseScenario.GrowthPa.AsSpan());
        // L-C3a (spec A40, E1): the upkeep of the start fleet is below the procurement item at t0.
        var startUpkeep = new Fx6[scenario.ActorCount];
        for (int a = 0; a < startUpkeep.Length; a++)
        {
            for (int d = 0; d < ProductsLayout.Designs; d++)
            {
                if (Products.Stage(a, d) is DesignStage.Series or DesignStage.InService)
                    startUpkeep[a] += Products.Upkeep(a, d) * Products.InService(a, d);
            }
        }
        PolicyLiveness.ValidateStartUpkeep(PolicyParameters, startUpkeep, budget0.AsSpan());
        Policy = new PolicySystem(Table, PolicyParameters, actorNumber, budget0, growth, Streams.Ai, new PolicyInputsAdapter(this),
            Industry.CreateMailbox(chunks, Cadence.Monthly), QualityRequester, forced,
            products: config.ProductAi ? new PolicyProductsAdapter(this) : null, designs: ProductsLayout.Designs,
            generations: ProductsLayout.Generations, derive: config.Derive, startLines: StartLines(pStart),
            events: Streams.Events, eventInputs: new PolicyEventsAdapter(this), disruptionExposure: scenario.Exposure);
        Commands = new CommandState(Table, ProductsLayout.SlotCount, ProductsLayout.Designs);
        Limits = new CommandLimits(scenario.ActorCount, ProductsLayout.SlotCount, ProductsLayout.Designs,
            [.. IndustryLayout.Order.Select(r => IndustryLayout.Number[r]).OrderBy(n => n)]);
        RegisterRunColumns();
        // why_not reads the three layers' snapshots only (contract §6); ProcessDevelopment is off in the EffortZero ablation (E_full = 0 makes it inert).
        WhyNot = new WhyNotAnalyzer(Graph, kp, Knowledge, Industry, Products, pp, new WhyNotInputsAdapter(this),
            new LeverRegistry(Enum.GetValues<Lever>().Where(l => l != Lever.ProcessDevelopment || !config.Channels.HasFlag(SliceChannels.EffortZero))));

        Policy.Initialize();
        Runner = new TickRunner(Table, config.Seed, [Knowledge, Industry, Products, Policy], config.ChunkSize, config.Threads, Calendar);
    }

    // L_K and L_K0 replace the knowledge model by a schedule; the other layers read `known` as before.
    private KnowledgeSystem NewKnowledge(KnowledgeStart kStart, KnowledgeParams kp, int chunks)
    {
        var inputs = new KnowledgeInputsAdapter(this);
        switch (Config.Layer)
        {
            case LayerControl.Lk:
                return new KnowledgeSystem(Table, Graph, kStart, kp, inputs, Streams.Know, chunks, KnowledgeMode.ScheduleLk,
                    KnowledgeSchedule.BuildLk(Graph, kStart, Config.LkTables!, Streams.Know), Config.LicKnows);
            case LayerControl.Lk0:
                // The window middles of KnowledgeSchedule.Lk0WindowMid are counted from 1945-09: another start needs its own, which the scenario does not carry yet.
                if (Scenario.StartYear != 1945 || Scenario.StartMonth != 9)
                    throw new InvalidOperationException($"L_K0 is defined for a start in 1945-09, the scenario starts in {Scenario.StartYear}-{Scenario.StartMonth:00}.");
                return new KnowledgeSystem(Table, Graph, kStart, kp, inputs, Streams.Know, chunks, KnowledgeMode.ScheduleLk0,
                    KnowledgeSchedule.BuildLk0(Graph, kStart), Config.LicKnows);
            default:
                return new KnowledgeSystem(Table, Graph, kStart, kp, inputs, Streams.Know, chunks, licenceTransfersKnowledge: Config.LicKnows);
        }
    }

    // E1: the designs that start in stage С (one per actor at most).
    private static ImmutableArray<(int Row, int Design)> StartLines(ProductsStart start)
    {
        var lines = new List<(int, int)>();
        for (int a = 0; a < start.Actors; a++)
        {
            for (int d = 0; d < start.Layout.Designs; d++)
            {
                if (start.Stage[a * start.Layout.Designs + d] == DesignStage.Series)
                    lines.Add((a, d));
            }
        }
        return [.. lines];
    }

    public SliceRunConfig Config { get; }
    public GameData Data { get; }
    public ScenarioData Scenario { get; }
    public SliceStreams Streams { get; }
    public KnowledgeParams KnowledgeParameters { get; }
    public IndustryParams IndustryParameters { get; }
    public ProductsParams ProductsParameters { get; }
    public PolicyParams PolicyParameters { get; }
    public KnowledgeGraph Graph { get; }
    public IndustryLayout IndustryLayout { get; }
    public ProductsLayout ProductsLayout { get; }
    public Table Table { get; }
    public TickCalendar Calendar { get; }
    public KnowledgeSystem Knowledge { get; }
    public IndustryInputsAdapter IndustryInputs { get; }
    public ProductsInputsAdapter ProductsInputs { get; }
    public int QualityRequester { get; }
    public IndustrySystem Industry { get; }
    public ProductsSystem Products { get; }
    public PolicySystem Policy { get; }
    public CommandState Commands { get; }
    public WhyNotAnalyzer WhyNot { get; }
    public CommandLimits Limits { get; }
    public TickRunner Runner { get; }
    public ImmutableArray<Fx6> BudgetAtStart { get; }

    /// <summary>Shares in force: the player's <c>set_budget_shares</c> if one was applied, else the AI profile's.</summary>
    public BudgetShares SharesOf(int actor) => Commands.SharesOverride(actor) ?? Policy.Shares(actor);

    /// <summary>Procurement budget of the month in force: <c>share_P·budget_a</c> (spec §3.3, §3.4); one definition for the product layer and <c>why_not</c>.</summary>
    public Fx6 ProcurementBudget(int actor) => ProcurementBase(actor) + LicenceFlow(actor);

    /// <summary><c>ProcurementBase(a, t) = share_P·budget_a(t)</c>: the procurement item without licence flows and sale revenue (contract r6 §5).</summary>
    public Fx6 ProcurementBase(int actor) => SharesOf(actor).Procurement * Policy.Budget(actor);

    /// <summary>
    /// <c>LicenceFlow(a, t)</c>: buyer pays, seller receives the licence price (spec A41). Licences are the events of T6d, which does not exist
    /// yet, so no money moves and the flow is 0; the sum is kept so that <see cref="ProcurementBudget"/> is base + licence as the contract has it.
    /// </summary>
    public Fx6 LicenceFlow(int actor) => Policy.LicenceFlow(actor);

    /// <summary><c>g_a</c> (spec A43.2): the highest engine generation among the actor's designs in С or В; 0 without one.</summary>
    public int EngineGen(int actor)
    {
        int g = 0;
        for (int d = 0; d < ProductsLayout.Designs; d++)
        {
            if (Products.Stage(actor, d) is not (DesignStage.Series or DesignStage.InService))
                continue;
            g = Math.Max(g, ProductsLayout.AltEngGen[Products.Alternatives(actor, d)[ProductsLayout.BaseSlot]]);
        }
        return g;
    }

    /// <summary><c>LicenceActive(b)</c> (contract §5): <c>lic_gen &gt; 0</c> and the buyer has no series of an engine generation above the licence.</summary>
    public bool LicenceActive(int actor) => Policy.LicenceGen(actor) > 0 && EngineGen(actor) <= Policy.LicenceGen(actor);

    /// <summary>
    /// The channel switches (spec §6.2): each one sets exactly one parameter to its "off" value; the culture rule is chosen separately
    /// (<see cref="SliceChannels.CultureR0"/>).
    /// </summary>
    public static (KnowledgeParams K, IndustryParams I, ProductsParams P) ApplyChannels(SliceChannels c, KnowledgeParams k, IndustryParams i, ProductsParams p)
    {
        if (c.HasFlag(SliceChannels.Alpha))
            i = i with { Alpha = Fx6.Zero };
        if (c.HasFlag(SliceChannels.Ramp))
            i = i with { Lambda0 = Fx6.One };
        if (c.HasFlag(SliceChannels.B))
            p = p with { LearningQ10 = 0 };
        if (c.HasFlag(SliceChannels.Eta))
            k = k with { Eta = Fx6.Zero };
        if (c.HasFlag(SliceChannels.KCatch))
            i = i with { KCatch = Fx6.Zero };
        if (c.HasFlag(SliceChannels.Sigma))
            i = i with { Sigma = Fx6.Zero };
        if (c.HasFlag(SliceChannels.KDiff))
            k = k with { KDiff = Fx6.Zero };
        if (c.HasFlag(SliceChannels.EffortZero))
            i = i with { EFull = Fx6.Zero };
        if (c.HasFlag(SliceChannels.KTr))
            i = i with { KTr = Fx6.Zero };
        if (c.HasFlag(SliceChannels.KQp))
            i = i with { KQp = Fx6.Zero };
        return (k, i, p);
    }

    /// <summary>
    /// The P5 grid (spec §7, #190): <c>h_max</c> and <c>k_catch</c> are scaled by ×0,5 (truncating the raw value) / ×2 (checked);
    /// the <c>k_diff</c> axis scales the price share <c>r = 1 − k_diff</c>, so <c>k_diff</c> stays below 1 (c_eff of a node known elsewhere is never zero).
    /// </summary>
    public static (KnowledgeParams K, IndustryParams I) ApplyGrid(GridCell cell, KnowledgeParams k, IndustryParams i)
    {
        static Fx6 Step(GridStep step, Fx6 v) => step switch
        {
            GridStep.Lo => Fx6.FromRaw(v.Raw / 2),
            GridStep.Hi => Fx6.FromRaw(checked(v.Raw * 2)),
            _ => v,
        };
        Fx6 kDiff = Fx6.One - Step(cell.KDiff, Fx6.One - k.KDiff);
        k = k with { KDiff = kDiff, HMax = Step(cell.HMax, k.HMax) };
        return (k, i with { KCatch = Step(cell.KCatch, i.KCatch) });
    }

    /// <summary>
    /// <c>EFFORT_SHARE</c> (spec §6.8): <c>share_D′ = x·share_D</c>, the other shares scaled by <c>(1 − share_D′)/(1 − share_D)</c> (floor, <see cref="Int128"/>),
    /// the rounding residue goes to procurement. [Предл.] The spec gives no rounding rule. Applied to the profiles and to the quality programme of P2;
    /// the arm X1 returns <paramref name="p"/> itself. A set of shares that is not valid is an error of the arm, not repaired.
    /// </summary>
    public static PolicyParams ApplyEffortShare(EffortShareArm arm, PolicyParams p)
    {
        if (arm == EffortShareArm.X1)
            return p;
        static BudgetShares Scale(EffortShareArm arm, BudgetShares s)
        {
            long d = s.Effort.Raw;
            long d2 = arm switch
            {
                EffortShareArm.X0 => 0,
                EffortShareArm.X05 => d / 2,
                EffortShareArm.X2 => checked(d * 2),
                _ => checked(d * 4),
            };
            Fx6 Part(Fx6 x) => Fx6.FromRaw((long)((Int128)x.Raw * (Fx6.Scale - d2) / (Fx6.Scale - d)));
            Fx6 research = Part(s.Research), cap = Part(s.Capability), quality = Part(s.Quality);
            var scaled = new BudgetShares(research, cap, Fx6.FromRaw(d2), Fx6.FromRaw(Fx6.Scale - d2 - research.Raw - cap.Raw - quality.Raw), quality);
            return scaled.IsValid ? scaled : throw new InvalidOperationException($"EFFORT_SHARE {arm}: the shares {scaled} are not valid.");
        }
        return p with { SharesByProfile = [.. p.SharesByProfile.Select(s => Scale(arm, s))], MassProgrammeShares = Scale(arm, p.MassProgrammeShares) };
    }

    /// <summary>The sale terms of an arm (spec A42.8): exactly one number changes.</summary>
    public static ProductsParams ApplySaleArm(SaleArm arm, ProductsParams p) => arm switch
    {
        SaleArm.SigmaLo => p with { SaleSigma = Fx6.FromRaw(150_000) },
        SaleArm.PhiFlat => p with { SalePhi = [Fx6.One, Fx6.One, Fx6.One] },
        SaleArm.QuotaLo => p with { SaleQuota = Fx6.FromRaw(50_000) },
        SaleArm.QuotaHi => p with { SaleQuota = Fx6.FromRaw(200_000) },
        SaleArm.ChiOff => p with { SaleChiOff = true },
        _ => p,
    };

    /// <summary>Hash of the column schema: names and types in table order. A layout change without a <see cref="SchemaVersion"/> bump fails a test.</summary>
    public string SchemaFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (Column c in Table.Columns)
            hash.AppendData(Encoding.UTF8.GetBytes(c.Name + ":" + (c is IntColumn ? "i" : "l") + "\n"));
        hash.AppendData(BitConverter.GetBytes(Table.RowCount));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private void StartVectors(ScenarioData s, out ImmutableArray<int> actorNumber,
        out ImmutableArray<Fx6> budget0, out ImmutableArray<Fx6> growth,
        out ImmutableArray<int> forced)
    {
        actorNumber = [.. Enumerable.Range(0, s.ActorCount)];
        budget0 = s.BudgetIndex;
        growth = s.GrowthPa;
        int[] f = [.. Enumerable.Repeat(-1, s.ActorCount)];
        if (Config.ProfileCell is int cell)
        {
            var (usa, gbr, sun) = PolicyRules.ProfilesOfCell(cell);
            (int Index, PolicyProfile Profile)[] great = [(s.ActorIndex("USA"), usa), (s.ActorIndex("GBR"), gbr), (s.ActorIndex("SUN"), sun)];
            foreach ((int index, PolicyProfile profile) in great)
            {
                if (index < 0)
                    throw new InvalidOperationException("The factorial plan needs the actors USA, GBR and SUN.");
                f[index] = (int)profile;
            }
        }
        forced = [.. f];
    }

    // run.*: the configuration of the run as columns (every row holds the same value), so it is hashed and saved with the state (ADR-0013 п. 4).
    private void RegisterRunColumns()
    {
        int rows = Table.RowCount;
        static long[] Fill(int n, long v) => [.. Enumerable.Repeat(v, n)];
        // The effective seed of each stream, not the run seed: under FIX_* the fixed source has the same value for every seed, so FIX_ALL gives
        // one trajectory and one hash. The START stream is used only by START_PERT.
        Table.AddLong("run.seed.know").Load(Fill(rows, unchecked((long)Streams.Know.Seed)));
        Table.AddLong("run.seed.events").Load(Fill(rows, unchecked((long)Streams.Events.Seed)));
        Table.AddLong("run.seed.ai").Load(Fill(rows, unchecked((long)Streams.Ai.Seed)));
        Table.AddLong("run.seed.start").Load(Fill(rows, Config.Start == StartVariant.Perturbed ? unchecked((long)Streams.Start.Seed) : 0));
        Table.AddInt("run.fix").Load([.. Enumerable.Repeat((Config.FixK ? 1 : 0) | (Config.FixE ? 2 : 0) | (Config.FixA ? 4 : 0), rows)]);
        Table.AddInt("run.start").Load([.. Enumerable.Repeat((int)Config.Start, rows)]);
        // Only a run on the RHMS_OFF data set carries this column, so every other hash is that of a build without the switch.
        if (Config.SkipStartValidation)
            Table.AddInt("run.skip_s2").Load([.. Enumerable.Repeat(1, rows)]);
        Table.AddInt("run.cell").Load([.. Enumerable.Repeat(Config.ProfileCell ?? -1, rows)]);
        Table.AddInt("run.ai").Load([.. Enumerable.Repeat((Config.ProductAi ? 1 : 0) | (Config.Derive ? 2 : 0), rows)]);
        // T6D_OFF / LIC_OFF / lic_knows are part of the state, so a switch cannot give the hash of main.
        Table.AddInt("run.t6d").Load([.. Enumerable.Repeat((Config.LicOff ? 1 : 0) | (Config.T6dOff ? 2 : 0) | (Config.LicKnows ? 4 : 0), rows)]);
        Table.AddInt("run.channels").Load([.. Enumerable.Repeat((int)Config.Channels, rows)]);
        // Bit 12 = LINE_CLOSE YPROD (F17b) of a run with the product AI; it is clear for ADMIT and for a player-script run, so their hashes are those of the build before F17b.
        int lineCloseBit = Config.ProductAi && Config.LineClose == LineCloseRule.YProd ? 1 << 12 : 0;
        Table.AddInt("run.fleet").Load([.. Enumerable.Repeat((int)Config.FleetArm | ((int)Config.FleetLineCond << 4) | ((int)Config.SaleArm << 8) | lineCloseBit, rows)]);
        // TOOLING_TARGET and its numbers are part of the hash; V0 (the ablation arm) adds nothing, so its trajectory and hash are those of a build before V5 became the default.
        ToolingTargetSpec tt = Config.ToolingTarget;
        if (tt.Variant != ToolingTarget.V0)
        {
            Table.AddInt("run.tooling").Load([.. Enumerable.Repeat((int)tt.Variant, rows)]);
            Table.AddLong("run.tooling.rho_max").Load(Fill(rows, tt.RhoMax.Raw));
            Table.AddLong("run.tooling.h_lo").Load(Fill(rows, tt.HLo.Raw));
            Table.AddLong("run.tooling.h_hi").Load(Fill(rows, tt.HHi.Raw));
            Table.AddLong("run.tooling.m_lead").Load(Fill(rows, tt.MLead.Raw));
            Table.AddLong("run.tooling.h_hi_b").Load(Fill(rows, tt.HHiControl.Raw));
        }
        // POOL_ORDER and k_cap (Lab only) are part of the hash when they are not the defaults CAN and 1, so a default run carries neither column.
        if (tt.PoolOrder != PoolOrder.Can)
            Table.AddInt("run.tooling.pool").Load([.. Enumerable.Repeat((int)tt.PoolOrder, rows)]);
        if (tt.KCap != Fx6.One)
            Table.AddLong("run.tooling.k_cap").Load(Fill(rows, tt.KCap.Raw));
        // Layer, grid and effort-share are part of the hash when they are not main, so a default run carries none of these columns.
        if (Config.Layer != LayerControl.Full)
            Table.AddInt("run.layer").Load([.. Enumerable.Repeat((int)Config.Layer, rows)]);
        if (Config.LkTables is { } tables)
            Table.AddLong("run.layer.tables").Load(Fill(rows, TablesDigest(tables)));
        if (Config.Grid.Code != 0)
            Table.AddInt("run.grid").Load([.. Enumerable.Repeat(Config.Grid.Code, rows)]);
        if (Config.EffortShare != EffortShareArm.X1)
            Table.AddInt("run.effort_share").Load([.. Enumerable.Repeat((int)Config.EffortShare, rows)]);
        Table.AddLong("run.scenario").Load(Fill(rows, BinaryPrimitives.ReadInt64LittleEndian(Convert.FromHexString(Scenario.Hash.AsSpan(0, 16)))));
    }

    private static LkTables CopyTables(LkTables t)
    {
        var copy = new LkTables(t.ActorCount);
        t.PCens.CopyTo(copy.PCens, 0);
        t.Mean.CopyTo(copy.Mean, 0);
        t.HalfWidth.CopyTo(copy.HalfWidth, 0);
        t.Delta.CopyTo(copy.Delta, 0);
        return copy;
    }

    // First 8 bytes of SHA-256 over PCens.Raw, Mean, HalfWidth, Delta (little-endian, in this order).
    private static long TablesDigest(LkTables t)
    {
        var bytes = new byte[t.PCens.Length * 8 + (t.Mean.Length + t.HalfWidth.Length + t.Delta.Length) * 4];
        int pos = 0;
        foreach (Fx6 v in t.PCens)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(pos), v.Raw);
            pos += 8;
        }
        foreach (int[] array in new[] { t.Mean, t.HalfWidth, t.Delta })
        {
            foreach (int v in array)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(pos), v);
                pos += 4;
            }
        }
        return BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(bytes));
    }
}
