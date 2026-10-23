using Office.Application.Panel;

namespace Office.UnitTests;

/// <summary>DECISIONS v1.8 T-23: the RBAC table, who sees which task, and the password rule.</summary>
public sealed class PanelRulesTests
{
    // The whole table, cell by cell: a change to a role must change this test on purpose.
    [Theory]
    [InlineData("manager", "tasks.view_own tasks.view_all tasks.update tasks.assign users.manage account.self")]
    [InlineData("lawyer", "tasks.view_own tasks.view_all tasks.update tasks.assign account.self")]
    [InlineData("staff", "tasks.view_own tasks.update account.self")]
    public void Each_role_has_exactly_its_permissions(string role, string expected)
    {
        var granted = Permissions.All.Where(p => Permissions.Has(role, p));
        Assert.Equal(expected.Split(' '), granted);
    }

    [Fact]
    public void An_unknown_role_has_no_permission_and_no_role_has_them_all_by_default()
    {
        Assert.DoesNotContain(Permissions.All, p => Permissions.Has("admin", p));
        Assert.DoesNotContain(Permissions.All, p => Permissions.Has("", p));
        Assert.False(Permissions.IsRole("superadmin"));
        Assert.Equal(["manager", "lawyer", "staff"], Permissions.Roles);
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
}
