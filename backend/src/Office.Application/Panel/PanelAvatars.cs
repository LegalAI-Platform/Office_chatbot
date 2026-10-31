namespace Office.Application.Panel;

/// <summary>An account's photo (migration 0040): the bytes and their checked type.</summary>
public sealed record PanelAvatar(byte[] Bytes, string Type);

/// <summary>
/// Each account sets its own photo from «حسابي» (the owner: «أنا ممكن أحط صورتي عادي»). The panel makes
/// it a 256×256 JPEG before upload; the API still takes only JPEG, PNG or WebP up to 300 KB, and only
/// when the bytes really are that format (an HTML or SVG page sent as image/png is refused).
/// </summary>
public sealed class PanelAvatars(IPanelUserStore store, TimeProvider clock)
{
    public const int MaxBytes = 300 * 1024;

    public const string WrongType = "avatar_type";
    public const string TooLarge = "avatar_too_large";
    public const string NotAnImage = "avatar_not_image";

    public static readonly IReadOnlyList<string> Types = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>
    /// <paramref name="bytes"/> may be longer than <see cref="MaxBytes"/> (the caller reads one byte more to
    /// know): that is refused like any other photo that is too large.
    /// </summary>
    public async Task<PanelResult<DateTimeOffset>> SetAsync(Guid userId, string? contentType, byte[] bytes, CancellationToken ct)
    {
        var type = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!Types.Contains(type))
        {
            return new(PanelOutcome.Invalid, Error: WrongType);
        }

        if (bytes.Length > MaxBytes)
        {
            return new(PanelOutcome.Invalid, Error: TooLarge);
        }

        if (Sniff(bytes) != type)
        {
            return new(PanelOutcome.Invalid, Error: NotAnImage);
        }

        // Microseconds, like the column: the time the panel sees in /auth/me is the one stored.
        var now = clock.GetUtcNow();
        var at = now.AddTicks(-(now.Ticks % 10));
        await store.SetAvatarAsync(userId, new PanelAvatar(bytes, type), at, ct);
        await store.AuditAsync(userId, "avatar.set", userId,
            new Dictionary<string, string?> { ["type"] = type, ["bytes"] = bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);
        return PanelResult<DateTimeOffset>.Ok(at);
    }

    public async Task RemoveAsync(Guid userId, CancellationToken ct)
    {
        await store.SetAvatarAsync(userId, null, null, ct);
        await store.AuditAsync(userId, "avatar.remove", userId, PanelAccounts.Empty, ct);
    }

    public Task<PanelAvatar?> FindAsync(Guid userId, CancellationToken ct) => store.AvatarAsync(userId, ct);

    /// <summary>The format the first bytes say (magic numbers), or null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> b) =>
        b is [0xFF, 0xD8, 0xFF, ..] ? "image/jpeg"
        : b is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] ? "image/png"
        : b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;
}
