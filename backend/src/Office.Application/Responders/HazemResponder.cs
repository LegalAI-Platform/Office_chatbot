using Office.Application.Conversations;
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
/// </summary>
public sealed class HazemResponder(
    IConversationStore conversations,
    IIntentClassifier classifier,
    IApprovedTexts approvedTexts,
    IClientLists lists,
    TimeProvider clock) : ITurnResponder
{
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
            var aiText = AiInput.Build(turn);
            if (aiText is not null)
            {
                classification = Classification.From(await classifier.ClassifyAsync(aiText, settings, ct), settings);
            }

            var flow = new Flow(turn, state, list, classification, plan, settings.Flag(HandoffEnabledKey));
            flow.Respond(firstContact);
            state = flow.State;
        }

        var texts = await approvedTexts.GetActiveAsync(ct);
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

    /// <summary>A refused transition here is a bug in the flow, not a client's mistake.</summary>
    private static void Check(Result r)
    {
        if (!r.IsOk)
        {
            throw new InvalidOperationException($"{r.Error!.Code}: {r.Error.Message}");
        }
    }

    /// <summary>The decisions for one turn. Changes the state in memory; the store saves it with the turn.</summary>
    private sealed class Flow(Turn turn, ClientState state, ListMatch list, Classification? c, Plan plan, bool handoffEnabled)
    {
        public ClientState State { get; private set; } = state;

        private Intake Intake => State.Intake;

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

        /// <summary>Colleagues, press and vendors (B-14.4): a neutral reply and a task, every turn.</summary>
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

            plan.Say(key);
            plan.Task(PendingTask.ExternalContact);
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
            i is Intent.ConsultationRequest or Intent.ServiceByName or Intent.Urgent or Intent.PriceQuestion
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
                    plan.Say(TextKeys.DocumentReviewRequest); // B-16 step 1 (step 2 promises a contact: later)
                    plan.Task(PendingTask.ServiceRequest);
                    Check(Intake.MoveTo(IntakeState.InServiceFlow));
                    break;
                case Service.ContractDrafting:
                    InService(TextKeys.ContractDraftingRequest);
                    break;
                case Service.FormalNotice:
                    InService(TextKeys.FormalNoticeRequest);
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

        /// <summary>B-09 first sentence and, by the owner's decision, the offer (never the topic question).</summary>
        private void Urgent()
        {
            plan.Say(TextKeys.UrgentPrice);
            plan.Task(PendingTask.UrgentRequest);
            Offer(askTopic: false);
        }

        /// <summary>
        /// The consultation offer. B-25: when the topic is still unknown, the one optional question
        /// follows it in the same answer — once per request, never on the urgent path.
        /// </summary>
        private void Offer(bool askTopic = true)
        {
            plan.Say(TextKeys.ConsultationOffer);
            if (askTopic && !Intake.MatterKnown && !Intake.MatterAsked)
            {
                plan.Say(TextKeys.AskTopic);
                Intake.MarkMatterAsked();
            }

            Check(Intake.MoveTo(IntakeState.OfferSent));
        }

        private void InService(string key)
        {
            plan.Say(key, TextKeys.ConsultationOffer);
            plan.Task(PendingTask.ServiceRequest);
            Check(Intake.MoveTo(IntakeState.InServiceFlow));
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

    private sealed class Plan(DateTimeOffset now)
    {
        public List<ReplyStep> Steps { get; } = [];

        public List<PendingTask> Tasks { get; } = [];

        public void Say(string key, string? fallback = null, IReadOnlyDictionary<string, string>? Fill = null) =>
            Steps.Add(new ReplyStep(key, fallback, Fill));

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
