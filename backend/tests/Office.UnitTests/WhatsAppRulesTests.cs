using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Office.Core.WhatsApp;
using Office.Infrastructure.WhatsApp;

namespace Office.UnitTests;

public class WebhookSignatureTests
{
    private const string Secret = "test-app-secret";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[]}""");

    [Fact]
    public void Valid_signature_is_accepted() =>
        Assert.True(WebhookSignature.IsValid(Body, WebhookSignature.Compute(Body, Secret), Secret));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=abc")]
    [InlineData("sha256=")]
    [InlineData("sha256=zz")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public void Missing_or_wrong_signature_is_rejected(string? header) =>
        Assert.False(WebhookSignature.IsValid(Body, header, Secret));

    [Fact]
    public void Signature_for_a_different_body_is_rejected()
    {
        var header = WebhookSignature.Compute(Encoding.UTF8.GetBytes("{}"), Secret);
        Assert.False(WebhookSignature.IsValid(Body, header, Secret));
    }

    [Fact]
    public void Signature_with_another_secret_is_rejected() =>
        Assert.False(WebhookSignature.IsValid(Body, WebhookSignature.Compute(Body, "other"), Secret));

    [Fact]
    public void Empty_secret_never_validates() =>
        Assert.False(WebhookSignature.IsValid(Body, WebhookSignature.Compute(Body, ""), ""));
}

public class ContactWindowPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_one_minute_before_24_hours() => Assert.True(ContactWindowPolicy.IsOpen(Now.AddHours(-24).AddMinutes(1), Now));

    [Fact]
    public void Closed_at_exactly_24_hours() => Assert.False(ContactWindowPolicy.IsOpen(Now.AddHours(-24), Now));

    [Fact]
    public void Closed_after_25_hours() => Assert.False(ContactWindowPolicy.IsOpen(Now.AddHours(-25), Now));

    [Fact]
    public void Closed_when_the_customer_never_wrote() => Assert.False(ContactWindowPolicy.IsOpen(null, Now));
}

public class OutboundStatusTests
{
    [Theory]
    [InlineData("sent", "delivered", true)]
    [InlineData("delivered", "read", true)]
    [InlineData("read", "delivered", false)]  // late delivery receipt never moves read back
    [InlineData("read", "sent", false)]
    [InlineData("sent", "failed", true)]
    [InlineData("failed", "read", false)]     // failed is terminal
    [InlineData("blocked_window", "sent", false)]
    public void Status_only_moves_forward(string current, string incoming, bool expected) =>
        Assert.Equal(expected, OutboundStatuses.CanAdvance(current, incoming));
}

public class MetaErrorMapperTests
{
    [Theory]
    [InlineData(131047, SendErrorCodes.OutsideWindow)]
    [InlineData(190, SendErrorCodes.TokenInvalid)]
    [InlineData(130429, SendErrorCodes.RateLimit)]
    [InlineData(131056, SendErrorCodes.RateLimit)]
    [InlineData(80007, SendErrorCodes.RateLimit)]
    [InlineData(131026, SendErrorCodes.Undeliverable)]
    [InlineData(131030, SendErrorCodes.RecipientNotAllowed)]
    [InlineData(100, SendErrorCodes.MetaError)]
    [InlineData(null, SendErrorCodes.NetworkOrUnknown)]
    public void Meta_codes_map_to_our_codes(int? metaCode, string expected) =>
        Assert.Equal(expected, MetaErrorMapper.Map(metaCode));
}

public class JsonCanonicalizerTests
{
    [Fact]
    public void Same_content_in_different_key_order_has_the_same_hash()
    {
        var a = JsonCanonicalizer.Canonicalize(JsonNode.Parse("""{"b":1,"a":{"y":[1,{"q":2,"p":1}],"x":"s"}}"""));
        var b = JsonCanonicalizer.Canonicalize(JsonNode.Parse("""{"a":{"x":"s","y":[1,{"p":1,"q":2}]},"b":1}"""));
        Assert.Equal(a, b);
        Assert.Equal(JsonCanonicalizer.Sha256Hex(a), JsonCanonicalizer.Sha256Hex(b));
    }

    [Fact]
    public void Array_order_matters()
    {
        var a = JsonCanonicalizer.Canonicalize(JsonNode.Parse("[1,2]"));
        var b = JsonCanonicalizer.Canonicalize(JsonNode.Parse("[2,1]"));
        Assert.NotEqual(a, b);
    }
}

public class PhoneMaskTests
{
    [Fact]
    public void Masks_the_middle_digits() => Assert.Equal("2010****099", PhoneMask.Mask("201000000099"));
}

public class WhatsAppOptionsValidatorTests
{
    private static readonly WhatsAppOptionsValidator Validator = new();

    private static WhatsAppOptions Complete() => new()
    {
        Enabled = true,
        AppId = "1",
        WabaId = "2",
        PhoneNumberId = "3",
        GraphApiVersion = "v24.0",
        AppSecret = "s",
        AccessToken = "t",
        VerifyToken = "v",
        TestRecipients = "201000000001, 201000000002",
    };

    [Fact]
    public void Disabled_needs_no_secrets() => Assert.True(Validator.Validate(null, new WhatsAppOptions()).Succeeded);

    [Fact]
    public void Enabled_with_everything_is_valid() => Assert.True(Validator.Validate(null, Complete()).Succeeded);

    [Fact]
    public void Enabled_without_secret_fails()
    {
        var o = Complete();
        o.AppSecret = "";
        Assert.False(Validator.Validate(null, o).Succeeded);
    }

    [Fact]
    public void Placeholder_values_are_rejected()
    {
        var o = Complete();
        o.AccessToken = "CHANGE_ME";
        Assert.False(Validator.Validate(null, o).Succeeded);
    }

    [Fact]
    public void Bad_graph_version_fails()
    {
        var o = Complete();
        o.GraphApiVersion = "24";
        Assert.False(Validator.Validate(null, o).Succeeded);
    }

    [Fact]
    public void Recipient_with_plus_sign_fails()
    {
        var o = Complete();
        o.TestRecipients = "+201000000001";
        Assert.False(Validator.Validate(null, o).Succeeded);
    }

    [Fact]
    public void Recipients_are_trimmed() =>
        Assert.Equal(["201000000001", "201000000002"], Complete().TestRecipientList);
}

public class TemplatesCatalogTests
{
    [Fact]
    public void Catalog_has_the_eleven_M03_templates_in_arabic()
    {
        var templates = TemplatesCatalog.Load();
        Assert.Equal(11, templates.Count);
        Assert.All(templates, t =>
        {
            Assert.Equal("ar", t.Language);
            Assert.Equal("UTILITY", t.Category);
        });
    }

    [Fact]
    public void Create_request_only_has_fields_meta_accepts()
    {
        var request = TemplatesCatalog.Load()[0].ToCreateRequest();
        Assert.Equal(["category", "components", "language", "name"], request.Select(p => p.Key).Order().ToArray());
    }
}

public class ArchitectureTests
{
    [Fact]
    public void Core_depends_on_nothing_but_the_base_library()
    {
        var references = typeof(EventProcessor).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        var forbidden = references.Where(n =>
            n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || n.StartsWith("Npgsql", StringComparison.Ordinal)
            || n.StartsWith("Hangfire", StringComparison.Ordinal)
            || n.StartsWith("Office.", StringComparison.Ordinal)
            || n == "System.Net.Http").ToList();
        Assert.Empty(forbidden);
    }

    [Fact]
    public void Options_validator_is_registered_type() =>
        Assert.True(typeof(IValidateOptions<WhatsAppOptions>).IsAssignableFrom(typeof(WhatsAppOptionsValidator)));

    [Fact]
    public void Infrastructure_assembly_loads() =>
        Assert.NotNull(Assembly.GetAssembly(typeof(WhatsAppOptions)));
}
