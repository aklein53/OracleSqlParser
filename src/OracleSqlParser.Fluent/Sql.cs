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
        foreach (var part in parts) ValidatePart(part, nameof(parts));
        return new ColumnReference { Parts = [.. parts] };
    }

    /// <summary>
    /// Checks a caller's identifier before it is printed. A quoted part must be non-empty and have no double quote. An
    /// unquoted part is printed as its spelling, so that spelling must read back as one plain name whose folded value is
    /// <see cref="Identifier.Value"/>.
    /// </summary>
    internal static void ValidatePart(Identifier part, string paramName)
    {
        if (part.Value is null) throw new ArgumentException("A name part has no value.", paramName);
        if (part.Quoted)
        {
            if (part.Value.Length == 0) throw new ArgumentException("A quoted name part cannot be empty.", paramName);
            if (part.Value.Contains('"')) throw new ArgumentException($"A quoted name part cannot contain a double quote: {part.Value}", paramName);
            return;
        }
        var spelling = part.Text ?? part.Value;
        if (spelling.Length == 0 || spelling.Trim() != spelling)
            throw new ArgumentException($"'{spelling}' is not an unquoted name.", paramName);
        var read = Names.Parse(spelling, paramName);
        if (read.Count != 1 || read[0].Quoted || read[0].Value != part.Value)
            throw new ArgumentException(
                $"'{spelling}' does not read back as the unquoted name '{part.Value}'. Use Quoted = true for names that are not plain upper-case identifiers.",
                paramName);
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
    /// <param name="name">The table name: one or two parts (<c>table</c> or <c>schema.table</c>). Throws <see cref="ArgumentException"/> when it is not a plain name.</param>
    /// <param name="alias">The alias, or null for none. Must be one name that Oracle reads back as an alias (not a keyword such as <c>LEVEL</c>, which is read as a pseudo-column).</param>
    /// <returns>A new <see cref="NamedTable"/>.</returns>
    /// <exception cref="ArgumentException">The name has more than two parts, or the alias would not read back as an alias.</exception>
    public static NamedTable Table(string name, string? alias = null) => new()
    {
        Name = TableName(name, nameof(name)),
        Alias = alias is null ? null : TableAlias(alias, nameof(alias)),
    };

    /// <summary>Reads a table name: one or two parts, such as <c>t</c> or <c>schema.t</c>.</summary>
    internal static List<Identifier> TableName(string name, string paramName)
    {
        var parts = Names.Parse(name, paramName);
        if (parts.Count > 2) throw new ArgumentException($"'{name}' has {parts.Count} parts; a table name is at most schema.table.", paramName);
        return parts;
    }

    /// <summary>Reads a table alias, one name that must print and read back as that alias.</summary>
    internal static Identifier TableAlias(string alias, string paramName)
    {
        var id = SingleName(alias, paramName);
        CheckAliasReadsBack(id, alias, paramName);
        return id;
    }

    /// <summary>
    /// Checks that a table alias reads back as itself in each place the builders print one: last in <c>FROM</c>, before a
    /// comma, and before a join keyword. Oracle reads some words (LEVEL, ROWNUM, JOIN, LEFT, NATURAL, ...) as something else
    /// in one of those places. Checking by parsing keeps the rule exact, with no word list to maintain.
    /// </summary>
    internal static void CheckAliasReadsBack(Identifier id, string alias, string paramName)
    {
        // Printed the way the generator prints a name: quoted names keep their case, and unquoted ones their spelling.
        string text = id.Quoted ? "\"" + id.Value + "\"" : id.Text ?? id.Value;
        foreach (var context in AliasContexts)
        {
            if (!ReadsBack(id, text, context.Suffix, context.Pick))
                throw new ArgumentException(
                    $"'{alias}' cannot be a table alias: Oracle would not read it back as the alias {text}.", paramName);
        }
    }

    private static readonly (string Suffix, Func<FromClause, TableReference?> Pick)[] AliasContexts =
    [
        ("", from => from.Tables.Count == 1 ? from.Tables[0] : null),
        (", u", from => from.Tables.Count == 2 ? from.Tables[0] : null),
        (" JOIN u ON 1 = 1", from => from.Tables.Count == 1 && from.Tables[0] is JoinTable { Kind: JoinKind.Inner, Natural: false } join ? join.Left : null),
    ];

    private static bool ReadsBack(Identifier id, string text, string suffix, Func<FromClause, TableReference?> pick)
    {
        try
        {
            var parsed = (SelectStatement)SqlParser.ParseStatement("SELECT 1 FROM t " + text + suffix);
            var from = ((QuerySpecification)parsed.Body).From;
            var table = from is null ? null : pick(from);
            return table is NamedTable { Alias: { } read } && read.Value == id.Value && read.Quoted == id.Quoted;
        }
        catch (ParseException)
        {
            return false;
        }
    }

    /// <summary>Reads a name that must be one part, such as an alias.</summary>
    internal static Identifier SingleName(string name, string paramName)
    {
        var parts = Names.Parse(name, paramName);
        if (parts.Count != 1) throw new ArgumentException($"'{name}' must be a single name, without dots.", paramName);
        return parts[0];
    }
}
