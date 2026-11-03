using System.Collections.Immutable;
using System.Globalization;
using Singularity.Core.Data.Yaml;

namespace Singularity.Core.Data;

/// <summary>Semantic version MAJOR.MINOR.PATCH (no pre-release part in v0).</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    public int CompareTo(SemVer other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    internal static SemVer Parse(YNode node)
    {
        string text = Scalars.Str(node);
        string[] parts = text.Split('.');
        if (parts.Length != 3)
            throw DataLoadException.At(node, $"version '{text}' is not MAJOR.MINOR.PATCH");
        int[] numbers = new int[3];
        for (int i = 0; i < 3; i++)
        {
            string p = parts[i];
            bool ok = p.Length > 0 && (p == "0" || p[0] != '0') && p.All(char.IsAsciiDigit) &&
                      int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]);
            if (!ok)
                throw DataLoadException.At(node, $"version '{text}' is not MAJOR.MINOR.PATCH");
        }
        return new SemVer(numbers[0], numbers[1], numbers[2]);
    }
}

/// <summary>Dependency on another mod: version in [Min, Below); Below is optional.</summary>
public sealed record ModDependency(string Id, SemVer Min, SemVer? Below)
{
    public bool Accepts(SemVer version) => version >= Min && (Below is not {} below || version < below);
}

/// <summary>Parsed <c>mod.yaml</c> (ADR-0010; schema <c>mods/schemas/mod.schema.json</c>).</summary>
public sealed record ModManifest(
    string Id, SemVer Version, string Name, string Directory,
    ImmutableArray<ModDependency> Dependencies, ImmutableArray<string> Files)
{
    public const string FileName = "mod.yaml";

    public static ModManifest Read(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            throw new DataLoadException($"{path}: mod manifest not found");
        var map = new MapReader(StrictYaml.Parse(File.ReadAllText(path), path));

        string id = map.Str("id");
        if (!IsLocalId(id))
            throw DataLoadException.At(map.Node, $"mod id '{id}' must match [a-z][a-z0-9_]*");
        SemVer version = SemVer.Parse(map.Required("version"));
        string name = map.Optional("name") is {} n ? Scalars.Str(n) : id;

        var deps = ImmutableArray.CreateBuilder<ModDependency>();
        foreach (YNode item in map.Seq("dependencies").Items)
        {
            var dep = new MapReader(item);
            string depId = dep.Str("id");
            SemVer min = SemVer.Parse(dep.Required("min"));
            SemVer? below = dep.Optional("below") is {} b ? SemVer.Parse(b) : null;
            dep.End();
            if (depId == id)
                throw DataLoadException.At(item, "a mod cannot depend on itself");
            foreach (ModDependency existing in deps)
            {
                if (existing.Id == depId)
                    throw DataLoadException.At(item, $"duplicate dependency '{depId}'");
            }
            deps.Add(new ModDependency(depId, min, below));
        }

        var files = ImmutableArray.CreateBuilder<string>();
        foreach (YNode item in map.Seq("files").Items)
        {
            string file = Scalars.Str(item);
            if (file.Contains('/') || file.Contains('\\') || !file.EndsWith(".yaml", StringComparison.Ordinal) || file == FileName)
                throw DataLoadException.At(item, $"data file '{file}' must be a plain *.yaml name in the mod directory");
            if (files.Contains(file))
                throw DataLoadException.At(item, $"file '{file}' is listed twice");
            files.Add(file);
        }
        if (files.Count == 0)
            throw DataLoadException.At(map.Node, "a mod must list at least one data file");
        map.End();
        return new ModManifest(id, version, name, directory, deps.ToImmutable(), files.ToImmutable());
    }

    internal static bool IsLocalId(string s) =>
        s.Length > 0 && char.IsAsciiLetterLower(s[0]) && s.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
}
