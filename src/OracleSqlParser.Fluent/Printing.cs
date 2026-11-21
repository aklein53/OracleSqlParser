namespace OracleSqlParser.Fluent;

/// <summary>
/// <c>Validate</c> and <c>ToSql</c> for built and parsed trees. They check the tree is complete, print it with
/// <see cref="SqlScriptGenerator"/>, and check the printed SQL for a target Oracle version.
/// </summary>
/// <remarks>
/// For a <see cref="SqlScript"/>, a <see cref="Statement"/> or an <see cref="Expression"/> the printed text is parsed again with
/// the target version. Any diagnostic is an error, and the reparsed tree must be <see cref="SqlNodeComparer.StructurallyEqual"/>
/// to the node, so a tree that would print as different SQL is refused.
/// <para>
/// Other node types (a <c>WHERE</c> clause, a <c>FROM</c> clause, a table reference, a select item) are not statements, so
/// <see cref="Validate{T}"/> checks their completeness only. <see cref="ToSql"/> hands them to <see cref="SqlScriptGenerator"/>,
/// which throws <see cref="NotSupportedException"/> for a node it cannot print on its own; that exception is not caught.
/// </para>
/// </remarks>
public static class SqlNodeFluentExtensions
{
    /// <summary>
    /// Checks that the node is complete and that its printed SQL parses for <paramref name="target"/> and reads back as the same
    /// tree. The check prints with the default generator options.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="node">The node to check.</param>
    /// <param name="target">The Oracle version the SQL must parse for. Defaults to Oracle 23.</param>
    /// <returns>The same node.</returns>
    /// <exception cref="InvalidOperationException">
    /// A part is missing (for example an UPDATE with no SET items), the printed SQL does not parse for <paramref name="target"/>,
    /// or it does not read back as the same tree (statements, scripts and expressions only).
    /// </exception>
    public static T Validate<T>(this T node, OracleVersion target = OracleVersion.Oracle23) where T : SqlNode
    {
        ArgumentNullException.ThrowIfNull(node);
        Validation.Check(node, target);
        return node;
    }

    /// <summary>
    /// Checks the node as <see cref="Validate{T}"/> does, then prints it with <see cref="SqlScriptGenerator"/>. The text returned is
    /// the text that was checked.
    /// </summary>
    /// <param name="node">The node to print.</param>
    /// <param name="options">The generator options, or null for the defaults. They change layout and casing only, not the tree.</param>
    /// <param name="target">The Oracle version the SQL must parse for. Defaults to Oracle 23.</param>
    /// <returns>The SQL text.</returns>
    /// <exception cref="InvalidOperationException">
    /// A part is missing, the printed SQL does not parse for <paramref name="target"/> (for example <c>Lit(true)</c> on Oracle 19), or
    /// it does not read back as the same tree.
    /// </exception>
    public static string ToSql(this SqlNode node, SqlScriptGeneratorOptions? options = null, OracleVersion target = OracleVersion.Oracle23)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Validation.Print(node, options ?? new SqlScriptGeneratorOptions(), target);
    }
}
