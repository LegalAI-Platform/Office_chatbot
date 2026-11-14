namespace Office.Application.Panel;

/// <summary>
/// v1.110 (the owner, 2026-11-14: «https://panel.hazem-office.tech/documents شوف دي عاوز تعملها» … «اعمل كله مع بعض»):
/// «المستندات», every file a client sent (hazem.client_documents, migration 0079), by client: the client whose latest file
/// is the newest first. Who may see a client's files is who may see his tasks: the lawyer, the manager and the maintenance
/// account every client's; a staff member the clients of her tasks and of the unassigned ones.
/// </summary>
public static class DocumentSources
{
    public const string Message = "message";
    public const string Receipt = "receipt";
    public const string Service = "service";
    public const string Urf = "urf";

    public static readonly IReadOnlyList<string> All = [Message, Receipt, Service, Urf];

    public const string Image = "image";
    public const string Document = "document";
}

public sealed record DocumentQuery(string? Search, string? Kind, string? Source, int Page);

/// <summary>One file. <paramref name="Index"/>: its message's place in the turn (a message's file); 0 otherwise.</summary>
public sealed record DocumentFile(string Source, Guid Id, int Index, string Kind, string? Mime, string? Name, DateTimeOffset ReceivedAt,
    Guid? TaskId);

/// <summary>A client and his newest files (<see cref="PanelDocuments.PerClient"/> at most; <paramref name="Count"/> is how many he has).</summary>
public sealed record DocumentClient(Guid Id, string? Name, string? PhoneLast3, int Count, DateTimeOffset LatestAt, IReadOnlyList<DocumentFile> Files);

public sealed record DocumentPage(IReadOnlyList<DocumentClient> Clients, int Total, int Page, int PageSize);

public interface IPanelDocumentStore
{
    /// <summary>
    /// A page of the clients with files (of <paramref name="persons"/> only, when a search named them), the newest first,
    /// each with his newest files. <paramref name="allClients"/>: every client's; else those of <paramref name="staffId"/>'s
    /// tasks and of the unassigned ones.
    /// </summary>
    Task<DocumentPage> ListAsync(SearchTerm term, string? kind, string? source, bool allClients, Guid? staffId, int page, int pageSize,
        int perClient, CancellationToken ct);

    /// <summary>Where one file is, and whose it is; null when there is no such file.</summary>
    Task<(ClientFileRef File, Guid PersonId)?> FileAsync(string source, Guid id, int index, CancellationToken ct);

    /// <summary>Whether a staff member may see this client's files (a task of hers, or an unassigned one).</summary>
    Task<bool> StaffMaySeeAsync(Guid personId, Guid? staffId, CancellationToken ct);

    Task RecordOpenedAsync(Guid by, string source, Guid id, CancellationToken ct);
}

public sealed class PanelDocuments(IPanelDocumentStore store)
{
    public const int PageSize = 20;
    public const int PerClient = 12;

    public Task<DocumentPage> ListAsync(Viewer viewer, DocumentQuery q, CancellationToken ct)
    {
        var kind = q.Kind is DocumentSources.Image or DocumentSources.Document ? q.Kind : null;
        var source = q.Source is { } s && DocumentSources.All.Contains(s) ? s : null;
        var term = SearchTerm.Parse(q.Search);
        if (!string.IsNullOrWhiteSpace(q.Search) && term.IsEmpty)
        {
            // something typed that names no one (two digits): nothing, never everyone
            return Task.FromResult(new DocumentPage([], 0, 1, PageSize));
        }

        return store.ListAsync(term, kind, source, viewer.Can(Permissions.TasksViewAll), viewer.StaffId, Math.Clamp(q.Page, 1, 10_000), PageSize,
            PerClient, ct);
    }

    /// <summary>A file, for whoever may see its client's files (recorded once it is opened, by the endpoint).</summary>
    public async Task<ClientFileRef?> FileAsync(Viewer viewer, string source, Guid id, int index, CancellationToken ct)
    {
        if (!DocumentSources.All.Contains(source) || index < 0 || await store.FileAsync(source, id, index, ct) is not { } found)
        {
            return null;
        }

        return viewer.Can(Permissions.TasksViewAll) || await store.StaffMaySeeAsync(found.PersonId, viewer.StaffId, ct) ? found.File : null;
    }

    public Task RecordOpenedAsync(Viewer viewer, string source, Guid id, CancellationToken ct) =>
        store.RecordOpenedAsync(viewer.UserId, source, id, ct);
}
