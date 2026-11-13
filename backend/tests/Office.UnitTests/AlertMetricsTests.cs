using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Office.Infrastructure.Monitoring;

namespace Office.UnitTests;

/// <summary>
/// v1.107 (the monitoring): the alerts' rounds and e-mails are counted by result, and the last round that worked is kept, so
/// Alertmanager hears when the bell stops (the bell cannot say it itself).
/// </summary>
public sealed class AlertMetricsTests
{
    private sealed record Seen(string Name, long Value, string? Result);

    [Fact]
    public void Rounds_and_emails_are_counted_by_result_and_the_last_good_round_is_kept()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        var metrics = new AlertMetrics(factory);
        var seen = new List<Seen>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            // this test's own meter only: other tests may make one with the same name at the same time
            if (instrument.Meter.Name == AlertMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? result = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "result")
                {
                    result = tag.Value as string;
                }
            }

            lock (seen)
            {
                seen.Add(new Seen(instrument.Name, value, result));
            }
        });
        listener.Start();

        listener.RecordObservableInstruments();
        Assert.DoesNotContain(seen, m => m.Name == "office.alerts.last_ok_round.timestamp"); // nothing before the first good round

        var at = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        metrics.RoundOk(mailed: 2, failed: 1, at);
        metrics.RoundOk(mailed: 0, failed: 0, at.AddSeconds(3));
        metrics.RoundFailed();
        listener.RecordObservableInstruments();

        long Sum(string name, string result) => seen.Where(m => m.Name == name && m.Result == result).Sum(m => m.Value);
        Assert.Equal(2, Sum("office.alerts.rounds", "ok"));
        Assert.Equal(1, Sum("office.alerts.rounds", "failed"));
        Assert.Equal(2, Sum("office.alerts.emails", "sent"));
        Assert.Equal(1, Sum("office.alerts.emails", "failed"));
        Assert.Equal(at.AddSeconds(3).ToUnixTimeSeconds(), Assert.Single(seen, m => m.Name == "office.alerts.last_ok_round.timestamp").Value);
    }
}
