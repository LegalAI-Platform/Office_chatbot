using Office.Application.Panel;

namespace Office.UnitTests;

/// <summary>DECISIONS v1.8 T-23: the RBAC table, who sees which task, and the password rule.</summary>
public sealed class PanelRulesTests
{
    // The whole table, cell by cell: a change to a role must change this test on purpose.
    // v1.100: texts.manage and testing.forget are the maintenance role's only (with maintenance.manage).
    // v1.106: reports.view too (the owner: «اكونت صيانه بس»).
    // v1.110: the lawyer has users.manage («الحسابات»), as the manager (the owner: «خلي المستشار يشوفها»).
    [Theory]
    [InlineData("maintenance", "tasks.view_own tasks.view_all tasks.update tasks.assign users.manage account.self lists.manage texts.manage reports.view conversations.unmute schedule.manage office.manage payments.verify payments.refund services.manage services.price urf.manage urf.review prices.manage testing.forget maintenance.manage")]
    [InlineData("manager", "tasks.view_own tasks.view_all tasks.update tasks.assign users.manage account.self lists.manage conversations.unmute schedule.manage office.manage payments.verify payments.refund services.manage services.price urf.manage urf.review prices.manage")]
    [InlineData("lawyer", "tasks.view_own tasks.view_all tasks.update tasks.assign users.manage account.self lists.manage schedule.manage office.manage payments.verify payments.refund services.manage services.price urf.manage urf.review prices.manage")]
    [InlineData("staff", "tasks.view_own tasks.update account.self schedule.manage payments.verify payments.refund services.manage urf.manage")]
    public void Each_role_has_exactly_its_permissions(string role, string expected)
    {
        var granted = Permissions.All.Where(p => Permissions.Has(role, p));
        Assert.Equal(expected.Split(' '), granted);
    }

    [Fact]
    public void An_unknown_role_has_no_permission_and_only_the_maintenance_role_has_them_all()
    {
        Assert.DoesNotContain(Permissions.All, p => Permissions.Has("admin", p));
        Assert.DoesNotContain(Permissions.All, p => Permissions.Has("", p));
        Assert.False(Permissions.IsRole("superadmin"));
        Assert.Equal(["maintenance", "manager", "lawyer", "staff"], Permissions.Roles);
        // v1.100 (the owner): «حساب الصيانة» passes every check, and no office role has Hazem's texts
        Assert.All(Permissions.All, p => Assert.True(Permissions.Has(Permissions.Maintenance, p), p));
        Assert.All(new[] { Permissions.Manager, Permissions.Lawyer, Permissions.Staff }, r =>
        {
            Assert.False(Permissions.Has(r, Permissions.TextsManage), r);
            Assert.False(Permissions.Has(r, Permissions.MaintenanceManage), r);
            Assert.False(Permissions.Has(r, Permissions.TestingForget), r);
        });
    }

    private static readonly Guid Mine = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid();

    private static TaskRecord Task(Guid? staff) => new(Guid.NewGuid(), "human_requested", "open", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow, staff is { } s ? new StaffRef(s, "x") : null, null, "201000000001", null, null, null, null, null, []);

    [Fact]
    public void A_staff_member_sees_own_and_unassigned_tasks_only()
    {
        var staff = new Viewer(Guid.NewGuid(), "staff", Mine);
        Assert.True(PanelTasks.MaySee(staff, Task(Mine)));
        Assert.True(PanelTasks.MaySee(staff, Task(null)));
        Assert.False(PanelTasks.MaySee(staff, Task(Theirs)));

        var lawyer = new Viewer(Guid.NewGuid(), "lawyer", null);
        Assert.True(PanelTasks.MaySee(lawyer, Task(Theirs)));
    }

    [Theory]
    [InlineData("open", true, true, true)]
    [InlineData("in_progress", false, true, true)]
    [InlineData("overdue", true, true, true)]
    [InlineData("done", false, false, false)]
    [InlineData("cancelled", false, false, false)]
    public void The_task_buttons_follow_the_S05_machine(string state, bool start, bool done, bool cancel)
    {
        var view = PanelTasks.View(new Viewer(Guid.NewGuid(), "staff", Mine), Task(Mine) with { State = state });
        Assert.Equal((start, done, cancel, false), (view.Can.Start, view.Can.Done, view.Can.Cancel, view.Can.Assign));
    }

    [Theory]
    [InlineData(null, "weak_password")]
    [InlineData("eleven char", "weak_password")]
    [InlineData("twelve chars", null)]
    [InlineData("كلمة سر عربي طويلة", null)] // any characters: length is the rule (NIST)
    public void A_password_needs_12_characters_and_nothing_else(string? password, string? problem) =>
        Assert.Equal(problem, PanelAccounts.PasswordProblem(password));

    [Fact]
    public void A_password_longer_than_128_is_refused() =>
        Assert.Equal("weak_password", PanelAccounts.PasswordProblem(new string('x', 129)));

    [Theory]
    [InlineData("  Yasmin ", "yasmin")]
    [InlineData(null, "")]
    public void User_names_are_trimmed_and_lower_case(string? raw, string expected) =>
        Assert.Equal(expected, PanelAccounts.NormalizeUsername(raw));

    // The API has no culture data (InvariantGlobalization): the alert e-mail's time is written by hand.
    [Theory]
    [InlineData(15, 5, "الجمعة 23 أكتوبر، 3:05 م")]
    [InlineData(0, 30, "الجمعة 23 أكتوبر، 12:30 ص")]
    [InlineData(12, 0, "الجمعة 23 أكتوبر، 12:00 م")]
    public void The_alert_time_is_in_arabic_without_culture_data(int hour, int minute, string expected) =>
        Assert.Equal(expected, PanelMails.ArabicTime(new DateTimeOffset(2026, 10, 23, hour, minute, 0, TimeSpan.FromHours(3))));

    [Fact]
    public void An_alert_email_builds_with_no_culture_data() =>
        Assert.Contains("المطلوب قبل:", PanelMails.Alert("a@example.test",
            new AlertToMail(1, AlertKinds.Urgent, Guid.NewGuid(), "urgent_request", DateTimeOffset.UtcNow, null), "http://x").Text);

    // v1.110 (the owner: «كميه ايميلات كتيره اوي» … «موافق»): a paid urgent call is one task. Its alerts e-mail whose turn
    // it is: nobody while the lawyer's WhatsApp alert is his, the lawyer when it did not reach him, the staff when nobody
    // took it in time, and never «late». Every other urgent alert, as before (the lawyer and the staff both).
    [Theory]
    [InlineData("task_urgent", "urgent_call", true, "urgent")]
    [InlineData("task_urgent", "urgent_call_failed", false, "lawyer")]
    [InlineData("task_urgent", "urgent_call_unanswered", false, "office")]
    [InlineData("task_overdue", "urgent_call", true, "office")]
    [InlineData("task_overdue", "urgent_call_failed", true, "office")]
    [InlineData("task_overdue", "urgent_call_unanswered", true, "office")]
    [InlineData("task_urgent", "urgent_request", false, "urgent")]
    [InlineData("task_urgent", "safety_alert", false, "urgent")]
    [InlineData("task_overdue", "urgent_request", false, "office")]
    [InlineData("task_overdue", "service_pricing", false, "lawyer")]
    public void A_paid_urgent_call_emails_only_whose_turn_it_is(string kind, string type, bool bellOnly, string audience)
    {
        Assert.Equal(bellOnly, AlertKinds.BellOnly(kind, type));
        Assert.Equal(audience, AlertAudience.For(kind, type));
    }

    [Fact]
    public void A_paid_urgent_call_s_two_emails_say_what_happened_and_never_the_client()
    {
        var task = Guid.NewGuid();
        var failed = PanelMails.Alert("lawyer@example.test",
            new AlertToMail(1, AlertKinds.Urgent, task, "urgent_call_failed", DateTimeOffset.UtcNow, null), "http://x");
        Assert.StartsWith("لم يصلك تنبيه استشارة عاجلة على واتساب |", failed.Subject, StringComparison.Ordinal); // then the office's name
        Assert.Contains("النوع: استشارة عاجلة مدفوعة: لم يصل تنبيهها إلى المستشار", failed.Text);
        Assert.Contains($"http://x/tasks/{task}", failed.Text);

        var unanswered = PanelMails.Alert("staff@example.test",
            new AlertToMail(2, AlertKinds.Urgent, task, "urgent_call_unanswered", DateTimeOffset.UtcNow, null), "http://x");
        Assert.StartsWith("استشارة عاجلة مدفوعة لم يتولّها أحد |", unanswered.Subject, StringComparison.Ordinal);
        Assert.Contains("تواصل مع المستشار أو مع العميل الآن.", unanswered.Text);

        // any other urgent alert, as before
        Assert.StartsWith("طلب عاجل |", PanelMails.Alert("a@example.test",
            new AlertToMail(3, AlertKinds.Urgent, task, "safety_alert", DateTimeOffset.UtcNow, null), "http://x").Subject, StringComparison.Ordinal);
    }
}
