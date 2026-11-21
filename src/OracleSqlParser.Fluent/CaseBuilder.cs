namespace OracleSqlParser.Fluent;

/// <summary>Builds a <see cref="CaseExpression"/>: add <c>WHEN</c> branches, an optional <c>ELSE</c>, then <see cref="End"/>.</summary>
public sealed class CaseBuilder
{
    private readonly Expression? _operand;
    private readonly List<WhenClause> _whens = [];
    private Expression? _else;

    internal CaseBuilder(Expression? operand) => _operand = operand;

    /// <summary>Adds a <c>WHEN when THEN then</c> branch. For a simple CASE, <paramref name="when"/> is the value compared with the operand.</summary>
    /// <param name="when">The condition, or the value to compare.</param>
    /// <param name="then">The result of the branch.</param>
    /// <returns>This builder, for chaining.</returns>
    public CaseBuilder When(Expression when, Expression then)
    {
        ArgumentNullException.ThrowIfNull(when);
        ArgumentNullException.ThrowIfNull(then);
        _whens.Add(new WhenClause { When = when, Then = then });
        return this;
    }

    /// <summary>Sets the <c>ELSE</c> result. Calling it again replaces the earlier value.</summary>
    /// <param name="value">The result when no branch matches.</param>
    /// <returns>This builder, for chaining.</returns>
    public CaseBuilder Else(Expression value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _else = value;
        return this;
    }

    /// <summary>Finishes the expression.</summary>
    /// <returns>A new <see cref="CaseExpression"/>.</returns>
    /// <exception cref="InvalidOperationException">No <c>WHEN</c> branch was added.</exception>
    public CaseExpression End()
    {
        if (_whens.Count == 0) throw new InvalidOperationException("A CASE expression needs at least one WHEN branch.");
        return new CaseExpression { Operand = _operand, Whens = [.. _whens], Else = _else };
    }
}
