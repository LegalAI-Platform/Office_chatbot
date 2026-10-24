using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Services;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
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
/// </summary>
public sealed class HazemResponder(
    IConversationStore conversations,
    IIntentClassifier classifier,
    IApprovedTexts approvedTexts,
    IClientLists lists,
    TimeProvider clock,
    BookingDesk? booking = null,
    ServiceDesk? services = null) : ITurnResponder
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
            if (list.ExistingClient?.Name is { } listed && PersonName.TryParse(listed, out var name) && name.IsFull)
            {
                person.GiveName(name);
            }

            state = new ClientState(person, Conversation.Start(person), Intake.Open(person));
        }

        RecordSource(state.Person, turn, settings, now);

        var plan = new Plan(now);
        Check(state.Conversation.ClientWroteAt(now)); // B-12: 12 hours after the staff
        Classification? classification = null;
        var texts = await approvedTexts.GetActiveAsync(ct);
        var bookingSettings = BookingSettings.From(settings);
        var bookingOn = booking is not null && bookingSettings.Enabled && BookingTextKeys.All(k => texts.TryGetValue(k, out var t) && t.Active);
        var servicesOn = services is not null && ServiceTextKeys.All(k => texts.TryGetValue(k, out var t) && t.Active);

        if (list.Blocked)
        {
            // B-14.1: a blocked number is silent for good; messages are still stored.
            if (state.Conversation.State == ConversationState.BotActive)
            {
                Check(state.Conversation.Mute(Actor.System));
            }
        }
        else if (state.Conversation.HazemMayAnswer)
        {
            var flow = new Flow(turn, state, list, null, plan, settings.Flag(HandoffEnabledKey), bookingOn ? bookingSettings : null, servicesOn);
            // TASK 08: a button or a list row Hazem sent is read without the AI.
            var choice = bookingOn && flow.MayBook ? ChoiceIn(turn) : null;
            // TASK 09 / 10 (B-05, B-16, T-09): a file from the client never reaches the AI, nor any word sent with it:
            // a receipt (an image) for what waits for one, the documents of a service order, or «send an image».
            ReceiptOutcome? receipt = null;
            AddFileOutcome? documents = null;
            var askImage = false;
            if (choice is null && (bookingOn || servicesOn) && flow.MayOrder && services is not null && FileIn(turn) is { } m)
            {
                var image = m.Type == MessageType.Image && m.Media!.Sha256 is not null;
                var target = await services.FileTargetAsync(state.Person.Id, image, ct);
                switch (target.Target)
                {
                    case FileTarget.OrderPayment when servicesOn && booking is not null:
                    case FileTarget.Hold or FileTarget.BookingPayment or FileTarget.LateHold when bookingOn:
                        receipt = await booking!.ReceiptAsync(bookingSettings, settings,
                            new ReceiptRequest(state.Person.Id, m.Wamid, m.Media!.Bucket!, m.Media.Key!, m.Media.Sha256!, m.Media.MimeType), now, ct);
                        break;
                    case FileTarget.OrderDocuments when servicesOn:
                        documents = await services.AddFilesAsync(state.Person.Id, StoredFiles(turn), ct);
                        break;
                    case FileTarget.AskImage:
                        askImage = true; // P7: a PDF or Word is no receipt
                        break;
                }
            }

            if (choice is not null)
            {
                flow.RespondToChoice(choice);
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
                if (aiText is not null)
                {
                    classification = Classification.From(await classifier.ClassifyAsync(aiText, settings, ct), settings);
                }

                flow = new Flow(turn, state, list, classification, plan, settings.Flag(HandoffEnabledKey), bookingOn ? bookingSettings : null,
                    servicesOn);
                flow.Respond(firstContact);
            }

            if (flow.HasBookingWork)
            {
                await flow.RunBookingAsync(booking!, settings, ct);
            }

            if (flow.ServiceToOpen is { } kind)
            {
                await services!.OpenAsync(state.Person.Id, flow.State.Intake.Id, kind, turn.TurnId, ct);
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

    /// <summary>The booking id of the last button or list row the client pressed, if any.</summary>
    private static BookingChoice? ChoiceIn(Turn turn)
    {
        var last = turn.Messages.LastOrDefault(m => m.Interactive?.Id is not null || m.Button?.Payload is not null);
        return BookingChoice.Parse(last?.Interactive?.Id ?? last?.Button?.Payload);
    }

    /// <summary>
    /// B-05 (P7), B-16: the last stored image or document with its place in storage. An image may be a
    /// receipt; a PDF or Word goes to a service order waiting for documents, or Hazem asks for an image.
    /// </summary>
    private static IncomingMessage? FileIn(Turn turn) => turn.Messages.LastOrDefault(IsStoredFile);

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
        BookingSettings? booking, bool servicesOn)
    {
        /// <summary>TASK 10: the written service to open an order for (B-16.1), once the flow is done.</summary>
        public ServiceKind? ServiceToOpen { get; private set; }

        public ClientState State { get; private set; } = state;

        private Intake Intake => State.Intake;

        /// <summary>TASK 08: what is left to do with the booking (it needs the calendar and the database).</summary>
        private BookingWork? work;

        /// <summary>The classification being answered (the turn's, or the request kept from before the name).</summary>
        private Classification? answering;

        public bool HasBookingWork => work is not null;

        /// <summary>A new or current client past the name question; never the lists' lawyer or the roles (B-14).</summary>
        public bool MayOrder =>
            State.Person.NameSettled && !list.LawyerDirect
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
                'm' => new OfferTimes(choice.Service!, null, TextKeys.BookingChooseSlot, null),
                's' => new PickTime(choice.Service!, choice.At!.Value),
                'n' => new OfferTimes(choice.Service!, choice.At, TextKeys.BookingChooseSlot, null),
                'r' => new StartChange(choice.AppointmentId!.Value),
                't' => new ChangeTo(choice.AppointmentId!.Value, choice.At!.Value),
                'u' => new MoreChanges(choice.AppointmentId!.Value, choice.At!.Value),
                'c' => new CancelIt(choice.AppointmentId!.Value),
                _ => new RefundIt(choice.AppointmentId!.Value),
            };
        }

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

            // B-01.1: the first message from an unknown number always gets the welcome and the name
            // question; whatever was asked is kept and answered after the name (B-01.4).
            if ((firstContact || Intake.State == IntakeState.New) && !State.Person.NameSettled)
            {
                RememberRequest();
                Check(Intake.AskForName());
                plan.Say(TextKeys.WelcomeAskName);
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
                    Offer(); // B-01.3: a new client gets the consultation offer
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
                case Intent.HumanRequest or Intent.RescheduleOrCancel or Intent.ThanksOrClosing:
                    Answer(turnClass);
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

        /// <summary>B-02. True when Hazem can carry on (a full name, or the one extra ask is used up).</summary>
        private bool HandleName(Classification turnClass)
        {
            PersonName? name = null;
            if (turnClass.FullName is { } extracted && PersonName.TryParse(extracted, out var fromAi))
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
            Intake.CountUnclear(turnClass.Intent == Intent.Other);
            switch (turnClass.Intent)
            {
                case Intent.ConsultationRequest:
                    Offer();
                    break;
                case Intent.LegalQuestion:
                    plan.Say(TextKeys.LegalQuestion); // B-20, then the offer
                    Offer();
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
                case Intent.Greeting:
                case Intent.ProvideName:
                    plan.Say(TextKeys.Greeting, TextKeys.ConsultationOffer);
                    break;
                case Intent.ThanksOrClosing:
                    plan.Say(TextKeys.Thanks);
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
                case Service.UrfMarriage:
                case Service.UrfDivorce:
                    InService(TextKeys.UrfRequest); // the Urfi questions are TASK 11
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
        /// </summary>
        private void Urgent()
        {
            plan.Say(TextKeys.UrgentPrice);
            plan.Task(PendingTask.UrgentRequest);
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
            var known = MayBook ? KindAsked() : null;
            if (MayBook && known is null)
            {
                plan.Ask(TextKeys.ConsultationOffer, new ReplyChoices(InteractivePayload.Buttons,
                [
                    new ReplyChoice(BookingChoice.Mode(BookingService.PhoneVideo), TextKeys.BtnBookPhoneVideo),
                    new ReplyChoice(BookingChoice.Mode(BookingService.Office), TextKeys.BtnBookOffice),
                ]));
            }
            else
            {
                plan.Say(TextKeys.ConsultationOffer);
            }

            if (askTopic && !Intake.MatterKnown && !Intake.MatterAsked)
            {
                plan.Say(TextKeys.AskTopic);
                Intake.MarkMatterAsked();
            }

            if (known is not null)
            {
                work = new OfferTimes(known, null, TextKeys.BookingChooseSlot, null);
            }

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
            var b = booking!;
            switch (work)
            {
                case OfferTimes o:
                    await OfferTimesAsync(desk, b, settings, o.Service, o.After ?? plan.Now, o.IntroKey, o.Moving, ct);
                    break;
                case PickTime p:
                    await PickAsync(desk, b, settings, p.Service, p.At, ct);
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

            switch (outcome.Status)
            {
                case ReceiptStatus.Confirmed or ReceiptStatus.ConfirmedLate:
                case ReceiptStatus.Exists when a.State == AppointmentInfo.Confirmed:
                    plan.Say(TextKeys.BookingPaid, Fill: Describe(a));
                    break;
                case ReceiptStatus.LateTaken:
                    await OfferTimesAsync(desk, b, settings, ServiceOf(a), plan.Now, TextKeys.BookingHoldLate, null, ct, Describe(a));
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
            DateTimeOffset after, string introKey, Guid? moving, CancellationToken ct, IReadOnlyDictionary<string, string>? fill = null)
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
                moving is { } m ? BookingChoice.MoveTo(m, s.Start) : BookingChoice.Pick(service, s.Start),
                Title: BookingWords.Row(s.Start, now))).ToList();
            if (slots.Count == count && !service.IsUrgent)
            {
                var last = slots[^1].Start;
                rows.Add(new ReplyChoice(moving is { } m ? BookingChoice.MoreMoves(m, last) : BookingChoice.More(service, last), TextKeys.BtnMoreSlots));
            }

            plan.Ask(introKey, new ReplyChoices(InteractivePayload.List, rows, TextKeys.BtnSlotsList), Fill: fill);
        }

        /// <summary>
        /// B-08.3 (1.13): the time is held until the receipt (or confirmed at once when a receipt he already
        /// sent pays for it), or why not.
        /// </summary>
        private async Task PickAsync(BookingDesk desk, BookingSettings b, HazemSettings settings, BookingService service, DateTimeOffset at,
            CancellationToken ct)
        {
            var request = new BookRequest(Guid.NewGuid(), State.Person.Id, Intake.Id, service, at, turn.TurnId, b.Rules.BufferMinutes,
                Intake.MatterType, b.Hold.Until(plan.Now, at));
            var outcome = await desk.BookAsync(b, settings, request, plan.Now, ct);
            var a = outcome?.AppointmentId is { } id ? await OwnAsync(desk, id, ct) : null;
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
                    await OfferTimesAsync(desk, b, settings, service, plan.Now, TextKeys.BookingSlotTaken, null, ct);
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
                await OfferTimesAsync(desk, b, settings, ServiceOf(a), plan.Now, TextKeys.BookingChooseSlot, null, ct);
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

    private sealed record OfferTimes(BookingService Service, DateTimeOffset? After, string IntroKey, Guid? Moving) : BookingWork;

    private sealed record PickTime(BookingService Service, DateTimeOffset At) : BookingWork;

    private sealed record ShowExisting(Guid? Id) : BookingWork;

    private sealed record StartChange(Guid Id) : BookingWork;

    private sealed record ChangeTo(Guid Id, DateTimeOffset At) : BookingWork;

    private sealed record MoreChanges(Guid Id, DateTimeOffset After) : BookingWork;

    private sealed record CancelIt(Guid Id) : BookingWork;

    private sealed record RefundIt(Guid Id) : BookingWork;

    private sealed record Receipted(ReceiptOutcome Outcome) : BookingWork;

    private sealed class Plan(DateTimeOffset now)
    {
        public DateTimeOffset Now => now;

        public List<ReplyStep> Steps { get; } = [];

        public List<PendingTask> Tasks { get; } = [];

        public void Say(string key, string? fallback = null, IReadOnlyDictionary<string, string>? Fill = null) =>
            Steps.Add(new ReplyStep(key, fallback, Fill));

        /// <summary>A text with buttons or a list under it (TASK 08).</summary>
        public void Ask(string key, ReplyChoices choices, IReadOnlyDictionary<string, string>? Fill = null) =>
            Steps.Add(new ReplyStep(key, null, Fill, choices));

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
