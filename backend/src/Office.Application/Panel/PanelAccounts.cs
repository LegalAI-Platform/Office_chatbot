using System.Text.RegularExpressions;

namespace Office.Application.Panel;

/// <summary>One row of hazem.panel_users. Never leaves the API with its hash or stamp.</summary>
public sealed class PanelUser
{
    public Guid Id { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public Guid? StaffId { get; set; }
    public string? StaffName { get; set; }
    public required string PasswordHash { get; set; }
    public required string SecurityStamp { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// <see cref="ApprovalPending"/>: registered from the sign-up page (v1.9), inactive until a
    /// manager approves it and picks its role; until then the role is a placeholder.
    /// </summary>
    public string Approval { get; set; } = ApprovalApproved;
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>The authenticator secret, encrypted (IPanelCrypto.Protect); set before it is enabled.</summary>
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    /// <summary>The last 30-second step a code was accepted for: a code works once.</summary>
    public long? TotpLastStep { get; set; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The Chatwoot user this account opens (TASK 07b); made on the first sync.</summary>
    public long? ChatwootUserId { get; set; }

    /// <summary>When the account's photo was set (migration 0040), null without one. The photo itself is never loaded here.</summary>
    public DateTimeOffset? AvatarAt { get; init; }

    public const string ApprovalPending = "pending";
    public const string ApprovalApproved = "approved";

    public bool Pending => Approval == ApprovalPending;

    public bool IsLocked(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public Viewer AsViewer() => new(Id, Role, StaffId);
}

public interface IPanelUserStore
{
    Task<PanelUser?> FindAsync(Guid id, CancellationToken ct);

    Task<PanelUser?> FindByUsernameAsync(string username, CancellationToken ct);

    Task<PanelUser?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>Only a pending account is ever deleted (refused, or never confirmed); its codes go with it.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>The e-mail of every active manager: they hear about new registrations.</summary>
    Task<IReadOnlyList<string>> ManagerEmailsAsync(CancellationToken ct);

    Task SetChatwootUserIdAsync(Guid id, long chatwootUserId, CancellationToken ct);

    Task<bool> EmailTakenAsync(string email, Guid? except, CancellationToken ct);

    Task<IReadOnlyList<PanelUser>> ListAsync(CancellationToken ct);

    Task InsertAsync(PanelUser user, CancellationToken ct);

    Task UpdateAsync(PanelUser user, CancellationToken ct);

    Task<int> ActiveManagersAsync(CancellationToken ct);

    /// <summary>The hazem.staff row with this name (the name used in the office lists), created if missing.</summary>
    Task<Guid> EnsureStaffAsync(string name, CancellationToken ct);

    Task AuditAsync(Guid? userId, string action, Guid? targetUserId, IReadOnlyDictionary<string, string?> detail, CancellationToken ct);

    /// <summary>The account's photo (migration 0040), null when there is none.</summary>
    Task<PanelAvatar?> AvatarAsync(Guid id, CancellationToken ct);

    /// <summary>A null <paramref name="avatar"/> removes the photo (and its time).</summary>
    Task SetAvatarAsync(Guid id, PanelAvatar? avatar, DateTimeOffset? at, CancellationToken ct);
}

/// <summary>Hashing (ASP.NET Core Identity's PasswordHasher) and random secrets.</summary>
public interface IPanelSecrets
{
    string Hash(string password);

    /// <summary>Verified, and whether the hash should be upgraded (a newer Identity format).</summary>
    (bool Ok, bool Rehash) Verify(string hash, string password);

    /// <summary>Spends the same time as a real check, so a wrong user name answers as slowly as a wrong password.</summary>
    void VerifyDummy(string password);

    string TemporaryPassword();

    string NewStamp();
}

/// <summary>
/// Accounts and sign-in (DECISIONS v1.8 T-23): RBAC, no super admin, no default account, lockout,
/// and every change written to hazem.panel_audit. The manager manages accounts from the panel;
/// the CLI only creates the first manager and handles emergencies.
/// </summary>
public sealed partial class PanelAccounts(IPanelUserStore store, IPanelSecrets secrets, TimeProvider clock)
{
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 128;
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);

    public const string InvalidCredentials = "invalid_credentials";
    public const string WrongCurrentPassword = "wrong_current_password";
    public const string WeakPassword = "weak_password";
    public const string SamePassword = "same_password";
    public const string UsernameTaken = "username_taken";
    public const string EmailTaken = "email_taken";
    public const string LastManager = "last_manager";
    public const string CannotChangeSelf = "cannot_change_self";
    public const string EmailNotVerified = "email_not_verified";
    public const string PendingApproval = "pending_approval";

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,31}$")]
    internal static partial Regex UsernameRule();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    internal static partial Regex EmailRule();

    public static string NormalizeUsername(string? username) => (username ?? "").Trim().ToLowerInvariant();

    // ---------- sign-in ----------

    /// <summary>
    /// Every refusal is the same <see cref="InvalidCredentials"/>: unknown name, wrong password,
    /// disabled or locked account. Five wrong passwords lock the account for 15 minutes. Only the
    /// right password learns that a registered account still waits (<see cref="EmailNotVerified"/>,
    /// <see cref="PendingApproval"/>). With the authenticator on, the right password is half of it:
    /// the caller asks for the code (PanelTwoFactor.SecondStepAsync) before any session exists.
    /// </summary>
    public async Task<PanelResult<PanelUser>> SignInAsync(string? username, string? password, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var user = await store.FindByUsernameAsync(NormalizeUsername(username), ct);
        if (user is null || string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            secrets.VerifyDummy(password ?? "");
            return new(PanelOutcome.Forbidden, Error: InvalidCredentials);
        }

        if (user.Pending && !user.IsLocked(now))
        {
            if (!secrets.Verify(user.PasswordHash, password).Ok)
            {
                // counted like any account, so a password cannot be guessed while the account waits for approval
                await CountFailureAsync(user, "login.failed", ct);
                return new(PanelOutcome.Forbidden, Error: InvalidCredentials);
            }

            return new(PanelOutcome.Forbidden, user, Error: user.EmailVerifiedAt is null ? EmailNotVerified : PendingApproval);
        }

        if (!user.Active || user.IsLocked(now))
        {
            secrets.VerifyDummy(password);
            await store.AuditAsync(user.Id, "login.refused", user.Id,
                new Dictionary<string, string?> { ["reason"] = user.Active ? "locked" : "disabled" }, ct);
            return new(PanelOutcome.Forbidden, Error: InvalidCredentials);
        }

        var (ok, rehash) = secrets.Verify(user.PasswordHash, password);
        if (!ok)
        {
            user.FailedLogins++;
            var locked = user.FailedLogins >= MaxFailedLogins;
            if (locked)
            {
                user.FailedLogins = 0;
                user.LockoutEnd = now + LockoutFor;
            }

            await store.UpdateAsync(user, ct);
            await store.AuditAsync(user.Id, locked ? "login.locked" : "login.failed", user.Id, Empty, ct);
            return new(PanelOutcome.Forbidden, Error: InvalidCredentials);
        }

        if (rehash)
        {
            user.PasswordHash = secrets.Hash(password);
        }

        if (user.TotpEnabled)
        {
            // Failed tries keep counting through the second step: the lockout covers both.
            await store.UpdateAsync(user, ct);
            await store.AuditAsync(user.Id, "login.password_ok", user.Id, Empty, ct);
            return PanelResult<PanelUser>.Ok(user);
        }

        await CompleteSignInAsync(user, "password", ct);
        return PanelResult<PanelUser>.Ok(user);
    }

    /// <summary>The account is in: lockout cleared, last sign-in noted, written to the audit.</summary>
    public async Task CompleteSignInAsync(PanelUser user, string method, CancellationToken ct)
    {
        user.FailedLogins = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = clock.GetUtcNow();
        await store.UpdateAsync(user, ct);
        await store.AuditAsync(user.Id, "login", user.Id, new Dictionary<string, string?> { ["method"] = method }, ct);
    }

    /// <summary>A wrong second-step code counts like a wrong password (five lock the account).</summary>
    public Task FailedSecondStepAsync(PanelUser user, CancellationToken ct) => CountFailureAsync(user, "login.code_failed", ct);

    /// <summary>One more wrong password or code; the fifth locks the account for <see cref="LockoutFor"/>.</summary>
    private async Task CountFailureAsync(PanelUser user, string audit, CancellationToken ct)
    {
        user.FailedLogins++;
        var locked = user.FailedLogins >= MaxFailedLogins;
        if (locked)
        {
            user.FailedLogins = 0;
            user.LockoutEnd = clock.GetUtcNow() + LockoutFor;
        }

        await store.UpdateAsync(user, ct);
        await store.AuditAsync(user.Id, locked ? "login.locked" : audit, user.Id, Empty, ct);
    }

    /// <summary>The account behind a session, if the session is still valid (same stamp, still active).</summary>
    public async Task<PanelUser?> ValidateSessionAsync(Guid userId, string? stamp, CancellationToken ct)
    {
        var user = await store.FindAsync(userId, ct);
        return user is { Active: true } && stamp is not null && user.SecurityStamp == stamp ? user : null;
    }

    public async Task<PanelResult<PanelUser>> ChangePasswordAsync(Guid userId, string? current, string? next, CancellationToken ct)
    {
        var user = await store.FindAsync(userId, ct);
        if (user is null || !user.Active)
        {
            return new(PanelOutcome.NotFound);
        }

        // A wrong current password counts towards the lockout like a wrong sign-in, and a locked account is not
        // checked at all: an open session cannot be used to guess the password.
        if (user.IsLocked(clock.GetUtcNow()) || string.IsNullOrEmpty(current) || !secrets.Verify(user.PasswordHash, current).Ok)
        {
            await store.AuditAsync(user.Id, "password.change_refused", user.Id, Empty, ct);
            if (!user.IsLocked(clock.GetUtcNow()))
            {
                await CountFailureAsync(user, "password.change_failed", ct);
            }

            return new(PanelOutcome.Invalid, Error: WrongCurrentPassword);
        }

        if (PasswordProblem(next) is { } problem)
        {
            return new(PanelOutcome.Invalid, Error: problem);
        }

        if (next == current)
        {
            return new(PanelOutcome.Invalid, Error: SamePassword);
        }

        user.PasswordHash = secrets.Hash(next!);
        user.MustChangePassword = false;
        user.SecurityStamp = secrets.NewStamp(); // every other session ends
        await store.UpdateAsync(user, ct);
        await store.AuditAsync(user.Id, "password.changed", user.Id, Empty, ct);
        return PanelResult<PanelUser>.Ok(user);
    }

    /// <summary>NIST: length only (12 to 128), no composition rules.</summary>
    public static string? PasswordProblem(string? password) =>
        password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength ? WeakPassword : null;

    // ---------- accounts (users.manage) ----------

    public sealed record NewAccount(string? Username, string? DisplayName, string? Email, string? Role, string? StaffName);

    /// <summary>
    /// A new account with a temporary password shown once (the manager never types someone else's
    /// password) that must be changed at the first sign-in. <paramref name="by"/> is null for the CLI.
    /// </summary>
    public async Task<PanelResult<(PanelUser User, string TemporaryPassword)>> CreateAsync(
        Guid? by, NewAccount input, string? password, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var username = NormalizeUsername(input.Username);
        var email = (input.Email ?? "").Trim();
        var display = (input.DisplayName ?? "").Trim();
        if (!UsernameRule().IsMatch(username))
        {
            errors["username"] = "invalid";
        }
        else if (await store.FindByUsernameAsync(username, ct) is not null)
        {
            errors["username"] = UsernameTaken;
        }

        if (email.Length > 254 || !EmailRule().IsMatch(email))
        {
            errors["email"] = "invalid";
        }
        else if (await store.EmailTakenAsync(email, null, ct))
        {
            errors["email"] = EmailTaken;
        }

        if (display.Length is 0 or > 120)
        {
            errors["display_name"] = "invalid";
        }

        if (!Permissions.IsRole(input.Role))
        {
            errors["role"] = "invalid";
        }

        if (password is not null && PasswordProblem(password) is { } weak)
        {
            errors["password"] = weak;
        }

        if (errors.Count > 0)
        {
            return new(PanelOutcome.Invalid, Error: errors.First().Value, Fields: errors);
        }

        var temporary = password ?? secrets.TemporaryPassword();
        var user = new PanelUser
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            DisplayName = display,
            Email = email,
            Role = input.Role!,
            PasswordHash = secrets.Hash(temporary),
            SecurityStamp = secrets.NewStamp(),
            // A password typed by its owner in the terminal (first manager) is not temporary.
            MustChangePassword = password is null,
        };
        if (user.Role == Permissions.Staff)
        {
            var staffName = string.IsNullOrWhiteSpace(input.StaffName) ? display : input.StaffName.Trim();
            user.StaffId = await store.EnsureStaffAsync(staffName, ct);
            user.StaffName = staffName;
        }

        await store.InsertAsync(user, ct);
        await store.AuditAsync(by, "user.create", user.Id, new Dictionary<string, string?> { ["role"] = user.Role }, ct);
        return PanelResult<(PanelUser, string)>.Ok((user, temporary));
    }

    public Task<IReadOnlyList<PanelUser>> ListAsync(CancellationToken ct) => store.ListAsync(ct);

    public Task<PanelResult<PanelUser>> DisableAsync(Guid by, Guid id, CancellationToken ct) =>
        ChangeAsync(by, id, "user.disable", async u =>
        {
            if (u.Role == Permissions.Manager && u.Active && await store.ActiveManagersAsync(ct) <= 1)
            {
                return LastManager;
            }

            u.Active = false;
            u.SecurityStamp = secrets.NewStamp();
            return null;
        }, ct);

    public Task<PanelResult<PanelUser>> EnableAsync(Guid by, Guid id, CancellationToken ct) =>
        ChangeAsync(by, id, "user.enable", u =>
        {
            if (u.Pending)
            {
                return Task.FromResult<string?>(PendingApproval); // approved instead, where it gets its role
            }

            u.Active = true;
            u.FailedLogins = 0;
            u.LockoutEnd = null;
            return Task.FromResult<string?>(null);
        }, ct);

    public Task<PanelResult<PanelUser>> ChangeRoleAsync(Guid by, Guid id, string? role, CancellationToken ct) =>
        ChangeAsync(by, id, "user.role", async u =>
        {
            if (u.Pending)
            {
                return PendingApproval;
            }

            if (!Permissions.IsRole(role))
            {
                return "invalid_role";
            }

            if (u.Role == Permissions.Manager && role != Permissions.Manager && u.Active && await store.ActiveManagersAsync(ct) <= 1)
            {
                return LastManager;
            }

            if (role == Permissions.Staff && u.StaffId is null)
            {
                u.StaffId = await store.EnsureStaffAsync(u.DisplayName, ct);
                u.StaffName = u.DisplayName;
            }

            u.Role = role!;
            u.SecurityStamp = secrets.NewStamp(); // the old sessions carried the old permissions
            return null;
        }, ct, new Dictionary<string, string?> { ["to"] = role });

    /// <summary>A new temporary password, shown once; unlocks the account and ends its sessions.</summary>
    public async Task<PanelResult<string>> ResetPasswordAsync(Guid? by, Guid id, string? password, CancellationToken ct)
    {
        if (by == id)
        {
            return new(PanelOutcome.Conflict, Error: CannotChangeSelf);
        }

        if (password is not null && PasswordProblem(password) is { } weak)
        {
            return new(PanelOutcome.Invalid, Error: weak);
        }

        var user = await store.FindAsync(id, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (user.Pending)
        {
            return new(PanelOutcome.Conflict, Error: PendingApproval);
        }

        var temporary = password ?? secrets.TemporaryPassword();
        user.PasswordHash = secrets.Hash(temporary);
        user.MustChangePassword = password is null;
        user.SecurityStamp = secrets.NewStamp();
        user.FailedLogins = 0;
        user.LockoutEnd = null;
        await store.UpdateAsync(user, ct);
        await store.AuditAsync(by, "user.reset_password", user.Id, Empty, ct);
        return PanelResult<string>.Ok(temporary);
    }

    public async Task<PanelResult<PanelUser>> UnlockAsync(Guid id, CancellationToken ct)
    {
        var user = await store.FindAsync(id, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        user.FailedLogins = 0;
        user.LockoutEnd = null;
        await store.UpdateAsync(user, ct);
        await store.AuditAsync(null, "user.unlock", user.Id, Empty, ct);
        return PanelResult<PanelUser>.Ok(user);
    }

    public Task<PanelUser?> FindByUsernameAsync(string username, CancellationToken ct) =>
        store.FindByUsernameAsync(NormalizeUsername(username), ct);

    public Task<PanelUser?> FindAsync(Guid id, CancellationToken ct) => store.FindAsync(id, ct);

    public Task AuditAsync(Guid? by, string action, CancellationToken ct) => store.AuditAsync(by, action, by, Empty, ct);

    private async Task<PanelResult<PanelUser>> ChangeAsync(
        Guid by, Guid id, string action, Func<PanelUser, Task<string?>> change, CancellationToken ct,
        IReadOnlyDictionary<string, string?>? detail = null)
    {
        if (by == id)
        {
            return new(PanelOutcome.Conflict, Error: CannotChangeSelf);
        }

        var user = await store.FindAsync(id, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (await change(user) is { } refused)
        {
            return new(refused is LastManager or PendingApproval ? PanelOutcome.Conflict : PanelOutcome.Invalid, Error: refused);
        }

        await store.UpdateAsync(user, ct);
        await store.AuditAsync(by, action, user.Id, detail ?? Empty, ct);
        return PanelResult<PanelUser>.Ok(user);
    }

    internal static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();
}
