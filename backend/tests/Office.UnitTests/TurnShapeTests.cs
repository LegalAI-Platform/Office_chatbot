using Office.Core.Hazem;

namespace Office.UnitTests;

/// <summary>TASK 03: the turn shape with media, transcripts and the other message types.</summary>
public sealed class TurnShapeTests
{
    private const string Recipient = "201000000041";

    private static TurnMessage Text(string wamid = "wamid.t", string text = "مرحبا") =>
        new(wamid, "text", text, DateTimeOffset.Parse("2026-10-20T10:00:00+03:00"), null);

    private static TurnMessage Image(TurnMedia? media, string? caption = null) =>
        new("wamid.i", "image", null, DateTimeOffset.Parse("2026-10-20T10:00:01+03:00"), null, Caption: caption, Media: media);

    private static TurnMessage Voice(TurnTranscript? transcript) =>
        new("wamid.v", "audio", null, DateTimeOffset.Parse("2026-10-20T10:00:02+03:00"), null,
            Media: new TurnMedia("stored", "office-media", "wa/2026/10/abc.ogg", "audio/ogg", 2048, "aa", Voice: true),
            Transcript: transcript);

    private static TurnMedia StoredImage => new("stored", "office-sensitive", "wa/2026/10/0b6c.jpg", "image/jpeg", 1234, "aa");

    private static TurnRequest Turn(params TurnMessage[] messages) => new("wt:1", Recipient, "Client", messages);

    private static HazemSettings Echo => new(new Dictionary<string, string>
    {
        [HazemSettings.EchoEnabledKey] = "true",
        [HazemSettings.EchoRecipientsKey] = Recipient,
    });

    [Fact]
    public void Old_text_shape_is_still_valid() => Assert.Empty(TurnRules.Validate(Turn(Text())));

    [Fact]
    public void Every_message_type_in_the_contract_is_accepted() => Assert.Empty(TurnRules.Validate(Turn(
        Text(),
        Image(StoredImage, caption: "بطاقتي"),
        Voice(new TurnTranscript("done", "نص", "ar")),
        new TurnMessage("wamid.d", "document", null, null, null, Media: new TurnMedia("too_large", MimeType: "application/pdf", SizeBytes: 73400320, Filename: "عقد.pdf")),
        new TurnMessage("wamid.l", "location", null, null, null, Location: new TurnLocation(30.0m, 31.2m)),
        new TurnMessage("wamid.r", "reaction", null, null, null, Reaction: new TurnReaction("👍", "wamid.t")),
        new TurnMessage("wamid.n", "interactive", null, null, null, Interactive: new TurnInteractive("button_reply", "yes", "نعم")),
        new TurnMessage("wamid.b", "button", null, null, null, Button: new TurnButton("CONFIRM", "تأكيد")),
        new TurnMessage("wamid.u", "unsupported", null, null, null, OriginalType: "contacts"))));

    [Fact]
    public void Unknown_type_is_refused() =>
        Assert.Contains("messages[0].type", TurnRules.Validate(Turn(new TurnMessage("wamid.x", "poll", null, null, null))).Keys);

    [Fact]
    public void Media_message_without_media_is_refused() =>
        Assert.Contains("messages[0].media", TurnRules.Validate(Turn(Image(null))).Keys);

    [Fact]
    public void Text_message_with_media_is_refused() =>
        Assert.Contains("messages[0].media", TurnRules.Validate(Turn(Text() with { Media = StoredImage })).Keys);

    [Theory]
    [InlineData("downloading")]
    [InlineData("pending")]
    [InlineData("")]
    public void Media_status_must_be_final(string status) =>
        Assert.Contains("messages[0].media.status", TurnRules.Validate(Turn(Image(StoredImage with { Status = status }))).Keys);

    [Fact]
    public void Bucket_outside_the_list_is_refused() =>
        Assert.Contains("messages[0].media.bucket", TurnRules.Validate(Turn(Image(StoredImage with { Bucket = "office-backups" }))).Keys);

    [Theory]
    [InlineData("2026/10/x.jpg")]
    [InlineData("wa/26/10/x.jpg")]
    [InlineData("wa/2026/10/../x.jpg")]
    [InlineData("wa/2026/10/sub/x.jpg")]
    public void Key_must_be_wa_yyyy_mm_name(string key) =>
        Assert.Contains("messages[0].media.key", TurnRules.Validate(Turn(Image(StoredImage with { Key = key }))).Keys);

    [Fact]
    public void Bucket_and_key_only_when_stored() =>
        Assert.Contains("messages[0].media.key", TurnRules.Validate(Turn(Image(StoredImage with { Status = "failed" }))).Keys);

    [Theory]
    [InlineData("key", "https://garage:3900/office-sensitive/x.jpg")]
    [InlineData("filename", "http://evil/a.pdf")]
    [InlineData("mime_type", "s3://x")]
    public void No_url_in_a_file_reference(string field, string value)
    {
        var media = field switch
        {
            "key" => StoredImage with { Key = value },
            "filename" => StoredImage with { Filename = value },
            _ => StoredImage with { MimeType = value },
        };
        Assert.Contains($"messages[0].media.{field}", TurnRules.Validate(Turn(Image(media))).Keys);
    }

    [Fact]
    public void A_link_the_client_typed_is_just_text() =>
        Assert.Empty(TurnRules.Validate(Turn(Text(text: "شوف https://example.com"))));

    [Fact]
    public void Caption_over_4096_is_refused() =>
        Assert.Contains("messages[0].caption", TurnRules.Validate(Turn(Image(StoredImage, caption: new string('x', 4097)))).Keys);

    [Fact]
    public void Transcript_up_to_16000_is_accepted_and_longer_is_refused()
    {
        Assert.Empty(TurnRules.Validate(Turn(Voice(new TurnTranscript("done", new string('x', 16000), "ar")))));
        Assert.Contains("messages[0].transcript.text",
            TurnRules.Validate(Turn(Voice(new TurnTranscript("done", new string('x', 16001), "ar")))).Keys);
    }

    [Fact]
    public void Transcript_only_on_audio() =>
        Assert.Contains("messages[0].transcript",
            TurnRules.Validate(Turn(Text() with { Transcript = new TurnTranscript("done", "x", "ar") })).Keys);

    [Fact]
    public void Echo_for_voice_quotes_the_first_60_characters()
    {
        var text = string.Concat(Enumerable.Repeat("أ", 80));
        var action = Assert.Single(TurnResponder.Decide(Turn(Text(), Voice(new TurnTranscript("done", text, "ar"))), Echo).Actions);
        Assert.Equal(TurnResponder.EchoVoicePrefix + text[..60], action.Text);
        Assert.Equal("wamid.v", action.ContextWamid);
    }

    [Fact]
    public void Echo_for_media_names_the_type_once_per_turn()
    {
        var action = Assert.Single(TurnResponder.Decide(Turn(Text("wamid.1"), Text("wamid.2"), Image(StoredImage)), Echo).Actions);
        Assert.Equal("تم استلام الصورة (اختبار) ✅", action.Text);
        Assert.Equal("hazem:wt:1:0", action.IdempotencyKey);
    }

    [Fact]
    public void Echo_for_a_failed_transcript_falls_back_to_the_media_name()
    {
        var action = Assert.Single(TurnResponder.Decide(Turn(Voice(new TurnTranscript("failed", null, "ar"))), Echo).Actions);
        Assert.Equal("تم استلام الرسالة الصوتية (اختبار) ✅", action.Text);
    }

    [Fact]
    public void Without_echo_media_gets_no_answer() =>
        Assert.Empty(TurnResponder.Decide(Turn(Image(StoredImage)), new HazemSettings(new Dictionary<string, string>())).Actions);
}
