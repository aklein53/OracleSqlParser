namespace OracleSqlParser.Fluent;

/// <summary>Reads names given as strings with the parser's own identifier rules, so folding never drifts from the parser.</summary>
internal static class Names
{
    /// <summary>
    /// Parses a dotted name such as <c>e.sal</c>, <c>hr.emp</c> or <c>"MixedCase"</c>. Unquoted parts are upper-cased in
    /// <see cref="Identifier.Value"/>, quoted parts are kept exactly, and both keep the spelling for printing.
    /// </summary>
    /// <remarks>
    /// The text is parsed as an expression and must be exactly one plain column reference covering the whole (trimmed)
    /// text, so nothing is silently dropped. Comments are rejected. Spaces around the dots are allowed, since the parser
    /// accepts them and the tree is the same. The parts are copied, so the result carries no spans or token indexes from
    /// the throwaway parse.
    /// </remarks>
    /// <exception cref="ArgumentException">The text is not a plain dotted name.</exception>
    public static List<Identifier> Parse(string name, string paramName = "name")
    {
        ArgumentNullException.ThrowIfNull(name, paramName);
        var text = name.Trim();
        if (text.Length == 0) throw new ArgumentException("A name is required.", paramName);
        RejectComments(text, name, paramName);

        Expression parsed;
        try
        {
            parsed = SqlParser.ParseExpression(text);
        }
        catch (ParseException ex)
        {
            throw new ArgumentException($"'{name}' is not a valid name: {ex.Message}", paramName);
        }
        if (parsed is not ColumnReference { Attribute: null, OuterJoin: false } column || parsed.Start != 0 || parsed.End != text.Length)
            throw new ArgumentException($"'{name}' is not a plain name made of identifiers, separated by dots.", paramName);

        var parts = column.Parts.Select(p => new Identifier { Value = p.Value, Quoted = p.Quoted, Text = p.Text }).ToList();
        if (parts.Any(p => p.Quoted && p.Value.Length == 0))
            throw new ArgumentException($"'{name}' has an empty quoted name, which Oracle does not allow.", paramName);
        if (parts.Any(p => p.Quoted && p.Value.Contains('"')))
            throw new ArgumentException($"'{name}' has a double quote inside a quoted name, which Oracle does not allow.", paramName);
        return parts;
    }

    /// <summary>Rejects a <c>--</c> or <c>/*</c> outside a double-quoted name: the parser would drop the rest of the text.</summary>
    internal static void RejectComments(string text, string original, string paramName)
    {
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted || i + 1 >= text.Length) continue;
            if ((c == '-' && text[i + 1] == '-') || (c == '/' && text[i + 1] == '*'))
                throw new ArgumentException($"'{original}' contains a comment. Names cannot contain comments.", paramName);
        }
    }
}
