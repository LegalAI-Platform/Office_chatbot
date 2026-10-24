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

    // TASK 08 (DECISIONS v1.12): everyone keeps the booking hours and sees every appointment (owner: b,
    // P4); the office's payment details are the manager's and the lawyer's (P1).
    public const string ScheduleManage = "schedule.manage";
    public const string OfficeManage = "office.manage";

    // TASK 09 (DECISIONS v1.13): every role reviews a transfer (P1: the lawyer, the manager and the staff)
    // and records a refund it made (P2: a person, never the system).
    public const string PaymentsVerify = "payments.verify";
    public const string PaymentsRefund = "payments.refund";

    // TASK 10 (DECISIONS v1.14): every role follows the service orders (documents, upload, delivery, close);
    // only the lawyer and the manager price one or ask for a written opinion (owner: b).
    public const string ServicesManage = "services.manage";
    public const string ServicesPrice = "services.price";

    public static IReadOnlyList<string> All { get; } =
        [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, UsersManage, AccountSelf, ListsManage, TextsManage, ReportsView, ConversationsUnmute,
            ScheduleManage, OfficeManage, PaymentsVerify, PaymentsRefund, ServicesManage, ServicesPrice];

    public const string Manager = "manager";
    public const string Lawyer = "lawyer";
    public const string Staff = "staff";

    public static IReadOnlyList<string> Roles { get; } = [Manager, Lawyer, Staff];

    private static readonly Dictionary<string, string[]> ByRole = new(StringComparer.Ordinal)
    {
        [Manager] = [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, UsersManage, AccountSelf, ListsManage, TextsManage, ReportsView,
            ConversationsUnmute, ScheduleManage, OfficeManage, PaymentsVerify, PaymentsRefund, ServicesManage, ServicesPrice],
        [Lawyer] = [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, AccountSelf, ListsManage, TextsManage, ReportsView, ScheduleManage,
            OfficeManage, PaymentsVerify, PaymentsRefund, ServicesManage, ServicesPrice],
        [Staff] = [TasksViewOwn, TasksUpdate, AccountSelf, ScheduleManage, PaymentsVerify, PaymentsRefund, ServicesManage],
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
