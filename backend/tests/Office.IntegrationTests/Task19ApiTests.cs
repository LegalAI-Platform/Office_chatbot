using Microsoft.Extensions.DependencyInjection;
using Office.Application.Ratings;
using Office.Infrastructure.Booking;

namespace Office.IntegrationTests;

/// <summary>
/// TASK 19 (DECISIONS v1.50) through the real database: a confirmed appointment nobody marked becomes «خلص (تلقائي)»
/// booking.auto_complete_hours after its end when its transfer was verified; one under review gives the office a task, once;
/// a moved appointment counts from its new time; 0 turns it off; an automatic mark may become «ماحضرش» until its rating went.
/// The app's clock stands at Thursday 19 November 2026, 12:00 Cairo.
/// </summary>
[Collection(nameof(HazemApiTests))]
public sealed class Task19ApiTests(TestDatabase db) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly MutableClock _clock = new(Now.ToUniversalTime());
    private ApiFactory _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Clean = """
        DELETE FROM hazem.service_ratings; DELETE FROM hazem.appointment_reminders;
        DELETE FROM hazem.payment_receipts; DELETE FROM hazem.payments; DELETE FROM hazem.appointments;
        UPDATE hazem.settings SET value = '3' WHERE key = 'booking.auto_complete_hours';
        TRUNCATE hazem.panel_audit; DELETE FROM hazem.panel_users;
        TRUNCATE hazem.turns, hazem.generated_replies, hazem.send_results, hazem.turn_replies, hazem.classifications, hazem.panel_alerts, hazem.panel_alert_reads, hazem.staff_tasks, hazem.list_entries;
        DELETE FROM hazem.staff;
        DELETE FROM hazem.intakes; DELETE FROM hazem.conversations; DELETE FROM hazem.persons;
        """;

    public async ValueTask InitializeAsync()
    {
        await db.ExecAsync(Clean);
        _factory = new ApiFactory(db, _clock);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await db.ExecAsync(Clean);
    }

    /// <summary>A confirmed phone consultation that ended <paramref name="endedHoursAgo"/> ago, with its payment in that state.</summary>
    private async Task<Guid> Appointment(double endedHoursAgo, string? payment)
    {
        var person = Guid.NewGuid();
        var id = Guid.NewGuid();
        var end = Now.AddHours(-endedHoursAgo).ToUniversalTime();
        await db.ExecAsync($"""
            INSERT INTO hazem.persons (id, wa_id) VALUES ('{person}', '2010{Random.Shared.Next(10_000_000, 99_999_999)}');
            INSERT INTO hazem.appointments (id, person_id, service, starts_at, ends_at, amount, booked_turn_id, state)
            VALUES ('{id}', '{person}', 'consultation_phone_video', '{end.AddMinutes(-15):O}', '{end:O}', 500, 't-{id:N}', 'confirmed');
            """);
        if (payment is not null)
        {
            await db.ExecAsync($"INSERT INTO hazem.payments (appointment_id, state, amount_due) VALUES ('{id}', '{payment}', 500)");
        }

        return id;
    }

    private async Task Worker()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BookingNotices>().RunAsync(Ct);
    }

    private Task<string?> State(Guid id) =>
        db.ScalarAsync<string>($"SELECT state || '|' || coalesce(completed_by, '-') FROM hazem.appointments WHERE id = '{id}'");

    [Fact]
    public async Task A_verified_appointment_nobody_marked_is_completed_three_hours_after_its_end_and_never_before()
    {
        var due = await Appointment(3.5, "verified");
        var early = await Appointment(1, "verified");
        var noPayment = await Appointment(4, null); // booked by the office, nothing to review
        await Worker();
        Assert.Equal("completed|auto", await State(due));
        Assert.Equal("confirmed|-", await State(early));
        Assert.Equal("completed|auto", await State(noPayment));
    }

    [Fact]
    public async Task A_transfer_still_under_review_is_never_marked_and_the_office_gets_one_task()
    {
        var id = await Appointment(4, "proof_received");
        await Worker();
        await Worker();
        Assert.Equal("confirmed|-", await State(id));
        Assert.Equal(1, await db.ScalarAsync<long>("SELECT count(*) FROM hazem.staff_tasks WHERE type = 'appointment_unmarked'"));
    }

    [Fact]
    public async Task A_moved_appointment_counts_from_its_new_time_and_zero_turns_it_off()
    {
        var moved = await Appointment(5, "verified");
        await db.ExecAsync($"UPDATE hazem.appointments SET starts_at = '{Now.AddHours(1):O}', ends_at = '{Now.AddHours(1.25):O}' WHERE id = '{moved}'");
        await Worker();
        Assert.Equal("confirmed|-", await State(moved)); // its new time has not come

        await db.SetAsync("booking.auto_complete_hours", "0");
        var off = await Appointment(10, "verified");
        await Worker();
        Assert.Equal("confirmed|-", await State(off));
    }

    [Fact]
    public async Task An_automatic_mark_may_become_no_show_until_its_rating_went_and_a_staff_mark_stays()
    {
        var by = Guid.NewGuid();
        await db.ExecAsync($"""
            INSERT INTO hazem.panel_users (id, username, display_name, email, role, password_hash, security_stamp)
            VALUES ('{by}', 'task19.boss', 'Boss', 'boss19@example.test', 'manager', '{new string('x', 40)}', '{new string('s', 20)}')
            """);
        var auto = await Appointment(4, "verified");
        var sent = await Appointment(5, "verified"); // another time: appointments never overlap
        await Worker();
        await db.ExecAsync($"""
            INSERT INTO hazem.service_ratings (person_id, appointment_id, service, finished_at, due_at, state, sent_at, channel)
            SELECT person_id, id, service, now(), now(), 'sent', now(), 'message' FROM hazem.appointments WHERE id = '{sent}'
            """);

        using var scope = _factory.Services.CreateScope();
        var panel = scope.ServiceProvider.GetRequiredService<PanelBooking>();
        Assert.Null(await panel.MarkAsync(by, auto, "no_show", Ct));
        Assert.Equal("no_show|-", await State(auto));
        Assert.Equal("not_confirmed", await panel.MarkAsync(by, sent, "no_show", Ct)); // its rating went: it stays

        var staff = await Appointment(0.5, "verified");
        Assert.Null(await panel.MarkAsync(by, staff, "completed", Ct));
        Assert.Equal("completed|staff", await State(staff));
        Assert.Equal("not_confirmed", await panel.MarkAsync(by, staff, "no_show", Ct)); // a staff mark is one-way, as before
    }

    [Theory]
    [InlineData("2026-11-19T15:00:00+02:00", "2026-11-20T10:00:00+02:00")] // marked in the afternoon: the next morning
    [InlineData("2026-11-19T07:00:00+02:00", "2026-11-19T10:00:00+02:00")] // marked early: that morning's 10:00
    [InlineData("2026-11-19T09:00:00+02:00", "2026-11-20T10:00:00+02:00")] // 2 hours later is past 10:00: the next one
    public void After_an_automatic_mark_the_rating_waits_for_the_next_ten_o_clock_and_two_hours(string marked, string due)
    {
        var s = RatingSettings.From(new Office.Application.Responders.HazemSettings(new Dictionary<string, string>()));
        Assert.Equal(DateTimeOffset.Parse(due).ToUniversalTime(), RatingRules.DueAfterAuto(DateTimeOffset.Parse(marked), s));
    }
}
