using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Infrastructure;
using Office.Infrastructure.Ai;

namespace Office.UnitTests;

/// <summary>T-07/T-09: what reaches the AI, the closed lists, the fallback, and the OpenAI call itself (faked).</summary>
public sealed class UnderstandingTests
{
    private static readonly HazemSettings Settings = new(new Dictionary<string, string>());

    private static Turn TurnOf(params IncomingMessage[] m) => new("wt:u", "201000000041", null, m);

    private static IncomingMessage Text(string t) => new("wamid.t", MessageType.Text, t, null, null);

    // ---------- the text the AI sees ----------

    [Fact]
    public void No_media_field_ever_reaches_the_ai_text()
    {
        var image = new IncomingMessage("wamid.i", MessageType.Image, null, null, null, Caption: "دي البطاقة",
            Media: new MediaRef(MediaStatus.Stored, "office-sensitive", "wa/2026/10/KEY-SECRET.jpg", "image/jpeg", 1234, "SHA-SECRET", "FILENAME-SECRET.jpg"));
        var voice = new IncomingMessage("wamid.v", MessageType.Audio, null, null, null,
            Media: new MediaRef(MediaStatus.Stored, "office-media", "wa/2026/10/VOICE-KEY.ogg", "audio/ogg"),
            Transcript: new Transcript(Transcript.Done, "عايز أحجز", "ar"));
        var location = new IncomingMessage("wamid.l", MessageType.Location, null, null, null, Location: new Location(30.1m, 31.2m, "LOCATION-SECRET"));

        var text = AiInput.Build(TurnOf(Text("السلام عليكم"), image, voice, location))!;

        Assert.Equal("السلام عليكم\nدي البطاقة\nعايز أحجز", text);
        foreach (var leak in new[] { "office-", "KEY-SECRET", "SHA-SECRET", "FILENAME-SECRET", "image/jpeg", "audio/ogg", "VOICE-KEY", "LOCATION-SECRET", "30.1", "wa/2026" })
        {
            Assert.DoesNotContain(leak, text);
        }
    }

    [Fact]
    public void A_photo_without_words_never_reaches_the_ai() =>
        Assert.Null(AiInput.Build(TurnOf(new IncomingMessage("wamid.i", MessageType.Image, null, null, null,
            Media: new MediaRef(MediaStatus.Stored, "office-sensitive", "wa/2026/10/a.jpg")))));

    [Fact]
    public void A_failed_transcript_is_not_sent() =>
        Assert.Null(AiInput.Build(TurnOf(new IncomingMessage("wamid.v", MessageType.Audio, null, null, null,
            Media: new MediaRef(MediaStatus.Stored), Transcript: new Transcript(Transcript.Failed, "partial words", "ar")))));

    [Theory]
    [InlineData("كلمني على 01012345678 بكرة", "كلمني على [رقم] بكرة")]
    [InlineData("رقمي ٠١٠١٢٣٤٥٦٧٨", "رقمي [رقم]")]
    [InlineData("رقمي +20 101 234 5678", "رقمي [رقم]")]
    [InlineData("الرقم القومي 29801011234567", "الرقم القومي [رقم_قومي]")]
    [InlineData("ابعت على a.b@example.com", "ابعت على [ايميل]")]
    [InlineData("عندي 3 قضايا من 2019", "عندي 3 قضايا من 2019")]
    public void Phones_ids_and_emails_are_replaced_before_the_ai(string input, string expected) =>
        Assert.Equal(expected, AiInput.Redact(input));

    // ---------- closed lists and fallback ----------

    private static ClassifierOutcome Answer(string intent, double confidence = 0.9, string? service = null, string? mode = "unknown", string? name = null) =>
        new(new RawClassification(intent, service, mode, name, confidence), null, "m", "classify-v1", 10);

    [Fact]
    public void A_valid_answer_is_kept()
    {
        var c = Classification.From(Answer("service_by_name", service: "document_review", mode: "office"), Settings);
        Assert.False(c.IsFallback);
        Assert.Equal(Intent.ServiceByName, c.Intent);
        Assert.Equal(Service.DocumentReview, c.Service);
        Assert.Equal(ConsultationMode.Office, c.Mode);
    }

    [Theory]
    [InlineData("book_now", null, 0.9, Classification.UnknownValue)]      // intent outside the list
    [InlineData("ServiceByName", null, 0.9, Classification.UnknownValue)] // not the exact code
    [InlineData("service_by_name", "divorce_lawsuit", 0.9, Classification.UnknownValue)]
    [InlineData("consultation_request", null, 1.5, Classification.UnknownValue)]
    [InlineData("consultation_request", null, 0.59, Classification.LowConfidence)]
    public void Anything_outside_the_lists_or_unsure_becomes_other(string intent, string? service, double confidence, string reason)
    {
        var c = Classification.From(Answer(intent, confidence, service), Settings);
        Assert.Equal(Intent.Other, c.Intent);
        Assert.Equal(reason, c.FallbackReason);
    }

    [Theory]
    [InlineData(ClassifierOutcome.NoKey)]
    [InlineData(ClassifierOutcome.Timeout)]
    [InlineData(ClassifierOutcome.Error)]
    [InlineData(ClassifierOutcome.InvalidOutput)]
    public void A_failed_call_becomes_other_with_its_reason(string reason)
    {
        var c = Classification.From(new ClassifierOutcome(null, reason, "m", "classify-v1", 5), Settings);
        Assert.Equal(Intent.Other, c.Intent);
        Assert.Equal(reason, c.FallbackReason);
    }

    [Fact]
    public void Min_confidence_comes_from_the_settings() =>
        Assert.False(Classification.From(Answer("greeting", 0.5), new HazemSettings(new Dictionary<string, string> { [Classification.MinConfidenceKey] = "0.4" })).IsFallback);

    // ---------- the OpenAI call (faked HTTP) ----------

    private sealed class FakeOpenAi(Func<int, HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(ct)));
            return await answer(Calls.Count, request);
        }
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = content } }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 321, ["completion_tokens"] = 12 },
        }.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private const string GoodContent = """{"intent":"legal_question","service":null,"consultation_mode":"unknown","full_name":null,"confidence":0.93}""";

    private static (OpenAiIntentClassifier Classifier, FakeOpenAi Http) Make(Func<int, HttpRequestMessage, Task<HttpResponseMessage>> answer,
        string key = "sk-REAL-KEY-SECRET", string env = "test", TimeSpan? budget = null)
    {
        var http = new FakeOpenAi(answer);
        var options = Options.Create(new HazemOptions { OpenAiApiKey = key });
        return (new OpenAiIntentClassifier(new HttpClient(http), options, new Env(env), TimeProvider.System)
        {
            Budget = budget ?? OpenAiIntentClassifier.DefaultBudget,
        }, http);
    }

    [Fact]
    public async Task Structured_output_request_with_temperature_0_and_the_prompt_version()
    {
        var (classifier, http) = Make((_, _) => Task.FromResult(Ok(GoodContent)));
        var outcome = await classifier.ClassifyAsync("ينفع أطلق؟", Settings, CancellationToken.None);

        Assert.Equal("legal_question", outcome.Answer?.Intent);
        Assert.Equal((321, 12, "classify-v1"), (outcome.InputTokens!.Value, outcome.OutputTokens!.Value, outcome.PromptVersion));
        var body = JsonNode.Parse(Assert.Single(http.Calls).Body)!;
        Assert.Equal(0, (int)body["temperature"]!);
        Assert.Equal("json_schema", (string?)body["response_format"]!["type"]);
        Assert.True((bool)body["response_format"]!["json_schema"]!["strict"]!);
        Assert.Equal("https://api.openai.com/v1/chat/completions", http.Calls[0].Url);
    }

    [Fact]
    public void The_schema_lists_exactly_the_closed_intents()
    {
        var intents = OpenAiIntentClassifier.Schema["properties"]!["intent"]!["enum"]!.AsArray().Select(n => (string?)n).ToList();
        Assert.Equal(Enum.GetValues<Intent>().Select(Classification.Code), intents);
        Assert.False((bool)OpenAiIntentClassifier.Schema["additionalProperties"]!);
    }

    [Fact]
    public async Task Empty_key_means_no_call_at_all()
    {
        var (classifier, http) = Make((_, _) => Task.FromResult(Ok(GoodContent)), key: "");
        Assert.Equal(ClassifierOutcome.NoKey, (await classifier.ClassifyAsync("x", Settings, CancellationToken.None)).FailureReason);
        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task One_retry_for_a_temporary_error_then_it_gives_up()
    {
        var (once, h1) = Make((n, _) => Task.FromResult(n == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(GoodContent)));
        Assert.Equal("legal_question", (await once.ClassifyAsync("x", Settings, CancellationToken.None)).Answer?.Intent);
        Assert.Equal(2, h1.Calls.Count);

        var (always, h2) = Make((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Assert.Equal(ClassifierOutcome.Error, (await always.ClassifyAsync("x", Settings, CancellationToken.None)).FailureReason);
        Assert.Equal(2, h2.Calls.Count);
    }

    [Fact]
    public async Task A_slow_answer_is_a_timeout()
    {
        var (classifier, _) = Make(async (_, r) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
            return Ok(GoodContent);
        }, budget: TimeSpan.FromMilliseconds(200));
        Assert.Equal(ClassifierOutcome.Timeout, (await classifier.ClassifyAsync("x", Settings, CancellationToken.None)).FailureReason);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"intent":5,"confidence":0.9}""")]
    [InlineData("""{"service":null}""")]
    [InlineData("")]
    public async Task Output_that_is_not_the_schema_is_invalid(string content)
    {
        var (classifier, _) = Make((_, _) => Task.FromResult(Ok(content)));
        Assert.Equal(ClassifierOutcome.InvalidOutput, (await classifier.ClassifyAsync("x", Settings, CancellationToken.None)).FailureReason);
    }

    private static readonly HazemSettings Stub = new(new Dictionary<string, string>
    {
        [OpenAiIntentClassifier.StubEnabledKey] = "true",
        [OpenAiIntentClassifier.StubBaseUrlKey] = "http://meta-stub:8080/v1",
    });

    [Fact]
    public async Task The_stub_gets_a_dummy_key_never_the_real_one()
    {
        var (classifier, http) = Make((_, _) => Task.FromResult(Ok(GoodContent)));
        await classifier.ClassifyAsync("x", Stub, CancellationToken.None);
        Assert.Equal("http://meta-stub:8080/v1/chat/completions", http.Calls[0].Url);
        Assert.Equal("Bearer " + OpenAiIntentClassifier.StubKey, http.Calls[0].Auth);
        Assert.DoesNotContain("REAL-KEY", string.Join("|", http.Calls));
    }

    [Fact]
    public async Task The_stub_setting_is_ignored_outside_the_test_environment()
    {
        var (classifier, http) = Make((_, _) => Task.FromResult(Ok(GoodContent)), env: "production");
        await classifier.ClassifyAsync("x", Stub, CancellationToken.None);
        Assert.StartsWith("https://api.openai.com/v1", http.Calls[0].Url);
    }
}

/// <summary>Approved texts: slots, part-5 placeholders, B-21 splitting, fallback and inactive texts.</summary>
public sealed class ApprovedTextTests
{
    private static ApprovedText T(string key, string text, bool active = true, int max = 300) => new(key, 1, text, max, active);

    [Fact]
    public void System_slots_are_filled_and_missing_office_values_block_the_text()
    {
        Assert.Equal("استلمت الصورة ✅", TextRenderer.Render(T("m", "استلمت {{media}} ✅"), new Dictionary<string, string> { ["media"] = "الصورة" }).Text);
        Assert.Equal(TextRenderer.MissingValue, TextRenderer.Render(T("a", "العنوان: {{OFFICE_ADDRESS}}"), null).Reason);
        Assert.Equal(TextRenderer.MissingValue, TextRenderer.Render(T("b", "وهيرد عليك [المسؤول]."), null).Reason);
        Assert.Equal(TextRenderer.MissingValue, TextRenderer.Render(T("c", "استلمت {{media}}"), null).Reason);
    }

    [Fact]
    public void A_long_text_is_split_at_the_end_of_a_sentence()
    {
        var first = new string('أ', 200) + ".";
        var second = new string('ب', 150) + ".";
        var parts = TextRenderer.Split(first + " " + second, 300);
        Assert.Equal([first, second], parts);
        Assert.All(TextRenderer.Split(string.Join(' ', Enumerable.Repeat("كلمة", 200)), 300), p => Assert.True(p.Length <= 300));
    }

    [Fact]
    public void Inactive_text_is_never_sent_and_the_fallback_goes_instead_with_the_reason()
    {
        var texts = new Dictionary<string, ApprovedText>
        {
            ["unclear"] = T("unclear", "مش واضح", active: false),
            ["offer"] = T("offer", "عرض"),
        }.Where(kv => kv.Value.Active).ToDictionary();
        var c = ReplyComposer.Compose("wt:1", "wamid.1", [new ReplyStep("unclear", "offer")], texts);

        var a = Assert.Single(c.Actions);
        Assert.Equal("عرض", a.Text);
        Assert.Equal(("hazem:wt:1:0", "wamid.1"), (a.IdempotencyKey, a.ContextWamid));
        var r = Assert.Single(c.Records);
        Assert.Equal(("offer", 1, "unclear", TextRenderer.Inactive), (r.Key, r.Version, r.InsteadOf, r.Reason));
    }

    [Fact]
    public void Without_a_fallback_nothing_is_sent_and_the_reason_is_kept()
    {
        var c = ReplyComposer.Compose("wt:1", null, [new ReplyStep("thanks")], new Dictionary<string, ApprovedText>());
        Assert.Empty(c.Actions);
        Assert.Equal(new SkippedText("thanks", TextRenderer.Inactive), Assert.Single(c.Skipped));
    }

    [Fact]
    public void The_same_text_is_never_sent_twice_in_one_answer()
    {
        var texts = new Dictionary<string, ApprovedText> { ["offer"] = T("offer", "عرض") };
        var c = ReplyComposer.Compose("wt:1", null, [new ReplyStep("price", "offer"), new ReplyStep("offer")], texts);
        Assert.Single(c.Actions);
    }
}
