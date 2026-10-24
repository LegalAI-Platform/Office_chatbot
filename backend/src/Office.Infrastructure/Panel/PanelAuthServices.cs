using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Office.Application.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

/// <summary>
/// Keyed by PANEL_SECRET_KEY (64 hex characters, gen-secrets): HKDF gives one key to hash the
/// e-mail and recovery codes (HMAC-SHA256) and another to encrypt the authenticator secret
/// (AES-GCM). Without the key, sign-up and 2FA answer «not configured»; sign-in still works.
/// </summary>
public sealed class PanelCrypto : IPanelCrypto
{
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    // No 0/o, 1/l/i: written down on paper and typed back.
    private const string RecoveryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    private readonly byte[]? _hashKey;
    private readonly byte[]? _encKey;

    public PanelCrypto(IConfiguration config)
    {
        var hex = config["Panel:SecretKey"];
        if (string.IsNullOrWhiteSpace(hex))
        {
            return;
        }

        var key = Convert.FromHexString(hex.Trim());
        if (key.Length < 32)
        {
            throw new InvalidOperationException("Panel:SecretKey (PANEL_SECRET_KEY) must be at least 64 hex characters.");
        }

        _hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: "panel-codes"u8.ToArray());
        _encKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: "panel-totp"u8.ToArray());
    }

    public bool Configured => _hashKey is not null;

    public string NewEmailCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public string HashCode(Guid userId, string purpose, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Key(_hashKey), Encoding.UTF8.GetBytes($"{userId:N}|{purpose}|{code}")));

    public bool SameHash(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>xxxx-xxxx, about 40 bits; kept only as a keyed hash.</summary>
    public string NewRecoveryCode() =>
        RandomNumberGenerator.GetString(RecoveryAlphabet, 4) + "-" + RandomNumberGenerator.GetString(RecoveryAlphabet, 4);

    public string NewTotpSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public string Protect(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key(_encKey), 16);
        aes.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(string protectedText)
    {
        try
        {
            var all = Convert.FromBase64String(protectedText);
            var plain = new byte[all.Length - 28];
            using var aes = new AesGcm(Key(_encKey), 16);
            aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            return null; // a different PANEL_SECRET_KEY: the person sets the authenticator up again
        }
    }

    public long? MatchTotp(string base32Secret, string code, DateTimeOffset now)
    {
        var key = FromBase32(base32Secret);
        var step = now.ToUnixTimeSeconds() / 30;
        for (var s = step - 1; s <= step + 1; s++)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Totp(key, s)), Encoding.ASCII.GetBytes(code)))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>RFC 6238 (HMAC-SHA1, six digits): what Google and Microsoft Authenticator show.</summary>
    public static string Totp(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Base32[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('=').ToUpperInvariant())
        {
            var v = Base32.IndexOf(c, StringComparison.Ordinal);
            if (v < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }

    private static byte[] Key(byte[]? key) =>
        key ?? throw new InvalidOperationException("PANEL_SECRET_KEY is not set.");
}

/// <summary>
/// The no-reply sender: the owner's SMTP server (SMTP_HOST, SMTP_PORT, SMTP_USERNAME, SMTP_PASSWORD,
/// SMTP_FROM, SMTP_FROM_NAME). Port 587 with STARTTLS (SMTP_SSL=true); System.Net.Mail has no
/// implicit TLS, so not port 465. Failures are logged without the mail's content.
/// </summary>
public sealed class SmtpPanelMailer(IConfiguration config, ILogger<SmtpPanelMailer> log) : IPanelMailer
{
    private string? Host => config["Mail:Host"] is { Length: > 0 } h ? h : null;

    private string? From => config["Mail:From"] is { Length: > 0 } f ? f : null;

    public bool Configured => Host is not null && From is not null;

    public async Task<bool> SendAsync(PanelMail mail, CancellationToken ct)
    {
        if (!Configured)
        {
            return false;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(From!, config["Mail:FromName"] is { Length: > 0 } n ? n : PanelMails.Office, Encoding.UTF8),
                Subject = mail.Subject,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                Body = mail.Text,
                IsBodyHtml = false,
            };
            message.To.Add(new MailAddress(mail.To));
            message.Headers.Add("Auto-Submitted", "auto-generated");
            var html = AlternateView.CreateAlternateViewFromString(mail.Html, Encoding.UTF8, MediaTypeNames.Text.Html);
            if (mail.Html.Contains("cid:" + PanelMails.SealCid, StringComparison.Ordinal) && Seal() is { } seal)
            {
                html.LinkedResources.Add(new LinkedResource(new MemoryStream(seal), "image/png") { ContentId = PanelMails.SealCid });
            }

            message.AlternateViews.Add(html);

            using var client = new SmtpClient(Host, config.GetValue("Mail:Port", 587))
            {
                EnableSsl = config.GetValue("Mail:Ssl", true),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20_000,
            };
            if (config["Mail:Username"] is { Length: > 0 } user)
            {
                client.Credentials = new NetworkCredential(user, config["Mail:Password"]);
            }

            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception e) when (e is SmtpException or InvalidOperationException or FormatException or IOException)
        {
            // The server's own words (e.g. "5.7.8 Username and Password not accepted"), never the mail.
            var reason = string.Join(" <- ", Chain(e).Select(x => $"{x.GetType().Name}: {x.Message}"));
            log.LogWarning("Panel mail not sent: {Reason}", reason);
            return false;
        }
    }

    private static byte[]? _seal;

    /// <summary>The office seal (Panel/Mail/office-seal.png, embedded in this assembly).</summary>
    private static byte[]? Seal()
    {
        if (_seal is null)
        {
            using var s = typeof(SmtpPanelMailer).Assembly.GetManifestResourceStream("Office.Infrastructure.Panel.Mail.office-seal.png");
            if (s is null)
            {
                return null;
            }

            using var m = new MemoryStream();
            s.CopyTo(m);
            _seal = m.ToArray();
        }

        return _seal;
    }

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            yield return x;
        }
    }
}

/// <summary>hazem.panel_codes and hazem.panel_recovery_codes (migration 0010).</summary>
public sealed class PanelCodeStore(HazemDbContext db) : IPanelCodeStore
{
    public Task AddAsync(Guid userId, string purpose, string codeHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.panel_codes (user_id, purpose, code_hash, created_at, expires_at)
            VALUES ({userId}, {purpose}, {codeHash}, {createdAt.ToUniversalTime()}, {expiresAt.ToUniversalTime()})
            """, ct);

    public async Task<PanelCode?> LatestAsync(Guid userId, string purpose, CancellationToken ct)
    {
        var r = await db.PanelCodes.AsNoTracking().Where(c => c.UserId == userId && c.Purpose == purpose)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        return r is null || r.UsedAt is not null ? null : new PanelCode(r.Id, r.CodeHash, r.ExpiresAt, r.Attempts, r.CreatedAt);
    }

    public Task<int> SentSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken ct) =>
        db.PanelCodes.CountAsync(c => c.UserId == userId && c.Purpose == purpose && c.CreatedAt > since, ct);

    public Task FailAsync(Guid codeId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.panel_codes SET attempts = attempts + 1 WHERE id = {codeId}", ct);

    public Task UseAsync(Guid codeId, DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.panel_codes SET used_at = {at.ToUniversalTime()} WHERE id = {codeId}", ct);

    public async Task ReplaceRecoveryCodesAsync(Guid userId, IReadOnlyList<string> hashes, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM hazem.panel_recovery_codes WHERE user_id = {userId}", ct);
        foreach (var hash in hashes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO hazem.panel_recovery_codes (user_id, code_hash) VALUES ({userId}, {hash})", ct);
        }
    }

    public async Task<bool> UseRecoveryCodeAsync(Guid userId, string hash, DateTimeOffset at, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.panel_recovery_codes SET used_at = {at.ToUniversalTime()}
             WHERE user_id = {userId} AND code_hash = {hash} AND used_at IS NULL
            """, ct) == 1;

    public Task<int> RecoveryCodesLeftAsync(Guid userId, CancellationToken ct) =>
        db.PanelRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null, ct);
}
