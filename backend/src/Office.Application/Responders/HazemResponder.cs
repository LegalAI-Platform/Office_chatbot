using Office.Application.Conversations;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Domain.Clients;
using Office.Domain.Common;

namespace Office.Application.Responders;

/// <summary>
/// Hazem (TASK 04): understands the turn (AI classification only) and answers from approved
/// texts only. The flow and its limits are TASK-04-prompt.md section 5.
/// </summary>
public sealed class HazemResponder(
    IConversationStore conversations,
    IIntentClassifier classifier,
    IApprovedTexts approvedTexts,
    TimeProvider clock) : ITurnResponder
{
    public const string ResponderName = "hazem";

    /// <summary>Numbers Hazem answers while hazem.open_to_all is false (CLAUDE.md: test numbers only).</summary>
    public const string RecipientsKey = "hazem.recipients";

    /// <summary>true only when the owner opens Hazem to every client.</summary>
    public const string OpenToAllKey = "hazem.open_to_all";

    public string Name => ResponderName;

    public async Task<TurnDecision> DecideAsync(Turn turn, HazemSettings settings, CancellationToken ct)
    {
        if (!settings.Flag(OpenToAllKey) && !settings.List(RecipientsKey).Contains(turn.WaId))
        {
            return new TurnDecision(ResponderName, []);
        }

        var state = await conversations.LoadAsync(turn.WaId, ct);
        var firstContact = state is null;
        if (state is null)
        {
            var person = Person.New(WaId.From(turn.WaId));
            state = new ClientState(person, Conversation.Start(person), Intake.Open(person));
        }

        var plan = new Plan();
        Check(state.Conversation.ClientWroteAt(clock.GetUtcNow())); // B-12: 12 hours after the staff
        Classification? classification = null;
        if (state.Conversation.HazemMayAnswer)
        {
            var aiText = AiInput.Build(turn);
            if (aiText is not null)
            {
                classification = Classification.From(await classifier.ClassifyAsync(aiText, settings, ct), settings);
            }

            Respond(turn, state, firstContact, classification, plan);
        }

        var texts = await approvedTexts.GetActiveAsync(ct);
        var composed = ReplyComposer.Compose(turn.TurnId, turn.Messages[^1].Wamid, plan.Steps, texts);
        var effects = new TurnEffects(state, plan.Tasks,
            classification is null ? null : ClassificationRecord.From(turn.TurnId, classification), composed.Records);
        // Keys and reasons only (never a text): the API logs them with the turn.
        var notes = composed.Skipped.Select(x => $"text {x.Key} not sent: {x.Reason}").ToList();
        if (classification is { IsFallback: true })
        {
            notes.Add($"classification fell back to other: {classification.FallbackReason}");
        }

        return new TurnDecision(ResponderName, composed.Actions, effects, notes);
    }

    private static void Respond(Turn turn, ClientState s, bool firstContact, Classification? c, Plan plan)
    {
        var intake = s.Intake;

        // B-01.1: the first message from an unknown number always gets the welcome and the name
        // question; whatever was asked is kept and answered after the name (B-01.4).
        if (firstContact || (intake.State == IntakeState.New && !s.Person.NameSettled))
        {
            RememberRequest(intake, c);
            Check(intake.AskForName());
            plan.Say(TextKeys.WelcomeAskName);
            return;
        }

        if (intake.State == IntakeState.New)
        {
            Check(intake.Classify(ClientType.NewClient)); // known number with its name settled (B-01.5)
        }

        if (intake.State == IntakeState.AwaitingName)
        {
            if (c is null)
            {
                // A photo or file instead of the name is not a refusal (B-02): answer the file and
                // ask again without using up the one extra ask.
                AnswerMediaOnly(turn, plan);
                plan.Say(TextKeys.AskNameAgain, TextKeys.WelcomeAskName);
                return;
            }

            if (!HandleName(turn, s, c, plan))
            {
                return; // asked for the name again
            }

            var pending = intake.TakePending();
            Check(intake.Classify(ClientType.NewClient)); // the lists come in TASK 05
            if (pending is not null)
            {
                Answer(ToClassification(pending), intake, plan);
            }
            else if (c is not null && IsRequest(c.Intent))
            {
                Answer(c, intake, plan);
            }
            else
            {
                Offer(intake, plan); // B-01.3: a new client gets the consultation offer
            }

            AnswerMediaProblems(turn, plan);
            return;
        }

        if (c is null)
        {
            AnswerMediaOnly(turn, plan);
            return;
        }

        Answer(c, intake, plan);
        AnswerMediaProblems(turn, plan);
    }

    /// <summary>B-02. True when Hazem can carry on (a full name, or the one extra ask is used up).</summary>
    private static bool HandleName(Turn turn, ClientState s, Classification c, Plan plan)
    {
        PersonName? name = null;
        if (c.FullName is { } extracted && PersonName.TryParse(extracted, out var fromAi))
        {
            name = fromAi;
        }
        else if ((c.Intent == Intent.ProvideName || c.IsFallback) && NameGuess.TryRead(PlainText(turn), out var typed))
        {
            // The AI says it is a name but did not extract it, or the AI is down/unsure: a short
            // letters-only message with no everyday words («عايز», «مش», «استشارة»…) is the name.
            name = typed;
        }

        if (name is not null && s.Person.GiveName(name))
        {
            return true;
        }

        RememberRequest(s.Intake, c);
        if (s.Intake.MayAskNameAgain)
        {
            Check(s.Intake.AskForName());
            plan.Say(TextKeys.AskNameAgain, TextKeys.WelcomeAskName);
            return false;
        }

        s.Person.ContinueWithoutFullName();
        return true;
    }

    private static void Answer(Classification c, Intake intake, Plan plan)
    {
        intake.CountUnclear(c.Intent == Intent.Other);
        switch (c.Intent)
        {
            case Intent.ConsultationRequest:
                Offer(intake, plan);
                break;
            case Intent.LegalQuestion:
                plan.Say(TextKeys.LegalQuestion); // B-20, then the offer
                Offer(intake, plan);
                break;
            case Intent.ServiceByName:
                AnswerService(c.Service, intake, plan);
                break;
            case Intent.Urgent:
                Urgent(intake, plan);
                break;
            case Intent.PriceQuestion:
                AnswerPrice(c.Service, intake, plan);
                break;
            case Intent.ExistingMatter:
                HandToOffice(intake, plan, TextKeys.ExistingMatter, PendingTask.ExistingClientMessage);
                break;
            case Intent.HumanRequest:
                HandToOffice(intake, plan, TextKeys.HumanRequest, PendingTask.HumanRequested);
                break;
            case Intent.RescheduleOrCancel:
                HandToOffice(intake, plan, TextKeys.RescheduleOrCancel, PendingTask.RescheduleOrCancel);
                break;
            case Intent.Greeting:
            case Intent.ProvideName:
                plan.Say(TextKeys.Greeting, TextKeys.ConsultationOffer);
                break;
            case Intent.ThanksOrClosing:
                plan.Say(TextKeys.Thanks);
                break;
            default:
                if (intake.UnclearInARow <= 1)
                {
                    plan.Say(TextKeys.Unclear, TextKeys.ConsultationOffer); // owner's decision until "unclear" is approved
                }
                else
                {
                    HandToOffice(intake, plan, TextKeys.UnclearAgain, PendingTask.UnclearMessage);
                }

                break;
        }
    }

    private static void AnswerService(Service? service, Intake intake, Plan plan)
    {
        switch (service)
        {
            case Service.ConsultationUrgent:
                Urgent(intake, plan);
                break;
            case Service.DocumentReview:
                plan.Say(TextKeys.DocumentReviewRequest); // B-16 step 1 (step 2 promises a contact: later)
                plan.Task(PendingTask.ServiceRequest);
                Check(intake.MoveTo(IntakeState.InServiceFlow));
                break;
            case Service.ContractDrafting:
                InService(intake, plan, TextKeys.ContractDraftingRequest);
                break;
            case Service.FormalNotice:
                InService(intake, plan, TextKeys.FormalNoticeRequest);
                break;
            case Service.UrfMarriage:
            case Service.UrfDivorce:
                InService(intake, plan, TextKeys.UrfRequest); // the Urfi questions are TASK 11
                break;
            case Service.UrfUnclear:
                plan.Say(TextKeys.UrfUnclear);
                Check(intake.MoveTo(IntakeState.InServiceFlow));
                break;
            case Service.UrfProof:
                plan.Say(TextKeys.UrfProof); // B-11: the only exception to B-20, then the offer
                Offer(intake, plan);
                break;
            case Service.WrittenOpinion:
                plan.Say(TextKeys.WrittenOpinion); // B-17, then the offer
                Offer(intake, plan);
                break;
            default:
                Offer(intake, plan); // consultations, case representation (priced after it) or unknown
                break;
        }
    }

    private static void AnswerPrice(Service? service, Intake intake, Plan plan)
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

        Offer(intake, plan);
    }

    /// <summary>B-09 first sentence and, by the owner's decision, the consultation offer; the booking itself is TASK 08/10.</summary>
    private static void Urgent(Intake intake, Plan plan)
    {
        plan.Say(TextKeys.UrgentPrice);
        plan.Task(PendingTask.UrgentRequest);
        Offer(intake, plan);
    }

    private static void Offer(Intake intake, Plan plan)
    {
        plan.Say(TextKeys.ConsultationOffer);
        Check(intake.MoveTo(IntakeState.OfferSent));
    }

    private static void InService(Intake intake, Plan plan, string key)
    {
        plan.Say(key, TextKeys.ConsultationOffer);
        plan.Task(PendingTask.ServiceRequest);
        Check(intake.MoveTo(IntakeState.InServiceFlow));
    }

    /// <summary>A task and, once its text is approved, a reply without any contact time (B-06).</summary>
    private static void HandToOffice(Intake intake, Plan plan, string key, string task)
    {
        plan.Say(key);
        plan.Task(task);
        Check(intake.MoveTo(IntakeState.HandedToStaff));
    }

    /// <summary>A turn with no words: it never reaches the AI (T-09); answered by its type.</summary>
    private static void AnswerMediaOnly(Turn turn, Plan plan)
    {
        if (AnswerMediaProblems(turn, plan))
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
    private static bool AnswerMediaProblems(Turn turn, Plan plan)
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

    private static void RememberRequest(Intake intake, Classification? c)
    {
        if (c is not null && IsRequest(c.Intent))
        {
            intake.Remember(new PendingRequest(Classification.Code(c.Intent),
                c.Service is { } s ? Classification.Code(s) : null, Classification.Code(c.Mode)));
        }
    }

    /// <summary>Intents that ask for something Hazem must come back to after the name.</summary>
    private static bool IsRequest(Intent i) =>
        i is not (Intent.Greeting or Intent.ProvideName or Intent.ThanksOrClosing or Intent.Other);

    private static Classification ToClassification(PendingRequest p) => new(
        StateName.Parse<Intent>(p.Intent),
        p.Service is null ? null : StateName.Parse<Service>(p.Service),
        p.ConsultationMode is null ? ConsultationMode.Unknown : StateName.Parse<ConsultationMode>(p.ConsultationMode),
        null, 1, null, null);

    /// <summary>Typed text only (not captions or transcripts): what a client types as a name.</summary>
    private static string PlainText(Turn turn) =>
        string.Join(' ', turn.Messages.Where(m => m.Type == MessageType.Text).Select(m => m.Text));

    /// <summary>A refused transition here is a bug in the flow, not a client's mistake.</summary>
    private static void Check(Result r)
    {
        if (!r.IsOk)
        {
            throw new InvalidOperationException($"{r.Error!.Code}: {r.Error.Message}");
        }
    }

    private sealed class Plan
    {
        public List<ReplyStep> Steps { get; } = [];

        public List<PendingTask> Tasks { get; } = [];

        public void Say(string key, string? fallback = null, IReadOnlyDictionary<string, string>? Fill = null) =>
            Steps.Add(new ReplyStep(key, fallback, Fill));

        public void Task(string kind)
        {
            if (Tasks.All(t => t.Kind != kind))
            {
                Tasks.Add(new PendingTask(kind));
            }
        }
    }
}
