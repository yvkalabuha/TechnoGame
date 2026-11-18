using System.Collections;
using Singularity.Core.Commands;

namespace Singularity.Session;

/// <summary>
/// The outcome of every applied command as text, formatted on access: the tick stores a small struct per command and builds no string
/// (TASK-0107: the strings were about 0.2 MiB a month at 200 commands per actor-month load). An observation, not state.
/// </summary>
internal sealed class OutcomeLog : IReadOnlyList<string>
{
    private readonly ChunkedList<(CommandKey Key, CommandVerdict Verdict)> _items = new();

    public int Count => _items.Count;

    public string this[int index] => Format(_items[index]);

    public void Add(CommandKey key, CommandVerdict verdict) => _items.Add((key, verdict));

    private static string Format((CommandKey Key, CommandVerdict Verdict) o) =>
        o.Verdict.Accepted ? $"{o.Key}: accepted" : $"{o.Key}: rejected: {o.Verdict.Reason}: {o.Verdict.Message}";

    public IEnumerator<string> GetEnumerator()
    {
        for (int i = 0; i < _items.Count; i++)
            yield return Format(_items[i]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
