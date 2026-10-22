using Office.Domain.Common;

namespace Office.Domain.Clients;

/// <summary>The person behind a WhatsApp number (B-02, B-03). May later have several numbers and matters.</summary>
public sealed class Person
{
    private Person(Guid id, WaId waId) => (Id, WaId) = (id, waId);

    public Guid Id { get; }

    public WaId WaId { get; }

    public PersonName? Name { get; private set; }

    /// <summary>null = not asked yet; false = the client did not give four names after the second ask (B-02).</summary>
    public bool? FullNameProvided { get; private set; }

    /// <summary>Phone consultations only (B-03); never replaces <see cref="WaId"/>.</summary>
    public string? PreferredCallPhone { get; private set; }

    public bool HasFullName => FullNameProvided == true;

    /// <summary>Known number whose name question is settled (B-01.5 and B-02): never asked again.</summary>
    public bool NameSettled => FullNameProvided is not null;

    public static Person New(WaId waId) => new(Guid.NewGuid(), waId);

    public static Person Load(Guid id, WaId waId, PersonName? name, bool? fullNameProvided, string? preferredCallPhone) =>
        new(id, waId) { Name = name, FullNameProvided = fullNameProvided, PreferredCallPhone = preferredCallPhone };

    /// <summary>Records the name; true when it is a full (four-part) name.</summary>
    public bool GiveName(PersonName name)
    {
        Name = name;
        if (name.IsFull)
        {
            FullNameProvided = true;
        }

        return name.IsFull;
    }

    /// <summary>B-02: after the one extra ask, carry on with whatever name there is.</summary>
    public void ContinueWithoutFullName() => FullNameProvided ??= false;

    public Result SetPreferredCallPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < 8 or > 15)
        {
            return Result.Fail("invalid_phone", "a call phone has 8-15 digits");
        }

        PreferredCallPhone = digits;
        return Result.Ok;
    }
}

/// <summary>S-01.</summary>
public enum ConversationState
{
    BotActive,
    HumanHandoff,
    Muted,
    Ignored,
}

/// <summary>The WhatsApp conversation with one person (S-01, B-12).</summary>
public sealed class Conversation
{
    /// <summary>B-12: Hazem comes back 12 hours after the last staff message, when the client writes again.</summary>
    public static readonly TimeSpan HandBackAfter = TimeSpan.FromHours(12);

    public static readonly StateMachine<ConversationState> Machine = new("S-01 conversation",
    [
        new(ConversationState.BotActive, ConversationState.HumanHandoff, Actor.Staff, Actor.System),
        new(ConversationState.BotActive, ConversationState.Muted, Actor.System, Actor.Manager),
        new(ConversationState.HumanHandoff, ConversationState.BotActive, Actor.Staff, Actor.System),
        new(ConversationState.HumanHandoff, ConversationState.Muted, Actor.System, Actor.Manager),
        new(ConversationState.Muted, ConversationState.BotActive, Actor.Manager),
    ]);

    private Conversation(Guid id, Guid personId, ConversationState state, DateTimeOffset? lastStaffMessageAt)
    {
        Id = id;
        PersonId = personId;
        State = state;
        LastStaffMessageAt = lastStaffMessageAt;
    }

    public Guid Id { get; }

    public Guid PersonId { get; }

    public ConversationState State { get; private set; }

    public DateTimeOffset? LastStaffMessageAt { get; private set; }

    /// <summary>Hazem answers only while the conversation is bot_active.</summary>
    public bool HazemMayAnswer => State == ConversationState.BotActive;

    public static Conversation Start(Person person) => new(Guid.NewGuid(), person.Id, ConversationState.BotActive, null);

    /// <summary>A group chat: ignored for good.</summary>
    public static Conversation StartForGroup(Person person) => new(Guid.NewGuid(), person.Id, ConversationState.Ignored, null);

    public static Conversation Load(Guid id, Guid personId, ConversationState state, DateTimeOffset? lastStaffMessageAt) =>
        new(id, personId, state, lastStaffMessageAt);

    /// <summary>B-12.1: any staff message to the client silences Hazem.</summary>
    public Result StaffReplied(DateTimeOffset at)
    {
        if (State != ConversationState.HumanHandoff)
        {
            var r = Move(ConversationState.HumanHandoff, Actor.Staff);
            if (!r.IsOk)
            {
                return r;
            }
        }

        LastStaffMessageAt = at;
        return Result.Ok;
    }

    /// <summary>The client wrote «موظف».</summary>
    public Result ClientAskedForStaff() => Move(ConversationState.HumanHandoff, Actor.System);

    /// <summary>B-12: «رجّع لحازم» in the panel.</summary>
    public Result HandBackToHazem() => Move(ConversationState.BotActive, Actor.Staff);

    /// <summary>B-12.2: the client wrote again and 12 hours passed since the last staff message.</summary>
    public Result ClientWroteAt(DateTimeOffset at)
    {
        if (State != ConversationState.HumanHandoff || LastStaffMessageAt is not { } last || at - last < HandBackAfter)
        {
            return Result.Ok;
        }

        return Move(ConversationState.BotActive, Actor.System);
    }

    public Result Mute(Actor by) => Move(ConversationState.Muted, by);

    public Result Unmute(Actor by) => Move(ConversationState.BotActive, by);

    private Result Move(ConversationState to, Actor by)
    {
        var r = Machine.Check(State, to, by);
        if (r.IsOk)
        {
            State = to;
        }

        return r;
    }
}

/// <summary>S-02.</summary>
public enum IntakeState
{
    New,
    AwaitingName,
    Identifying,
    Classified,
    InServiceFlow,
    OfferSent,
    FollowupSent,
    HandedToStaff,
    Closed,
}

/// <summary>S-02 `classified` types.</summary>
public enum ClientType
{
    NewClient,
    ExistingClient,
    LawyerDirect,
    Blocked,
    Colleague,
    Press,
    Vendor,
}

/// <summary>What the client asked for before giving the name (B-01.4), so Hazem carries on after it.</summary>
public sealed record PendingRequest(string Intent, string? Service, string? ConsultationMode);

/// <summary>One request (Matter) from a person, from the first message until it is closed (S-02).</summary>
public sealed class Intake
{
    /// <summary>B-02: the name is asked once more, then Hazem carries on.</summary>
    public const int MaxNameAsks = 2;

    private static readonly IntakeState[] AfterClassification =
        [IntakeState.InServiceFlow, IntakeState.OfferSent, IntakeState.FollowupSent, IntakeState.HandedToStaff];

    public static readonly StateMachine<IntakeState> Machine = new("S-02 intake", BuildTransitions());

    private Intake(Guid id, Guid personId) => (Id, PersonId) = (id, personId);

    public Guid Id { get; }

    public Guid PersonId { get; }

    public IntakeState State { get; private set; } = IntakeState.New;

    public ClientType? ClientType { get; private set; }

    public int NameAsks { get; private set; }

    public PendingRequest? Pending { get; private set; }

    /// <summary>Unclear turns in a row (S-02 / TASK 04: the second one becomes a task).</summary>
    public int UnclearInARow { get; private set; }

    public bool IsClassified => State is not (IntakeState.New or IntakeState.AwaitingName or IntakeState.Identifying);

    public static Intake Open(Person person) => new(Guid.NewGuid(), person.Id);

    public static Intake Load(Guid id, Guid personId, IntakeState state, ClientType? clientType, int nameAsks,
        PendingRequest? pending, int unclearInARow) =>
        new(id, personId)
        {
            State = state,
            ClientType = clientType,
            NameAsks = nameAsks,
            Pending = pending,
            UnclearInARow = unclearInARow,
        };

    /// <summary>B-01.1: welcome and ask for the name. Counts every ask (B-02: two at most).</summary>
    public Result AskForName()
    {
        if (State != IntakeState.AwaitingName)
        {
            var r = Move(IntakeState.AwaitingName, Actor.System);
            if (!r.IsOk)
            {
                return r;
            }
        }

        NameAsks++;
        return Result.Ok;
    }

    public bool MayAskNameAgain => NameAsks < MaxNameAsks;

    /// <summary>B-01.4: keep the first request; only the first one is kept.</summary>
    public void Remember(PendingRequest request) => Pending ??= request;

    public PendingRequest? TakePending()
    {
        var p = Pending;
        Pending = null;
        return p;
    }

    /// <summary>
    /// B-01.2/3: the name is settled, the number is checked against the lists (TASK 05) and the
    /// intake gets its type. Until TASK 05 there are no lists, so every number is a new client.
    /// </summary>
    public Result Classify(ClientType type)
    {
        var r = Move(IntakeState.Identifying, Actor.System);
        if (r.IsOk)
        {
            r = Move(IntakeState.Classified, Actor.System);
        }

        if (r.IsOk)
        {
            ClientType = type;
        }

        return r;
    }

    public Result MoveTo(IntakeState to, Actor by = Actor.System) => State == to ? Result.Ok : Move(to, by);

    public void CountUnclear(bool unclear) => UnclearInARow = unclear ? UnclearInARow + 1 : 0;

    private Result Move(IntakeState to, Actor by)
    {
        var r = Machine.Check(State, to, by);
        if (r.IsOk)
        {
            State = to;
        }

        return r;
    }

    // DECISIONS S-02 lists the states but not the arrows; these follow B-01 and the states'
    // meaning (written in docs/sessions/*-task04.md). Once classified, the request moves freely
    // between service flow, offer, follow-up and staff, and anything but closed can be closed.
    private static IEnumerable<Transition<IntakeState>> BuildTransitions()
    {
        yield return new(IntakeState.New, IntakeState.AwaitingName, Actor.System);
        yield return new(IntakeState.New, IntakeState.Identifying, Actor.System);
        yield return new(IntakeState.AwaitingName, IntakeState.Identifying, Actor.System);
        yield return new(IntakeState.Identifying, IntakeState.Classified, Actor.System);
        foreach (var to in AfterClassification)
        {
            yield return new(IntakeState.Classified, to, Actor.System, Actor.Staff);
            foreach (var from in AfterClassification.Where(f => f != to))
            {
                yield return new(from, to, Actor.System, Actor.Staff);
            }
        }

        foreach (var from in Enum.GetValues<IntakeState>().Where(s => s != IntakeState.Closed))
        {
            yield return new(from, IntakeState.Closed, Actor.System, Actor.Staff);
        }
    }
}
