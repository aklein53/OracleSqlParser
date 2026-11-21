namespace OracleSqlParser.Fluent;

/// <summary>Factories that create OracleSqlParser syntax-tree nodes for the fluent builders.</summary>
/// <remarks>
/// A <c>string</c> parameter here is a name, an alias or a literal value, never SQL text. The only call that takes SQL text
/// is <c>Sql.Expr</c>, which parses its input. Names follow the parser's identifier rules: unquoted parts are
/// upper-cased, and quoted parts (<c>"MixedCase"</c>) are kept exactly.
/// </remarks>
public static partial class Sql
{
    /// <summary>A column reference such as <c>e.sal</c>, or <c>"Order"."Id"</c> for quoted names.</summary>
    /// <param name="name">The dotted column name. Throws <see cref="ArgumentException"/> when it is not a plain name.</param>
    /// <returns>A new <see cref="ColumnReference"/>.</returns>
    public static ColumnReference Col(string name) => new() { Parts = Names.Parse(name) };

    /// <summary>A column reference from identifier nodes, for callers who already have them. The nodes are used as-is.</summary>
    /// <param name="parts">The name parts, qualifier first.</param>
    /// <returns>A new <see cref="ColumnReference"/>.</returns>
    public static ColumnReference Col(params Identifier[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0) throw new ArgumentException("A column reference needs at least one name part.", nameof(parts));
        if (parts.Any(p => p is null)) throw new ArgumentNullException(nameof(parts), "A name part is null.");
        return new ColumnReference { Parts = [.. parts] };
    }

    /// <summary>A single name, such as <c>emp</c> or <c>"MixedCase"</c>. Throws <see cref="ArgumentException"/> when the text is not one name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>A new <see cref="Identifier"/>.</returns>
    public static Identifier Id(string name)
    {
        var parts = Names.Parse(name);
        if (parts.Count != 1) throw new ArgumentException($"'{name}' must be a single name, without dots.", nameof(name));
        return parts[0];
    }

    /// <summary>A table or view name such as <c>hr.emp</c>, with an optional single-name alias.</summary>
    /// <param name="name">The dotted table name. Throws <see cref="ArgumentException"/> when it is not a plain name.</param>
    /// <param name="alias">The alias, or null for none. Must be one name.</param>
    /// <returns>A new <see cref="NamedTable"/>.</returns>
    public static NamedTable Table(string name, string? alias = null) => new()
    {
        Name = Names.Parse(name),
        Alias = alias is null ? null : SingleName(alias, nameof(alias)),
    };

    /// <summary>Reads a name that must be one part, such as an alias.</summary>
    internal static Identifier SingleName(string name, string paramName)
    {
        var parts = Names.Parse(name, paramName);
        if (parts.Count != 1) throw new ArgumentException($"'{name}' must be a single name, without dots.", paramName);
        return parts[0];
    }
}
