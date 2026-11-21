namespace OracleSqlParser.Fluent;

/// <summary>
/// Inserts <see cref="ParenthesizedExpression"/> nodes where <see cref="SqlScriptGenerator"/> would print parentheses, so a built
/// tree is the tree the parser returns for the printed text. The precedences mirror the generator's <c>Prec</c> table
/// (<c>SqlScriptGenerator.Expressions.cs</c>), and every operand slot passes through <see cref="Wrap"/> with the same minimum
/// precedence the generator's <c>Child</c> call uses.
/// </summary>
internal static class Parens
{
    /// <summary>The precedence of an expression, as the generator uses it to decide on parentheses.</summary>
    internal static int Precedence(Expression e) => e switch
    {
        BinaryExpression b => BinaryPrecedence(b.Operator),
        UnaryExpression u => u.Operator == UnaryOperator.Not ? 3 : 7,
        BetweenExpression or InExpression or LikeExpression or IsExpression or QuantifiedComparison => 4,
        AtTimeZoneExpression or CollateExpression => 9,
        _ => 100,
    };

    /// <summary>The precedence of a binary operator, as the generator uses it.</summary>
    internal static int BinaryPrecedence(BinaryOperator op) => op switch
    {
        BinaryOperator.Or => 1,
        BinaryOperator.And => 2,
        BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Concatenate => 5,
        BinaryOperator.Multiply or BinaryOperator.Divide => 6,
        BinaryOperator.Power => 8,
        BinaryOperator.MultisetUnion or BinaryOperator.MultisetUnionAll or BinaryOperator.MultisetUnionDistinct
            or BinaryOperator.MultisetIntersect or BinaryOperator.MultisetIntersectAll or BinaryOperator.MultisetIntersectDistinct
            or BinaryOperator.MultisetExcept or BinaryOperator.MultisetExceptAll or BinaryOperator.MultisetExceptDistinct => 5,
        _ => 4,
    };

    /// <summary>
    /// Returns <paramref name="operand"/> wrapped in a <see cref="ParenthesizedExpression"/> when it would print with
    /// parentheses at <paramref name="minPrec"/>; otherwise returns it unchanged.
    /// </summary>
    internal static Expression Wrap(Expression operand, int minPrec)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return Precedence(operand) < minPrec ? new ParenthesizedExpression { Inner = operand } : operand;
    }

    /// <summary>
    /// A binary operation. The left operand keeps the operator's precedence (the parser is left-associative), and the
    /// right operand needs one more level, for every operator including AND and OR, so that <c>a AND (b AND c)</c> keeps its
    /// grouping.
    /// </summary>
    internal static BinaryExpression Binary(Expression left, BinaryOperator op, Expression right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        int p = BinaryPrecedence(op);
        return new BinaryExpression { Left = Wrap(left, p), Operator = op, Right = Wrap(right, p + 1) };
    }
}

/// <summary>Adds a condition to an existing <c>WHERE</c> or <c>HAVING</c> condition, grouping the existing one as needed.</summary>
internal static class Conditions
{
    /// <summary>Combines <paramref name="added"/> with <paramref name="existing"/> using AND; returns <paramref name="added"/> when there is no existing condition.</summary>
    internal static Expression And(Expression? existing, Expression added) =>
        existing is null ? added : Parens.Binary(existing, BinaryOperator.And, added);

    /// <summary>Combines <paramref name="added"/> with <paramref name="existing"/> using OR; returns <paramref name="added"/> when there is no existing condition.</summary>
    internal static Expression Or(Expression? existing, Expression added) =>
        existing is null ? added : Parens.Binary(existing, BinaryOperator.Or, added);
}
