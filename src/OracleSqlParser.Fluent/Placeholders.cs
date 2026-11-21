namespace OracleSqlParser.Fluent;

/// <summary>
/// Stand-ins for required parts that a builder has not filled yet, and the checks that recognise them. Each placeholder is a
/// fresh node in a shape the parser never produces (an empty name, an empty column list), so <see cref="IsMissing(TableReference)"/>
/// and <see cref="IsMissing(Expression)"/> still detect it after <c>DeepClone</c>. Nothing is compared by reference.
/// </summary>
internal static class Placeholders
{
    /// <summary>The <c>USING</c> source of a MERGE that has not been set yet: a table with no name.</summary>
    internal static NamedTable NewSource() => new();

    /// <summary>The <c>ON</c> condition of a MERGE that has not been set yet: a column reference with no parts.</summary>
    internal static ColumnReference NewCondition() => new();

    /// <summary><c>true</c> when the table is the placeholder from <see cref="NewSource"/>: a named table with no name.</summary>
    internal static bool IsMissing(TableReference table) => table is NamedTable { Name.Count: 0 };

    /// <summary><c>true</c> when the expression is the placeholder from <see cref="NewCondition"/>: a column reference with no parts.</summary>
    internal static bool IsMissing(Expression expression) => expression is ColumnReference { Parts.Count: 0 };
}
