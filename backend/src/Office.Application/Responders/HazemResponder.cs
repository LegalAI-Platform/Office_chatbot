using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Ratings;
using Office.Application.Reminders;
using Office.Application.Services;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Application.Urf;
using Office.Domain.Clients;
using Office.Domain.Common;
using Office.Domain.Tasks;

namespace Office.Application.Responders;

/// <summary>
/// Hazem: understands the turn (AI classification only) and answers from approved texts only.
/// TASK 04: the conversation flow. TASK 05a: who is writing (B-14 lists, B-04 existing clients,
/// B-14.4 roles), where they came from (B-13), the topic (B-25), insults (S-01), real tasks (S-05).
/// TASK 08: booking (B-08, B-09, B-15) when <paramref name="booking"/> is there, booking.enabled is
/// true and its texts are approved; otherwise the consultation offer as before.
/// v1.31: a returning client's new request (B-01.7, <paramref name="history"/>) and the service rating's answers (B-26,
/// <paramref name="ratings"/>).
/// </summary>
public sealed class HazemResponder(
    IConversationStore conversations,
    IIntentClassifier classifier,
    IApprovedTexts approvedTexts,
    IClientLists lists,
    TimeProvider clock,
    BookingDesk? booking = null,
    ServiceDesk? services = null,
    UrfDesk? urf = null,
    IIntakeHistory? history = null,
    IRatingStore? ratings = null,
    IUrgentCalls? urgentCalls = null) : ITurnResponder
{
    /// <summary>
    /// TASK 10 (v1.14): the written services run (an order from the request to the delivery) only when every
    /// one of their texts is approved; otherwise a service named by the client is answered as before.
    /// </summary>
    public static IReadOnlyList<string> ServiceTextKeys { get; } =
    [
        TextKeys.ServiceFilesReceived, TextKeys.ServicePriced, TextKeys.ServicePriceReminder, TextKeys.ServicePriceExpired,
        TextKeys.ServiceReceiptReceived, TextKeys.ServiceStarted, TextKeys.ServiceDelivered, TextKeys.ServicePaymentPartial,
        TextKeys.ServicePaymentNotReceived, TextKeys.ServiceClosed, TextKeys.ReceiptSendImage,
    ];

    /// <summary>
    /// TASK 11 (v1.15): the Urfi questions run (B-11, from the request to the lawyer's review) only with the booking on
    /// and every one of their texts approved; otherwise a Urfi request is answered as before.
    /// </summary>
    public static IReadOnlyList<string> UrfTextKeys { get; } =
    [
        TextKeys.UrfAskAges, TextKeys.UrfUnderAge, TextKeys.UrfAskWifePast, TextKeys.UrfAskGuardian, TextKeys.UrfNoGuardian,
        TextKeys.UrfAskWitnesses, TextKeys.UrfAskCopy, TextKeys.UrfAskAgreed, TextKeys.UrfAskChildren, TextKeys.UrfConfirmed,
        TextKeys.UrfAgesAgain, TextKeys.UrfChooseSlot, TextKeys.UrfSendDocuments, TextKeys.UrfDocumentsReceived, TextKeys.UrfRejected,
        TextKeys.BtnUrfFirst, TextKeys.BtnUrfDivorced, TextKeys.BtnUrfKhul, TextKeys.BtnUrfGuardianList, TextKeys.BtnUrfFather,
        TextKeys.BtnUrfBrother, TextKeys.BtnUrfUncle, TextKeys.BtnUrfNoGuardian, TextKeys.BtnUrfWitnessOffice, TextKeys.BtnUrfWitnessOwn,
        TextKeys.BtnYes, TextKeys.BtnNo,
    ];

    /// <summary>The booking runs only when every one of its texts is approved: never half a conversation.</summary>
    /// TASK 09 (1.13): the hold, the receipt and the refund texts too; booking_confirmed (1.4) is no longer used.
    public static IReadOnlyList<string> BookingTextKeys { get; } =
    [
        TextKeys.BtnBookPhoneVideo, TextKeys.BtnBookOffice, TextKeys.BookingChooseSlot,
        TextKeys.BtnSlotsList, TextKeys.BtnMoreSlots, TextKeys.BookingSlotTaken, TextKeys.BookingNoSlots, TextKeys.BookingUnavailable,
        TextKeys.BookingUrgentNoneToday, TextKeys.BookingExisting, TextKeys.BtnReschedule, TextKeys.BtnCancelBooking,
        TextKeys.BookingRescheduled, TextKeys.BookingCancelled, TextKeys.BtnRefund, TextKeys.BookingRefundRequested,
        TextKeys.BookingCancelledByOffice,
        TextKeys.BookingHold, TextKeys.BookingHoldReminder, TextKeys.BookingPaid, TextKeys.BookingHoldExpired, TextKeys.BookingHoldLate,
        TextKeys.BookingHoldOne, TextKeys.BookingHoldCancelled, TextKeys.BookingOfficeUnpaid, TextKeys.ReceiptSendImage,
        TextKeys.ProofReceivedAgain, TextKeys.PaymentPartial, TextKeys.PaymentNotReceived, TextKeys.RefundFull, TextKeys.RefundNone,
        TextKeys.RefundCompleted,
        TextKeys.AskCallApp, TextKeys.BtnAppWhatsApp, TextKeys.BtnAppFaceTime, TextKeys.BtnAppMeet, // 1.25 (B-23)
        TextKeys.BookingConfirmedCall, TextKeys.BookingConfirmedOffice,
    ];

    public const string ResponderName = "hazem";

    /// <summary>Numbers Hazem answers while hazem.open_to_all is false (CLAUDE.md: test numbers only).</summary>
    public const string RecipientsKey = "hazem.recipients";

    /// <summary>true only when the owner opens Hazem to every client.</summary>
    public const string OpenToAllKey = "hazem.open_to_all";

    /// <summary>B-12 «موظف» hands over (true) or stays a task (false, until the panel, TASK 07).</summary>
    public const string HandoffEnabledKey = "handoff.enabled";

    /// <summary>B-13: codes put in wa.me links.</summary>
    public const string LinkCodesKey = "source.link_codes";

    public string Name => ResponderName;

    public async Task<TurnDecision> DecideAsync(Turn turn, HazemSettings settings, CancellationToken ct)
    {
        // v1.33 (B-09): the urgent alert's buttons («شوف التحويل», then «التحويل تمام، هكلمه» or «فيه مشكلة»), from whoever got the
        // template (the lawyer or the manager, on the lawyer's list or not a client at all): read by code before anything else,
        // never the AI, and no reply in this turn: the urgent desk sends what each press needs (IUrgentCalls).
        if (urgentCalls is not null && UrgentButtons.In(turn) is { } press)
        {
            var decided = await urgentCalls.PressAsync(press, turn.WaId, turn.TurnId, ct);
            return new TurnDecision(ResponderName, [], null, [$"urgent press {press.Kind}: {(decided ? "decided" : "no decision")}"]);
        }

        if (!settings.Flag(OpenToAllKey) && !settings.List(RecipientsKey).Contains(turn.WaId))
        {
            return new TurnDecision(ResponderName, []);
        }

        var now = clock.GetUtcNow();
        var list = await lists.FindAsync(turn.WaId, ct);
        var state = await conversations.LoadAsync(turn.WaId, ct);
        var firstContact = state is null;
        if (state is null)
        {
            var person = Person.New(WaId.From(turn.WaId));
            // B-01.5: a listed existing client with a full name is a known number: no name question.
            if (list.ExistingClient?.Name is { } listed && PersonName.TryParse(listed, out var name)) // v1.29: any listed name
            {
                person.GiveName(name);
            }

            state = new ClientState(person, Conversation.Start(person), Intake.Open(person));
        }

        RecordSource(state.Person, turn, settings, now);

        var plan = new Plan(now);
        Check(state.Conversation.ClientWroteAt(now)); // B-12: 12 hours after the staff
        Classification? classification = null;
        var texts = await approvedTexts.GetActiveAsync(settings, ct);
        var bookingSettings = BookingSettings.From(settings);
        var bookingOn = booking is not null && bookingSettings.Enabled && BookingTextKeys.All(k => texts.TryGetValue(k, out var t) && t.Active);
        // v1.33 (B-09): the urgent call-back with the booking on and every one of its texts approved; otherwise urgent as in 1.32.
        var urgentOn = bookingOn && UrgentTexts.Keys.All(k => texts.TryGetValue(k, out var t) && t.Active);
        // TASK 12 (B-18): the reminders run whenever their texts are approved, the booking from WhatsApp on or off.
        var remindersOn = booking is not null && ReminderTexts.Keys.All(k => texts.TryGetValue(k, out var t) && t.Active);
        var servicesOn = services is not null && ServiceTextKeys.All(k => texts.TryGetValue(k, out var t) && t.Active);
        if (servicesOn && !list.Blocked)
        {
            // The client wrote: the 24-hour window is open again for the price, the work started or the file that could not reach him.
            await services!.RetryNoticesAsync(state.Person.Id, ct);
        }

        if (list.Blocked)
        {
            // B-14.1: a blocked number is silent for good; messages are still stored.
            if (state.Conversation.State == ConversationState.BotActive)
            {
                Check(state.Conversation.Mute(Actor.System));
            }
        }
        else if (state.Conversation.HazemMayAnswer && !firstContact && ratings is not null && await RatingTurnAsync(turn, state, plan, now, ct))
        {
            // v1.31 (B-26): a row of the rating's list, or the reason after «يحتاج للتحسين»: answered by code, nothing else.
        }
        else if (state.Conversation.HazemMayAnswer)
        {
            // v1.31 (B-01.7): a known client whose request is finished starts a new one, the name kept («أهلًا بحضرتك تاني»).
            var returning = false;
            if (!firstContact && history is not null && await FreshIntakeAsync(turn, state, list, settings, now, ct) is { } fresh)
            {
                state = fresh;
                returning = true;
            }

            var urfOn = urf is not null && bookingOn && UrfTextKeys.All(k => texts.TryGetValue(k, out var t) && t.Active);
            IReadOnlyList<UrfCase> urfOpen = urfOn ? await urf!.OpenAsync(state.Person.Id, ct) : [];
            var urfCase = urfOpen.FirstOrDefault();
            // v1.20 (B-11.1): an age refusal in the last 24 hours, when its closing text is approved.
            var ageRefusal = urfOn && texts.TryGetValue(TextKeys.UrfAgeClosed, out var closing) && closing.Active
                ? await urf!.AgeRefusalAsync(state.Person.Id, now, ct)
                : null;
            var flow = new Flow(turn, state, list, null, plan, settings.Flag(HandoffEnabledKey), bookingOn ? bookingSettings : null, servicesOn,
                urfOn, urfOpen, ageRefusal: ageRefusal, urgentOn: urgentOn);
            // TASK 08: a button or a list row Hazem sent is read without the AI. TASK 12: a reminder's two buttons
            // whenever the reminders run (an appointment booked before the booking was switched off still gets them).
            var pressed = ChoiceIn(turn);
            var choice = pressed is { Kind: 'a' or 'e' }
                ? (remindersOn && flow.MayOrder ? pressed : null)
                : (bookingOn && flow.MayBook ? pressed : null);
            // TASK 11 (B-11): the answer to the Urfi question waiting: its button, or the typed ages (never the AI). Only words
            // with a number are read as the ages: «عايز أكلم موظف» or «بكام؟» go to the AI as any message.
            var urfButton = choice is null ? UrfAnswerIn(turn) : null;
            var urfAnswer = urfButton is not null && urfCase is { State: UrfCase.Collecting } && flow.MayBook ? urfButton : null;
            var urfAges = choice is null && urfButton is null && urfCase is { State: UrfCase.Collecting, Step: UrfStep.Ages } && flow.MayBook
                          && FileIn(turn) is null && AiInput.Build(turn) is { } typed && typed.Any(char.IsDigit);
            // TASK 09 / 10 (B-05, B-16, T-09): a file from the client never reaches the AI, nor any word sent with it:
            // a receipt (an image) for what waits for one, the documents of a service order, or «send an image».
            ReceiptOutcome? receipt = null;
            AddFileOutcome? documents = null;
            AddFileOutcome? urfDocuments = null;
            var askImage = false;
            if (choice is null && urfButton is null && !urfAges && (bookingOn || servicesOn) && flow.MayOrder && services is not null
                && FileIn(turn) is { } m)
            {
                var image = m.Type == MessageType.Image;
                var target = await services.FileTargetAsync(state.Person.Id, image, ct);
                switch (target.Target)
                {
                    case FileTarget.OrderPayment when servicesOn && booking is not null:
                    case FileTarget.Hold or FileTarget.BookingPayment or FileTarget.LateHold when bookingOn:
                        receipt = await booking!.ReceiptAsync(bookingSettings, settings,
                            new ReceiptRequest(state.Person.Id, m.Wamid, m.Media!.Bucket!, m.Media.Key!, m.Media.Sha256!, m.Media.MimeType), now, ct,
                            urgentCall: urgentOn);
                        break;
                    case FileTarget.OrderDocuments when servicesOn:
                        documents = await services.AddFilesAsync(state.Person.Id, StoredFiles(turn), ct);
                        break;
                    case FileTarget.UrfDocuments when urfOn:
                        urfDocuments = await urf!.AddFilesAsync(state.Person.Id, StoredFiles(turn), ct);
                        break;
                    case FileTarget.AskImage:
                        askImage = true; // P7: a PDF or Word is no receipt
                        break;
                }
            }

            // v1.30 (B-01.3): a row of the services list, read without the AI: the service's own flow (or «احكي مشكلتي»).
            var servicePress = choice is null && urfButton is null && !urfAges && receipt is null && !askImage && documents is null
                               && urfDocuments is null && flow.MayOrder ? ServiceRow.In(turn) : null;
            if (servicePress is { } row)
            {
                if (row.IsTell)
                {
                    plan.Say(TextKeys.ServicesTell);
                }
                else if (row.IsMenu)
                {
                    flow.ShowServices(); // v1.32: the welcome's «خدمات المكتب»
                }
                else
                {
                    flow = new Flow(turn, state, list, row.AsClassification, plan, settings.Flag(HandoffEnabledKey),
                        bookingOn ? bookingSettings : null, servicesOn, urfOn, urfOpen, null, ageRefusal, returning, urgentOn);
                    flow.Respond(firstContact);
                }
            }
            else if (choice is not null)
            {
                // v1.22 (B-25): words typed with the press («أخويا عنده قضية قتل» + a time): the AI reads their topic only,
                // for the booking and the lawyer's calendar; the press is still answered by code, never by the AI.
                if (AiInput.Build(turn with { Messages = [.. turn.Messages.Where(m => m.Interactive is null && m.Button is null)] })
                    is { } withPress && withPress.Any(char.IsLetter)) // the press's own title is no words of the client
                {
                    classification = Classification.From(await classifier.ClassifyAsync(withPress, settings, ct), settings);
                    flow.NoteTopic(classification);
                }

                flow.RespondToChoice(choice);
            }
            else if (urfAnswer is not null || urfAges)
            {
                flow.RespondToUrf(urfAnswer);
            }
            else if (urfButton is not null)
            {
                flow.RespondToOldUrfButton(); // a button from a question no longer waiting: never to the AI
            }
            else if (urfDocuments is { Status: not AddFileStatus.None })
            {
                flow.RespondToUrfDocuments(urfDocuments);
            }
            else if (receipt is not null || askImage)
            {
                flow.RespondToReceipt(receipt);
            }
            else if (documents is { Status: not AddFileStatus.None })
            {
                flow.RespondToDocuments(documents);
            }
            else
            {
                var aiText = AiInput.Build(turn);
                // v1.21 (B-01.6): the greeting answered in kind (by code). A goodbye alone, past the first message, is
                // answered alone: no AI, nothing else. The first message still gets the welcome and the name (B-01.1).
                var courtesy = Courtesy.Read(aiText);
                var greet = courtesy.TextKey is { } gk && texts.TryGetValue(gk, out var gt) && gt.Active ? gk : null;
                if (greet is not null && courtesy.EndsTheChat && !firstContact)
                {
                    plan.Say(greet);
                }
                else
                {
                    if (aiText is not null)
                    {
                        // 0032: the AI down → an insult or a plain thanks is still read by code (Backstop)
                        classification = Backstop.Apply(
                            Classification.From(await classifier.ClassifyAsync(aiText, settings, ct), settings), aiText);
                    }

                    var office = classification is { Intent: Intent.OfficeInfo } asked
                        ? await OfficeDetailsAsync(settings, asked.Topic == InfoTopic.Hours, now, ct)
                        : null;
                    flow = new Flow(turn, state, list, classification, plan, settings.Flag(HandoffEnabledKey), bookingOn ? bookingSettings : null,
                        servicesOn, urfOn, urfOpen, office, ageRefusal, returning, urgentOn);
                    // v1.32: the list did not know the greeting («صباحين الخير»): the kind the AI read, answered the same way
                    if (greet is null && classification is { IsFallback: false, Courtesy: not CourtesyKind.None } read
                        && new CourtesyMatch(read.Courtesy, false).TextKey is { } ak && texts.TryGetValue(ak, out var at) && at.Active)
                    {
                        greet = ak;
                    }

                    if (greet is not null && classification?.Intent != Intent.Abusive)
                    {
                        plan.Say(greet); // first, so it opens Hazem's answer
                    }

                    flow.Respond(firstContact);
                }
            }

            if (flow.HasBookingWork)
            {
                await flow.RunBookingAsync(booking!, settings, ct);
            }

            if (flow.ServiceToOpen is { } kind)
            {
                await services!.OpenAsync(state.Person.Id, flow.State.Intake.Id, kind, turn.TurnId, ct);
                if (flow.FilesToOrder)
                {
                    await services.AddFilesAsync(state.Person.Id, StoredFiles(turn), ct);
                }
            }

            if (flow.UrfChanged && flow.Urf is { } changed)
            {
                await urf!.SaveAsync(changed, flow.State.Intake.Id, ct);
            }

            state = flow.State;
        }

        var composed = ReplyComposer.Compose(turn.TurnId, turn.Messages[^1].Wamid, plan.Steps, texts);

        // Keys and reasons only (never a text): the API logs them with the turn.
        var notes = composed.Skipped.Select(x => $"text {x.Key} not sent: {x.Reason}").ToList();
        if (classification is { IsFallback: true })
        {
            notes.Add($"classification fell back to other: {classification.FallbackReason}");
        }
        else if (classification?.BackstopFor is { } failed)
        {
            notes.Add($"classification {failed}: read by code as {Classification.Code(classification.Intent)}");
        }

        var effects = new TurnEffects(state, plan.Tasks,
            classification is null ? null : ClassificationRecord.From(turn.TurnId, classification), composed.Records);
        return new TurnDecision(ResponderName, composed.Actions, effects, notes);
    }

    /// <summary>B-13: an ad (Meta referral), a wa.me link code, or direct. First contact only.</summary>
    private static void RecordSource(Person person, Turn turn, HazemSettings settings, DateTimeOffset now)
    {
        if (person.FirstSeenAt is not null)
        {
            return;
        }

        if (turn.Messages.FirstOrDefault(m => m.Referral is not null)?.Referral is { } r)
        {
            person.FirstSeen("ad", System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, string?> { ["source_type"] = r.SourceType, ["source_id"] = r.SourceId, ["headline"] = r.Headline }), now);
            return;
        }

        var firstText = turn.Messages.FirstOrDefault(m => m.Type == MessageType.Text)?.Text ?? "";
        var code = settings.List(LinkCodesKey).FirstOrDefault(c => firstText.Contains(c, StringComparison.OrdinalIgnoreCase));
        if (code is not null)
        {
            person.FirstSeen("link", System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["code"] = code }), now);
            return;
        }

        person.FirstSeen("direct", null, now);
    }

    /// <summary>
    /// v1.31 (B-26): the rating's row the client pressed (a list row inside the 24 hours, the template's quick reply outside),
    /// read by code: the first answer is stored and answered; «يحتاج للتحسين» is a task for the managers; a second press of
    /// an answered rating gets the same kind of reply and changes nothing; somebody else's id gets nothing. Words without a
    /// press: the reason after «يحتاج للتحسين» when they are the client's next message within 24 hours (no reply: the
    /// office calls, as rating_low told him). True when the turn was one of these.
    /// </summary>
    private async Task<bool> RatingTurnAsync(Turn turn, ClientState state, Plan plan, DateTimeOffset now, CancellationToken ct)
    {
        var last = turn.Messages.LastOrDefault(m => m.Interactive?.Id is not null || m.Button?.Payload is not null);
        if (RatingButtons.Parse(last?.Interactive?.Id ?? last?.Button?.Payload) is { } press)
        {
            var answer = await ratings!.AnswerAsync(state.Person.Id, press, turn.TurnId, now, ct);
            switch (answer.Status)
            {
                case RatingAnswerStatus.Recorded:
                    plan.Say(RatingTexts.Reply(press.Score));
                    if (press.IsLow)
                    {
                        plan.Task(PendingTask.RatingLow);
                    }

                    break;
                case RatingAnswerStatus.Again:
                    plan.Say(answer.Score is null ? TextKeys.RatingLater : TextKeys.RatingThanks);
                    break;
            }

            return true;
        }

        return last is null && !turn.Messages.Any(IsStoredFile) && AiInput.Build(turn) is { } words && words.Any(char.IsLetter)
               && await ratings!.TakeReasonAsync(state.Person.Id, turn.WaId, turn.TurnId, now, ct);
    }

    /// <summary>
    /// v1.31 (B-01.7, S-02): a new client (never a listed one, the lawyer's list or a colleague) whose name is settled and whose
    /// open request is finished (<see cref="ReturningClient.Finished"/>): that intake is closed and a new one opens for the
    /// same person, classified at once. Null when the request goes on.
    /// </summary>
    private async Task<ClientState?> FreshIntakeAsync(Turn turn, ClientState state, ListMatch list, HazemSettings settings, DateTimeOffset now,
        CancellationToken ct)
    {
        var open = state.Intake;
        if (list.ExistingClient is not null || list.LawyerDirect || !open.IsClassified
            || open.ClientType != ClientType.NewClient
            || !ReturningClient.Finished(await history!.FactsAsync(state.Person.Id, turn.WaId, open.Id, ct), now, ReturningClient.Quiet(settings)))
        {
            return null;
        }

        Check(open.MoveTo(IntakeState.Closed));
        var fresh = Intake.Open(state.Person);
        Check(fresh.Classify(ClientType.NewClient));
        state.ClosedIntakes.Add(open);
        return state with { Intake = fresh };
    }

    /// <summary>The booking id of the last button or list row the client pressed, if any.</summary>
    private static BookingChoice? ChoiceIn(Turn turn)
    {
        var last = turn.Messages.LastOrDefault(m => m.Interactive?.Id is not null || m.Button?.Payload is not null);
        var choice = BookingChoice.Parse(last?.Interactive?.Id ?? last?.Button?.Payload);
        // v1.22: «🏢 مقابلة في المكتب» and then, in the same turn, a time from the phone list: the client changed his mind
        // on the way; the kind he pressed wins and its own times are offered, rather than holding the other kind.
        if (choice is { Kind: 's' })
        {
            var mode = turn.Messages.Select(m => BookingChoice.Parse(m.Interactive?.Id ?? m.Button?.Payload))
                .LastOrDefault(p => p is { Kind: 'm' });
            if (mode is not null && mode.Service != choice.Service)
            {
                return mode;
            }
        }

        return choice;
    }

    /// <summary>
    /// TASK 06 (B-20): what an office answer is filled with, from «بيانات المكتب» and (for the hours) the panel's default week.
    /// A detail not written yet is left out, so its answer is not sent half.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> OfficeDetailsAsync(HazemSettings settings, bool hours, DateTimeOffset now,
        CancellationToken ct)
    {
        var o = OfficeInfo.From(settings);
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string slot, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                details[slot] = value;
            }
        }

        Add("address", o.Address);
        Add("map", o.MapUrl);
        Add("instapay", o.InstaPay);
        Add("wallet", o.WalletNumber.Length > 0 ? o.Wallet : null);
        if (hours && booking is not null)
        {
            Add("hours", await booking.WeekHoursAsync(now, ct));
        }

        return details;
    }

    /// <summary>TASK 11: the Urfi button the client pressed last, if any.</summary>
    private static UrfAnswer? UrfAnswerIn(Turn turn)
    {
        var last = turn.Messages.LastOrDefault(m => m.Interactive?.Id is not null || m.Button?.Payload is not null);
        return UrfAnswer.Parse(last?.Interactive?.Id ?? last?.Button?.Payload);
    }

    /// <summary>
    /// B-05 (P7), B-16: the last stored image or document with its place in storage. An image may be a
    /// receipt; a PDF or Word goes to a service order waiting for documents, or Hazem asks for an image.
    /// </summary>
    private static IncomingMessage? FileIn(Turn turn) => turn.Messages.LastOrDefault(m =>
        IsStoredFile(m) && (m.Type != MessageType.Image || m.Media!.Sha256 is not null)); // a receipt needs its sha256 (duplicates)

    /// <summary>B-16.2: every stored image and document of the turn (a contract comes in several pages).</summary>
    private static IEnumerable<StoredFile> StoredFiles(Turn turn) => turn.Messages.Where(IsStoredFile)
        .Select(m => new StoredFile(m.Wamid, m.Media!.Bucket!, m.Media.Key!, m.Media.MimeType, m.Media.Filename));

    private static bool IsStoredFile(IncomingMessage m) =>
        m.Type is MessageType.Image or MessageType.Document
        && m.Media is { Status: MediaStatus.Stored, Bucket: not null, Key: not null };

    /// <summary>A refused transition here is a bug in the flow, not a client's mistake.</summary>
    private static void Check(Result r)
    {
        if (!r.IsOk)
        {
            throw new InvalidOperationException($"{r.Error!.Code}: {r.Error.Message}");
        }
    }

    /// <summary>The decisions for one turn. Changes the state in memory; the store saves it with the turn.</summary>
    private sealed class Flow(Turn turn, ClientState state, ListMatch list, Classification? c, Plan plan, bool handoffEnabled,
        BookingSettings? booking, bool servicesOn, bool urfOn = false, IReadOnlyList<UrfCase>? urfOpen = null,
        IReadOnlyDictionary<string, string>? office = null, AgeRefusal? ageRefusal = null, bool returning = false, bool urgentOn = false)
    {
        /// <summary>TASK 11: the client's Urfi case (B-11) this turn is about (the newest running one), as the turn leaves it.</summary>
        public UrfCase? Urf { get; private set; } = urfOpen?.FirstOrDefault();

        /// <summary>The case changed in this turn and must be saved.</summary>
        public bool UrfChanged { get; private set; }

        /// <summary>TASK 10: the written service to open an order for (B-16.1), once the flow is done.</summary>
        public ServiceKind? ServiceToOpen { get; private set; }

        /// <summary>The turn's documents go to that order once it is open (they came with the request).</summary>
        public bool FilesToOrder { get; private set; }

        public ClientState State { get; private set; } = state;

        private Intake Intake => State.Intake;

        /// <summary>TASK 08: what is left to do with the booking (it needs the calendar and the database).</summary>
        private BookingWork? work;

        /// <summary>The classification being answered (the turn's, or the request kept from before the name).</summary>
        private Classification? answering;

        public bool HasBookingWork => work is not null;

        /// <summary>v1.22: the Urfi case has its price and its times sent, and the client has not picked one yet.</summary>
        private bool UrfWaitsForTime => Urf is { State: UrfCase.DocumentsPending, AppointmentId: null };

        /// <summary>v1.22 (B-25): the topic of words typed together with a press, recorded for the booking.</summary>
        public void NoteTopic(Classification typed) =>
            Intake.RecordMatter(typed.Matter is { } m ? Classification.Code(m) : null,
                typed.Subtype is { } st ? Classification.Code(st) : null);

        /// <summary>
        /// A new or current client past the first message (v1.32: the name is no longer needed first); never the lists'
        /// lawyer or the roles (B-14).
        /// </summary>
        public bool MayOrder =>
            !list.LawyerDirect
            && Intake.State is not (IntakeState.New or IntakeState.AwaitingName)
            && Intake.ClientType is not (ClientType.Colleague or ClientType.Press or ClientType.Vendor or ClientType.LawyerDirect);

        public bool MayBook => booking is not null && MayOrder;

        /// <summary>
        /// The client pressed one of Hazem's buttons or list rows (no AI). Words typed or said in the same
        /// turn are not lost: the office gets a task with them (the panel shows the turn's words).
        /// </summary>
        public void RespondToChoice(BookingChoice choice)
        {
            WordsToOffice();
            work = choice.Kind switch
            {
                'm' when choice.Service == BookingService.PhoneVideo => new AskCallApp(), // 1.25 (B-23): where the lawyer calls
                'm' => new OfferTimes(choice.Service!, null, TextKeys.BookingChooseSlot, null),
                'v' => new OfferTimes(BookingService.PhoneVideo, null, TextKeys.BookingChooseSlot, null, App: choice.App),
                's' when !State.Person.NameSettled => AskNameFor(BookingChoice.Pick(choice.Service!, choice.At!.Value, choice.App)),
                's' => new PickTime(choice.Service!, choice.At!.Value, choice.App),
                'n' => new OfferTimes(choice.Service!, choice.At, TextKeys.BookingChooseSlot, null, App: choice.App),
                'r' => new StartChange(choice.AppointmentId!.Value),
                't' => new ChangeTo(choice.AppointmentId!.Value, choice.At!.Value),
                'u' => new MoreChanges(choice.AppointmentId!.Value, choice.At!.Value),
                'c' => new CancelIt(choice.AppointmentId!.Value),
                'a' => new AttendIt(choice.AppointmentId!.Value, choice.At!.Value),
                'e' => new ReminderChange(choice.AppointmentId!.Value),
                _ => new RefundIt(choice.AppointmentId!.Value),
            };
        }

        /// <summary>v1.32 (B-02): a time picked before the name: the name is asked, the time waits for it (no hold yet).</summary>
        private BookingWork? AskNameFor(string pickId)
        {
            Intake.AskNameFor(pickId);
            plan.Say(TextKeys.AskNameBooking);
            return null;
        }

        /// <summary>
        /// v1.32 (B-02): the answer to the name asked at the booking. A name (any length) books the time waiting for it; anything
        /// else asks once more, then the time is booked without the name (as B-02 carried on before).
        /// </summary>
        private void AnswerNameForPick(string pickId)
        {
            if (c is null)
            {
                // A photo or file instead of the name is not a refusal (B-02): answer the file and ask again without
                // using up the one extra ask; the time keeps waiting.
                AnswerMediaOnly();
                plan.Say(TextKeys.AskNameAgain, TextKeys.AskNameBooking);
                return;
            }

            PersonName? name = null;
            if (c.FullName is { } extracted && PersonName.TryParse(extracted, out var fromAi, minWords: 1))
            {
                name = fromAi;
            }
            else if (c.Intent == Intent.ProvideName || c.IsFallback)
            {
                name = NameGuess.TryRead(PlainText(), out var typed) ? typed : null;
            }

            if (name is null && IsRequest(c.Intent))
            {
                // Another request instead of the name («بكام؟»، «عايز أكلم موظف»): it is answered, and the time is let go
                // (a new pick asks the name again).
                Intake.TakePending();
                AnswerTurn();
                return;
            }

            if (name is not null)
            {
                State.Person.GiveName(name);
            }
            else if (Intake.MayAskNameAgain)
            {
                Intake.AskNameFor(pickId);
                plan.Say(TextKeys.AskNameAgain, TextKeys.AskNameBooking);
                return;
            }
            else
            {
                State.Person.ContinueWithoutFullName();
            }

            Intake.TakePending();
            var pick = BookingChoice.Parse(pickId)!;
            work = new PickTime(pick.Service!, pick.At!.Value, pick.App);
        }

        /// <summary>
        /// v1.32 (B-01.1): the welcome with its two buttons, «خدمات المكتب» (the services list) and «أحكي مشكلتي». After
        /// «أهلًا بحضرتك تاني يا …» (<paramref name="back"/>) the question alone. Without the services, the consultation offer.
        /// </summary>
        private void Welcome(bool back = false)
        {
            if (!MayOrder)
            {
                Offer();
                return;
            }

            var key = back ? TextKeys.WelcomeChoose : TextKeys.Welcome;
            plan.Ask(key, new ReplyChoices(InteractivePayload.Buttons,
            [
                new ReplyChoice(ServiceRow.Id(ServiceRow.Menu), TextKeys.BtnServicesList),
                new ReplyChoice(ServiceRow.Id(ServiceRow.Tell), TextKeys.RowTell),
            ]), fallback: TextKeys.ConsultationOffer); // never a silent first answer while the welcome is switched off
            Check(Intake.MoveTo(IntakeState.OfferSent));
        }

        /// <summary>v1.32: the welcome's «خدمات المكتب» button.</summary>
        public void ShowServices() => ServicesMenu();

        /// <summary>
        /// TASK 09 (B-05): a receipt image was attached (<paramref name="outcome"/>), or a PDF came while a
        /// booking waits for a receipt (null: Hazem asks for an image, P7). Like a press, the words go to the office.
        /// </summary>
        public void RespondToReceipt(ReceiptOutcome? outcome)
        {
            WordsToOffice();
            if (outcome is null)
            {
                plan.Say(TextKeys.ReceiptSendImage);
                return;
            }

            if (outcome is { OrderId: not null, Status: ReceiptStatus.OrderReceived or ReceiptStatus.Exists })
            {
                // B-16.5: the work starts after a person found the money (never «اتأكد الدفع»).
                plan.Say(TextKeys.ServiceReceiptReceived, Fill: new Dictionary<string, string>
                {
                    ["service"] = ServiceKind.ByCode(outcome.OrderKind)?.Name ?? "الخدمة",
                });
                return;
            }

            work = new Receipted(outcome);
        }

        /// <summary>
        /// TASK 10 (B-16.2): documents for a service order. The first ones: «استلمت…» and a task for the lawyer or
        /// the manager to price it; more later: «استلمت المستند ✅». Words with them go to the office.
        /// </summary>
        public void RespondToDocuments(AddFileOutcome documents)
        {
            WordsToOffice();
            switch (documents.Status)
            {
                case AddFileStatus.First:
                    plan.Say(TextKeys.ServiceFilesReceived);
                    plan.Task(PendingTask.ServicePricing);
                    break;
                case AddFileStatus.More:
                    plan.Say(TextKeys.MediaReceived, Fill: new Dictionary<string, string> { ["media"] = "المستند" });
                    break;
            }
        }

        /// <summary>
        /// TASK 11 (B-11): the answer to the Urfi question waiting (a button, or the typed ages), read without the AI.
        /// </summary>
        public void RespondToUrf(UrfAnswer? answer)
        {
            if (answer is not null)
            {
                WordsToOffice();
            }

            var (next, how) = answer is not null ? UrfRules.Answer(Urf!, answer) : UrfRules.Ages(Urf!, AiInput.Build(turn));
            Urf = next;
            UrfChanged = true;
            AfterUrf(how);
        }

        /// <summary>
        /// An Urfi button from a question that no longer waits (the case moved on or closed): «تحت أمرك، قولّي المطلوب».
        /// </summary>
        public void RespondToOldUrfButton()
        {
            WordsToOffice();
            plan.Say(TextKeys.UrfUnclear);
        }

        /// <summary>B-11: «عايز جواز / طلاق عرفي»: a new case, or the one already running picks up where it was.</summary>
        private void StartUrf(string kind)
        {
            Check(Intake.MoveTo(IntakeState.InServiceFlow));
            if (urfOpen?.FirstOrDefault(c => c.Kind == kind) is { } open) // one running case per kind (urf_cases_one_open)
            {
                Urf = open;
                AfterUrf(open.State == UrfCase.Collecting ? UrfNext.Ask : UrfNext.Book);
                return;
            }

            Urf = UrfCase.Start(State.Person.Id, kind);
            UrfChanged = true;
            AfterUrf(UrfNext.Ask);
        }

        /// <summary>
        /// TASK 11 (B-11): the case's first documents → «استلمت» and the lawyer's review task; more later → «استلمت المستند».
        /// Words with them go to the office.
        /// </summary>
        public void RespondToUrfDocuments(AddFileOutcome documents)
        {
            WordsToOffice();
            switch (documents.Status)
            {
                case AddFileStatus.First:
                    plan.Say(TextKeys.UrfDocumentsReceived);
                    plan.Task(PendingTask.UrfReview);
                    break;
                case AddFileStatus.More:
                    plan.Say(TextKeys.MediaReceived, Fill: new Dictionary<string, string> { ["media"] = "المستند" });
                    break;
            }
        }

        private void AfterUrf(UrfNext next)
        {
            var c = Urf!;
            switch (next)
            {
                case UrfNext.Ask:
                    AskUrf(c.Step);
                    break;
                case UrfNext.AskAgesAgain:
                    plan.Say(TextKeys.UrfAgesAgain);
                    break;
                case UrfNext.Help:
                    HandToOffice(TextKeys.UnclearAgain, PendingTask.UrfHelp);
                    break;
                case UrfNext.RejectAge:
                    plan.Say(TextKeys.UrfUnderAge); // closed: no price, no time
                    break;
                case UrfNext.RejectGuardian:
                    plan.Say(TextKeys.UrfNoGuardian); // the office's policy, never «the law»
                    break;
                case UrfNext.Redirect:
                    Offer(askTopic: false); // B-11 divorce: the consultation only
                    break;
                case UrfNext.Book:
                    // B-11.6 (1.15): the price, the documents and the free times in the office; the pick holds it (B-08).
                    var service = booking!.Fit(c.Service);
                    work = new OfferTimes(service, null, TextKeys.UrfChooseSlot, null, new Dictionary<string, string>
                    {
                        ["service"] = service.Name,
                        ["amount"] = BookingWords.Money(service.Amount),
                        ["documents"] = c.Documents(),
                    });
                    Check(Intake.MoveTo(IntakeState.OfferSent));
                    break;
            }
        }

        /// <summary>B-11: one question per message; the closed ones as buttons (or a list for the guardian).</summary>
        private void AskUrf(string? step)
        {
            ReplyChoice Btn(string value, string key) => new(UrfAnswer.Id(step!, value), key);
            ReplyChoices YesNo() => new(InteractivePayload.Buttons, [Btn("yes", TextKeys.BtnYes), Btn("no", TextKeys.BtnNo)]);
            switch (step)
            {
                case UrfStep.Ages:
                    plan.Say(TextKeys.UrfAskAges);
                    break;
                case UrfStep.WifePast:
                    plan.Ask(TextKeys.UrfAskWifePast, new ReplyChoices(InteractivePayload.Buttons,
                        [Btn("first", TextKeys.BtnUrfFirst), Btn("divorced", TextKeys.BtnUrfDivorced), Btn("khul", TextKeys.BtnUrfKhul)]));
                    break;
                case UrfStep.Guardian:
                    plan.Ask(TextKeys.UrfAskGuardian, new ReplyChoices(InteractivePayload.List,
                    [
                        Btn("father", TextKeys.BtnUrfFather), Btn("brother", TextKeys.BtnUrfBrother), Btn("uncle", TextKeys.BtnUrfUncle),
                        Btn("none", TextKeys.BtnUrfNoGuardian),
                    ], TextKeys.BtnUrfGuardianList));
                    break;
                case UrfStep.Witnesses:
                    plan.Ask(TextKeys.UrfAskWitnesses, new ReplyChoices(InteractivePayload.Buttons,
                        [Btn("own", TextKeys.BtnUrfWitnessOwn), Btn("office", TextKeys.BtnUrfWitnessOffice)]));
                    break;
                case UrfStep.HasCopy:
                    plan.Ask(TextKeys.UrfAskCopy, YesNo());
                    break;
                case UrfStep.Agreed:
                    plan.Ask(TextKeys.UrfAskAgreed, YesNo());
                    break;
                case UrfStep.Children:
                    plan.Ask(TextKeys.UrfAskChildren, YesNo());
                    break;
            }
        }

        /// <summary>Words typed or said with a press or a receipt are not lost: a task, and the panel shows the turn's words.</summary>
        private void WordsToOffice()
        {
            if (turn.Messages.Any(m => m.Type == MessageType.Text && !string.IsNullOrWhiteSpace(m.Text)
                                       || !string.IsNullOrWhiteSpace(m.Caption)
                                       || m.Transcript is { Status: Transcript.Done, Text.Length: > 0 }))
            {
                plan.Task(PendingTask.UnclearMessage);
            }
        }

        public void Respond(bool firstContact)
        {
            // S-01: insults or spam — a warning once, then silence for good (the manager unmutes).
            if (c?.Intent == Intent.Abusive)
            {
                if (State.Conversation.AbuseSeen())
                {
                    plan.Say(TextKeys.AbuseWarning);
                    plan.Task(PendingTask.Complaint);
                }

                return;
            }

            Intake.RecordMatter(c?.Matter is { } m ? Classification.Code(m) : null, c?.Subtype is { } st ? Classification.Code(st) : null);
            Intake.NoteSenderRole(c is null ? null : Classification.Code(c.Role));

            // B-14.2: the lawyer's direct list — no name question, a task for the lawyer.
            if (list.LawyerDirect)
            {
                if (!Intake.IsClassified)
                {
                    Check(Intake.Classify(ClientType.LawyerDirect));
                }

                plan.Say(TextKeys.LawyerDirectReceived);
                plan.Task(PendingTask.LawyerDirectMessage);
                return;
            }

            // v1.31 (B-01.7): a returning client (his last request finished, a new intake): «أهلًا بحضرتك تاني يا …», then
            // what a new client gets after his name (B-01.3 / B-01.4): his request if he said one, else the services list.
            // A thanks or a goodbye is answered as it is, without the welcome.
            if (returning)
            {
                if (c?.Intent == Intent.ThanksOrClosing)
                {
                    Answer(c);
                    return;
                }

                // v1.32: a client who never gave his name is welcomed as a new one
                var first = ReturningClient.FirstName(State.Person);
                if (first is not null)
                {
                    plan.Say(TextKeys.WelcomeBack, Fill: new Dictionary<string, string> { ["first_name"] = first });
                }

                if (c is not null && IsRequest(c.Intent))
                {
                    Answer(c);
                }
                else
                {
                    Welcome(back: first is not null);
                }

                AnswerMediaProblems();
                return;
            }

            // v1.32 (B-02): the name asked at the booking: this message answers it, and the time waiting is booked.
            if (Intake.PickWaitingForName is { } pickId && !State.Person.NameSettled)
            {
                AnswerNameForPick(pickId);
                return;
            }

            // v1.32 (B-01.1): the first message from an unknown number: no name question any more (it is asked at the
            // booking). A request is answered at once; anything else (a greeting, «ألو», a name, a photo) gets the welcome
            // with its two buttons. A name said on the way is kept.
            // v1.32: a name the client tells on the way (any message, before the booking asks it) is kept
            var named = !State.Person.NameSettled && Intake.State != IntakeState.AwaitingName
                        && c?.FullName is { } said && PersonName.TryParse(said, out var given, minWords: 1) && State.Person.GiveName(given);
            if ((firstContact || Intake.State == IntakeState.New) && (!State.Person.NameSettled || named))
            {
                Check(Intake.Classify(TypeOf()));
                if (AnswerByType())
                {
                    return;
                }

                if (Intake.ClientType == ClientType.ExistingClient || (c is not null && (IsRequest(c.Intent) || c.Intent == Intent.Abusive)))
                {
                    AnswerTurn();
                    return;
                }

                Welcome();
                AnswerMediaProblems();
                return;
            }

            if (Intake.State == IntakeState.New)
            {
                Check(Intake.Classify(TypeOf())); // known number with its name settled (B-01.5)
                if (AnswerByType())
                {
                    return;
                }

                AnswerTurn();
                return;
            }

            if (Intake.State == IntakeState.AwaitingName)
            {
                if (c is null)
                {
                    // A photo or file instead of the name is not a refusal (B-02): answer the file and
                    // ask again without using up the one extra ask.
                    AnswerMediaOnly();
                    plan.Say(TextKeys.AskNameAgain, TextKeys.WelcomeAskName);
                    return;
                }

                if (!HandleName(c))
                {
                    return; // asked for the name again
                }

                var pending = Intake.TakePending();
                Check(Intake.Classify(TypeOf())); // B-14 order (TASK 05a)
                if (AnswerByType())
                {
                    return;
                }

                if (Intake.ClientType == ClientType.ExistingClient)
                {
                    AnswerExistingClient(pending is not null ? ToClassification(pending) : c);
                    return;
                }

                if (pending is not null)
                {
                    Answer(ToClassification(pending));
                }
                else if (IsRequest(c.Intent))
                {
                    Answer(c);
                }
                else
                {
                    ServicesMenu(); // B-01.3 (1.30): the office's services; the consultation offer when they cannot be ordered
                }

                AnswerMediaProblems();
                return;
            }

            if (AnswerByType())
            {
                return;
            }

            AnswerTurn();
        }

        /// <summary>B-14 order: blocked (handled before) ← lawyer direct ← existing client ← colleague / press / vendor ← new client.</summary>
        private ClientType TypeOf() =>
            list.LawyerDirect ? ClientType.LawyerDirect
            : list.ExistingClient is not null ? ClientType.ExistingClient
            : Intake.SenderRole switch
            {
                "lawyer_colleague" => ClientType.Colleague,
                "press" => ClientType.Press,
                "vendor" => ClientType.Vendor,
                _ => ClientType.NewClient,
            };

        /// <summary>
        /// Colleagues, press and vendors (B-14.4): a neutral reply and a task once per request; later
        /// messages wait for the staff member who has the task (no repeated reply, no new task).
        /// </summary>
        private bool AnswerByType()
        {
            var key = Intake.ClientType switch
            {
                ClientType.Colleague => TextKeys.ColleagueReceived,
                ClientType.Press => TextKeys.PressReceived,
                ClientType.Vendor => TextKeys.VendorReceived,
                _ => null,
            };
            if (key is null)
            {
                return false;
            }

            if (Intake.State != IntakeState.HandedToStaff)
            {
                HandToOffice(key, PendingTask.ExternalContact);
            }

            return true;
        }

        private void AnswerTurn()
        {
            if (c is null)
            {
                AnswerMediaOnly();
                return;
            }

            if (Intake.ClientType == ClientType.ExistingClient)
            {
                AnswerExistingClient(c);
                return;
            }

            Answer(c);
            AnswerMediaProblems();
        }

        /// <summary>
        /// B-04. Hazem never confirms that the number is a client and says nothing about a case.
        /// The B-04 reply «وهيرد عليك [المسؤول]» stays off until the panel (owner's decision): the
        /// approved neutral text goes instead, with a task for the staff member in charge.
        /// </summary>
        private void AnswerExistingClient(Classification turnClass)
        {
            switch (turnClass.Intent)
            {
                case Intent.ExistingMatter:
                    plan.Say(TextKeys.ExistingMatter);
                    plan.Task(PendingTask.ExistingClientMessage, list.ExistingClient?.StaffId);
                    Check(Intake.MoveTo(IntakeState.HandedToStaff));
                    break;
                case Intent.Urgent:
                    // An urgent message from a listed client is most likely about their own case
                    // («الجلسة بكرة»): their staff member gets it at once, never the new-client price.
                    plan.Say(TextKeys.ExistingMatter);
                    plan.Task(PendingTask.UrgentRequest, list.ExistingClient?.StaffId);
                    Check(Intake.MoveTo(IntakeState.HandedToStaff));
                    break;
                case var i when IsNewMatter(i):
                    // A new matter under the same person: a new intake on the new-client path.
                    var closed = Intake;
                    Check(closed.MoveTo(IntakeState.Closed));
                    var fresh = Intake.Open(State.Person);
                    Check(fresh.Classify(ClientType.NewClient));
                    fresh.RecordMatter(turnClass.Matter is { } m ? Classification.Code(m) : null,
                        turnClass.Subtype is { } st ? Classification.Code(st) : null);
                    State.ClosedIntakes.Add(closed);
                    State = State with { Intake = fresh };
                    Answer(turnClass);
                    break;
                case Intent.HumanRequest or Intent.RescheduleOrCancel or Intent.ThanksOrClosing or Intent.OffTopic:
                    Answer(turnClass);
                    break;
                case Intent.OfficeInfo:
                    // TASK 06: the address or the hours do not depend on a new matter: answered at once, no offer.
                    AnswerOffice(turnClass.Topic, existingClient: true);
                    break;
                default:
                    if (!Intake.ExistingQuestionAsked)
                    {
                        plan.Say(TextKeys.ExistingOrNew); // asked once only
                        Intake.MarkExistingQuestionAsked();
                    }
                    else
                    {
                        Answer(turnClass);
                    }

                    break;
            }

            AnswerMediaProblems();
        }

        private static bool IsNewMatter(Intent i) =>
            i is Intent.ConsultationRequest or Intent.ServiceByName or Intent.PriceQuestion
                or Intent.LegalQuestion or Intent.DescribeMatter;

        /// <summary>B-02. True when Hazem can carry on (v1.29: any name, or the one extra ask is used up).</summary>
        private bool HandleName(Classification turnClass)
        {
            PersonName? name = null;
            if (turnClass.FullName is { } extracted && PersonName.TryParse(extracted, out var fromAi, minWords: 1)) // v1.29: one name too
            {
                name = fromAi;
            }
            else if ((turnClass.Intent == Intent.ProvideName || turnClass.IsFallback) && NameGuess.TryRead(PlainText(), out var typed))
            {
                // The AI says it is a name but did not extract it, or the AI is down/unsure: a short
                // letters-only message with no everyday words («عايز», «مش», «استشارة»…) is the name.
                name = typed;
            }

            if (name is not null && State.Person.GiveName(name))
            {
                return true;
            }

            RememberRequest();
            if (Intake.MayAskNameAgain)
            {
                Check(Intake.AskForName());
                plan.Say(TextKeys.AskNameAgain, TextKeys.WelcomeAskName);
                return false;
            }

            State.Person.ContinueWithoutFullName();
            return true;
        }

        private void Answer(Classification turnClass)
        {
            answering = turnClass;
            // v1.20 (B-11.1): after the age refusal, the same marriage gets the closing text (no offer, no price, no
            // buttons), twice at most, then silence. Any other matter goes on below.
            if (ageRefusal is not null && UrfRules.FollowsAgeRefusal(turnClass, ageRefusal))
            {
                if (ageRefusal.ClosingReplies < UrfRules.MaxClosingReplies)
                {
                    plan.Say(TextKeys.UrfAgeClosed);
                }

                return;
            }

            Intake.CountUnclear(turnClass.Intent == Intent.Other);
            switch (turnClass.Intent)
            {
                case Intent.ConsultationRequest:
                    Offer();
                    break;
                case Intent.LegalQuestion when UrfWaitsForTime:
                case Intent.Other when UrfWaitsForTime:
                    // v1.22: a question while the Urfi times are on the screen: the lawyer explains it at that
                    // appointment; no second offer (its buttons would replace the Urfi times).
                    plan.Say(TextKeys.UrfQuestionLater);
                    break;
                case Intent.LegalQuestion:
                    plan.Say(TextKeys.LegalQuestion); // B-20, then the offer
                    Offer();
                    break;
                case Intent.OffTopic:
                    plan.Say(TextKeys.OffTopic); // v1.22: no office task, no offer
                    break;
                case Intent.DescribeMatter when Intake.MatterAsked && turnClass.Service == Service.CaseRepresentation:
                    Offer(askTopic: false); // v1.28: his story asks for a case: the case offer, not «وصلت» alone
                    break;
                case Intent.DescribeMatter:
                    // B-25: the client told the topic. After the question: «تمام، وصلت ✅» only.
                    if (Intake.MatterAsked)
                    {
                        plan.Say(TextKeys.TopicReceived);
                    }
                    else
                    {
                        Offer();
                    }

                    break;
                case Intent.ServiceByName:
                    AnswerService(turnClass.Service);
                    break;
                case Intent.Urgent:
                    Urgent();
                    break;
                case Intent.PriceQuestion:
                    AnswerPrice(turnClass.Service);
                    break;
                case Intent.OfficeInfo:
                    AnswerOffice(turnClass.Topic);
                    break;
                case Intent.ExistingMatter:
                    HandToOffice(TextKeys.ExistingMatter, PendingTask.ExistingClientMessage);
                    break;
                case Intent.HumanRequest:
                    HandToOffice(TextKeys.HumanRequest, PendingTask.HumanRequested);
                    if (handoffEnabled)
                    {
                        Check(State.Conversation.ClientAskedForStaff()); // B-12: Hazem stays silent from now on
                    }

                    break;
                case Intent.RescheduleOrCancel when MayBook:
                    work = new ShowExisting(null); // B-15: the client's booking, with «change» and «cancel»
                    break;
                case Intent.RescheduleOrCancel:
                    HandToOffice(TextKeys.RescheduleOrCancel, PendingTask.RescheduleOrCancel);
                    break;
                case Intent.ProvideName when MayOrder:
                    // v1.32: a name told on its own (kept in Respond), after the welcome or an answer: «تحت أمرك، محتاج إيه؟»
                    plan.Say(TextKeys.Greeting, TextKeys.ConsultationOffer);
                    break;
                case Intent.Greeting:
                case Intent.ProvideName:
                    plan.Say(TextKeys.Greeting, TextKeys.ConsultationOffer);
                    break;
                case Intent.ThanksOrClosing:
                    plan.Say(TextKeys.Thanks);
                    break;
                case var _ when urfOn && Urf is { State: UrfCase.Collecting }:
                    AfterUrf(UrfNext.Ask); // B-11: the question still waiting, again (words without its answer)
                    break;
                default:
                    if (Intake.UnclearInARow <= 1)
                    {
                        plan.Say(TextKeys.Unclear, TextKeys.ConsultationOffer);
                    }
                    else
                    {
                        HandToOffice(TextKeys.UnclearAgain, PendingTask.UnclearMessage);
                    }

                    break;
            }
        }

        private void AnswerService(Service? service)
        {
            if (OrderWithFiles(service))
            {
                return;
            }

            switch (service)
            {
                case Service.ConsultationUrgent:
                    Urgent();
                    break;
                case Service.DocumentReview:
                    plan.Say(TextKeys.DocumentReviewRequest); // B-16 step 1
                    plan.Task(PendingTask.ServiceRequest);
                    Check(Intake.MoveTo(IntakeState.InServiceFlow));
                    OpenOrder(service);
                    break;
                case Service.ContractDrafting:
                    InService(TextKeys.ContractDraftingRequest);
                    OpenOrder(service);
                    break;
                case Service.FormalNotice:
                    InService(TextKeys.FormalNoticeRequest);
                    OpenOrder(service);
                    break;
                case Service.UrfMarriage or Service.UrfDivorce when urfOn && MayBook:
                    StartUrf(service == Service.UrfMarriage ? UrfCase.Marriage : UrfCase.Divorce); // TASK 11: B-11's questions
                    break;
                case Service.UrfMarriage:
                case Service.UrfDivorce:
                    InService(TextKeys.UrfRequest); // without the Urfi texts or the booking: the office takes it
                    break;
                case Service.UrfUnclear:
                    plan.Say(TextKeys.UrfUnclear);
                    Check(Intake.MoveTo(IntakeState.InServiceFlow));
                    break;
                case Service.UrfProof:
                    plan.Say(TextKeys.UrfProof); // B-11: the only exception to B-20, then the offer
                    Offer();
                    break;
                case Service.WrittenOpinion:
                    plan.Say(TextKeys.WrittenOpinion); // B-17, then the offer
                    Offer();
                    break;
                default:
                    Offer(); // consultations, case representation (priced after it) or unknown
                    break;
            }
        }

        /// <summary>
        /// TASK 06 (B-20, 1.17): a question about the office, answered from its approved text filled from the panel (the address,
        /// the week, the payment details, the price list), then the offer. A detail not written in the panel yet never leaves
        /// half a message: the offer alone, and the office gets a task to write it. A listed client (B-04) gets the answer
        /// without the offer, or the neutral reply and their staff member when there is nothing to answer with.
        /// </summary>
        private void AnswerOffice(InfoTopic? topic, bool existingClient = false)
        {
            // v1.22: who Hazem is (alone), and whether the office does one specific thing (the office answers: a task,
            // never a guess). Neither depends on a matter, so no offer after them.
            if (topic == InfoTopic.Assistant)
            {
                plan.Say(TextKeys.AboutHazem);
                return;
            }

            if (topic == InfoTopic.ServiceCheck)
            {
                plan.Say(TextKeys.ServiceCheck);
                plan.Task(PendingTask.ServiceQuestion);
                return;
            }

            (string Key, string[] Needs)? answer = topic switch
            {
                InfoTopic.Address => (TextKeys.OfficeAddress, ["address", "map"]),
                InfoTopic.Hours => (TextKeys.OfficeHours, ["hours"]),
                InfoTopic.HowToBook => (TextKeys.OfficeHowToBook, []),
                InfoTopic.HowToPay => (TextKeys.OfficeHowToPay, ["instapay", "wallet"]),
                InfoTopic.Refund => (TextKeys.OfficeRefund, []),
                InfoTopic.Prices => (TextKeys.OfficePrices, []),
                InfoTopic.FeePolicy => (TextKeys.OfficeFeePolicy, []),
                InfoTopic.PracticeAreas => (TextKeys.OfficePracticeAreas, []),
                InfoTopic.Lawyer => (TextKeys.AboutLawyer, []), // v1.22: the owner's words, shortened (B-21)
                _ => null, // mode: the offer itself says both ways and their prices
            };
            var answered = false;
            if (answer is var (key, needs))
            {
                if (needs.All(n => office?.GetValueOrDefault(n) is { Length: > 0 }))
                {
                    plan.Say(key, Fill: office);
                    answered = true;
                }
                else
                {
                    plan.Task(PendingTask.OfficeInfoMissing);
                }
            }

            if (!existingClient)
            {
                Offer(askTopic: false);
            }
            else if (!answered)
            {
                plan.Say(TextKeys.ExistingMatter);
                plan.Task(PendingTask.ExistingClientMessage, list.ExistingClient?.StaffId);
            }
        }

        private void AnswerPrice(Service? service)
        {
            var key = service switch
            {
                Service.ConsultationUrgent => TextKeys.UrgentPrice,
                Service.DocumentReview => TextKeys.PriceDocumentReview,
                Service.ContractDrafting => TextKeys.PriceContractDrafting,
                Service.FormalNotice => TextKeys.PriceFormalNotice,
                Service.UrfMarriage or Service.UrfDivorce or Service.UrfUnclear => TextKeys.PriceUrf,
                Service.WrittenOpinion => TextKeys.WrittenOpinion,
                Service.CaseRepresentation => TextKeys.PriceCaseRepresentation, // never a number (B-23)
                _ => null, // consultations: the offer itself has the prices
            };

            if (key is not null)
            {
                plan.Say(key, TextKeys.ConsultationOffer);
            }

            Offer();
        }

        /// <summary>
        /// B-09 first sentence and, by the owner's decision, the offer (never the topic question).
        /// TASK 08: the first sentence, then today's free times (B-09.2).
        /// v1.33 (B-09): the first sentence, then the first free moment held at once and where to pay (no times, no name).
        /// </summary>
        private void Urgent()
        {
            plan.Say(TextKeys.UrgentPrice);
            plan.Task(PendingTask.UrgentRequest);
            if (MayBook && urgentOn)
            {
                work = new UrgentHold();
                Check(Intake.MoveTo(IntakeState.OfferSent));
                return;
            }

            if (MayBook)
            {
                work = new OfferTimes(BookingService.Urgent, null, TextKeys.BookingChooseSlot, null);
                Check(Intake.MoveTo(IntakeState.OfferSent));
                return;
            }

            Offer(askTopic: false);
        }

        /// <summary>
        /// The consultation offer. B-25: when the topic is still unknown, the one optional question
        /// follows it in the same answer — once per request, never on the urgent path.
        /// TASK 08: with buttons for the kind of consultation, or the free times when the kind is known.
        /// </summary>
        private void Offer(bool askTopic = true)
        {
            if (UrfWaitsForTime)
            {
                return; // v1.22: the Urfi times are waiting for a pick; a consultation offer would replace them
            }

            var known = MayBook ? KindAsked() : null;
            // v1.28: a client who wants a case filed (the AI's service, from his words or his story) hears that the lawyer
            // listens first and sets the fees, and that the consultation counts towards them. A price question keeps its own.
            var offer = answering is { Service: Service.CaseRepresentation, Intent: not Intent.PriceQuestion }
                ? TextKeys.CaseOffer
                : TextKeys.ConsultationOffer;
            if (MayBook && known is null)
            {
                plan.Ask(offer, new ReplyChoices(InteractivePayload.Buttons,
                [
                    new ReplyChoice(BookingChoice.Mode(BookingService.PhoneVideo), TextKeys.BtnBookPhoneVideo),
                    new ReplyChoice(BookingChoice.Mode(BookingService.Office), TextKeys.BtnBookOffice),
                ]), fallback: TextKeys.ConsultationOffer);
            }
            else
            {
                plan.Say(offer, TextKeys.ConsultationOffer);
            }

            if (askTopic && !Intake.MatterKnown && !Intake.MatterAsked)
            {
                plan.Say(TextKeys.AskTopic);
                Intake.MarkMatterAsked();
            }

            if (known is not null)
            {
                // 1.25 (B-23): a phone or video consultation first asks where the lawyer calls.
                work = known == BookingService.PhoneVideo ? new AskCallApp() : new OfferTimes(known, null, TextKeys.BookingChooseSlot, null);
            }

            Check(Intake.MoveTo(IntakeState.OfferSent));
        }

        /// <summary>
        /// v1.30 (B-01.3): «اختار الخدمة… أو احكيلي الموضوع» with the office's services as a list. The two consultations are the
        /// booking's own buttons (their times follow); the others start their flow without the AI (<see cref="ServiceRow"/>).
        /// Without the booking the consultations ask the consultation offer; a sender who cannot order gets the offer alone.
        /// </summary>
        private void ServicesMenu()
        {
            if (!MayOrder)
            {
                Offer();
                return;
            }

            // The price under every row (the owner: the client knows it before he picks); the urgent one first.
            List<ReplyChoice> rows = [new(ServiceRow.Id(ServiceRow.Urgent), TextKeys.RowUrgent, DescriptionKey: TextKeys.RowUrgentDesc)];
            rows.AddRange(MayBook
                ?
                [
                    new(BookingChoice.Mode(BookingService.PhoneVideo), TextKeys.RowConsultPhone, DescriptionKey: TextKeys.RowConsultPhoneDesc),
                    new(BookingChoice.Mode(BookingService.Office), TextKeys.RowConsultOffice, DescriptionKey: TextKeys.RowConsultOfficeDesc),
                ]
                : [new(ServiceRow.Id(ServiceRow.Consult), TextKeys.RowConsultPhone, DescriptionKey: TextKeys.RowConsultPhoneDesc)]);
            rows.AddRange(
            [
                new(ServiceRow.Id(ServiceRow.Case), TextKeys.RowCase, DescriptionKey: TextKeys.RowCaseDesc),
                new(ServiceRow.Id(ServiceRow.Review), TextKeys.RowReview, DescriptionKey: TextKeys.RowReviewDesc),
                new(ServiceRow.Id(ServiceRow.Drafting), TextKeys.RowDrafting, DescriptionKey: TextKeys.RowDraftingDesc),
                new(ServiceRow.Id(ServiceRow.Notice), TextKeys.RowNotice, DescriptionKey: TextKeys.RowNoticeDesc),
                new(ServiceRow.Id(ServiceRow.Opinion), TextKeys.RowOpinion, DescriptionKey: TextKeys.RowOpinionDesc),
                new(ServiceRow.Id(ServiceRow.Urf), TextKeys.RowUrf, DescriptionKey: TextKeys.RowUrfDesc),
                new(ServiceRow.Id(ServiceRow.Tell), TextKeys.RowTell),
            ]);
            plan.Ask(TextKeys.ServicesMenu, new ReplyChoices(InteractivePayload.List, rows, TextKeys.BtnServicesList),
                fallback: TextKeys.ConsultationOffer);
            Check(Intake.MoveTo(IntakeState.OfferSent));
        }

        /// <summary>The kind of consultation the client already named (by service or by phone / office).</summary>
        private BookingService? KindAsked() => answering switch
        {
            { Service: Service.ConsultationOffice } or { Mode: ConsultationMode.Office } => BookingService.Office,
            { Service: Service.ConsultationPhoneVideo } or { Mode: ConsultationMode.PhoneVideo } => BookingService.PhoneVideo,
            _ => null,
        };

        // ---------- TASK 08: the booking (B-08, B-09, B-15, B-22) ----------

        public async Task RunBookingAsync(BookingDesk desk, HazemSettings settings, CancellationToken ct)
        {
            // Null only for a reminder's button with the booking from WhatsApp off (TASK 12): those two need no booking settings.
            var b = booking!;
            switch (work)
            {
                case AttendIt x:
                    await AttendAsync(desk, x.Id, x.At, ct);
                    return;
                case ReminderChange when booking is null:
                    // Hazem cannot move or cancel without the booking: the office does it (as before TASK 08).
                    HandToOffice(TextKeys.RescheduleOrCancel, PendingTask.RescheduleOrCancel);
                    return;
                case ReminderChange x:
                    await ShowExistingAsync(desk, x.Id, ct); // «تحب تغيّر الميعاد ولا تلغيه؟»: B-15 and B-22 as they are
                    return;
            }

            switch (work)
            {
                case AskCallApp:
                    AskApp(b);
                    break;
                case OfferTimes o:
                    await OfferTimesAsync(desk, b, settings, o.Service, o.After ?? plan.Now, o.IntroKey, o.Moving, ct, o.Fill, o.App);
                    break;
                case UrgentHold:
                    await UrgentHoldAsync(desk, b, settings, ct);
                    break;
                case PickTime p:
                    await PickAsync(desk, b, settings, p.Service, p.At, ct, p.App);
                    break;
                case ShowExisting s:
                    await ShowExistingAsync(desk, s.Id, ct);
                    break;
                case StartChange s:
                    await StartChangeAsync(desk, b, settings, s.Id, ct);
                    break;
                case ChangeTo t:
                    await ChangeToAsync(desk, b, settings, t.Id, t.At, ct);
                    break;
                case MoreChanges m when await OwnAsync(desk, m.Id, ct) is { State: AppointmentInfo.Confirmed } a:
                    await OfferTimesAsync(desk, b, settings, ServiceOf(a), m.After, TextKeys.BookingChooseSlot, a.Id, ct);
                    break;
                case MoreChanges:
                    await ShowExistingAsync(desk, null, ct);
                    break;
                case CancelIt x:
                    await CancelAsync(desk, x.Id, ct);
                    break;
                case RefundIt r when await OwnAsync(desk, r.Id, ct) is { State: AppointmentInfo.CancelledByOffice } a
                                     && await desk.RequestRefundAsync(a.Id, "office_cancelled", ct) is not null:
                    // B-22: the office cancelled and the client chose the refund. The amount is what came
                    // (or what was due, before the review); the office transfers it (P2).
                    plan.Say(TextKeys.BookingRefundRequested);
                    plan.Task(PendingTask.Refund);
                    break;
                case RefundIt r when await OwnAsync(desk, r.Id, ct) is { State: AppointmentInfo.CancelledByOffice } a
                                     && await desk.PaymentAsync(a.Id, ct) is { State: PaymentInfo.RefundRequested or PaymentInfo.Refunded }:
                    plan.Say(TextKeys.BookingRefundRequested); // pressed twice: already with the office
                    break;
                case RefundIt:
                    await ShowExistingAsync(desk, null, ct);
                    break;
                case Receipted r:
                    await ReceiptedAsync(desk, b, settings, r.Outcome, ct);
                    break;
            }
        }

        /// <summary>
        /// B-05 (1.13): what the receipt did. Confirmed → the confirmation (the payment is never said to be
        /// confirmed). A new receipt for a confirmed booking → «استلمنا». The hold had run out and its time is
        /// taken → the nearest times, and the receipt pays for the one he picks.
        /// </summary>
        private async Task ReceiptedAsync(BookingDesk desk, BookingSettings b, HazemSettings settings, ReceiptOutcome outcome, CancellationToken ct)
        {
            var a = outcome.AppointmentId is { } id ? await OwnAsync(desk, id, ct) : null;
            if (a is null)
            {
                plan.Say(TextKeys.ProofReceivedAgain);
                return;
            }

            if (urgentOn && ServiceOf(a).IsUrgent)
            {
                // v1.33 (B-09): the call set at the first free moment from the receipt (the same receipt again: as it is now).
                if (outcome is { Status: ReceiptStatus.Confirmed or ReceiptStatus.ConfirmedLate, Retimed: true }
                    || outcome.Status == ReceiptStatus.Exists && a.State == AppointmentInfo.Confirmed
                                                              && await desk.PaymentAsync(a.Id, ct) is not { State: PaymentInfo.Verified })
                {
                    SayUrgentPaid(a, b);
                    return;
                }

                if (outcome.Status == ReceiptStatus.LateTaken)
                {
                    // No free moment within the horizon: the receipt is kept and the office calls (never a list of times).
                    plan.Say(TextKeys.BookingNoSlots);
                    plan.Task(PendingTask.BookingUnavailable);
                    return;
                }
            }

            switch (outcome.Status)
            {
                case ReceiptStatus.Exists when a.State == AppointmentInfo.Confirmed
                                               && await desk.PaymentAsync(a.Id, ct) is { State: PaymentInfo.Verified }:
                    plan.Say(TextKeys.ProofReceivedAgain); // 1.25: already confirmed after the review, not «waiting» again
                    break;
                case ReceiptStatus.Confirmed or ReceiptStatus.ConfirmedLate:
                case ReceiptStatus.Exists when a.State == AppointmentInfo.Confirmed:
                    plan.Say(TextKeys.BookingPaid, Fill: Describe(a));
                    if (Urf is { State: UrfCase.Booked } u && u.AppointmentId == a.Id)
                    {
                        // B-11 (1.15): now the documents (an image before the receipt would have been taken as the receipt).
                        plan.Say(TextKeys.UrfSendDocuments, Fill: new Dictionary<string, string> { ["documents"] = u.Documents() });
                    }

                    break;
                case ReceiptStatus.LateTaken:
                    await OfferTimesAsync(desk, b, settings, ServiceOf(a), plan.Now, TextKeys.BookingHoldLate, null, ct, Describe(a), a.CallApp);
                    break;
                default:
                    plan.Say(TextKeys.ProofReceivedAgain);
                    break;
            }
        }

        /// <summary>
        /// B-08.2 / B-09.2: the first free times as a list, and «مواعيد تانية». No calendar → nothing is
        /// offered (B-24) and the office gets a task. Urgent with nothing today → tomorrow (B-09.4).
        /// </summary>
        private async Task OfferTimesAsync(BookingDesk desk, BookingSettings b, HazemSettings settings, BookingService service,
            DateTimeOffset after, string introKey, Guid? moving, CancellationToken ct, IReadOnlyDictionary<string, string>? fill = null,
            string? app = null)
        {
            var now = plan.Now;
            var count = b.Rules.OfferCount;
            var today = BookingDesk.Today(now);
            var found = await desk.FindAsync(b, settings, service, now, after, service.IsUrgent ? today : null, count, moving, ct);
            if (!found.CalendarAvailable)
            {
                plan.Say(TextKeys.BookingUnavailable);
                plan.Task(PendingTask.BookingUnavailable);
                return;
            }

            var slots = found.Slots;
            if (service.IsUrgent && slots.Count == 0 && after <= now)
            {
                slots = (await desk.FindAsync(b, settings, service, now, now, today.AddDays(1), count, moving, ct)).Slots;
                introKey = TextKeys.BookingUrgentNoneToday;
            }

            if (slots.Count == 0)
            {
                plan.Say(TextKeys.BookingNoSlots);
                plan.Task(PendingTask.BookingUnavailable);
                return;
            }

            var rows = slots.Select(s => new ReplyChoice(
                moving is { } m ? BookingChoice.MoveTo(m, s.Start) : BookingChoice.Pick(service, s.Start, app),
                Title: BookingWords.Row(s.Start, now))).ToList();
            if (slots.Count == count && !service.IsUrgent)
            {
                var last = slots[^1].Start;
                rows.Add(new ReplyChoice(moving is { } m ? BookingChoice.MoreMoves(m, last) : BookingChoice.More(service, last, app), TextKeys.BtnMoreSlots));
            }

            plan.Ask(introKey, new ReplyChoices(InteractivePayload.List, rows, TextKeys.BtnSlotsList), Fill: fill);
        }

        /// <summary>1.25 (B-23): «تحب المكالمة تبقى على إيه؟» — Google Meet only once «بيانات المكتب» has its link.</summary>
        private void AskApp(BookingSettings b)
        {
            List<ReplyChoice> apps =
            [
                new(BookingChoice.Call(CallApp.WhatsApp), TextKeys.BtnAppWhatsApp),
                new(BookingChoice.Call(CallApp.FaceTime), TextKeys.BtnAppFaceTime),
            ];
            if (b.Office.MeetLink.Trim().Length > 0)
            {
                apps.Add(new(BookingChoice.Call(CallApp.Meet), TextKeys.BtnAppMeet));
            }

            plan.Ask(TextKeys.AskCallApp, new ReplyChoices(InteractivePayload.Buttons, apps));
        }

        /// <summary>
        /// B-08.3 (1.13): the time is held until the receipt (or confirmed at once when a receipt he already
        /// sent pays for it), or why not.
        /// </summary>
        private async Task PickAsync(BookingDesk desk, BookingSettings b, HazemSettings settings, BookingService service, DateTimeOffset at,
            CancellationToken ct, string? app = null)
        {
            // 1.25 (B-23): a call is WhatsApp unless the client picked another app (the urgent call always).
            var callApp = service == BookingService.PhoneVideo ? app ?? CallApp.WhatsApp : service.IsUrgent ? CallApp.WhatsApp : null;
            var request = new BookRequest(Guid.NewGuid(), State.Person.Id, Intake.Id, service, at, turn.TurnId, b.Rules.BufferMinutes,
                Intake.MatterType, b.Hold.Until(plan.Now, at), callApp);
            var outcome = await desk.BookAsync(b, settings, request, plan.Now, ct);
            var a = outcome?.AppointmentId is { } id ? await OwnAsync(desk, id, ct) : null;
            if (service.IsUrf && a is { State: AppointmentInfo.PendingPayment or AppointmentInfo.Confirmed } && a.Service == service.Code
                && Urf is { State: UrfCase.DocumentsPending or UrfCase.Booked } u)
            {
                Urf = u with { State = UrfCase.Booked, AppointmentId = a.Id }; // B-11.6: the case has its time in the office
                UrfChanged = true;
            }

            switch (outcome?.Status)
            {
                case BookStatus.Held or BookStatus.Exists when a is { State: AppointmentInfo.PendingPayment }:
                    SayHold(a, b);
                    break;
                case BookStatus.BookedPaid or BookStatus.Exists when a is { State: AppointmentInfo.Confirmed }:
                    plan.Say(TextKeys.BookingPaid, Fill: Describe(a));
                    break;
                case BookStatus.HasHold when a is { HoldUntil: { } until }:
                    // P3: one hold at a time, so every receipt is for a booking we know.
                    plan.Say(TextKeys.BookingHoldOne, Fill: new Dictionary<string, string>(Describe(a))
                    {
                        ["deadline"] = BookingWords.Deadline(until, plan.Now),
                    });
                    break;
                case BookStatus.AlreadyBooked or BookStatus.Exists:
                    await ShowExistingAsync(desk, outcome.AppointmentId, ct);
                    break;
                default:
                    await OfferTimesAsync(desk, b, settings, service, plan.Now, TextKeys.BookingSlotTaken, null, ct, app: app);
                    break;
            }
        }

        /// <summary>B-08.4 (1.13): the hold with its steps, the amount, where to pay and the exact time the receipt is due.</summary>
        private void SayHold(AppointmentInfo a, BookingSettings b)
        {
            var fill = new Dictionary<string, string>(Describe(a))
            {
                ["amount"] = BookingWords.Money(a.Amount),
                ["deadline"] = BookingWords.Deadline(a.HoldUntil ?? a.Start, plan.Now),
            };
            if (b.Office.HasPayment)
            {
                fill["instapay"] = b.Office.InstaPay;
                fill["wallet"] = b.Office.Wallet;
            }
            else
            {
                // P1: the payment details are not written yet. The text keeps its open slots and is
                // not sent (never «InstaPay: » with nothing after it); the office gets a task.
                plan.Task(PendingTask.BookingMessageFailed);
            }

            plan.Say(TextKeys.BookingHold, Fill: fill);
        }

        /// <summary>
        /// v1.33 (B-09): the urgent call held at the first moment the lawyer is free (off the slot grid, no minimum notice), and
        /// where to pay; not today → first when the lawyer answers urgent calls (urgent_night). No calendar → as any booking.
        /// </summary>
        private async Task UrgentHoldAsync(BookingDesk desk, BookingSettings b, HazemSettings settings, CancellationToken ct)
        {
            // Asked again while his urgent hold waits: that hold is no obstacle (book_appointment replaces it).
            var mine = await desk.UpcomingForAsync(State.Person.Id, plan.Now, ct) is { State: AppointmentInfo.PendingPayment } h && ServiceOf(h).IsUrgent
                ? h.Id
                : (Guid?)null;
            BookOutcome? outcome = null;
            for (var attempt = 0; attempt < 2 && outcome is null or { Status: BookStatus.Taken }; attempt++) // taken a moment ago: once more
            {
                var free = await desk.FirstFreeAsync(b, settings, plan.Now, mine, ct);
                if (!free.CalendarAvailable)
                {
                    plan.Say(TextKeys.BookingUnavailable);
                    plan.Task(PendingTask.BookingUnavailable);
                    return;
                }

                if (free.Start is not { } start)
                {
                    break;
                }

                outcome = await desk.BookUrgentAsync(b, new BookRequest(Guid.NewGuid(), State.Person.Id, Intake.Id, BookingService.Urgent, start,
                    turn.TurnId, b.Rules.BufferMinutes, Intake.MatterType, UrgentRules.HoldUntil(b.Hold, plan.Now, start), CallApp.WhatsApp), ct);
            }

            var a = outcome?.AppointmentId is { } id ? await OwnAsync(desk, id, ct) : null;
            switch (outcome?.Status)
            {
                case BookStatus.Held or BookStatus.Exists when a is { State: AppointmentInfo.PendingPayment }:
                    SayUrgentPay(a, b);
                    break;
                case BookStatus.BookedPaid or BookStatus.Exists when a is { State: AppointmentInfo.Confirmed } && ServiceOf(a).IsUrgent:
                    SayUrgentPaid(a, b); // a receipt he already sent pays for it (B-05)
                    break;
                case BookStatus.HasHold when a is { HoldUntil: { } until }:
                    plan.Say(TextKeys.BookingHoldOne, Fill: new Dictionary<string, string>(Describe(a))
                    {
                        ["deadline"] = BookingWords.Deadline(until, plan.Now),
                    });
                    break;
                case BookStatus.AlreadyBooked or BookStatus.Exists:
                    await ShowExistingAsync(desk, outcome.AppointmentId, ct);
                    break;
                default:
                    plan.Say(TextKeys.BookingNoSlots);
                    plan.Task(PendingTask.BookingUnavailable);
                    break;
            }
        }

        /// <summary>
        /// v1.33 (B-09): urgent_pay with the amount and where to pay (as the hold, P1: without the office's details it is not
        /// sent and the office gets a task); before it urgent_night when the first free moment is not today.
        /// </summary>
        private void SayUrgentPay(AppointmentInfo a, BookingSettings b)
        {
            if (BookingDesk.Today(a.Start) != BookingDesk.Today(plan.Now))
            {
                plan.Say(TextKeys.UrgentNight, Fill: new Dictionary<string, string> { ["opens"] = UrgentTexts.Opens(a.Start) });
            }

            var fill = new Dictionary<string, string> { ["amount"] = BookingWords.Money(a.Amount) };
            if (b.Office.HasPayment)
            {
                fill["instapay"] = b.Office.InstaPay;
                fill["wallet"] = b.Office.Wallet;
            }
            else
            {
                plan.Task(PendingTask.BookingMessageFailed);
            }

            plan.Say(TextKeys.UrgentPay, Fill: fill);
        }

        /// <summary>
        /// v1.33 (B-09): the receipt came and the call has its time: within urgent.call_within_minutes → urgent_now; later today
        /// (the lawyer is in a call) → urgent_after; not today → B-08's booking_paid.
        /// </summary>
        private void SayUrgentPaid(AppointmentInfo a, BookingSettings b)
        {
            if (a.Start <= plan.Now.AddMinutes(b.Urgent.CallWithinMinutes))
            {
                plan.Say(TextKeys.UrgentNow);
            }
            else if (BookingDesk.Today(a.Start) == BookingDesk.Today(plan.Now))
            {
                plan.Say(TextKeys.UrgentAfter, Fill: new Dictionary<string, string> { ["time"] = BookingWords.Time(a.Start) });
            }
            else
            {
                plan.Say(TextKeys.BookingPaid, Fill: Describe(a));
            }
        }

        /// <summary>The client's booking with «change» and «cancel»; none → the office, as before TASK 08.</summary>
        private async Task ShowExistingAsync(BookingDesk desk, Guid? id, CancellationToken ct)
        {
            var a = id is { } known ? await OwnAsync(desk, known, ct) : await desk.UpcomingForAsync(State.Person.Id, plan.Now, ct);
            if (a is not { IsOpen: true } || a.Start <= plan.Now)
            {
                HandToOffice(TextKeys.RescheduleOrCancel, PendingTask.RescheduleOrCancel);
                return;
            }

            plan.Ask(TextKeys.BookingExisting, new ReplyChoices(InteractivePayload.Buttons,
            [
                new ReplyChoice(BookingChoice.Change(a.Id), TextKeys.BtnReschedule),
                new ReplyChoice(BookingChoice.Cancel(a.Id), TextKeys.BtnCancelBooking),
            ]), Fill: Describe(a));
        }

        /// <summary>B-15 (P6): the one free change, 12 hours before or more; otherwise the lawyer decides.</summary>
        private async Task StartChangeAsync(BookingDesk desk, BookingSettings b, HazemSettings settings, Guid id, CancellationToken ct)
        {
            if (await OwnAsync(desk, id, ct) is not { IsOpen: true } a || a.Start <= plan.Now)
            {
                await ShowExistingAsync(desk, null, ct);
                return;
            }

            if (a.State == AppointmentInfo.PendingPayment)
            {
                // B-15 (1.13): a hold is not moved, it is replaced by the time he picks; not counted as the free change.
                await OfferTimesAsync(desk, b, settings, ServiceOf(a), plan.Now, TextKeys.BookingChooseSlot, null, ct, app: a.CallApp);
                return;
            }

            if (a.Reschedules >= 1 || a.Start - plan.Now < TimeSpan.FromHours(12))
            {
                plan.Say(TextKeys.RescheduleOrCancel);
                plan.Task(PendingTask.LateReschedule);
                return;
            }

            await OfferTimesAsync(desk, b, settings, ServiceOf(a), plan.Now, TextKeys.BookingChooseSlot, a.Id, ct);
        }

        private async Task ChangeToAsync(BookingDesk desk, BookingSettings b, HazemSettings settings, Guid id, DateTimeOffset at,
            CancellationToken ct)
        {
            if (await OwnAsync(desk, id, ct) is not { State: AppointmentInfo.Confirmed } a)
            {
                await ShowExistingAsync(desk, null, ct);
                return;
            }

            if (a.Reschedules >= 1 || a.Start - plan.Now < TimeSpan.FromHours(12))
            {
                plan.Say(TextKeys.RescheduleOrCancel);
                plan.Task(PendingTask.LateReschedule);
                return;
            }

            switch (await desk.MoveAsync(b, settings, a, at, turn.TurnId, plan.Now, ct))
            {
                case MoveStatus.Moved or MoveStatus.Exists:
                    plan.Say(TextKeys.BookingRescheduled, Fill: new Dictionary<string, string>
                    {
                        ["day"] = BookingWords.Day(at, plan.Now),
                        ["time"] = BookingWords.Time(at),
                    });
                    break;
                case MoveStatus.Taken:
                    await OfferTimesAsync(desk, b, settings, ServiceOf(a), plan.Now, TextKeys.BookingSlotTaken, a.Id, ct);
                    break;
                case MoveStatus.NotAllowed:
                    plan.Say(TextKeys.RescheduleOrCancel);
                    plan.Task(PendingTask.LateReschedule);
                    break;
                default:
                    await ShowExistingAsync(desk, null, ct);
                    break;
            }
        }

        /// <summary>
        /// The client cancels. A hold: nothing was paid. A confirmed booking, B-22: within 24 hours of his
        /// receipt (and before it starts) a full refund, which the office transfers; later none.
        /// </summary>
        private async Task CancelAsync(BookingDesk desk, Guid id, CancellationToken ct)
        {
            var a = await OwnAsync(desk, id, ct);
            if (a is { State: AppointmentInfo.CancelledByClient })
            {
                plan.Say(TextKeys.BookingCancelled); // pressed twice
                return;
            }

            if (a is not { IsOpen: true } || !await desk.CancelAsync(a.Id, AppointmentInfo.CancelledByClient, ct))
            {
                await ShowExistingAsync(desk, null, ct);
                return;
            }

            if (a.State == AppointmentInfo.PendingPayment)
            {
                plan.Say(TextKeys.BookingHoldCancelled);
                return;
            }

            if (await desk.PaymentAsync(a.Id, ct) is not { ProofAt: { } paidAt })
            {
                plan.Say(TextKeys.BookingCancelled); // booked before TASK 09 and never paid: the office checks
                plan.Task(PendingTask.BookingCancelled);
                return;
            }

            if (plan.Now - paidAt <= TimeSpan.FromHours(24) && plan.Now < a.Start)
            {
                if (await desk.RequestRefundAsync(a.Id, "client_within_24h", ct) is not null)
                {
                    plan.Say(TextKeys.RefundFull);
                    plan.Task(PendingTask.Refund);
                }
                else
                {
                    plan.Say(TextKeys.BookingCancelled); // marked «ما وصلش»: nothing to give back, the office checks
                    plan.Task(PendingTask.BookingCancelled);
                }

                return;
            }

            plan.Say(TextKeys.RefundNone);
            plan.Task(PendingTask.BookingCancelled);
        }

        /// <summary>
        /// TASK 12 (B-18): «هحضر ✅» from a reminder, for the appointment's current time only. A reminder of a time that
        /// moved tells the new one (nothing recorded); a booking that ended or was cancelled → his current one, or the office.
        /// </summary>
        private async Task AttendAsync(BookingDesk desk, Guid id, DateTimeOffset at, CancellationToken ct)
        {
            if (await OwnAsync(desk, id, ct) is not { State: AppointmentInfo.Confirmed } a || a.Start <= plan.Now)
            {
                if (booking is null)
                {
                    HandToOffice(TextKeys.RescheduleOrCancel, PendingTask.RescheduleOrCancel);
                }
                else
                {
                    await ShowExistingAsync(desk, null, ct);
                }

                return;
            }

            if (a.Start != at)
            {
                plan.Say(TextKeys.ReminderOldButton, Fill: Describe(a));
                return;
            }

            await desk.ConfirmAttendanceAsync(a.Id, a.Start, ct);
            plan.Say(TextKeys.ReminderAttendOk, Fill: Describe(a));
        }

        /// <summary>The appointment, only if it is this client's (an id in a button is never trusted alone).</summary>
        private async Task<AppointmentInfo?> OwnAsync(BookingDesk desk, Guid id, CancellationToken ct) =>
            await desk.FindAppointmentAsync(id, ct) is { } a && a.PersonId == State.Person.Id ? a : null;

        private static BookingService ServiceOf(AppointmentInfo a) => BookingService.ByCode(a.Service) ?? BookingService.PhoneVideo;

        private Dictionary<string, string> Describe(AppointmentInfo a) => new()
        {
            ["service"] = ServiceOf(a).Name,
            ["day"] = BookingWords.Day(a.Start, plan.Now),
            ["time"] = BookingWords.Time(a.Start),
        };

        private void InService(string key)
        {
            plan.Say(key, TextKeys.ConsultationOffer);
            plan.Task(PendingTask.ServiceRequest);
            Check(Intake.MoveTo(IntakeState.InServiceFlow));
        }

        /// <summary>TASK 10 (B-16.1): the written service gets an order waiting for its documents (when its texts are approved).</summary>
        private void OpenOrder(Service? service)
        {
            if (servicesOn && MayOrder && ServiceKind.Requested(service) is { } kind)
            {
                ServiceToOpen = kind;
            }
        }

        /// <summary>
        /// B-16.1 + B-16.2 in one turn: «راجعلي العقد ده» with the contract. The order opens and takes the turn's
        /// documents (after the flow); the client hears they arrived, not «ابعتلي صورة العقد».
        /// </summary>
        private bool OrderWithFiles(Service? service)
        {
            if (!servicesOn || !MayOrder || ServiceKind.Requested(service) is not { } kind || !turn.Messages.Any(IsStoredFile))
            {
                return false;
            }

            ServiceToOpen = kind;
            FilesToOrder = true;
            plan.Say(TextKeys.ServiceFilesReceived);
            plan.Task(PendingTask.ServicePricing);
            Check(Intake.MoveTo(IntakeState.InServiceFlow));
            return true;
        }

        /// <summary>A task and a reply without any contact time (B-06: the promises wait for the panel).</summary>
        private void HandToOffice(string key, string task)
        {
            plan.Say(key);
            plan.Task(task);
            Check(Intake.MoveTo(IntakeState.HandedToStaff));
        }

        /// <summary>A turn with no words: it never reaches the AI (T-09); answered by its type.</summary>
        private void AnswerMediaOnly()
        {
            if (AnswerMediaProblems())
            {
                return;
            }

            if (turn.Messages.LastOrDefault(m => m.Type is MessageType.Image or MessageType.Document or MessageType.Video
                    && m.Media?.Status == MediaStatus.Stored) is { } media)
            {
                plan.Say(TextKeys.MediaReceived, Fill: new Dictionary<string, string> { ["media"] = MediaName(media.Type) });
            }
            else if (turn.Messages.Any(m => m.Type == MessageType.Location))
            {
                plan.Say(TextKeys.LocationReceived);
            }

            // Reactions and stickers: no answer.
        }

        /// <summary>One notice for the worst file problem in the turn, if any.</summary>
        private bool AnswerMediaProblems()
        {
            var m = turn.Messages;
            var key = m.Any(x => x.Media?.Status == MediaStatus.TooLarge) ? TextKeys.MediaTooLarge
                : m.Any(x => x.Media?.Status == MediaStatus.Rejected) ? TextKeys.MediaRejected
                : m.Any(x => x.Media?.Status == MediaStatus.Failed) ? TextKeys.MediaFailed
                : m.Any(x => x.Type == MessageType.Audio && x.Transcript?.Status != Transcript.Done) ? TextKeys.VoiceFailed
                : m.Any(x => x.Type == MessageType.Unsupported) ? TextKeys.Unsupported
                : null;
            if (key is null)
            {
                return false;
            }

            plan.Say(key);
            return true;
        }

        private static string MediaName(MessageType type) => type switch
        {
            MessageType.Image => "الصورة",
            MessageType.Video => "الفيديو",
            _ => "المستند",
        };

        private void RememberRequest()
        {
            if (c is not null && IsRequest(c.Intent))
            {
                Intake.Remember(new PendingRequest(Classification.Code(c.Intent),
                    c.Service is { } s ? Classification.Code(s) : null, Classification.Code(c.Mode)));
            }
        }

        /// <summary>Intents that ask for something Hazem must come back to after the name.</summary>
        private static bool IsRequest(Intent i) =>
            i is not (Intent.Greeting or Intent.ProvideName or Intent.ThanksOrClosing or Intent.Other or Intent.Abusive);

        private static Classification ToClassification(PendingRequest p) => new(
            StateName.Parse<Intent>(p.Intent),
            p.Service is null ? null : StateName.Parse<Service>(p.Service),
            p.ConsultationMode is null ? ConsultationMode.Unknown : StateName.Parse<ConsultationMode>(p.ConsultationMode),
            null, 1, null, null);

        /// <summary>Typed text only (not captions or transcripts): what a client types as a name.</summary>
        private string PlainText() =>
            string.Join(' ', turn.Messages.Where(m => m.Type == MessageType.Text).Select(m => m.Text));
    }

    // TASK 08: what the booking still has to do after the flow (it needs the calendar and the database).
    private abstract record BookingWork;

    private sealed record OfferTimes(BookingService Service, DateTimeOffset? After, string IntroKey, Guid? Moving,
        IReadOnlyDictionary<string, string>? Fill = null, string? App = null) : BookingWork;

    private sealed record PickTime(BookingService Service, DateTimeOffset At, string? App = null) : BookingWork;

    /// <summary>v1.33 (B-09): the urgent call held at the first free moment, then where to pay.</summary>
    private sealed record UrgentHold : BookingWork;

    /// <summary>1.25 (B-23): «تحب المكالمة تبقى على إيه؟» before the times of a phone or video consultation.</summary>
    private sealed record AskCallApp : BookingWork;

    private sealed record ShowExisting(Guid? Id) : BookingWork;

    private sealed record StartChange(Guid Id) : BookingWork;

    private sealed record ChangeTo(Guid Id, DateTimeOffset At) : BookingWork;

    private sealed record MoreChanges(Guid Id, DateTimeOffset After) : BookingWork;

    private sealed record CancelIt(Guid Id) : BookingWork;

    private sealed record RefundIt(Guid Id) : BookingWork;

    private sealed record Receipted(ReceiptOutcome Outcome) : BookingWork;

    /// <summary>TASK 12: «هحضر ✅» under the reminder of the appointment's time <paramref name="At"/>.</summary>
    private sealed record AttendIt(Guid Id, DateTimeOffset At) : BookingWork;

    /// <summary>TASK 12: «تغيير أو إلغاء» under a reminder.</summary>
    private sealed record ReminderChange(Guid Id) : BookingWork;

    private sealed class Plan(DateTimeOffset now)
    {
        public DateTimeOffset Now => now;

        public List<ReplyStep> Steps { get; } = [];

        public List<PendingTask> Tasks { get; } = [];

        public void Say(string key, string? fallback = null, IReadOnlyDictionary<string, string>? Fill = null) =>
            Steps.Add(new ReplyStep(key, fallback, Fill));

        /// <summary>A text with buttons or a list under it (TASK 08).</summary>
        public void Ask(string key, ReplyChoices choices, IReadOnlyDictionary<string, string>? Fill = null, string? fallback = null) =>
            Steps.Add(new ReplyStep(key, fallback, Fill, choices));

        /// <summary>A staff task, due by the B-06 table.</summary>
        public void Task(string kind, Guid? staffId = null)
        {
            if (Tasks.All(t => t.Kind != kind))
            {
                Tasks.Add(new PendingTask(kind, OfficeHours.DueAt(now), staffId));
            }
        }
    }
}
