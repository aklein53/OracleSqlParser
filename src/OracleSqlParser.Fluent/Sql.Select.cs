namespace OracleSqlParser.Fluent;

public static partial class Sql
{
    /// <summary>A <c>SELECT</c> of named columns, such as <c>e.name</c>. <c>*</c> and <c>q.*</c> become star items.</summary>
    /// <param name="columns">Column names, not SQL text. Use <see cref="Expr"/> or <c>Func</c> for expressions.</param>
    /// <returns>A new <see cref="SelectStatement"/> whose body is a <see cref="QuerySpecification"/>.</returns>
    /// <exception cref="ArgumentException">A name is not a plain name, or no name is given.</exception>
    public static SelectStatement Select(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return NewSelect(columns.Select(c => SelectItems.FromName(c, nameof(columns))).ToList(), nameof(columns));
    }

    /// <summary>A <c>SELECT</c> of expressions, such as <c>Sql.Col("e.sal").Times(Sql.Lit(12))</c>. A bare star becomes a star item.</summary>
    /// <param name="expressions">The select list, in order.</param>
    /// <returns>A new <see cref="SelectStatement"/> whose body is a <see cref="QuerySpecification"/>.</returns>
    public static SelectStatement Select(params Expression[] expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        if (expressions.Any(e => e is null)) throw new ArgumentNullException(nameof(expressions), "A select item is null.");
        return NewSelect(expressions.Select(SelectItems.FromExpression).ToList(), nameof(expressions));
    }

    /// <summary>A <c>SELECT</c> of ready-made select items, such as <c>Sql.Col("a").As("x")</c>.</summary>
    /// <param name="items">The select list, in order. The nodes are used as-is.</param>
    /// <returns>A new <see cref="SelectStatement"/> whose body is a <see cref="QuerySpecification"/>.</returns>
    public static SelectStatement Select(params SelectItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(i => i is null)) throw new ArgumentNullException(nameof(items), "A select item is null.");
        return NewSelect([.. items], nameof(items));
    }

    private static SelectStatement NewSelect(List<SelectItem> items, string paramName)
    {
        if (items.Count == 0) throw new ArgumentException("A SELECT list needs at least one item.", paramName);
        if (SelectItems.MixesBareStar(items)) throw new ArgumentException("A bare * cannot be combined with other select items; use a qualified star such as t.* instead.", paramName);
        return new SelectStatement { Body = new QuerySpecification { SelectList = items } };
    }
}

/// <summary>Turns select-list names and expressions into select items, the way the parser builds them.</summary>
internal static class SelectItems
{
    /// <summary>True for a bare <c>*</c>, which Oracle allows only as the whole select list.</summary>
    internal static bool IsBareStar(SelectItem item) => item is StarSelectItem { Qualifier.Count: 0 };

    /// <summary>True when a list holds a bare <c>*</c> together with another item, which Oracle rejects. A qualified <c>t.*</c> is fine.</summary>
    internal static bool MixesBareStar(IReadOnlyList<SelectItem> items) => items.Count > 1 && items.Any(IsBareStar);

    /// <summary>
    /// A name item: <c>*</c> is a plain star, <c>q.*</c> a qualified star, and anything else a column reference read by
    /// <see cref="Names"/>.
    /// </summary>
    internal static SelectItem FromName(string name, string paramName)
    {
        ArgumentNullException.ThrowIfNull(name, paramName);
        var text = name.Trim();
        if (text == "*") return new StarSelectItem();
        // Spaces around the dot are allowed, as they are in any name (e . * is e.*).
        int dot = text.LastIndexOf('.');
        if (dot >= 0 && text[(dot + 1)..].Trim() == "*")
            return new StarSelectItem { Qualifier = Names.Parse(text[..dot], paramName) };
        return new ExpressionSelectItem { Expression = new ColumnReference { Parts = Names.Parse(name, paramName) } };
    }

    /// <summary>An expression item. A bare <see cref="StarExpression"/> becomes a star item, as the parser builds it.</summary>
    internal static SelectItem FromExpression(Expression expression) => expression switch
    {
        StarExpression star => new StarSelectItem { Qualifier = [.. star.Qualifier] },
        _ => new ExpressionSelectItem { Expression = expression },
    };
}
