using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Application.Writing;
using Office.Infrastructure.Json;

namespace Office.Infrastructure.Persistence;

/// <summary>
/// v1.41 (TASK 17): the client's last turns and Hazem's answers, from hazem.turns (the messages and the actions the API
/// already keeps), oldest first. Text only (T-09): his words as the classifier sees them (AiInput: typed text, captions,
/// transcripts, the titles he pressed), Hazem's texts and buttons' bodies; numbers, IDs and e-mails masked; never a file.
/// </summary>
public sealed class ConversationHistory(HazemDbContext db) : IConversationHistory
{
    public async Task<IReadOnlyList<ChatLine>> RecentAsync(string waId, int turns, CancellationToken ct)
    {
        var rows = await db.Turns.AsNoTracking().Where(t => t.WaId == waId).OrderByDescending(t => t.CreatedAt).Take(turns)
            .Select(t => new { t.Messages, t.Actions }).ToListAsync(ct);
        var lines = new List<ChatLine>();
        foreach (var row in Enumerable.Reverse(rows))
        {
            var said = (JsonSerializer.Deserialize<List<IncomingMessage>>(row.Messages, HazemJson.Options) ?? [])
                .SelectMany(AiInput.Words).Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w!.Trim()).ToList();
            if (said.Count > 0)
            {
                lines.Add(new ChatLine(true, AiInput.Redact(string.Join("\n", said))));
            }

            foreach (var a in JsonSerializer.Deserialize<List<OutboundAction>>(row.Actions, HazemJson.Options) ?? [])
            {
                if (a.Type is ActionType.SendText or ActionType.SendInteractive && !string.IsNullOrWhiteSpace(a.Text))
                {
                    lines.Add(new ChatLine(false, AiInput.Redact(a.Text)));
                }
            }
        }

        return lines;
    }
}
