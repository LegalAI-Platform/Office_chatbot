using System.Text.RegularExpressions;
using Office.Application.Responders;
using Office.Application.Turns;

namespace Office.Application.Texts;

/// <summary>A row of hazem.approved_texts. Every word Hazem sends comes from one of these (T-07).</summary>
public sealed record ApprovedText(string Key, int Version, string Text, int MaxChars, bool Active);

public interface IApprovedTexts
{
    /// <summary>The active version of every key.</summary>
    Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(CancellationToken ct);

    /// <summary>The same, with the settings the caller has already read this turn (TASK 06: the prices in the texts).</summary>
    Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(HazemSettings settings, CancellationToken ct) => GetActiveAsync(ct);
}

/// <summary>The keys Hazem uses in TASK 04 (seeded by migration 0005; docs/approved-texts-proposal.md).</summary>
public static class TextKeys
{
    // From DECISIONS (v1.4, unchanged in v1.5), word for word (active).
    public const string WelcomeAskName = "welcome_ask_name";
    public const string ConsultationOffer = "consultation_offer";
    public const string LegalQuestion = "legal_question";
    public const string UrgentPrice = "urgent_price";
    public const string DocumentReviewRequest = "document_review_request";
    public const string WrittenOpinion = "written_opinion";
    public const string UrfUnclear = "urf_unclear";
    public const string UrfProof = "urf_proof";
    public const string ExistingOrNew = "existing_or_new";   // B-04 (TASK 05a)
    public const string AskTopic = "ask_topic";              // B-25
    public const string TopicReceived = "topic_received";    // B-25

    // Proposed in TASK 05a (inactive until approved).
    public const string AbuseWarning = "abuse_warning";
    public const string LawyerDirectReceived = "lawyer_direct_received";
    public const string ColleagueReceived = "colleague_received";
    public const string PressReceived = "press_received";
    public const string VendorReceived = "vendor_received";

    // Proposed, waiting for the owner (inactive until approved).
    public const string AskNameAgain = "ask_name_again";
    public const string Unclear = "unclear";
    public const string UnclearAgain = "unclear_again";
    public const string Greeting = "greeting";
    public const string Thanks = "thanks";
    public const string HumanRequest = "human_request";
    public const string ExistingMatter = "existing_matter";
    public const string RescheduleOrCancel = "reschedule_or_cancel";
    public const string ContractDraftingRequest = "contract_drafting_request";
    public const string FormalNoticeRequest = "formal_notice_request";
    public const string UrfRequest = "urf_request";
    public const string PriceDocumentReview = "price_document_review";
    public const string PriceContractDrafting = "price_contract_drafting";
    public const string PriceFormalNotice = "price_formal_notice";
    public const string PriceUrf = "price_urf";
    public const string PriceCaseRepresentation = "price_case_representation";
    public const string MediaReceived = "media_received";
    public const string MediaTooLarge = "media_too_large";
    public const string MediaFailed = "media_failed";
    public const string MediaRejected = "media_rejected";
    public const string VoiceFailed = "voice_failed";
    public const string Unsupported = "unsupported";
    public const string LocationReceived = "location_received";

    // TASK 08 (booking). From DECISIONS, word for word (active).
    public const string BookingConfirmed = "booking_confirmed";              // B-08 (1.4; replaced by booking_hold in 1.13)
    public const string BookingCancelledByOffice = "booking_cancelled_by_office"; // B-22

    // Proposed in TASK 08 (inactive until approved). Titles of buttons and list rows too.
    public const string BtnBookPhoneVideo = "btn_book_phone_video";
    public const string BtnBookOffice = "btn_book_office";
    public const string BookingChooseSlot = "booking_choose_slot";
    public const string BtnSlotsList = "btn_slots_list";
    public const string BtnMoreSlots = "btn_more_slots";
    public const string BookingSlotTaken = "booking_slot_taken";
    public const string BookingNoSlots = "booking_no_slots";
    public const string BookingUnavailable = "booking_unavailable";
    public const string BookingUrgentNoneToday = "booking_urgent_none_today";
    public const string BookingExisting = "booking_existing";
    public const string BtnReschedule = "btn_reschedule";
    public const string BtnCancelBooking = "btn_cancel_booking";
    public const string BookingRescheduled = "booking_rescheduled";
    public const string BookingCancelled = "booking_cancelled";
    public const string BtnRefund = "btn_refund";
    public const string BookingRefundRequested = "booking_refund_requested";

    // Proposed in TASK 09 (DECISIONS v1.13: the hold until the receipt; inactive until approved).
    public const string BookingHold = "booking_hold";                       // B-08.4
    public const string BookingHoldReminder = "booking_hold_reminder";      // B-08.6, the worker
    public const string BookingPaid = "booking_paid";                       // B-08.5: the receipt confirmed it
    public const string BookingHoldExpired = "booking_hold_expired";        // B-08.6, the worker
    public const string BookingHoldLate = "booking_hold_late";              // B-05: receipt after the time, taken
    public const string BookingHoldOne = "booking_hold_one";                // P3
    public const string BookingHoldCancelled = "booking_hold_cancelled";
    public const string BookingOfficeUnpaid = "booking_office_unpaid";      // B-22, a hold the office cancelled (the worker)
    public const string ReceiptSendImage = "receipt_send_image";            // P7
    public const string ProofReceivedAgain = "proof_received_again";        // B-05
    public const string PaymentPartial = "payment_partial";                 // B-05, the worker
    public const string PaymentNotReceived = "payment_not_received";        // B-05, the worker
    public const string RefundFull = "refund_full";                         // B-22
    public const string RefundNone = "refund_none";                         // B-22
    public const string RefundCompleted = "refund_completed";               // B-22, the worker

    // Proposed in TASK 10 (DECISIONS v1.14: written services, B-16 / B-17; inactive until approved).
    public const string ServiceFilesReceived = "service_files_received";    // B-16.2
    public const string ServicePriced = "service_priced";                   // B-16.4, the worker
    public const string ServicePriceReminder = "service_price_reminder";    // v1.14, the worker
    public const string ServicePriceExpired = "service_price_expired";      // v1.14, the worker
    public const string ServiceReceiptReceived = "service_receipt_received"; // B-16.5
    public const string ServiceStarted = "service_started";                 // B-16.5, the worker
    public const string ServiceDelivered = "service_delivered";             // B-16.6, the caption of the file
    public const string ServicePaymentPartial = "service_payment_partial";  // B-05, the worker
    public const string ServicePaymentNotReceived = "service_payment_not_received"; // B-05, the worker
    public const string ServiceClosed = "service_closed";                   // B-16.7, the worker

    // TASK 11 (v1.15, B-11): the Urfi questions (word for word from DECISIONS) and the proposed texts and buttons.
    public const string UrfAskAges = "urf_ask_ages";
    public const string UrfUnderAge = "urf_under_age";
    public const string UrfAgeClosed = "urf_age_closed";                    // v1.20 (B-11.1): the same matter after the age refusal
    public const string UrfAskWifePast = "urf_ask_wife_past";
    public const string UrfAskGuardian = "urf_ask_guardian";
    public const string UrfNoGuardian = "urf_no_guardian";
    public const string UrfAskWitnesses = "urf_ask_witnesses";
    public const string UrfAskCopy = "urf_ask_copy";
    public const string UrfAskAgreed = "urf_ask_agreed";
    public const string UrfAskChildren = "urf_ask_children";
    public const string UrfConfirmed = "urf_confirmed";                     // B-11.7, the worker after the lawyer's approval
    public const string UrfAgesAgain = "urf_ages_again";
    public const string UrfChooseSlot = "urf_choose_slot";                  // the price, the documents and the free times
    public const string UrfSendDocuments = "urf_send_documents";            // after the receipt
    public const string UrfDocumentsReceived = "urf_documents_received";
    public const string UrfRejected = "urf_rejected";                       // the worker after the lawyer's refusal
    public const string BtnUrfFirst = "btn_urf_first";
    public const string BtnUrfDivorced = "btn_urf_divorced";
    public const string BtnUrfKhul = "btn_urf_khul";
    public const string BtnUrfGuardianList = "btn_urf_guardian_list";
    public const string BtnUrfFather = "btn_urf_father";
    public const string BtnUrfBrother = "btn_urf_brother";
    public const string BtnUrfUncle = "btn_urf_uncle";
    public const string BtnUrfNoGuardian = "btn_urf_no_guardian";
    public const string BtnUrfWitnessOffice = "btn_urf_witness_office";
    public const string BtnUrfWitnessOwn = "btn_urf_witness_own";
    public const string BtnYes = "btn_yes";
    public const string BtnNo = "btn_no";

    // TASK 06 (v1.17, B-20): the answers to the office's questions, filled from «بيانات المكتب», the price list and the week.
    public const string OfficeAddress = "office_address";
    public const string OfficeHours = "office_hours";
    public const string OfficeHowToBook = "office_how_to_book";
    public const string OfficeHowToPay = "office_how_to_pay";
    public const string OfficeRefund = "office_refund";
    public const string OfficePrices = "office_prices";
    public const string OfficeFeePolicy = "office_fee_policy";
    public const string OfficePracticeAreas = "office_practice_areas";

    // TASK 12 (v1.16, B-18): the reminders. The two M-03 bodies (active, word for word as submitted to Meta) and the
    // texts proposed in TASK 12 (approved by the owner as proposed).
    public const string ReminderDayBefore = "reminder_day_before";          // the worker, the day before (with two buttons)
    public const string Reminder2hBefore = "reminder_2h_before";            // the worker, two hours before
    public const string BtnReminderAttend = "btn_reminder_attend";
    public const string BtnReminderChange = "btn_reminder_change";
    public const string ReminderAttendOk = "reminder_attend_ok";            // «هحضر ✅» pressed
    public const string ReminderDetailsOffice = "reminder_details_office";  // the details line: the office's address and map
    public const string ReminderDetailsPhone = "reminder_details_phone";    // the details line: the lawyer calls
    public const string ReminderDetailsUrf = "reminder_details_urf";        // the details line: who comes to an Urfi appointment
    public const string ReminderOldButton = "reminder_old_button";          // «هحضر» from the reminder of a time that moved
}

/// <summary>
/// One reply Hazem wants to send: a text key, and what to send instead when it cannot be sent.
/// <see cref="Choices"/> (TASK 08): buttons or a list under that text.
/// </summary>
public sealed record ReplyStep(string Key, string? Fallback = null, IReadOnlyDictionary<string, string>? Fill = null,
    ReplyChoices? Choices = null);

/// <summary>Buttons or a list. <see cref="ButtonKey"/>: the text of the list's button (a list only).</summary>
public sealed record ReplyChoices(string Kind, IReadOnlyList<ReplyChoice> Items, string? ButtonKey = null);

/// <summary>One button or row: its title is an approved text (<see cref="TitleKey"/>) or a value of the system (<see cref="Title"/>).</summary>
public sealed record ReplyChoice(string Id, string? TitleKey = null, string? Title = null);

/// <summary>Which approved text (key and version) went out under which idempotency key, or why another one did.</summary>
public sealed record ReplyRecord(string IdempotencyKey, string Key, int Version, int Part, string? InsteadOf, string? Reason);

/// <summary>A text that could not be sent and why (logged by key, never by content).</summary>
public sealed record SkippedText(string Key, string Reason);

public static partial class TextRenderer
{
    public const string Inactive = "inactive_or_missing";
    public const string MissingValue = "missing_value";

    /// <summary>
    /// Fills {{lower_case}} slots from <paramref name="fill"/>. Any slot left — and any DECISIONS
    /// part-5 placeholder such as {{OFFICE_ADDRESS}} or a [bracket] slot — means the text cannot
    /// go out (DECISIONS part 5). Returns null and the reason in that case.
    /// </summary>
    public static (string? Text, string? Reason) Render(ApprovedText text, IReadOnlyDictionary<string, string>? fill)
    {
        var rendered = Slot().Replace(text.Text, m =>
            fill is not null && fill.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return HasOpenSlot(rendered) ? (null, MissingValue) : (rendered, null);
    }

    /// <summary>{{anything}} or [anything]: a value nobody filled (healthcheck and tests use it too).</summary>
    public static bool HasOpenSlot(string text) => OpenSlot().IsMatch(text);

    /// <summary>
    /// B-21: a text longer than its limit is cut at a natural point — the end of a sentence,
    /// else a line, else a space — never in the middle of a word.
    /// </summary>
    public static IReadOnlyList<string> Split(string text, int maxChars)
    {
        var parts = new List<string>();
        var rest = text.Trim();
        while (rest.Length > maxChars)
        {
            var window = rest[..(maxChars + 1)];
            var cut = LastBreak(window, ['.', '؟', '?', '!', '؛']) is var s and > 0 ? s + 1
                : window.LastIndexOf('\n') is var n and > 0 ? n
                : window.LastIndexOf(' ') is var w and > 0 ? w
                : maxChars;
            parts.Add(rest[..cut].Trim());
            rest = rest[cut..].Trim();
        }

        if (rest.Length > 0)
        {
            parts.Add(rest);
        }

        return parts;
    }

    private static int LastBreak(string window, char[] marks)
    {
        var best = -1;
        foreach (var c in marks)
        {
            best = Math.Max(best, window[..^1].LastIndexOf(c));
        }

        return best;
    }

    [GeneratedRegex(@"\{\{([a-z_]+)\}\}")]
    private static partial Regex Slot();

    [GeneratedRegex(@"\{\{[^}]*\}\}|\[[^\]]*\]")]
    private static partial Regex OpenSlot();
}

/// <summary>Turns reply steps into n8n actions, with one stable idempotency key per part.</summary>
public static class ReplyComposer
{
    public sealed record Composed(IReadOnlyList<OutboundAction> Actions, IReadOnlyList<ReplyRecord> Records, IReadOnlyList<SkippedText> Skipped);

    public static Composed Compose(string turnId, string? contextWamid, IEnumerable<ReplyStep> steps,
        IReadOnlyDictionary<string, ApprovedText> texts)
    {
        var actions = new List<OutboundAction>();
        var records = new List<ReplyRecord>();
        var skipped = new List<SkippedText>();
        var sent = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in steps)
        {
            var (chosen, rendered, reason) = Resolve(step.Key, step.Fill, texts);
            string? insteadOf = null;
            if (chosen is null)
            {
                skipped.Add(new SkippedText(step.Key, reason!));
                if (step.Fallback is null)
                {
                    continue;
                }

                insteadOf = step.Key;
                (chosen, rendered, var fallbackReason) = Resolve(step.Fallback, step.Fill, texts);
                if (chosen is null)
                {
                    skipped.Add(new SkippedText(step.Fallback, fallbackReason!));
                    continue;
                }
            }

            if (!sent.Add(chosen.Key))
            {
                continue; // the same text twice in one answer (e.g. a fallback equal to the next step)
            }

            // TASK 08: buttons or a list under the text. A title that cannot be sent (not approved
            // yet, too long) sends the text alone: the client can still answer in words.
            if (step.Choices is { } choices && insteadOf is null)
            {
                var (payload, problem) = Interactive(choices, texts);
                if (payload is not null && rendered!.Length <= InteractivePayload.MaxBody)
                {
                    var key = OutboundAction.KeyFor(turnId, actions.Count);
                    actions.Add(new OutboundAction(ActionType.SendInteractive, key, Text: rendered,
                        ContextWamid: actions.Count == 0 ? contextWamid : null, Interactive: payload));
                    records.Add(new ReplyRecord(key, chosen.Key, chosen.Version, 0, null, null));
                    continue;
                }

                skipped.Add(problem ?? new SkippedText(chosen.Key, "too_long_for_buttons"));
            }

            var parts = TextRenderer.Split(rendered!, chosen.MaxChars);
            for (var p = 0; p < parts.Count; p++)
            {
                var key = OutboundAction.KeyFor(turnId, actions.Count);
                actions.Add(new OutboundAction(ActionType.SendText, key, Text: parts[p], ContextWamid: actions.Count == 0 ? contextWamid : null));
                records.Add(new ReplyRecord(key, chosen.Key, chosen.Version, p, insteadOf, insteadOf is null ? null : reason));
            }
        }

        return new Composed(actions, records, skipped);
    }

    public const string TitleTooLong = "title_too_long";
    public const string TooManyChoices = "too_many_choices";

    /// <summary>The buttons or the list, every title within WhatsApp's limits; or what stopped it.</summary>
    private static (InteractivePayload? Payload, SkippedText? Problem) Interactive(ReplyChoices choices,
        IReadOnlyDictionary<string, ApprovedText> texts)
    {
        var isList = choices.Kind == InteractivePayload.List;
        var maxTitle = isList ? InteractivePayload.MaxRowTitle : InteractivePayload.MaxButtonTitle;
        if (choices.Items.Count == 0 || choices.Items.Count > (isList ? InteractivePayload.MaxRows : InteractivePayload.MaxButtons))
        {
            return (null, new SkippedText(choices.Kind, TooManyChoices));
        }

        string? button = null;
        if (isList)
        {
            var (b, _, reason) = Resolve(choices.ButtonKey ?? "", null, texts);
            if (b is null)
            {
                return (null, new SkippedText(choices.ButtonKey ?? "list_button", reason!));
            }

            button = b.Text.Trim();
            if (button.Length > InteractivePayload.MaxButtonTitle)
            {
                return (null, new SkippedText(b.Key, TitleTooLong));
            }
        }

        var items = new List<InteractiveChoice>();
        foreach (var item in choices.Items)
        {
            var title = item.Title;
            if (item.TitleKey is { } key)
            {
                var (t, rendered, reason) = Resolve(key, null, texts);
                if (t is null)
                {
                    return (null, new SkippedText(key, reason!));
                }

                title = rendered!.Trim();
            }

            if (string.IsNullOrWhiteSpace(title) || title.Length > maxTitle)
            {
                return (null, new SkippedText(item.TitleKey ?? item.Id, TitleTooLong));
            }

            items.Add(new InteractiveChoice(item.Id, title));
        }

        return (new InteractivePayload(choices.Kind, button, items), null);
    }

    private static (ApprovedText? Text, string? Rendered, string? Reason) Resolve(string key,
        IReadOnlyDictionary<string, string>? fill, IReadOnlyDictionary<string, ApprovedText> texts)
    {
        if (!texts.TryGetValue(key, out var text) || !text.Active)
        {
            return (null, null, TextRenderer.Inactive);
        }

        var (rendered, reason) = TextRenderer.Render(text, fill);
        return rendered is null ? (null, null, reason) : (text, rendered, null);
    }
}
