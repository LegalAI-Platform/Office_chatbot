using Office.Application.Panel;

namespace Office.UnitTests;

/// <summary>
/// v1.106 (the owner: «عايزين كله يشتغل بنفس الطريقه دي»): the open panels hear which areas of the office's data changed,
/// once each, and a stream that missed a wake still hears every area since the last one it saw.
/// </summary>
public sealed class DataSignalTests
{
    private static Dictionary<string, string> Stamps(string tasks, string ai) => new(StringComparer.Ordinal) { ["tasks"] = tasks, ["ai"] = ai };

    [Fact]
    public void The_first_look_only_notes_then_each_change_wakes_the_streams_once_with_its_areas()
    {
        var signal = new DataSignal();
        Assert.Empty(signal.Observe(Stamps("1:5", "3"))); // the first look after a start
        Assert.Empty(signal.Observe(Stamps("1:5", "3"))); // nothing changed
        Assert.Equal(0L, signal.Version);

        var seen = signal.Version;
        var next = signal.Next;
        Assert.Equal(["ai", "tasks"], signal.Observe(Stamps("2:9", "4")));
        Assert.True(next.IsCompleted); // the streams woke
        Assert.Equal(["ai", "tasks"], signal.ChangedSince(seen));

        var later = signal.Version;
        Assert.Equal(["ai"], signal.Observe(Stamps("2:9", "5")));
        Assert.Equal(["ai"], signal.ChangedSince(later));
        Assert.Equal(["ai", "tasks"], signal.ChangedSince(seen)); // a stream busy writing still hears both
        Assert.Empty(signal.ChangedSince(signal.Version));
    }
}
