using Microsoft.AspNetCore.Http.HttpResults;
using PolvorApp.SharedKernel.Http;

namespace PolvorApp.ComparsaOrders;

/// <summary>
/// The result of an order operation: its outcome, the value when it was done, and what the problem
/// response needs otherwise (invalid fields, or extensions such as the invalid <c>entries</c>).
/// </summary>
internal sealed record OrderResult<T>(
    OrderOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string>? Errors = null,
    IReadOnlyDictionary<string, object?>? Extra = null)
{
    public static OrderResult<T> Done(T value) => new(OrderOutcome.Done, value);

    public static OrderResult<T> Failed(OrderOutcome outcome, IReadOnlyDictionary<string, object?>? extra = null) => new(outcome, Extra: extra);

    public static OrderResult<T> Invalid(IReadOnlyDictionary<string, string> errors) => new(OrderOutcome.Invalid, Errors: errors);

    /// <summary>The same failure for another value type.</summary>
    public OrderResult<TOther> As<TOther>() => new(Outcome, default, Errors, Extra);

    /// <summary>The problem response of a failed operation.</summary>
    public ProblemHttpResult Problem() => Outcome == OrderOutcome.Invalid
        ? ProblemResults.Invalid(Errors ?? new Dictionary<string, string>())
        : OrderProblems.From(Outcome, Extra);
}
