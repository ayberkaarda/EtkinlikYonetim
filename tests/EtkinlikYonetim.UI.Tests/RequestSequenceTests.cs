using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI.Tests;

public sealed class RequestSequenceTests
{
    [Fact]
    public void Next_MakesEveryEarlierRequestStale()
    {
        var sequence = new RequestSequence();

        var first = sequence.Next();
        var second = sequence.Next();

        Assert.False(sequence.IsLatest(first));
        Assert.True(sequence.IsLatest(second));
    }
}
