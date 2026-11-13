namespace Office.Application.Panel;

/// <summary>
/// RBAC for the panel (DECISIONS v1.8 T-23). The code checks permissions, never role names: a
/// role is the set of permissions below, defined here and nowhere else. v1.100 (the owner): only the maintenance
/// role, «حساب الصيانة» (Mohamed Emad's), passes every check; no office role (manager, lawyer, staff) does.
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

    /// <summary>
    /// v1.106 (the owner, 2026-11-13: «التقارير ما يشوفهاش اكونت المستشار، اللي يقدر يشوف التقارير هو الصيانه، اكونت صيانه
    /// بس»): «التقارير» and the home's «الشهر ده» are the maintenance role's only.
    /// </summary>
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
    public const string UrfManage = "urf.manage";   // TASK 11: the Urfi cases and their documents (every role)
    public const string UrfReview = "urf.review";   // TASK 11: approve or refuse a case (the lawyer and the manager)
    public const string PricesManage = "prices.manage"; // TASK 06: the price list of B-23 (the lawyer and the manager)
    // «أدوات التجربة» (the owner, 2026-10-30): forget a test number, test environment only. v1.100: the maintenance role's only.
    public const string TestingForget = "testing.forget";

    // v1.100 (the owner, 2026-11-10: «محدش يبقى ليه صلاحية غير الحساب بتاع محمد عماد … حساب الصيانة»): the maintenance accounts
    // themselves (make one, give the role, disable it, a new password, its authenticator off) are the maintenance role's
    // only, as Hazem's texts («النصوص» and «دليل التعامل», TextsManage) and «أدوات التجربة» (TestingForget) are. The CLI on
    // the server can still do it (emergencies).
    public const string MaintenanceManage = "maintenance.manage";

    public static IReadOnlyList<string> All { get; } =
        [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, UsersManage, AccountSelf, ListsManage, TextsManage, ReportsView, ConversationsUnmute,
            ScheduleManage, OfficeManage, PaymentsVerify, PaymentsRefund, ServicesManage, ServicesPrice, UrfManage, UrfReview, PricesManage, TestingForget, MaintenanceManage];

    public const string Manager = "manager";
    public const string Lawyer = "lawyer";
    public const string Staff = "staff";

    /// <summary>v1.100: «الصيانة», the owner's maintenance account: every permission, and the only one with Hazem's texts.</summary>
    public const string Maintenance = "maintenance";

    public static IReadOnlyList<string> Roles { get; } = [Maintenance, Manager, Lawyer, Staff];

    private static readonly Dictionary<string, string[]> ByRole = new(StringComparer.Ordinal)
    {
        [Maintenance] = [.. All],
        // v1.100: without TextsManage and TestingForget (the maintenance role's); v1.106: and without ReportsView
        [Manager] = [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, UsersManage, AccountSelf, ListsManage,
            ConversationsUnmute, ScheduleManage, OfficeManage, PaymentsVerify, PaymentsRefund, ServicesManage, ServicesPrice, UrfManage, UrfReview, PricesManage],
        [Lawyer] = [TasksViewOwn, TasksViewAll, TasksUpdate, TasksAssign, AccountSelf, ListsManage, ScheduleManage,
            OfficeManage, PaymentsVerify, PaymentsRefund, ServicesManage, ServicesPrice, UrfManage, UrfReview, PricesManage],
        [Staff] = [TasksViewOwn, TasksUpdate, AccountSelf, ScheduleManage, PaymentsVerify, PaymentsRefund, ServicesManage, UrfManage],
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
