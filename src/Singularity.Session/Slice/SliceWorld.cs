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
/// <see cref="SliceSession"/>. The Events system (T6d) takes its place between Products and Policy when it exists.
/// </summary>
internal sealed class SliceWorld
{
    /// <summary>Version of the column schema (ADR-0013 п. 2); raised with every change of the table layout (pinned by a fingerprint test).</summary>
    public const int SchemaVersion = 1;

    // Layout constants of the slice (spec §1.4, §3.3, §2): engineer and skilled pools, bureaus, cohort years 1945…1977.
    public const int WorkforceCategories = 2;
    public const int Bureaus = 2;
    public const int Years = 33;
    public const string ProductClass = "jet_fighter";

    public SliceWorld(GameData data, SliceRunConfig config)
    {
        Config = config;
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
        KnowledgeParameters = kp;
        IndustryParameters = ip;
        ProductsParameters = pp;
        PolicyParameters = new PolicyParams();

        Graph = KnowledgeGraph.FromData(data.Nodes, scenario.Disciplines, kp);
        KnowledgeStart kStart = KnowledgeStart.FromScenario(scenario, Graph);
        // The product layout fixes how many requester ids the capability layer must hold (lines, bureaus, fix teams).
        IndustryLayout probe = IndustryLayout.FromData(data.Nodes, scenario, WorkforceCategories, 64);
        int requesters = ProductsLayout.FromData(data.Nodes, data.ProductClasses, ProductClass, probe, Bureaus, scenario.StartYear, Years, scenario.MaxDesigns).Requesters;
        IndustryLayout = IndustryLayout.FromData(data.Nodes, scenario, WorkforceCategories, requesters);
        IndustryStart iStart = IndustryStart.FromScenario(scenario, data.Nodes, IndustryLayout, ip);
        ProductsLayout = ProductsLayout.FromData(data.Nodes, data.ProductClasses, ProductClass, IndustryLayout, Bureaus, scenario.StartYear, Years, scenario.MaxDesigns);
        ProductsStart pStart = ProductsStart.FromScenario(scenario, data.Nodes, data.ProductClasses, ProductsLayout);

        Table = new Table(scenario.ActorCount);
        Calendar = new TickCalendar(new CivilDate(scenario.StartYear, scenario.StartMonth, 1));
        int chunks = TickRunner.ChunkCountFor(Table.RowCount, config.ChunkSize);

        Knowledge = new KnowledgeSystem(Table, Graph, kStart, kp, new KnowledgeInputsAdapter(this), Streams.Know, chunks);
        ICultureRule rule = config.Channels.HasFlag(SliceChannels.CultureR0) ? new V23CultureRule() : new CbCultureRule();
        Industry = new IndustrySystem(Table, IndustryLayout, iStart, ip, new IndustryInputsAdapter(this), chunks, rule);
        Products = new ProductsSystem(Table, ProductsLayout, pStart, pp, new ProductsInputsAdapter(this), Industry.CreateMailbox(chunks, Cadence.Monthly));

        StartVectors(scenario, out var actorNumber, out var budget0, out var growth, out var forced);
        BudgetAtStart = budget0;
        Policy = new PolicySystem(Table, PolicyParameters, actorNumber, budget0, growth, Streams.Ai, forced);
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
    public Fx6 ProcurementBudget(int actor) => SharesOf(actor).Procurement * Policy.Budget(actor);

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
        Table.AddInt("run.cell").Load([.. Enumerable.Repeat(Config.ProfileCell ?? -1, rows)]);
        Table.AddInt("run.channels").Load([.. Enumerable.Repeat((int)Config.Channels, rows)]);
        Table.AddLong("run.scenario").Load(Fill(rows, BinaryPrimitives.ReadInt64LittleEndian(Convert.FromHexString(Scenario.Hash.AsSpan(0, 16)))));
    }
}
