namespace Office.Application.Panel;

/// <summary>A six-digit code sent by e-mail (hazem.panel_codes, migration 0010).</summary>
public sealed record PanelCode(Guid Id, string CodeHash, DateTimeOffset ExpiresAt, int Attempts, DateTimeOffset CreatedAt);

public interface IPanelCodeStore
{
    Task AddAsync(Guid userId, string purpose, string codeHash, DateTimeOffset expiresAt, CancellationToken ct);

    /// <summary>The newest code not used yet for this purpose; an older one stops counting when a new one is sent.</summary>
    Task<PanelCode?> LatestAsync(Guid userId, string purpose, CancellationToken ct);

    Task<int> SentSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken ct);

    Task FailAsync(Guid codeId, CancellationToken ct);

    Task UseAsync(Guid codeId, DateTimeOffset at, CancellationToken ct);

    Task ReplaceRecoveryCodesAsync(Guid userId, IReadOnlyList<string> hashes, CancellationToken ct);

    /// <summary>Marks one unused recovery code used, in one statement: true if it was there.</summary>
    Task<bool> UseRecoveryCodeAsync(Guid userId, string hash, DateTimeOffset at, CancellationToken ct);

    Task<int> RecoveryCodesLeftAsync(Guid userId, CancellationToken ct);
}

/// <summary>An e-mail from the office's no-reply address (the owner's SMTP server).</summary>
public sealed record PanelMail(string To, string Subject, string Text, string Html);

public interface IPanelMailer
{
    /// <summary>A mail server and a sender address are set (SMTP_HOST, SMTP_FROM).</summary>
    bool Configured { get; }

    /// <summary>False if the server refused or could not be reached; the code is never logged.</summary>
    Task<bool> SendAsync(PanelMail mail, CancellationToken ct);
}

/// <summary>Codes, the authenticator (RFC 6238) and the encryption of its secret, keyed by PANEL_SECRET_KEY.</summary>
public interface IPanelCrypto
{
    /// <summary>PANEL_SECRET_KEY is set: without it nothing here works, and sign-up and 2FA say so.</summary>
    bool Configured { get; }

    string NewEmailCode();

    /// <summary>A keyed hash bound to the account and the purpose: a code for one is useless for another.</summary>
    string HashCode(Guid userId, string purpose, string code);

    bool SameHash(string a, string b);

    string NewRecoveryCode();

    /// <summary>20 random bytes in base32, what the authenticator app is given.</summary>
    string NewTotpSecret();

    string Protect(string plain);

    string? Unprotect(string protectedText);

    /// <summary>The 30-second step the code belongs to (one step either side for a slow clock), or null.</summary>
    long? MatchTotp(string base32Secret, string code, DateTimeOffset now);
}

/// <summary>
/// Sign-up, e-mail codes and a forgotten password (DECISIONS v1.9 T-23). Anyone may register; the
/// e-mail is confirmed with a code; the account stays pending, with no way in, until a manager
/// approves it and picks its role. Replies never tell a stranger whether an address has an account.
/// </summary>
public sealed class PanelSignup(
    IPanelUserStore users, IPanelCodeStore codes, IPanelSecrets secrets, IPanelCrypto crypto, IPanelMailer mailer, TimeProvider clock)
{
    public const string PurposeVerify = "verify_email";
    public const string PurposeReset = "reset_password";

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan UnconfirmedKeptFor = TimeSpan.FromHours(24);
    public const int MaxCodeAttempts = 5;
    public const int MaxCodesPerHour = 5;

    public const string NotConfigured = "not_configured";
    public const string InvalidCode = "invalid_code";
    public const string NotPending = "not_pending";

    public sealed record Registration(string? Username, string? DisplayName, string? Email, string? Password);

    public bool Ready => crypto.Configured && mailer.Configured;

    // ---------- registration ----------

    /// <summary>
    /// A taken user name is said (the person must pick another). A taken e-mail is not: the answer
    /// is the same, and the address's owner gets a note that someone tried, instead of a code.
    /// </summary>
    public async Task<PanelResult<bool>> RegisterAsync(Registration input, CancellationToken ct)
    {
        if (!Ready)
        {
            return new(PanelOutcome.Unavailable, Error: NotConfigured);
        }

        var username = PanelAccounts.NormalizeUsername(input.Username);
        var email = (input.Email ?? "").Trim();
        var display = (input.DisplayName ?? "").Trim();
        var errors = new Dictionary<string, string>();
        if (!PanelAccounts.UsernameRule().IsMatch(username))
        {
            errors["username"] = "invalid";
        }

        if (email.Length > 254 || !PanelAccounts.EmailRule().IsMatch(email))
        {
            errors["email"] = "invalid";
        }

        if (display.Length is 0 or > 120)
        {
            errors["display_name"] = "invalid";
        }

        if (PanelAccounts.PasswordProblem(input.Password) is { } weak)
        {
            errors["password"] = weak;
        }

        if (errors.Count == 0)
        {
            await ForgetUnconfirmedAsync(await users.FindByUsernameAsync(username, ct), ct);
            await ForgetUnconfirmedAsync(await users.FindByEmailAsync(email, ct), ct);
            if (await users.FindByUsernameAsync(username, ct) is not null)
            {
                errors["username"] = PanelAccounts.UsernameTaken;
            }
        }

        if (errors.Count > 0)
        {
            return new(PanelOutcome.Invalid, Error: errors.First().Value, Fields: errors);
        }

        var hash = secrets.Hash(input.Password!); // spent either way: the time does not tell either
        if (await users.FindByEmailAsync(email, ct) is { } owner)
        {
            await users.AuditAsync(null, "user.register_email_taken", owner.Id, PanelAccounts.Empty, ct);
            return PanelResult<bool>.Ok(await mailer.SendAsync(PanelMails.SomeoneTried(owner), ct));
        }

        var user = new PanelUser
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            DisplayName = display,
            Email = email,
            Role = Permissions.Staff, // a placeholder: the manager picks the role when approving
            PasswordHash = hash,
            SecurityStamp = secrets.NewStamp(),
            MustChangePassword = false, // the person chose it
            Active = false,
            Approval = PanelUser.ApprovalPending,
            CreatedAt = clock.GetUtcNow(),
        };
        await users.InsertAsync(user, ct);
        await users.AuditAsync(null, "user.register", user.Id, PanelAccounts.Empty, ct);
        return PanelResult<bool>.Ok(await SendCodeAsync(user, PurposeVerify, ct));
    }

    /// <summary>Another code for an address that is registered and not confirmed yet; silent otherwise.</summary>
    public async Task<PanelResult<bool>> ResendAsync(string? email, CancellationToken ct)
    {
        if (!Ready)
        {
            return new(PanelOutcome.Unavailable, Error: NotConfigured);
        }

        var user = await users.FindByEmailAsync((email ?? "").Trim(), ct);
        var sent = user is { Pending: true, EmailVerifiedAt: null } && await SendCodeAsync(user, PurposeVerify, ct);
        return PanelResult<bool>.Ok(sent);
    }

    /// <summary>The address is confirmed; every manager hears that an account waits for them.</summary>
    public async Task<PanelResult<bool>> VerifyEmailAsync(string? email, string? code, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync((email ?? "").Trim(), ct);
        if (user is not { Pending: true, EmailVerifiedAt: null } || !await CheckCodeAsync(user, PurposeVerify, code, ct))
        {
            return new(PanelOutcome.Invalid, Error: InvalidCode);
        }

        user.EmailVerifiedAt = clock.GetUtcNow();
        await users.UpdateAsync(user, ct);
        await users.AuditAsync(null, "user.email_verified", user.Id, PanelAccounts.Empty, ct);
        foreach (var manager in await users.ManagerEmailsAsync(ct))
        {
            await mailer.SendAsync(PanelMails.WaitingForApproval(manager, user), ct);
        }

        return PanelResult<bool>.Ok(true);
    }

    // ---------- a forgotten password ----------

    /// <summary>A code to the account's address, if <paramref name="login"/> (user name or e-mail) is an active account. Silent otherwise.</summary>
    public async Task<PanelResult<bool>> ForgotAsync(string? login, CancellationToken ct)
    {
        if (!Ready)
        {
            return new(PanelOutcome.Unavailable, Error: NotConfigured);
        }

        var user = await FindByLoginAsync(login, ct);
        if (user is { Active: true, Pending: false })
        {
            await SendCodeAsync(user, PurposeReset, ct);
        }

        return PanelResult<bool>.Ok(true);
    }

    /// <summary>A new password with the code: unlocks the account and ends every session it had.</summary>
    public async Task<PanelResult<bool>> ResetAsync(string? login, string? code, string? password, CancellationToken ct)
    {
        if (PanelAccounts.PasswordProblem(password) is { } weak)
        {
            return new(PanelOutcome.Invalid, Error: weak, Fields: new Dictionary<string, string> { ["new_password"] = weak });
        }

        var user = await FindByLoginAsync(login, ct);
        if (user is not { Active: true, Pending: false } || !await CheckCodeAsync(user, PurposeReset, code, ct))
        {
            return new(PanelOutcome.Invalid, Error: InvalidCode);
        }

        user.PasswordHash = secrets.Hash(password!);
        user.MustChangePassword = false;
        user.SecurityStamp = secrets.NewStamp();
        user.FailedLogins = 0;
        user.LockoutEnd = null;
        await users.UpdateAsync(user, ct);
        await users.AuditAsync(null, "password.reset_by_code", user.Id, PanelAccounts.Empty, ct);
        await mailer.SendAsync(PanelMails.PasswordChanged(user), ct);
        return PanelResult<bool>.Ok(true);
    }

    // ---------- the manager ----------

    /// <summary>The account can sign in from now on, with the role the manager picked.</summary>
    public async Task<PanelResult<PanelUser>> ApproveAsync(Guid by, Guid id, string? role, string? staffName, CancellationToken ct)
    {
        var user = await users.FindAsync(id, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (!user.Pending)
        {
            return new(PanelOutcome.Conflict, Error: NotPending);
        }

        if (user.EmailVerifiedAt is null)
        {
            return new(PanelOutcome.Conflict, Error: PanelAccounts.EmailNotVerified);
        }

        if (!Permissions.IsRole(role))
        {
            return new(PanelOutcome.Invalid, Error: "invalid_role", Fields: new Dictionary<string, string> { ["role"] = "invalid" });
        }

        user.Role = role!;
        if (role == Permissions.Staff)
        {
            var name = string.IsNullOrWhiteSpace(staffName) ? user.DisplayName : staffName.Trim();
            user.StaffId = await users.EnsureStaffAsync(name, ct);
            user.StaffName = name;
        }

        user.Approval = PanelUser.ApprovalApproved;
        user.Active = true;
        user.ApprovedBy = by;
        user.ApprovedAt = clock.GetUtcNow();
        user.SecurityStamp = secrets.NewStamp();
        await users.UpdateAsync(user, ct);
        await users.AuditAsync(by, "user.approve", user.Id, new Dictionary<string, string?> { ["role"] = user.Role }, ct);
        await mailer.SendAsync(PanelMails.Approved(user), ct);
        return PanelResult<PanelUser>.Ok(user);
    }

    /// <summary>A refused registration is deleted: the name and the address are free again.</summary>
    public async Task<PanelResult<bool>> RejectAsync(Guid by, Guid id, CancellationToken ct)
    {
        var user = await users.FindAsync(id, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (!user.Pending)
        {
            return new(PanelOutcome.Conflict, Error: NotPending);
        }

        await users.DeleteAsync(user.Id, ct);
        await users.AuditAsync(by, "user.reject", user.Id, PanelAccounts.Empty, ct);
        if (user.EmailVerifiedAt is not null)
        {
            await mailer.SendAsync(PanelMails.Rejected(user), ct);
        }

        return PanelResult<bool>.Ok(true);
    }

    // ---------- codes ----------

    /// <summary>At most one a minute and five an hour per account and purpose.</summary>
    private async Task<bool> SendCodeAsync(PanelUser user, string purpose, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var last = await codes.LatestAsync(user.Id, purpose, ct);
        if (last is not null && now - last.CreatedAt < ResendAfter
            || await codes.SentSinceAsync(user.Id, purpose, now.AddHours(-1), ct) >= MaxCodesPerHour)
        {
            return false;
        }

        var code = crypto.NewEmailCode();
        await codes.AddAsync(user.Id, purpose, crypto.HashCode(user.Id, purpose, code), now + CodeLifetime, ct);
        var mail = purpose == PurposeVerify ? PanelMails.VerifyCode(user, code) : PanelMails.ResetCode(user, code);
        return await mailer.SendAsync(mail, ct);
    }

    /// <summary>The newest code only, ten minutes, five tries, once.</summary>
    private async Task<bool> CheckCodeAsync(PanelUser user, string purpose, string? code, CancellationToken ct)
    {
        code = (code ?? "").Trim();
        var now = clock.GetUtcNow();
        var last = await codes.LatestAsync(user.Id, purpose, ct);
        if (last is null || last.ExpiresAt <= now || last.Attempts >= MaxCodeAttempts || code.Length != 6 || !crypto.Configured)
        {
            return false;
        }

        if (!crypto.SameHash(last.CodeHash, crypto.HashCode(user.Id, purpose, code)))
        {
            await codes.FailAsync(last.Id, ct);
            return false;
        }

        await codes.UseAsync(last.Id, now, ct);
        return true;
    }

    /// <summary>A registration never confirmed within a day frees its name and address.</summary>
    private async Task ForgetUnconfirmedAsync(PanelUser? user, CancellationToken ct)
    {
        if (user is { Pending: true, EmailVerifiedAt: null } && clock.GetUtcNow() - user.CreatedAt > UnconfirmedKeptFor)
        {
            await users.DeleteAsync(user.Id, ct);
            await users.AuditAsync(null, "user.register_expired", user.Id, PanelAccounts.Empty, ct);
        }
    }

    private Task<PanelUser?> FindByLoginAsync(string? login, CancellationToken ct)
    {
        login = (login ?? "").Trim();
        return login.Contains('@') ? users.FindByEmailAsync(login, ct) : users.FindByUsernameAsync(PanelAccounts.NormalizeUsername(login), ct);
    }
}

/// <summary>
/// The authenticator app (RFC 6238, 30 seconds, six digits; DECISIONS v1.9 T-23): a person turns
/// it on from «حسابي» and gets ten one-time recovery codes; from then on a sign-in needs the code
/// too. A manager can turn it off for someone who lost their phone.
/// </summary>
public sealed class PanelTwoFactor(
    IPanelUserStore users, IPanelCodeStore codes, IPanelSecrets secrets, IPanelCrypto crypto, IPanelMailer mailer,
    PanelAccounts accounts, TimeProvider clock)
{
    public const int RecoveryCodeCount = 10;
    public const string Issuer = "مكتب سعد فتحي سعد";

    public const string AlreadyOn = "already_enabled";
    public const string NotOn = "not_enabled";
    public const string NoSetup = "no_setup";

    public sealed record Status(bool Enabled, int RecoveryCodesLeft);

    public sealed record Setup(string Secret, string Uri);

    public async Task<Status> StatusAsync(PanelUser user, CancellationToken ct) =>
        new(user.TotpEnabled, user.TotpEnabled ? await codes.RecoveryCodesLeftAsync(user.Id, ct) : 0);

    /// <summary>A new secret, kept (encrypted) until the first right code turns it on.</summary>
    public async Task<PanelResult<Setup>> SetupAsync(Guid userId, CancellationToken ct)
    {
        if (!crypto.Configured)
        {
            return new(PanelOutcome.Unavailable, Error: PanelSignup.NotConfigured);
        }

        var user = await users.FindAsync(userId, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (user.TotpEnabled)
        {
            return new(PanelOutcome.Conflict, Error: AlreadyOn);
        }

        var secret = crypto.NewTotpSecret();
        user.TotpSecret = crypto.Protect(secret);
        await users.UpdateAsync(user, ct);
        var label = Uri.EscapeDataString($"{Issuer}:{user.Username}");
        var uri = $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";
        return PanelResult<Setup>.Ok(new(secret, uri));
    }

    /// <summary>On, with ten recovery codes shown once. Every other session ends.</summary>
    public async Task<PanelResult<IReadOnlyList<string>>> EnableAsync(Guid userId, string? code, CancellationToken ct)
    {
        var user = await users.FindAsync(userId, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (user.TotpEnabled)
        {
            return new(PanelOutcome.Conflict, Error: AlreadyOn);
        }

        if (user.TotpSecret is null || !crypto.Configured)
        {
            return new(PanelOutcome.Conflict, Error: NoSetup);
        }

        if (!await AppCodeAsync(user, code, ct))
        {
            return new(PanelOutcome.Invalid, Error: PanelSignup.InvalidCode);
        }

        user.TotpEnabled = true;
        user.SecurityStamp = secrets.NewStamp();
        await users.UpdateAsync(user, ct);
        var recovery = await NewRecoveryCodesAsync(user, ct);
        await users.AuditAsync(user.Id, "two_factor.enable", user.Id, PanelAccounts.Empty, ct);
        await mailer.SendAsync(PanelMails.TwoFactorOn(user), ct);
        return PanelResult<IReadOnlyList<string>>.Ok(recovery);
    }

    /// <summary>Off again: the password and a code (from the app or a recovery code) both.</summary>
    public async Task<PanelResult<bool>> DisableAsync(Guid userId, string? password, string? code, CancellationToken ct)
    {
        var user = await users.FindAsync(userId, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (!user.TotpEnabled)
        {
            return new(PanelOutcome.Conflict, Error: NotOn);
        }

        if (string.IsNullOrEmpty(password) || !secrets.Verify(user.PasswordHash, password).Ok)
        {
            return new(PanelOutcome.Invalid, Error: PanelAccounts.WrongCurrentPassword);
        }

        if (!await AnyCodeAsync(user, code, ct))
        {
            return new(PanelOutcome.Invalid, Error: PanelSignup.InvalidCode);
        }

        await TurnOffAsync(user, ct);
        await users.AuditAsync(user.Id, "two_factor.disable", user.Id, PanelAccounts.Empty, ct);
        await mailer.SendAsync(PanelMails.TwoFactorOff(user), ct);
        return PanelResult<bool>.Ok(true);
    }

    /// <summary>Ten new recovery codes; the old ones stop working.</summary>
    public async Task<PanelResult<IReadOnlyList<string>>> RenewRecoveryCodesAsync(Guid userId, string? code, CancellationToken ct)
    {
        var user = await users.FindAsync(userId, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (!user.TotpEnabled)
        {
            return new(PanelOutcome.Conflict, Error: NotOn);
        }

        if (!await AppCodeAsync(user, code, ct))
        {
            return new(PanelOutcome.Invalid, Error: PanelSignup.InvalidCode);
        }

        var recovery = await NewRecoveryCodesAsync(user, ct);
        await users.AuditAsync(user.Id, "two_factor.recovery_codes", user.Id, PanelAccounts.Empty, ct);
        return PanelResult<IReadOnlyList<string>>.Ok(recovery);
    }

    /// <summary>The manager turns it off for someone who lost their phone (not for themself).</summary>
    public async Task<PanelResult<PanelUser>> ResetAsync(Guid? by, Guid id, CancellationToken ct)
    {
        if (by == id)
        {
            return new(PanelOutcome.Conflict, Error: PanelAccounts.CannotChangeSelf);
        }

        var user = await users.FindAsync(id, ct);
        if (user is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (!user.TotpEnabled)
        {
            return new(PanelOutcome.Conflict, Error: NotOn);
        }

        await TurnOffAsync(user, ct);
        await users.AuditAsync(by, "two_factor.reset", user.Id, PanelAccounts.Empty, ct);
        await mailer.SendAsync(PanelMails.TwoFactorOff(user), ct);
        return PanelResult<PanelUser>.Ok(user);
    }

    /// <summary>
    /// The second step of a sign-in: the password was right a moment ago (same security stamp).
    /// A wrong code counts towards the lockout like a wrong password.
    /// </summary>
    public async Task<PanelResult<PanelUser>> SecondStepAsync(Guid userId, string stamp, string? code, CancellationToken ct)
    {
        var user = await users.FindAsync(userId, ct);
        if (user is not { Active: true, TotpEnabled: true } || user.SecurityStamp != stamp || user.IsLocked(clock.GetUtcNow()))
        {
            return new(PanelOutcome.Forbidden, Error: PanelSignup.InvalidCode);
        }

        var recovery = IsRecoveryCode(code);
        if (!await AnyCodeAsync(user, code, ct))
        {
            await accounts.FailedSecondStepAsync(user, ct);
            return new(PanelOutcome.Forbidden, Error: PanelSignup.InvalidCode);
        }

        await accounts.CompleteSignInAsync(user, recovery ? "recovery_code" : "authenticator", ct);
        return PanelResult<PanelUser>.Ok(user);
    }

    private async Task TurnOffAsync(PanelUser user, CancellationToken ct)
    {
        user.TotpEnabled = false;
        user.TotpSecret = null;
        user.TotpLastStep = null;
        user.SecurityStamp = secrets.NewStamp();
        await users.UpdateAsync(user, ct);
        await codes.ReplaceRecoveryCodesAsync(user.Id, [], ct);
    }

    private async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(PanelUser user, CancellationToken ct)
    {
        var plain = Enumerable.Range(0, RecoveryCodeCount).Select(_ => crypto.NewRecoveryCode()).ToList();
        await codes.ReplaceRecoveryCodesAsync(user.Id, plain.Select(c => crypto.HashCode(user.Id, "recovery", c)).ToList(), ct);
        return plain;
    }

    private static bool IsRecoveryCode(string? code) => (code ?? "").Trim().Contains('-');

    private Task<bool> AnyCodeAsync(PanelUser user, string? code, CancellationToken ct) =>
        IsRecoveryCode(code)
            ? codes.UseRecoveryCodeAsync(user.Id, crypto.HashCode(user.Id, "recovery", code!.Trim().ToLowerInvariant()), clock.GetUtcNow(), ct)
            : AppCodeAsync(user, code, ct);

    /// <summary>A code from the app, never the same step twice (a code seen over a shoulder is spent).</summary>
    private async Task<bool> AppCodeAsync(PanelUser user, string? code, CancellationToken ct)
    {
        code = (code ?? "").Replace(" ", "", StringComparison.Ordinal).Trim();
        if (user.TotpSecret is null || code.Length != 6 || !crypto.Configured || crypto.Unprotect(user.TotpSecret) is not { } secret)
        {
            return false;
        }

        var step = crypto.MatchTotp(secret, code, clock.GetUtcNow());
        if (step is null || step <= user.TotpLastStep)
        {
            return false;
        }

        user.TotpLastStep = step;
        await users.UpdateAsync(user, ct);
        return true;
    }
}
