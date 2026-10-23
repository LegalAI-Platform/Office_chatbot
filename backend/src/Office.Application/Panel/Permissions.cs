namespace Office.Application.Panel;

/// <summary>
/// RBAC for the panel (DECISIONS v1.8 T-23). The code checks permissions, never role names: a
/// role is the set of permissions below, defined here and nowhere else. No role passes every check.
/// </summary>
public static class Permissions
{
    public const string TasksViewOwn = "tasks.view_own";
    public const string TasksViewAll = "tasks.view_all";
    public const string TasksUpdate = "tasks.update";
    public const string TasksAssign = "tasks.assign";
    public const string UsersManage = "users.manage";
    public const string AccountSelf = "account.self";
    // TASK 07c (DECISIONS v1.11): the manager and the lawyer keep the office's lists and Hazem's
    // texts and read the reports; only the manager unmutes a conversation (S-01).
    public const string ListsManage = "lists.manage";
    public const string TextsManage = "texts.manage";
    public const string ReportsView = "reports.view";
    public const string ConversationsUnmute = "conversations.unmute";

    public static IReadOnlyList<string> All { get; } =
        [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, UsersManage, AccountSelf, ListsManage, TextsManage, ReportsView, ConversationsUnmute];

    public const string Manager = "manager";
    public const string Lawyer = "lawyer";
    public const string Staff = "staff";

    public static IReadOnlyList<string> Roles { get; } = [Manager, Lawyer, Staff];

    private static readonly Dictionary<string, string[]> ByRole = new(StringComparer.Ordinal)
    {
        [Manager] = [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, UsersManage, AccountSelf, ListsManage, TextsManage, ReportsView,
            ConversationsUnmute],
        [Lawyer] = [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, AccountSelf, ListsManage, TextsManage, ReportsView],
        [Staff] = [TasksViewOwn, TasksUpdate, AccountSelf],
    };

    public static IReadOnlyList<string> Of(string role) =>
        ByRole.TryGetValue(role, out var p) ? p : [];

    public static bool Has(string role, string permission) => Of(role).Contains(permission, StringComparer.Ordinal);

    public static bool IsRole(string? role) => role is not null && ByRole.ContainsKey(role);
}

/// <summary>Who is asking: the signed-in account and the staff member it stands for.</summary>
public sealed record Viewer(Guid UserId, string Role, Guid? StaffId)
{
    public bool Can(string permission) => Permissions.Has(Role, permission);
}
