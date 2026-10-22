using System.Text;

namespace Office.Domain.Common;

/// <summary>Who changes a state ("مين يغيرها" in DECISIONS part 4).</summary>
public enum Actor
{
    System,
    Staff,
    Lawyer,
    Manager,
}

/// <summary>A refused business rule: a stable code and a sentence for the logs.</summary>
public sealed record DomainError(string Code, string Message)
{
    public const string TransitionNotAllowed = "transition_not_allowed";
    public const string ActorNotAllowed = "actor_not_allowed";
}

/// <summary>Success, or the rule that refused. Never an exception for a business refusal.</summary>
public readonly record struct Result(DomainError? Error)
{
    public static Result Ok => default;

    public bool IsOk => Error is null;

    public static Result Fail(string code, string message) => new(new DomainError(code, message));
}

public sealed record Transition<TState>(TState From, TState To, params Actor[] By)
    where TState : struct, Enum;

/// <summary>The allowed transitions of one state machine, and who may make each one.</summary>
public sealed class StateMachine<TState>
    where TState : struct, Enum
{
    private readonly Dictionary<(TState From, TState To), Actor[]> _allowed = [];

    public StateMachine(string name, IEnumerable<Transition<TState>> transitions)
    {
        Name = name;
        foreach (var t in transitions)
        {
            _allowed.Add((t.From, t.To), t.By);
        }
    }

    public string Name { get; }

    public IEnumerable<Transition<TState>> Transitions =>
        _allowed.Select(kv => new Transition<TState>(kv.Key.From, kv.Key.To, kv.Value));

    public Result Check(TState from, TState to, Actor actor)
    {
        if (!_allowed.TryGetValue((from, to), out var by))
        {
            return Result.Fail(DomainError.TransitionNotAllowed,
                $"{Name}: {StateName.Of(from)} -> {StateName.Of(to)} is not allowed");
        }

        return by.Contains(actor)
            ? Result.Ok
            : Result.Fail(DomainError.ActorNotAllowed,
                $"{Name}: {StateName.Of(from)} -> {StateName.Of(to)} is not allowed for {StateName.Of(actor)}");
    }
}

/// <summary>Enum ↔ the snake_case names DECISIONS and the database use (HumanHandoff ↔ human_handoff).</summary>
public static class StateName
{
    public static string Of<T>(T value)
        where T : struct, Enum
    {
        var name = value.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }

    public static T Parse<T>(string value)
        where T : struct, Enum =>
        Enum.Parse<T>(value.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);
}
