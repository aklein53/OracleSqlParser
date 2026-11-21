using System.Globalization;

namespace OracleSqlParser.Fluent;

public static partial class Sql
{
    /// <summary>
    /// Unquoted names the parser routes to a special form, so <see cref="Func"/> refuses them. The first group is routed
    /// when followed by <c>(</c> or in a fixed position; SOME, ANY and ALL are routed after a comparison operator; PRIOR and
    /// CONNECT_BY_ROOT are unary operators in any position.
    /// </summary>
    private static readonly HashSet<string> SpecialFormNames = new(StringComparer.Ordinal)
    {
        "CAST", "XMLCAST", "TREAT", "VALIDATE_CONVERSION", "XMLSERIALIZE", "EXTRACT", "TRIM", "CASE", "CURSOR", "EXISTS",
        "NULL", "DEFAULT", "SOME", "ANY", "ALL", "PRIOR", "CONNECT_BY_ROOT",
    };

    /// <summary>A bind variable such as <c>:min</c>. The name may be given with or without the leading colon.</summary>
    /// <param name="name">The bind name, such as <c>min</c> or <c>:min</c>, or a quoted name such as <c>"my name"</c>.</param>
    /// <returns>A new <see cref="BindVariable"/>.</returns>
    /// <exception cref="ArgumentException">The name is not a bind name.</exception>
    public static BindVariable Bind(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        var text = trimmed.StartsWith(':') ? trimmed : ":" + trimmed;
        if (text.Length == 1) throw new ArgumentException("A bind name is required.", nameof(name));
        Names.RejectComments(text, name, nameof(name));
        Expression parsed;
        try
        {
            parsed = SqlParser.ParseExpression(text);
        }
        catch (ParseException ex)
        {
            throw new ArgumentException($"'{name}' is not a valid bind name: {ex.Message}", nameof(name));
        }
        if (parsed is not BindVariable bind || parsed.Start != 0 || parsed.End != text.Length)
            throw new ArgumentException($"'{name}' is not a bind name.", nameof(name));
        if (bind.Name.Length == 0)
            throw new ArgumentException($"'{name}' has an empty bind name.", nameof(name));
        if (!bind.Quoted && bind.Name.Contains('.'))
            throw new ArgumentException($"'{name}' is a dotted bind name, which Oracle does not allow.", nameof(name));
        return new BindVariable { Name = bind.Name, Quoted = bind.Quoted };
    }

    /// <summary>An integer literal. A negative value becomes a unary minus over a literal, as the parser reads it.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="Literal"/>, or a <see cref="UnaryExpression"/> for a negative value.</returns>
    public static Expression Lit(int value) => Number(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A 64-bit integer literal. A negative value becomes a unary minus over a literal, as the parser reads it.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="Literal"/>, or a <see cref="UnaryExpression"/> for a negative value.</returns>
    public static Expression Lit(long value) => Number(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A decimal literal, printed with invariant-culture formatting. A negative value becomes a unary minus.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="Literal"/>, or a <see cref="UnaryExpression"/> for a negative value.</returns>
    public static Expression Lit(decimal value) => Number(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A floating-point literal, printed with invariant-culture round-trip formatting. A negative value becomes a unary minus.</summary>
    /// <param name="value">The value. NaN and infinities have no SQL literal.</param>
    /// <returns>A <see cref="Literal"/>, or a <see cref="UnaryExpression"/> for a negative value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite.</exception>
    public static Expression Lit(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "NaN and infinity have no SQL number literal.");
        return Number(value.ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>A character string literal. The text is stored unescaped; the generator quotes it.</summary>
    /// <param name="value">The text. Use <see cref="Null"/> for SQL NULL.</param>
    /// <returns>A new <see cref="Literal"/>.</returns>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public static Expression Lit(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Literal { Kind = LiteralKind.String, Value = value };
    }

    /// <summary>A <c>TRUE</c> or <c>FALSE</c> literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="Literal"/>.</returns>
    public static Expression Lit(bool value) => new Literal { Kind = LiteralKind.Boolean, Value = value ? "TRUE" : "FALSE" };

    /// <summary>The <c>NULL</c> literal. A new node on every access.</summary>
    public static Literal Null => new() { Kind = LiteralKind.Null };

    /// <summary>A function call such as <c>NVL(a, 0)</c>. The name is read with the parser's name rules.</summary>
    /// <param name="name">The function name, optionally schema-qualified, such as <c>NVL</c> or <c>pkg.fn</c>.</param>
    /// <param name="args">The arguments, in order.</param>
    /// <returns>A new <see cref="FunctionCall"/>.</returns>
    /// <exception cref="ArgumentException">The name is not a plain name, or the parser reads it as a special form such as <c>CAST</c> or <c>TRIM</c>. Use the dedicated factory or <see cref="Expr"/>.</exception>
    public static FunctionCall Func(string name, params Expression[] args)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(args);
        var trimmed = name.Trim();
        if (!trimmed.Contains('.') && !trimmed.StartsWith('"') && SpecialFormNames.Contains(trimmed.ToUpperInvariant()))
            throw new ArgumentException(
                $"'{name}' is a special form in SQL, not a plain function call. Use its dedicated factory, or Sql.Expr for the full syntax.",
                nameof(name));
        var parts = Names.Parse(name);
        if (args.Any(a => a is null)) throw new ArgumentNullException(nameof(args), "An argument is null.");
        return new FunctionCall { Name = parts, Arguments = args.Select(a => new Argument { Value = a }).ToList() };
    }

    /// <summary>The <c>*</c> of <c>COUNT(*)</c>, or of a select list.</summary>
    /// <returns>A new <see cref="StarExpression"/>.</returns>
    public static StarExpression Star() => new();

    /// <summary>The <c>qualifier.*</c> form, such as <c>e.*</c>.</summary>
    /// <param name="qualifier">The table alias or name, possibly dotted.</param>
    /// <returns>A new <see cref="StarExpression"/>.</returns>
    public static StarExpression Star(string qualifier) => new() { Qualifier = Names.Parse(qualifier, nameof(qualifier)) };

    /// <summary><c>EXISTS (SELECT ...)</c>.</summary>
    /// <param name="query">The subquery.</param>
    /// <returns>A new <see cref="ExistsExpression"/>.</returns>
    public static ExistsExpression Exists(SelectStatement query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new ExistsExpression { Query = query };
    }

    /// <summary>A scalar subquery, <c>(SELECT ...)</c>, used as an expression.</summary>
    /// <param name="query">The subquery.</param>
    /// <returns>A new <see cref="SubqueryExpression"/>.</returns>
    public static SubqueryExpression Subquery(SelectStatement query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new SubqueryExpression { Query = query };
    }

    /// <summary>A searched <c>CASE</c> expression: <c>CASE WHEN ... THEN ... END</c>.</summary>
    /// <returns>A builder; add branches with <see cref="CaseBuilder.When"/> and finish with <see cref="CaseBuilder.End"/>.</returns>
    public static CaseBuilder Case() => new(null);

    /// <summary>A simple <c>CASE</c> expression: <c>CASE operand WHEN value THEN ... END</c>.</summary>
    /// <param name="operand">The value that each <c>WHEN</c> is compared with.</param>
    /// <returns>A builder; add branches with <see cref="CaseBuilder.When"/> and finish with <see cref="CaseBuilder.End"/>.</returns>
    public static CaseBuilder Case(Expression operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return new CaseBuilder(operand);
    }

    /// <summary>
    /// An expression parsed from SQL text: the escape hatch for syntax the builders do not cover. The text goes through
    /// <see cref="SqlParser.ParseExpression(string)"/>, so it is parsed, never spliced.
    /// </summary>
    /// <param name="sql">One SQL expression, such as <c>e.sal &gt; :min</c>.</param>
    /// <returns>The parsed expression.</returns>
    /// <exception cref="ArgumentException">The text is not one valid SQL expression.</exception>
    public static Expression Expr(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        try
        {
            return SqlParser.ParseExpression(sql);
        }
        catch (ParseException ex)
        {
            throw new ArgumentException($"'{sql}' is not a valid SQL expression: {ex.Message}", nameof(sql));
        }
    }

    private static Expression Number(string text)
    {
        if (text.StartsWith('-'))
        {
            // The sign comes off the text, never by negating the value, so int.MinValue and long.MinValue work.
            return new UnaryExpression
            {
                Operator = UnaryOperator.Minus,
                Operand = new Literal { Kind = LiteralKind.Number, Value = text[1..] },
            };
        }
        return new Literal { Kind = LiteralKind.Number, Value = text };
    }
}
