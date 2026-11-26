using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Data;

/// <summary>
/// The <c>research_speed</c> effect of a node (SCIENCE_SLOTS §3.2, §6): a share added to the research rate R of the listed
/// directions once the node is known. The slot model reads it (`KnowledgeSystem.ResearchFactor`); it is part of the data hash.
/// </summary>
public static class ResearchSpeedEffects
{
    /// <summary>Upper bound of one node's bonus (SCIENCE_SLOTS §6).</summary>
    public static readonly Fx6 MaxBonus = Fx6.FromRaw(Fx6.Scale * 8 / 100);

    /// <summary>All 13 directions as a bit mask.</summary>
    public static int AllMask => (1 << Directions.Ids.Length) - 1;

    /// <summary>
    /// Node field <c>effects</c>: a list of <c>{kind: research_speed, dirs: [ids] | all, bonus: x}</c>. Returns (direction mask, bonus)
    /// in file order; at most one effect per direction mask is not enforced, the sum per direction is a design check.
    /// </summary>
    internal static (int Mask, Fx6 Bonus)[] Read(YNode? node, string owner)
    {
        if (node is null)
            return [];
        var result = new List<(int, Fx6)>();
        foreach (YNode item in Scalars.Seq(node).Items)
        {
            var m = new MapReader(item);
            if (Scalars.Str(m.Required("kind")) != "research_speed")
                throw DataLoadException.At(item, $"'{owner}': unknown effect kind (only research_speed)");
            YNode dirs = m.Required("dirs");
            int mask = 0;
            if (dirs is YScalar)
            {
                if (Scalars.Str(dirs) != "all")
                    throw DataLoadException.At(dirs, $"'{owner}': dirs must be a list or 'all'");
                mask = AllMask;
            }
            else
            {
                foreach (YNode d in Scalars.Seq(dirs).Items)
                {
                    string id = Scalars.Str(d);
                    int i = Directions.Ids.IndexOf(id);
                    if (i < 0)
                        throw DataLoadException.At(d, $"'{owner}': unknown direction '{id}'");
                    if ((mask & (1 << i)) != 0)
                        throw DataLoadException.At(d, $"'{owner}': direction '{id}' is listed twice");
                    mask |= 1 << i;
                }
                if (mask == 0)
                    throw DataLoadException.At(dirs, $"'{owner}': dirs is empty");
            }
            Fx6 bonus = m.Fixed("bonus");
            if (bonus <= Fx6.Zero || bonus > MaxBonus)
                throw DataLoadException.At(item, $"'{owner}': bonus {bonus} must be in (0, {MaxBonus}]");
            m.End();
            result.Add((mask, bonus));
        }
        return [.. result];
    }
}
