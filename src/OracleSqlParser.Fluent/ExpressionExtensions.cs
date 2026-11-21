namespace OracleSqlParser.Fluent;

/// <summary>
/// Operators and predicates on expressions. Each method returns a new node and inserts parentheses where the printed SQL
/// needs them (see the Fluent README). The receiver and the arguments are used as-is, so do not pass one node to two places.
/// </summary>
public static class ExpressionExtensions
{
    /// <summary><c>this = other</c>.</summary>
    public static Expression Eq(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.Equals, right);

    /// <summary><c>this &lt;&gt; other</c>.</summary>
    public static Expression Ne(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.NotEquals, right);

    /// <summary><c>this &lt; other</c>.</summary>
    public static Expression Lt(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.LessThan, right);

    /// <summary><c>this &lt;= other</c>.</summary>
    public static Expression Le(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.LessThanOrEqual, right);

    /// <summary><c>this &gt; other</c>.</summary>
    public static Expression Gt(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.GreaterThan, right);

    /// <summary><c>this &gt;= other</c>.</summary>
    public static Expression Ge(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.GreaterThanOrEqual, right);

    /// <summary><c>this AND other</c>.</summary>
    public static Expression And(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.And, right);

    /// <summary><c>this OR other</c>.</summary>
    public static Expression Or(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.Or, right);

    /// <summary><c>NOT this</c>.</summary>
    public static Expression Not(this Expression operand) => new UnaryExpression
    {
        Operator = UnaryOperator.Not,
        Operand = Parens.Wrap(operand ?? throw new ArgumentNullException(nameof(operand)), 3),
    };

    /// <summary><c>this + other</c>.</summary>
    public static Expression Plus(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.Add, right);

    /// <summary><c>this - other</c>.</summary>
    public static Expression Minus(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.Subtract, right);

    /// <summary><c>this * other</c>.</summary>
    public static Expression Times(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.Multiply, right);

    /// <summary><c>this / other</c>.</summary>
    public static Expression DividedBy(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.Divide, right);

    /// <summary><c>this || other</c>.</summary>
    public static Expression Concat(this Expression left, Expression right) => Parens.Binary(left, BinaryOperator.Concatenate, right);

    /// <summary><c>this IN (v1, v2, ...)</c>. At least one value is required.</summary>
    public static Expression In(this Expression value, params Expression[] list) => InList(value, list, negated: false);

    /// <summary><c>this NOT IN (v1, v2, ...)</c>. At least one value is required.</summary>
    public static Expression NotIn(this Expression value, params Expression[] list) => InList(value, list, negated: true);

    /// <summary><c>this IN (SELECT ...)</c>.</summary>
    public static Expression In(this Expression value, SelectStatement query) => InQuery(value, query, negated: false);

    /// <summary><c>this NOT IN (SELECT ...)</c>.</summary>
    public static Expression NotIn(this Expression value, SelectStatement query) => InQuery(value, query, negated: true);

    /// <summary><c>this BETWEEN low AND high</c>.</summary>
    public static Expression Between(this Expression value, Expression low, Expression high)
    {
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(high);
        return new BetweenExpression
        {
            Value = Parens.Wrap(value ?? throw new ArgumentNullException(nameof(value)), 4),
            Low = Parens.Wrap(low, 5),
            High = Parens.Wrap(high, 5),
        };
    }

    /// <summary><c>this LIKE pattern</c>.</summary>
    public static Expression Like(this Expression value, Expression pattern) => Like(value, pattern, negated: false);

    /// <summary><c>this NOT LIKE pattern</c>.</summary>
    public static Expression NotLike(this Expression value, Expression pattern) => Like(value, pattern, negated: true);

    /// <summary><c>this IS NULL</c>.</summary>
    public static Expression IsNull(this Expression value) => Is(value, IsTest.Null, negated: false);

    /// <summary><c>this IS NOT NULL</c>.</summary>
    public static Expression IsNotNull(this Expression value) => Is(value, IsTest.Null, negated: true);

    /// <summary>The expression as a select-list item with an alias, <c>this AS alias</c>. The alias is one name.</summary>
    /// <exception cref="InvalidOperationException">The expression is a bare <c>*</c>, which takes no alias.</exception>
    public static ExpressionSelectItem As(this Expression expression, string alias)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression is StarExpression) throw new InvalidOperationException("A bare '*' cannot have an alias; use Select(\"*\") or a star item.");
        return new ExpressionSelectItem { Expression = expression, Alias = Sql.SingleName(alias, nameof(alias)) };
    }

    private static Expression InList(Expression value, Expression[] list, bool negated)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (list.Length == 0) throw new ArgumentException("IN needs at least one value.", nameof(list));
        if (list.Any(v => v is null)) throw new ArgumentNullException(nameof(list), "A value in the list is null.");
        return new InExpression
        {
            Value = Parens.Wrap(value ?? throw new ArgumentNullException(nameof(value)), 4),
            Not = negated,
            List = [.. list],
        };
    }

    private static Expression InQuery(Expression value, SelectStatement query, bool negated)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new InExpression
        {
            Value = Parens.Wrap(value ?? throw new ArgumentNullException(nameof(value)), 4),
            Not = negated,
            Query = query,
        };
    }

    private static Expression Like(Expression value, Expression pattern, bool negated)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new LikeExpression
        {
            Value = Parens.Wrap(value ?? throw new ArgumentNullException(nameof(value)), 4),
            Not = negated,
            Kind = LikeKind.Like,
            Pattern = Parens.Wrap(pattern, 5),
        };
    }

    private static Expression Is(Expression value, IsTest test, bool negated) => new IsExpression
    {
        Value = Parens.Wrap(value ?? throw new ArgumentNullException(nameof(value)), 4),
        Not = negated,
        Test = test,
    };
}
