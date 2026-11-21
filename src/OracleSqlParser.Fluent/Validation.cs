namespace OracleSqlParser.Fluent;

/// <summary>
/// The checks behind <c>Validate</c> and <c>ToSql</c>: the tree must be complete, and the printed SQL must parse for the target
/// Oracle version and read back as the same tree. The core generator checks neither, so it would print <c>SET </c> or
/// <c>ON </c> with nothing after them.
/// </summary>
internal static class Validation
{
    /// <summary>
    /// Prints <paramref name="node"/> and checks the result. Completeness is checked first, so the messages name the missing
    /// part and not a parse error. Statements, scripts and expressions are then reparsed for <paramref name="target"/>, and the
    /// reparsed tree must equal the node. Other node types are checked for completeness only.
    /// </summary>
    /// <returns>The printed SQL.</returns>
    internal static string Print(SqlNode node, SqlScriptGeneratorOptions options, OracleVersion target)
    {
        CheckComplete(node);
        string sql = new SqlScriptGenerator(options).Generate(node);
        var parse = new ParseOptions { OracleVersion = target };
        switch (node)
        {
            case SqlScript script:
            {
                var result = SqlParser.Parse(sql, parse);
                ThrowIfErrors(result.Errors, target, sql);
                CheckSameTree(script, result.Script, sql);
                break;
            }
            case Statement statement:
            {
                Statement parsed;
                try
                {
                    parsed = SqlParser.ParseStatement(sql, parse);
                }
                catch (ParseException ex)
                {
                    // ParseStatement reports only its first diagnostic; Parse reports every one.
                    var errors = SqlParser.Parse(sql, parse).Errors;
                    ThrowIfErrors(errors.Count > 0 ? errors : ex.Errors, target, sql);
                    throw;
                }
                CheckSameTree(statement, parsed, sql);
                break;
            }
            case Expression expression:
            {
                Expression parsed;
                try
                {
                    parsed = SqlParser.ParseExpression(sql, parse);
                }
                catch (ParseException ex)
                {
                    ThrowIfErrors(ex.Errors, target, sql);
                    throw;
                }
                CheckSameTree(expression, parsed, sql);
                break;
            }
        }
        return sql;
    }

    /// <summary>Throws when the tree has a missing part, or a shape that a fluent call would print as invalid SQL.</summary>
    internal static void CheckComplete(SqlNode node)
    {
        var all = node.Descendants().ToList();
        foreach (var n in all) CheckStatementParts(n);
        foreach (var n in all)
        {
            if (n is NamedTable { Name.Count: 0 })
                throw Incomplete("A table name is empty. A table was never set, such as a MERGE source that was not given with Using.");
            if (n is ColumnReference { Parts.Count: 0 })
                throw Incomplete("A column reference has no name, such as a column that was never set.");
        }
        foreach (var n in all)
        {
            if (n is InsertStatement insert) CheckInsertWidth(insert);
        }
    }

    /// <summary>The required parts of each statement and query block, checked before the generic walk so the messages name them.</summary>
    private static void CheckStatementParts(SqlNode node)
    {
        switch (node)
        {
            case UpdateStatement { Set.Items.Count: 0 }:
                throw Incomplete("UPDATE has no SET items. Add one with Set.");
            case MergeStatement merge:
                if (Placeholders.IsMissing(merge.Source)) throw Incomplete("MERGE has no USING source. Set one with Using.");
                if (Placeholders.IsMissing(merge.On)) throw Incomplete("MERGE has no ON condition. Set one with On.");
                if (merge.Clauses.Count == 0) throw Incomplete("MERGE has no WHEN MATCHED or WHEN NOT MATCHED clause. Add one.");
                break;
            case InsertStatement insert when !HasRows(insert):
                throw Incomplete("INSERT has neither VALUES nor a query. Add a row with Values, or a query with Query.");
            case QuerySpecification { SelectList.Count: 0 }:
                throw Incomplete("A SELECT list is empty.");
        }
    }

    /// <summary>True when an <c>INSERT</c> has a <c>VALUES</c> row, a <c>VALUES</c> record or a query.</summary>
    private static bool HasRows(InsertStatement insert) =>
        insert.Query is not null || insert.Values is { Record: not null } || insert.Values?.Rows.Count > 0;

    /// <summary>
    /// When an <c>INSERT</c> has a column list and a query, the query's leftmost query block must select as many items as there
    /// are columns. A star item makes the count unknown, so the check is skipped for it.
    /// </summary>
    private static void CheckInsertWidth(InsertStatement insert)
    {
        if (insert.Columns.Count == 0 || insert.Query is null) return;
        var block = LeftmostBlock(insert.Query);
        if (block is null || block.SelectList.Any(item => item is StarSelectItem)) return;
        if (block.SelectList.Count != insert.Columns.Count)
            throw new InvalidOperationException(
                $"INSERT lists {insert.Columns.Count} column(s) but its query selects {block.SelectList.Count} item(s); they must match.");
    }

    /// <summary>The query block that a set operation or parenthesized query starts with, or null for a <c>VALUES</c> list.</summary>
    private static QuerySpecification? LeftmostBlock(SelectStatement query) => Leftmost(query.Body);

    private static QuerySpecification? Leftmost(QueryExpression? body) => body switch
    {
        QuerySpecification block => block,
        SetOperation op => Leftmost(op.Left),
        ParenthesizedQuery paren => LeftmostBlock(paren.Query),
        _ => null,
    };

    private static void ThrowIfErrors(IReadOnlyList<ParseError> errors, OracleVersion target, string sql)
    {
        if (errors.Count == 0) return;
        var messages = string.Join("; ", errors.Select(e => e.Message));
        throw new InvalidOperationException($"The printed SQL is not valid for Oracle {(int)target}: {messages}. SQL: {sql}");
    }

    private static void CheckSameTree(SqlNode built, SqlNode parsed, string sql)
    {
        if (!SqlNodeComparer.StructurallyEqual(built, parsed))
            throw new InvalidOperationException($"The printed SQL does not read back as the same tree: {sql}");
    }

    private static InvalidOperationException Incomplete(string message) => new(message);
}
