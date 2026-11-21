using Singularity.Core.Commands;
using Singularity.Kernel;

namespace Singularity.Session.Tests;

/// <summary>
/// TASK-0124 (review A4): save → load → continue gives the same state hash at every month as the run that never stopped.
/// Anything that lives outside the table and decides the next tick (a cache, a queue, a counter in a field) shows up here as a hash difference.
/// </summary>
public class SnapshotContinuityTests
{
    private const long Months = 40;

    private static SliceRunConfig Cfg(ulong seed, int threads) => new() { Seed = seed, Threads = threads, ChunkSize = 1 };

    private static byte[] Save(SliceSession s)
    {
        using var m = new MemoryStream();
        s.WriteSnapshot(m);
        return m.ToArray();
    }

    private static SliceSession Load(SliceRunConfig cfg, byte[] snapshot)
    {
        SliceSession s = SliceTestData.Create(cfg);
        s.LoadSnapshot(new MemoryStream(snapshot));
        return s;
    }

    private static string[] Reference(ulong seed, int threads)
    {
        SliceSession s = SliceTestData.Create(Cfg(seed, threads));
        var hashes = new string[Months];
        for (long m = 0; m < Months; m++)
        {
            s.StepThroughMonth(m);
            hashes[m] = s.LastHash;
        }
        return hashes;
    }

    /// <summary>Runs to the end of month k, saves, loads into a new session and returns the hashes of months k+1… of the continued run.</summary>
    private static string[] Continued(ulong seed, int threads, long k, Action<SliceSession>? tamper = null)
    {
        SliceSession first = SliceTestData.Create(Cfg(seed, threads));
        first.StepThroughMonth(k);
        SliceSession s = Load(Cfg(seed, threads), Save(first));
        tamper?.Invoke(s);
        var hashes = new List<string>();
        for (long m = k + 1; m < Months; m++)
        {
            s.StepThroughMonth(m);
            hashes.Add(s.LastHash);
        }
        return [.. hashes];
    }

    [Theory]
    [InlineData(1UL, 5L)]
    [InlineData(1UL, 23L)]
    [InlineData(2UL, 5L)]
    [InlineData(2UL, 23L)]
    [InlineData(3UL, 5L)]
    [InlineData(3UL, 23L)]
    public void Continued_EqualsUninterrupted_L1_AndL2(ulong seed, long k)
    {
        string[] reference = Reference(seed, 1);
        string[] tail = reference[(int)(k + 1)..];
        foreach (int threads in new[] { 1, 2, 4 })
        {
            string[] got = Continued(seed, threads, k);
            Assert.Equal(tail.Length, got.Length);
            for (int i = 0; i < tail.Length; i++)
                HashAssert.Equal(tail[i], got[i], $"seed {seed}, K {k}, threads {threads}, month {k + 1 + i}");
        }
    }

    [Fact]
    public void LoadedSession_ReportsTheSavedStateAndClock()
    {
        SliceSession a = SliceTestData.Create(Cfg(1, 1));
        a.StepThroughMonth(11);
        SliceSession b = Load(Cfg(1, 1), Save(a));
        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.Date, b.Date);
        HashAssert.Equal(a.LastHash, b.LastHash, "hash right after the load");
        Assert.Equal(a.World.SchemaFingerprint(), b.World.SchemaFingerprint());
    }

    // Mutation: a column is not saved (the writer gives the values of a fresh world for it). The continuity check must see it.
    [Theory]
    [InlineData("pol.budget")]
    [InlineData("know.prog.")]
    public void Continuity_Fails_WhenOneColumnIsNotSaved(string prefix)
    {
        const ulong seed = 1;
        const long k = 12;
        string[] tail = Reference(seed, 1)[(int)(k + 1)..];

        SliceSession first = SliceTestData.Create(Cfg(seed, 1));
        first.StepThroughMonth(k);
        SliceSession fresh = SliceTestData.Create(Cfg(seed, 1));
        // The omitted column must matter: pick one whose saved values differ from a fresh world's.
        string name = first.World.Table.Columns.OfType<LongColumn>()
            .Where(c => c.Name.StartsWith(prefix, StringComparison.Ordinal))
            .First(c => !c.Current.SequenceEqual(fresh.World.Table.GetLong(c.Name).Current)).Name;

        first.OmitColumnInSnapshot = n => n == name;
        SliceSession s = Load(Cfg(seed, 1), Save(first));
        var got = new List<string>();
        for (long m = k + 1; m < Months; m++)
        {
            s.StepThroughMonth(m);
            got.Add(s.LastHash);
        }
        Assert.NotEqual(tail, got.ToArray());
    }

    [Fact]
    public void Load_IsRefused_WhenTheDestinationHasAPendingCommandAtTickZero()
    {
        var cfg = new SliceRunConfig { Seed = 7, ProductAi = false };
        SliceSession source = SliceTestData.Create(cfg);
        source.StepThroughMonth(1);
        byte[] snap = Save(source);

        SliceSession dest = SliceTestData.Create(cfg);
        var player = new PlayerScript(dest);
        player.Issue(0);
        Assert.True(player.Accepted > 0);
        Assert.Equal(0, dest.Tick);
        Assert.Throws<InvalidOperationException>(() => dest.LoadSnapshot(new MemoryStream(snap)));
    }

    [Fact]
    public void Journal_IsRestored_AndLocalNStaysMonotonic()
    {
        SliceSession a = SliceTestData.Create(new SliceRunConfig { Seed = 7, ProductAi = false });
        var player = new PlayerScript(a);
        for (long m = 0; m < 24; m++)
        {
            player.Issue(m);
            a.StepThroughMonth(m);
        }
        Assert.True(player.Accepted > 0, "the script must have issued commands");
        SliceSession b = Load(new SliceRunConfig { Seed = 7, ProductAi = false }, Save(a));
        Assert.Equal(a.CommandLog, b.CommandLog);
        HashAssert.Equal(a.LastHash, b.LastHash, "hash after the load");
        for (long m = 24; m < 30; m++)
        {
            a.StepThroughMonth(m);
            b.StepThroughMonth(m);
            HashAssert.Equal(a.LastHash, b.LastHash, $"month {m}");
        }
        // The first logged command of source/id: its local_n is not above the last one, so a replayed number is refused after the load too.
        LoggedCommand last = CommandCodec.DecodeLog(b.CommandLog).Last();
        CommandVerdict v = b.Submit(last.Key.Source, last.Key.SourceId, last.Key.LocalN, last.Command);
        Assert.False(v.Accepted);
    }


    // ---- pending queue (ADR-0013 p. 5) ----

    private static readonly SliceRunConfig PlayerCfg = new() { Seed = 7, ProductAi = false };

    /// <summary>
    /// A session after a few months of play, stopped right after the script issued commands of the month <paramref name="month"/>: they are
    /// accepted and wait for the next tick, so the snapshot is taken with a non-empty queue and a non-empty journal.
    /// </summary>
    private static SliceSession WithPending(out long month)
    {
        SliceSession a = SliceTestData.Create(PlayerCfg);
        var player = new PlayerScript(a);
        for (long m = 0; m < 80; m++)
        {
            int before = player.Accepted;
            player.Issue(m);
            if (m >= 6 && player.Accepted > before)
            {
                month = m;
                return a;
            }
            a.StepThroughMonth(m);
        }
        throw new InvalidOperationException("the script never issued a command after month 6");
    }

    [Fact]
    public void PendingQueue_IsSavedAndRestored_AndContinuesLikeTheOriginal()
    {
        SliceSession a = WithPending(out long month);
        SliceSession b = Load(PlayerCfg, Save(a));
        Assert.Equal(a.CommandLog, b.CommandLog);
        for (long m = month; m < month + 8; m++)
        {
            a.StepThroughMonth(m);
            b.StepThroughMonth(m);
            HashAssert.Equal(a.LastHash, b.LastHash, $"month {m}");
        }
        Assert.Equal(a.CommandLog, b.CommandLog);
    }

    [Fact]
    public void PendingQueue_NotSaved_IsSeenByTheContinuityCheck()
    {
        SliceSession a = WithPending(out long month);
        byte[] snap = Save(a);
        SliceSession reference = Load(PlayerCfg, snap);
        // The same state, but the queue is dropped from the saved bytes.
        SliceSession dropped = Load(PlayerCfg, Rebuild(a, snap, journal: s => s, pending: _ => CommandCodec.EncodeLog([])));
        var same = new List<string>();
        var other = new List<string>();
        for (long m = month; m < month + 6; m++)
        {
            reference.StepThroughMonth(m);
            dropped.StepThroughMonth(m);
            same.Add(reference.LastHash);
            other.Add(dropped.LastHash);
        }
        Assert.NotEqual(same, other);
    }

    // ---- the config that is not in the table ----

    [Theory]
    [InlineData("ProductAi")]
    [InlineData("BudgetRule")]
    [InlineData("LineClose")]
    [InlineData("Derive")]
    [InlineData("ToolingTarget")]
    [InlineData("CostDivisor")]
    [InlineData("ScenarioId")]
    public void Load_IsRefused_WhenTheConfigDiffers_AndSessionIsUnchanged(string field)
    {
        SliceRunConfig baseCfg = Cfg(1, 1);
        SliceRunConfig other = field switch
        {
            "ProductAi" => baseCfg with { ProductAi = false },
            "BudgetRule" => baseCfg with { BudgetRule = Singularity.Core.Policy.BudgetRule.A },
            "LineClose" => baseCfg with { LineClose = Singularity.Core.Policy.LineCloseRule.Admit },
            "Derive" => baseCfg with { Derive = true },
            "ToolingTarget" => baseCfg with { ToolingTarget = new Singularity.Core.Policy.ToolingTargetSpec { MLead = Fx6.FromRaw(300_000) } },
            "CostDivisor" => baseCfg with { CostDivisor = Fx6.FromRaw(2_000_000) },
            _ => baseCfg with { ScenarioId = "other" },
        };
        SliceSession src = SliceTestData.Create(baseCfg);
        src.StepThroughMonth(3);
        byte[] snap = Save(src);
        if (field == "ScenarioId")
        {
            // A scenario that does not exist cannot even create a session: the fingerprint is compared on the config of an existing one.
            Assert.NotEqual(baseCfg.SnapshotFingerprint(), other.SnapshotFingerprint());
            return;
        }
        SliceSession dest = SliceTestData.Create(other);
        Untouched untouched = Untouched.Of(dest);
        Assert.Throws<InvalidDataException>(() => dest.LoadSnapshot(new MemoryStream(snap)));
        untouched.Check(dest, other);
    }

    [Fact]
    public void Load_IsAllowed_AtAnotherThreadCountAndChunkSize()
    {
        SliceSession a = SliceTestData.Create(Cfg(1, 1));
        a.StepThroughMonth(5);
        SliceSession b = Load(new SliceRunConfig { Seed = 1, Threads = 4, ChunkSize = 2 }, Save(a));
        a.StepThroughMonth(6);
        b.StepThroughMonth(6);
        HashAssert.Equal(a.LastHash, b.LastHash, "month 6");
    }

    // ---- every failure path leaves the session unchanged ----

    private const int PrefixBytes = 4 + 4 + 32;
    private static int TableBytes(SliceSession s) => TableSnapshot.ExpectedBytes(s.World.Table);

    /// <summary>Hash, clock, journal and view of a session before a refused load, and a check that they are all still there.</summary>
    private readonly record struct Untouched(string Hash, long Tick, string Log, long ViewTick)
    {
        public static Untouched Of(SliceSession s) => new(s.LastHash, s.Tick, s.CommandLog, s.View.Tick);

        public void Check(SliceSession s, SliceRunConfig cfg)
        {
            HashAssert.Equal(Hash, s.LastHash, "hash after a refused load");
            Assert.Equal(Tick, s.Tick);
            Assert.Equal(Log, s.CommandLog);
            Assert.Equal(ViewTick, s.View.Tick);
            // And it still runs like a session that was never loaded.
            SliceSession fresh = SliceTestData.Create(cfg);
            for (long m = 0; m < 3; m++)
            {
                s.StepThroughMonth(m);
                fresh.StepThroughMonth(m);
                HashAssert.Equal(fresh.LastHash, s.LastHash, $"month {m} after a refused load");
            }
        }
    }

    private static void AssertRefused(byte[] snap, SliceRunConfig cfg)
    {
        SliceSession dest = SliceTestData.Create(cfg);
        Untouched untouched = Untouched.Of(dest);
        Assert.ThrowsAny<Exception>(() => dest.LoadSnapshot(new MemoryStream(snap)));
        untouched.Check(dest, cfg);
    }

    private static byte[] Patch(byte[] snap, int offset, byte[] value)
    {
        byte[] copy = (byte[])snap.Clone();
        value.CopyTo(copy, offset);
        return copy;
    }

    /// <summary>The snapshot of <paramref name="source"/> with its journal and queue texts replaced.</summary>
    private static byte[] Rebuild(SliceSession source, byte[] snap, Func<string, string> journal, Func<string, string> pending)
    {
        int at = PrefixBytes + 4 + TableBytes(source);
        using var m = new MemoryStream();
        m.Write(snap, 0, at);
        var enc = System.Text.Encoding.UTF8;
        string[] logs = new string[2];
        int p = at;
        for (int i = 0; i < 2; i++)
        {
            int len = BitConverter.ToInt32(snap, p);
            logs[i] = enc.GetString(snap, p + 4, len);
            p += 4 + len;
        }
        foreach (string text in new[] { journal(logs[0]), pending(logs[1]) })
        {
            byte[] b = enc.GetBytes(text);
            m.Write(BitConverter.GetBytes(b.Length));
            m.Write(b);
        }
        return m.ToArray();
    }

    [Fact]
    public void Load_RefusesEveryMalformedSnapshot_AndLeavesTheSessionUnchanged()
    {
        SliceSession src = WithPending(out _);
        byte[] snap = Save(src);
        int tableAt = PrefixBytes + 4;
        int tableLen = TableBytes(src);
        int journalLenAt = tableAt + tableLen;
        // header of the table: 4 magic, 4 format, 4 schema, 4 rows, 4 columns, 8 tick
        var cases = new Dictionary<string, byte[]>
        {
            ["bad session magic"] = Patch(snap, 0, [(byte)'X']),
            ["bad session format"] = Patch(snap, 4, BitConverter.GetBytes(9)),
            ["truncated"] = snap[..^1],
            ["trailing byte"] = [.. snap, 0],
            ["empty"] = [],
            ["table length negative"] = Patch(snap, PrefixBytes, BitConverter.GetBytes(-1)),
            ["table length huge"] = Patch(snap, PrefixBytes, BitConverter.GetBytes(int.MaxValue)),
            ["table length off by one"] = Patch(snap, PrefixBytes, BitConverter.GetBytes(tableLen + 1)),
            ["journal length negative"] = Patch(snap, journalLenAt, BitConverter.GetBytes(-1)),
            ["journal length huge"] = Patch(snap, journalLenAt, BitConverter.GetBytes(int.MaxValue)),
            ["journal length over the bound"] = Patch(snap, journalLenAt, BitConverter.GetBytes(SliceSession.MaxSnapshotLogBytes + 1)),
            ["table schema version"] = Patch(snap, tableAt + 8, BitConverter.GetBytes(SliceWorld.SchemaVersion + 1)),
            ["table fingerprint"] = Patch(snap, tableAt + 28, [(byte)(snap[tableAt + 28] ^ 1)]),
            ["table negative tick"] = Patch(snap, tableAt + 20, BitConverter.GetBytes(-1L)),
            ["tick beyond the calendar"] = Patch(snap, tableAt + 20, BitConverter.GetBytes(SliceSession.MaxSnapshotTick + 1)),
            ["tick of long.MaxValue"] = Patch(snap, tableAt + 20, BitConverter.GetBytes(long.MaxValue)),
            ["journal is not a log"] = Rebuild(src, snap, _ => "this is not json\n", p => p),
            ["queue is not a log"] = Rebuild(src, snap, j => j, _ => "{\n"),
            ["journal command at the snapshot tick"] = Rebuild(src, snap, _ => ReadLog(snap, src, 1), p => p),
            ["queue command of an earlier tick"] = Rebuild(src, snap, j => j, _ => ReadLog(snap, src, 0)),
            ["command of an actor that does not exist"] = Rebuild(src, snap, j => j, p => CommandCodec.EncodeLog(CommandCodec.DecodeLog(p).Select(c => c with { Command = c.Command with { Actor = 999 } }))),
        };
        foreach ((string name, byte[] bytes) in cases)
        {
            try
            {
                AssertRefused(bytes, PlayerCfg);
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                throw new InvalidOperationException($"case '{name}': {e.Message}", e);
            }
            catch (Xunit.Sdk.XunitException e)
            {
                throw new Xunit.Sdk.XunitException($"case '{name}': {e.Message}");
            }
        }
    }

    private static string ReadLog(byte[] snap, SliceSession source, int which)
    {
        int p = PrefixBytes + 4 + TableBytes(source);
        for (int i = 0; ; i++)
        {
            int len = BitConverter.ToInt32(snap, p);
            if (i == which)
                return System.Text.Encoding.UTF8.GetString(snap, p + 4, len);
            p += 4 + len;
        }
    }

    private static string Encode(IEnumerable<LoggedCommand> commands) => CommandCodec.EncodeLog(commands);

    // The records are re-encoded by the codec, so the log itself is well formed and CheckKeys is what refuses it.
    [Fact]
    public void Load_RefusesKeysOutOfOrder_AndLocalNumbersThatDoNotGrow_WithTheirOwnMessage()
    {
        SliceSession src = WithPending(out _);
        byte[] snap = Save(src);
        List<LoggedCommand> journal = CommandCodec.DecodeLog(ReadLog(snap, src, 0));
        List<LoggedCommand> pending = CommandCodec.DecodeLog(ReadLog(snap, src, 1));
        Assert.True(journal.Count >= 2 && pending.Count >= 1);

        (string Name, byte[] Bytes, string Message)[] cases =
        [
            ("journal reversed", Rebuild(src, snap, _ => Encode(Enumerable.Reverse(journal)), p => p), "not strictly ascending"),
            ("queue duplicated", Rebuild(src, snap, j => j, _ => Encode(pending.Concat(pending))), "not strictly ascending"),
            // The first queued command gets local_n 0 at a source that already issued numbers: its key still sorts after the journal.
            ("local_n does not grow", Rebuild(src, snap, j => j, _ => Encode(pending.Select((c, i) => i == 0 ? c with { Key = c.Key with { LocalN = 0 } } : c))), "local_n"),
        ];
        foreach ((string name, byte[] bytes, string message) in cases)
        {
            SliceSession dest = SliceTestData.Create(PlayerCfg);
            Untouched untouched = Untouched.Of(dest);
            var e = Assert.Throws<InvalidDataException>(() => dest.LoadSnapshot(new MemoryStream(bytes)));
            Assert.True(e.Message.Contains(message, StringComparison.Ordinal), $"case '{name}': {e.Message}");
            untouched.Check(dest, PlayerCfg);
        }
    }

    [Fact]
    public void RestoredLocalIndex_IsTheMaximumOfJournalAndQueue()
    {
        SliceSession src = WithPending(out _);
        SliceSession b = Load(PlayerCfg, Save(src));
        List<LoggedCommand> all = CommandCodec.DecodeLog(b.CommandLog);
        foreach (IGrouping<(SourceClass, int), LoggedCommand> g in all.GroupBy(c => (c.Key.Source, c.Key.SourceId)))
        {
            LoggedCommand top = g.OrderBy(c => c.Key.LocalN).Last();
            CommandVerdict at = b.Submit(top.Key.Source, top.Key.SourceId, top.Key.LocalN, top.Command);
            Assert.False(at.Accepted, "the highest local_n of a source must not be accepted again");
            CommandVerdict above = b.Submit(top.Key.Source, top.Key.SourceId, top.Key.LocalN + 1, top.Command);
            Assert.NotEqual(CommandRefusal.LocalNotMonotonic, above.Reason);
        }
    }

    // Same scenario id, one number of the scenario edited: the table is the same shape, the data is not.
    [Fact]
    public void Load_IsRefused_WhenTheDataDiffers_AndSessionIsUnchanged()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sing-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string f in Directory.GetFiles(Path.Combine(SliceTestData.RepoRoot, "mods", "core")))
            {
                string text = File.ReadAllText(f);
                if (Path.GetFileName(f) == "scenario_1945.yaml")
                {
                    Assert.Contains("growth_pa: 0.0299", text);
                    text = text.Replace("growth_pa: 0.0299", "growth_pa: 0.0300");
                }
                File.WriteAllText(Path.Combine(dir, Path.GetFileName(f)), text);
            }
            Singularity.Core.Data.GameData edited = Singularity.Core.Data.ModLoader.Load(dir);
            Assert.NotEqual(SliceTestData.Data.DataHash, edited.DataHash);

            SliceSession src = SliceTestData.Create(PlayerCfg);
            src.StepThroughMonth(3);
            byte[] snap = Save(src);
            SliceSession dest = SliceSession.Create(edited, PlayerCfg);
            string hash = dest.LastHash;
            Assert.Throws<InvalidDataException>(() => dest.LoadSnapshot(new MemoryStream(snap)));
            HashAssert.Equal(hash, dest.LastHash, "hash after a refused load");
            Assert.Equal(0, dest.Tick);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // The failure only a rollback can undo: the table passes every header check and is replaced before the seed is compared.
    [Fact]
    public void Load_OfAnotherSeedInTheTable_IsRolledBack_AfterTheTableWasReplaced()
    {
        SliceSession src = SliceTestData.Create(PlayerCfg);
        src.StepThroughMonth(6);
        byte[] snap = Save(src);
        Table t = src.World.Table;
        int offset = PrefixBytes + 4 + 60;
        for (int i = 0; t.Columns[i].Name != "run.seed.events"; i++)
            offset += t.Columns[i].Length * (t.Columns[i] is LongColumn ? sizeof(long) : sizeof(int));
        snap = Patch(snap, offset, [(byte)(snap[offset] ^ 1)]);

        SliceSession dest = SliceTestData.Create(PlayerCfg);
        Untouched untouched = Untouched.Of(dest);
        var e = Assert.Throws<InvalidDataException>(() => dest.LoadSnapshot(new MemoryStream(snap)));
        Assert.Contains("run.seed.events", e.Message);
        untouched.Check(dest, PlayerCfg);
    }

    [Fact]
    public void Load_IntoASessionThatHasRun_OrHasAJournal_OrAQueue_IsRefused()
    {
        SliceSession src = SliceTestData.Create(PlayerCfg);
        src.StepThroughMonth(2);
        byte[] snap = Save(src);

        SliceSession ran = SliceTestData.Create(PlayerCfg);
        ran.StepThroughMonth(0);
        string hash = ran.LastHash;
        Assert.Throws<InvalidOperationException>(() => ran.LoadSnapshot(new MemoryStream(snap)));
        HashAssert.Equal(hash, ran.LastHash, "a session that has run");

        SliceSession queued = SliceTestData.Create(PlayerCfg);
        var player = new PlayerScript(queued);
        player.Issue(0);
        Assert.True(player.Accepted > 0);
        string log = queued.CommandLog;
        Assert.Throws<InvalidOperationException>(() => queued.LoadSnapshot(new MemoryStream(snap)));
        Assert.Equal(log, queued.CommandLog);
        Assert.Equal(0, queued.Tick);
    }
}
