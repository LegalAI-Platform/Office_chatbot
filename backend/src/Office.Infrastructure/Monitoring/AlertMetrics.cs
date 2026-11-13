using System.Diagnostics.Metrics;

namespace Office.Infrastructure.Monitoring;

/// <summary>
/// v1.107 (the monitoring): the alerts' round as numbers, so Prometheus sees when the bell and its e-mails stop. The bell lives
/// inside the API, so it cannot say that it stopped itself; Alertmanager can (AlertWorkerStalled, AlertEmailsFailing).
/// Counts only: no alert, no task and no address is ever a label.
/// <list type="bullet">
/// <item><c>office_alerts_rounds_total{result="ok|failed"}</c>: the rounds (every few seconds).</item>
/// <item><c>office_alerts_emails_total{result="sent|failed"}</c>: the alert e-mails.</item>
/// <item><c>office_alerts_last_ok_round_timestamp_seconds</c>: when the last round worked (Unix seconds).</item>
/// </list>
/// </summary>
public sealed class AlertMetrics
{
    public const string MeterName = "Office.Alerts";

    private readonly Counter<long> _rounds;
    private readonly Counter<long> _emails;
    private long _lastOk;

    public AlertMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _rounds = meter.CreateCounter<long>("office.alerts.rounds", "{round}", "Rounds of the alerts worker, by result");
        _emails = meter.CreateCounter<long>("office.alerts.emails", "{email}", "Alert e-mails, by result");
        meter.CreateObservableGauge("office.alerts.last_ok_round.timestamp", LastOk, "s",
            "When the alerts worker last finished a round (Unix time)");
    }

    /// <summary>A round that worked: <paramref name="mailed"/> e-mails went out, <paramref name="failed"/> did not.</summary>
    public void RoundOk(int mailed, int failed, DateTimeOffset at)
    {
        _rounds.Add(1, new KeyValuePair<string, object?>("result", "ok"));
        if (mailed > 0)
        {
            _emails.Add(mailed, new KeyValuePair<string, object?>("result", "sent"));
        }

        if (failed > 0)
        {
            _emails.Add(failed, new KeyValuePair<string, object?>("result", "failed"));
        }

        Interlocked.Exchange(ref _lastOk, at.ToUnixTimeSeconds());
    }

    /// <summary>A round that threw (the database away, a bug): the next one tries again.</summary>
    public void RoundFailed() => _rounds.Add(1, new KeyValuePair<string, object?>("result", "failed"));

    // Nothing before the first round that worked, so a fresh start is not read as "stalled since 1970".
    private IEnumerable<Measurement<long>> LastOk()
    {
        var at = Interlocked.Read(ref _lastOk);
        return at > 0 ? [new Measurement<long>(at)] : [];
    }
}
