using OracleSqlParser;

namespace OracleSqlParser.Fluent.Tests;

/// <summary>Checks that a built tree prints the expected SQL and that the parser reads that SQL back as the same tree.</summary>
internal static class RoundTrip
{
    /// <summary>Asserts the statement prints as <paramref name="expectedSql"/> and is structurally equal to its parse.</summary>
    public static void AssertStatement(Statement built, string expectedSql)
    {
        Assert.Equal(expectedSql, new SqlScriptGenerator().Generate(built));
        Assert.Equal(expectedSql, built.ToSql());
        var parsed = SqlParser.ParseStatement(expectedSql);
        Assert.True(SqlNodeComparer.StructurallyEqual(built, parsed),
            $"Built tree is not structurally equal to the parse of: {expectedSql}");
    }

    /// <summary>Asserts the expression prints as <paramref name="expectedSql"/> and is structurally equal to its parse.</summary>
    public static void AssertExpression(Expression built, string expectedSql)
    {
        Assert.Equal(expectedSql, new SqlScriptGenerator().Generate(built));
        Assert.Equal(expectedSql, built.ToSql());
        var parsed = SqlParser.ParseExpression(expectedSql);
        Assert.True(SqlNodeComparer.StructurallyEqual(built, parsed),
            $"Built expression is not structurally equal to the parse of: {expectedSql}");
    }
}
