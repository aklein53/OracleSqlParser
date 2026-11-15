namespace OracleSqlParser;

/// <summary>
/// The Oracle Database release a script targets. Constructs introduced after the target release are still
/// parsed into the tree, but reported as a ParseError ("... requires Oracle 23 or later (targeting Oracle 19)").
/// The values are the major release numbers, mirroring ScriptDom's versioned parsers.
/// </summary>
public enum OracleVersion
{
    /// <summary>Oracle Database 19c, the long-term-support release. Rejects every construct added in 21c or 23ai.</summary>
    Oracle19 = 19,

    /// <summary>Oracle Database 21c.</summary>
    Oracle21 = 21,

    /// <summary>Oracle Database 23ai, the default. Accepts every construct the parser knows.</summary>
    Oracle23 = 23,
}

/// <summary>Options for <see cref="SqlParser"/>.</summary>
public sealed class ParseOptions
{
    /// <summary>
    /// Maximum nesting depth of expressions, subqueries, PL/SQL blocks and token-tree groups. Exceeding it
    /// is reported as a ParseError ("nesting depth exceeds N") instead of overflowing the stack.
    /// </summary>
    public int MaxNestingDepth { get; init; } = 200;

    /// <summary>
    /// When true (the default), <c>&amp;name</c> and <c>&amp;&amp;name</c> are substitution variables. When false, '&amp;' is
    /// an ordinary operator, as after SQL*Plus <c>SET DEFINE OFF</c>. Scripts can also switch it within the input.
    /// </summary>
    public bool SubstitutionEnabled { get; init; } = true;

    /// <summary>
    /// Reads the input as a SQL*Plus script, with the client's line rules: a '-' that ends a line continues the command
    /// onto the next line (the hyphen and the line break are removed), and a blank line ends an unterminated statement,
    /// as SQL*Plus does by default (SET SQLBLANKLINES OFF). SET SQLBLANKLINES ON in the input turns the blank-line rule off.
    /// SQL*Plus discards a statement that a blank line cuts short; this parser keeps it, so the tree shows what was written.
    /// Off by default, because in plain SQL a line ending in '-' is a minus and a blank line is only whitespace.
    /// </summary>
    public bool SqlPlusScript { get; init; }

    /// <summary>
    /// The Oracle release the input must run on. The default, Oracle23, accepts every construct the parser knows.
    /// Older targets keep the tree and add one ParseError per construct introduced after the target.
    /// </summary>
    public OracleVersion OracleVersion { get; init; } = OracleVersion.Oracle23;
}
