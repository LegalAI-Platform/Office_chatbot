using Office.Application.Understanding;

namespace Office.Application.Services;

// TASK 10 (DECISIONS v1.14, B-16 / B-17 / S-07): a written service from the client's request to its
// delivery. The order and its rules live in SQL (migration 0015); this is what the flow and the worker need.

/// <summary>The written services (B-16, B-17) and how Hazem names them.</summary>
public sealed record ServiceKind(string Code, string Name)
{
    public static ServiceKind DocumentReview { get; } = new("document_review", "مراجعة العقد");
    public static ServiceKind ContractDrafting { get; } = new("contract_drafting", "صياغة العقد");
    public static ServiceKind FormalNotice { get; } = new("formal_notice", "الإنذار الرسمي");
    public static ServiceKind WrittenOpinion { get; } = new("written_opinion", "الرأي القانوني المكتوب");

    public static IReadOnlyList<ServiceKind> All { get; } = [DocumentReview, ContractDrafting, FormalNotice, WrittenOpinion];

    public static ServiceKind? ByCode(string? code) => All.FirstOrDefault(k => k.Code == code);

    /// <summary>The services a client asks for by name and that open an order (the written opinion follows a consultation).</summary>
    public static ServiceKind? Requested(Service? s) => s switch
    {
        Service.DocumentReview => DocumentReview,
        Service.ContractDrafting => ContractDrafting,
        Service.FormalNotice => FormalNotice,
        _ => null,
    };

    /// <summary>«(ملف هنا على واتساب)» / «(مكالمة من المستشار)».</summary>
    public static string Delivery(string? delivery) => delivery == "call" ? "مكالمة من المستشار" : "ملف هنا على واتساب";
}

/// <summary>I4 (v1.14): what a file from the client is for.</summary>
public enum FileTarget
{
    None,
    Hold,           // a booking held until its receipt
    OrderPayment,   // a service order waiting for its payment
    BookingPayment, // a confirmed booking waiting for a (new) receipt
    OrderDocuments, // a service order waiting for documents (or its price)
    LateHold,       // a hold whose time ran out
    AskImage,       // a PDF or Word while a payment waits: a receipt is an image
    UrfDocuments,   // TASK 11: an Urfi case waiting for its documents (after its receipt)
}

public sealed record FileFor(FileTarget Target, Guid? Id)
{
    public static FileFor Nothing { get; } = new(FileTarget.None, null);

    public bool IsReceipt => Target is FileTarget.Hold or FileTarget.OrderPayment or FileTarget.BookingPayment or FileTarget.LateHold;
}

public enum AddFileStatus
{
    None,
    First,  // the order's first document: Hazem answers, the office gets a task
    More,
    Exists, // the same message again
}

public sealed record AddFileOutcome(AddFileStatus Status, Guid? OrderId, string? Kind);

public enum OpenOrderStatus
{
    Opened,
    Open,   // an order of that kind was already open
    Exists, // the same turn again
}

public sealed record OpenOrderOutcome(OpenOrderStatus Status, Guid Id, string State);

/// <summary>A client file as n8n stored it (a reference only, never the file: T-09, v1.3).</summary>
public sealed record StoredFile(string Wamid, string Bucket, string Key, string? MimeType, string? Filename);

public interface IServiceStore
{
    Task<FileFor> FileTargetAsync(Guid personId, bool image, CancellationToken ct);

    Task<OpenOrderOutcome> OpenAsync(Guid personId, Guid? intakeId, ServiceKind kind, string key, CancellationToken ct);

    Task<AddFileOutcome> AddFileAsync(Guid personId, StoredFile file, CancellationToken ct);

    /// <summary>The client wrote: the 24-hour window is open again, so what could not reach him is sent again.</summary>
    Task RetryNoticesAsync(Guid personId, CancellationToken ct);
}

/// <summary>Where the flow opens orders and hands them the client's documents (B-16.1, B-16.2).</summary>
public sealed class ServiceDesk(IServiceStore store)
{
    public Task<FileFor> FileTargetAsync(Guid personId, bool image, CancellationToken ct) => store.FileTargetAsync(personId, image, ct);

    public Task<OpenOrderOutcome> OpenAsync(Guid personId, Guid? intakeId, ServiceKind kind, string key, CancellationToken ct) =>
        store.OpenAsync(personId, intakeId, kind, key, ct);

    public Task RetryNoticesAsync(Guid personId, CancellationToken ct) => store.RetryNoticesAsync(personId, ct);

    /// <summary>Every stored file of the turn, in order. First when any of them was the order's first.</summary>
    public async Task<AddFileOutcome> AddFilesAsync(Guid personId, IEnumerable<StoredFile> files, CancellationToken ct)
    {
        var best = new AddFileOutcome(AddFileStatus.None, null, null);
        foreach (var f in files)
        {
            best = Better(best, await store.AddFileAsync(personId, f, ct));
        }

        return best;
    }

    /// <summary>Of a turn's files, the outcome that tells the client most (the first of an order or a case wins).</summary>
    public static AddFileOutcome Better(AddFileOutcome a, AddFileOutcome b) => Rank(b.Status) > Rank(a.Status) ? b : a;

    private static int Rank(AddFileStatus s) => s switch
    {
        AddFileStatus.First => 3,
        AddFileStatus.More => 2,
        AddFileStatus.Exists => 1,
        _ => 0,
    };
}
