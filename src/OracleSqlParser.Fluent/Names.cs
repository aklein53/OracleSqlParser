namespace OracleSqlParser.Fluent;

/// <summary>Reads names given as strings with the parser's own identifier rules, so folding never drifts from the parser.</summary>
internal static class Names
{
    /// <summary>
    /// Parses a dotted name such as <c>e.sal</c>, <c>hr.emp</c> or <c>"MixedCase"</c>. Unquoted parts are upper-cased in
    /// <see cref="Identifier.Value"/>, quoted parts are kept exactly, and both keep the spelling for printing.
    /// </summary>
    /// <remarks>
    /// The text is parsed as an expression and must be a plain column reference. The parts are copied, so the result
    /// carries no spans or token indexes from the throwaway parse.
    /// </remarks>
    /// <exception cref="ArgumentException">The text is not a plain dotted name.</exception>
    public static List<Identifier> Parse(string name, string paramName = "name")
    {
        ArgumentNullException.ThrowIfNull(name, paramName);
        Expression parsed;
        try
        {
            parsed = SqlParser.ParseExpression(name);
        }
        catch (ParseException ex)
        {
            throw new ArgumentException($"'{name}' is not a valid name: {ex.Message}", paramName);
        }
        if (parsed is not ColumnReference { Attribute: null, OuterJoin: false } column)
            throw new ArgumentException($"'{name}' is not a plain name made of identifiers, separated by dots.", paramName);
        return column.Parts.Select(p => new Identifier { Value = p.Value, Quoted = p.Quoted, Text = p.Text }).ToList();
    }
}
