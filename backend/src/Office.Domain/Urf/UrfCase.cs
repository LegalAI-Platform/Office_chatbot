using Office.Domain.Common;

namespace Office.Domain.Urf;

public enum UrfCaseState
{
    TypeUnclear,
    Collecting,
    RejectedAge,
    RejectedGuardian,
    RedirectedConsultation,
    DocumentsPending,
    Booked,
    LawyerRejected,
    Approved,
}

public enum UrfType
{
    NewContract,
    Divorce,
    Proof,
}

public enum Guardian
{
    Father,
    Brother,
    Uncle,
}

/// <summary>S-06 and the fixed Urfi rules of B-11. Every decision keeps the policy version it used.</summary>
public sealed class UrfCase
{
    public const int MinimumAge = 18;

    public static readonly StateMachine<UrfCaseState> Machine = new("S-06 urf case",
    [
        new(UrfCaseState.TypeUnclear, UrfCaseState.Collecting, Actor.System),
        new(UrfCaseState.TypeUnclear, UrfCaseState.RedirectedConsultation, Actor.System),
        new(UrfCaseState.Collecting, UrfCaseState.RejectedAge, Actor.System),
        new(UrfCaseState.Collecting, UrfCaseState.RejectedGuardian, Actor.System),
        new(UrfCaseState.Collecting, UrfCaseState.RedirectedConsultation, Actor.System),
        new(UrfCaseState.Collecting, UrfCaseState.DocumentsPending, Actor.System),
        new(UrfCaseState.DocumentsPending, UrfCaseState.Booked, Actor.System),
        new(UrfCaseState.Booked, UrfCaseState.Approved, Actor.Lawyer),
        new(UrfCaseState.Booked, UrfCaseState.LawyerRejected, Actor.Lawyer),
    ]);

    private UrfCase(Guid id, UrfCaseState state, UrfType? type) => (Id, State, Type) = (id, state, type);

    public Guid Id { get; }

    public UrfCaseState State { get; private set; }

    public UrfType? Type { get; private set; }

    /// <summary>The policy version of the last decision (B-11: age, guardian, rejection).</summary>
    public string? PolicyVersion { get; private set; }

    /// <summary>«عرفي» only: ask what is needed, without offering choices.</summary>
    public static UrfCase Unclear() => new(Guid.NewGuid(), UrfCaseState.TypeUnclear, null);

    public static UrfCase Start(UrfType type) =>
        type == UrfType.Proof
            ? new(Guid.NewGuid(), UrfCaseState.RedirectedConsultation, type) // proof = a family-court case: consultation
            : new(Guid.NewGuid(), UrfCaseState.Collecting, type);

    public Result SetType(UrfType type)
    {
        var r = Move(type == UrfType.Proof ? UrfCaseState.RedirectedConsultation : UrfCaseState.Collecting, Actor.System);
        if (r.IsOk)
        {
            Type = type;
        }

        return r;
    }

    /// <summary>B-11 step 1: a party under 18 closes the request, with no price and no appointment.</summary>
    public Result CheckAges(int husbandAge, int wifeAge, string policyVersion) =>
        husbandAge < MinimumAge || wifeAge < MinimumAge ? Decide(UrfCaseState.RejectedAge, policyVersion) : Result.Ok;

    /// <summary>B-11 step 3: father, then brother, then uncle. Nobody → office policy refusal.</summary>
    public static Guardian? GuardianFrom(bool father, bool brother, bool uncle) =>
        father ? Guardian.Father : brother ? Guardian.Brother : uncle ? Guardian.Uncle : null;

    public Result CheckGuardian(bool father, bool brother, bool uncle, string policyVersion) =>
        GuardianFrom(father, brother, uncle) is null ? Decide(UrfCaseState.RejectedGuardian, policyVersion) : Result.Ok;

    /// <summary>Divorce with children, no agreement or no contract copy → consultation only.</summary>
    public Result RedirectToConsultation(string policyVersion) => Decide(UrfCaseState.RedirectedConsultation, policyVersion);

    public Result AskForDocuments() => Move(UrfCaseState.DocumentsPending, Actor.System);

    public Result Booked() => Move(UrfCaseState.Booked, Actor.System);

    public Result Approve(string policyVersion) => Decide(UrfCaseState.Approved, policyVersion, Actor.Lawyer);

    /// <summary>Then the appointment is cancelled_by_office with a full refund (B-22).</summary>
    public Result Reject(string policyVersion) => Decide(UrfCaseState.LawyerRejected, policyVersion, Actor.Lawyer);

    private Result Decide(UrfCaseState to, string policyVersion, Actor by = Actor.System)
    {
        var r = Move(to, by);
        if (r.IsOk)
        {
            PolicyVersion = policyVersion;
        }

        return r;
    }

    private Result Move(UrfCaseState to, Actor by)
    {
        var r = Machine.Check(State, to, by);
        if (r.IsOk)
        {
            State = to;
        }

        return r;
    }
}
