using Singularity.Session;

namespace Singularity.Session.Tests;

public class ChunkedListTests
{
    [Fact]
    public void KeepsOrderAcrossChunks_AndRefusesAnIndexAtOrPastCount()
    {
        var list = new ChunkedList<int>();
        for (int i = 0; i < 2500; i++)
            list.Add(i);
        Assert.Equal(2500, list.Count);
        Assert.Equal(Enumerable.Range(0, 2500), list);
        Assert.Equal(2499, list[2499]);
        // 2500 is inside the last chunk (capacity 3072) but past Count.
        Assert.Throws<ArgumentOutOfRangeException>(() => list[2500]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[-1]);
    }
}
