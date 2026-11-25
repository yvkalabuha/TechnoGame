using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core.Data;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Policy;

/// <summary>
/// Blocs and friends by year for the buyer circle of the licence market (O-056). A bloc is one value per country and year (a country is in at most one);
/// friends are an explicit symmetric list. Intervals are inclusive years; a bloc and a friendship are looked up, never enumerated into world state.
/// </summary>
public sealed class AllianceTable
{
    private readonly ImmutableArray<(string Actor, string Bloc, int From, int To)> _members;
    private readonly ImmutableArray<(string A, string B, int From, int To)> _friends;

    private AllianceTable(ImmutableArray<(string, string, int, int)> members, ImmutableArray<(string, string, int, int)> friends, string identity)
    {
        _members = members;
        _friends = friends;
        Identity = identity;
    }

    /// <summary>No blocs and no friends: the circles Bloc and Friends are empty.</summary>
    public static AllianceTable Empty { get; } = new([], [], "empty");

    /// <summary>Canonical text of the rows, for the fingerprint of a run.</summary>
    public string Identity { get; }

    /// <summary>The bloc of the actor in the year, or an empty string.</summary>
    public string BlocOf(string actor, int year)
    {
        foreach ((string a, string bloc, int from, int to) in _members)
        {
            if (a == actor && year >= from && year <= to)
                return bloc;
        }
        return "";
    }

    /// <summary>Whether the two actors share a bloc or are listed friends in the year (never with themselves).</summary>
    public bool InCircle(string seller, string buyer, int year, SaleCircle circle)
    {
        if (seller == buyer)
            return false;
        switch (circle)
        {
            case SaleCircle.All:
                return true;
            case SaleCircle.Nobody:
                return false;
        }
        string bloc = BlocOf(seller, year);
        if (bloc.Length > 0 && bloc == BlocOf(buyer, year))
            return true;
        return circle == SaleCircle.Friends && AreFriends(seller, buyer, year);
    }

    public bool AreFriends(string a, string b, int year)
    {
        foreach ((string x, string y, int from, int to) in _friends)
        {
            if (year >= from && year <= to && (x == a && y == b || x == b && y == a))
                return true;
        }
        return false;
    }

    public static AllianceTable Load(string path) => Parse(File.ReadAllText(path), path);

    public static AllianceTable Parse(string text, string file)
    {
        var root = new MapReader(StrictYaml.Parse(text, file));
        if (root.Int("schema") != 1)
            throw new DataLoadException($"{file}: unknown alliances schema");
        var members = ImmutableArray.CreateBuilder<(string, string, int, int)>();
        foreach (YNode item in root.Seq("blocs").Items)
        {
            var bloc = new MapReader(item);
            string id = bloc.Str("id");
            foreach (YNode m in bloc.Seq("members").Items)
            {
                var r = new MapReader(m);
                string actor = r.Str("actor");
                int from = r.Int("from");
                int to = r.Optional("to") is { } t ? Scalars.Int(t) : int.MaxValue;
                r.End();
                if (to < from)
                    throw DataLoadException.At(m, "the interval ends before it begins");
                foreach ((string a, string b, int f, int e) in members)
                {
                    if (a == actor && from <= e && f <= to)
                        throw DataLoadException.At(m, $"{actor} is in two blocs ({b}, {id}) in the same year");
                }
                members.Add((actor, id, from, to));
            }
            bloc.End();
        }
        var friends = ImmutableArray.CreateBuilder<(string, string, int, int)>();
        foreach (YNode item in root.Seq("friends").Items)
        {
            var r = new MapReader(item);
            string a = r.Str("a"), b = r.Str("b");
            int from = r.Int("from");
            int to = r.Optional("to") is { } t ? Scalars.Int(t) : int.MaxValue;
            r.End();
            if (a == b || to < from)
                throw DataLoadException.At(item, "a friendship needs two different actors and a non-empty interval");
            friends.Add((a, b, from, to));
        }
        root.End();
        string identity = string.Join(';', members.Select(m => $"{m.Item1}/{m.Item2}/{m.Item3}/{m.Item4}")) + "|"
            + string.Join(';', friends.Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Item1}/{f.Item2}/{f.Item3}/{f.Item4}")));
        return new AllianceTable(members.ToImmutable(), friends.ToImmutable(), identity);
    }
}

/// <summary>Who may buy a seller's licence (O-056): the seller's own bloc, bloc plus friends, or anyone. <see cref="Default"/> acts as <see cref="Friends"/>.</summary>
public enum SaleCircle
{
    Default = 0,
    Nobody = 1,
    Bloc = 2,
    Friends = 3,
    All = 4,
}
