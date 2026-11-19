using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Tests for the nodes and members of the 1.0 API consistency pass: each new clause node spans its keyword, is taken as
/// written or replaced by a rewriter, and round trips through the generator.
/// </summary>
public class ApiConsistencyTests
{
    private static SqlScript Parsed(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return r.Script;
    }

    private static string Text(string sql, SqlNode node) => sql[node.Start..node.End];

    private static string Generate(SqlScript script) => new SqlScriptGenerator().Generate(script).Trim();

    /// <summary>The DML RETURNING clause spans its keyword through the last INTO target; the INTO keeps its own span.</summary>
    [Theory]
    [InlineData("UPDATE t SET a = 1 RETURNING a INTO v", "RETURNING a INTO v", "INTO v")]
    [InlineData("DELETE FROM t RETURNING a, b BULK COLLECT INTO x, y", "RETURNING a, b BULK COLLECT INTO x, y", "BULK COLLECT INTO x, y")]
    [InlineData("INSERT INTO t VALUES (1) RETURN a INTO v", "RETURN a INTO v", "INTO v")]
    [InlineData("INSERT INTO t VALUES (1) RETURNING /* c */ a INTO v", "RETURNING /* c */ a INTO v", "INTO v")]
    public void DmlReturningSpansItsKeywordThroughTheInto(string sql, string expected, string expectedInto)
    {
        var returning = Assert.IsAssignableFrom<DmlStatement>(Parsed(sql).Statements[0]).Returning!;

        Assert.Equal(expected, Text(sql, returning));
        Assert.Equal(expectedInto, Text(sql, returning.Into!));
    }

    /// <summary>A DML statement without RETURNING has no returning clause, rather than an empty one.</summary>
    [Fact]
    public void AStatementWithoutReturningHasNoReturningClause()
    {
        Assert.Null(Assert.IsAssignableFrom<DmlStatement>(Parsed("DELETE FROM t WHERE a = 1").Statements[0]).Returning);
    }

    [Theory]
    [InlineData("UPDATE t SET a = 1 RETURNING a INTO v")]
    [InlineData("DELETE FROM t RETURNING a, b BULK COLLECT INTO x, y")]
    [InlineData("INSERT INTO t VALUES (1) RETURNING a INTO v")]
    public void DmlReturningRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>NEXT and FIRST are synonyms: the spelling is kept for generation, but the two compare structurally equal.</summary>
    [Fact]
    public void FetchNextAndFirstCompareEqualButKeepTheirSpelling()
    {
        var next = Parsed("SELECT a FROM t FETCH NEXT 10 ROWS ONLY");
        var first = Parsed("SELECT a FROM t FETCH FIRST 10 ROWS ONLY");

        Assert.True(SqlNodeComparer.StructurallyEqual(next, first));
        Assert.True(Assert.IsType<FetchClause>(Assert.IsType<SelectStatement>(next.Statements[0]).Fetch).Next);
        Assert.Contains("FETCH NEXT 10", Generate(next));
        Assert.Contains("FETCH FIRST 10", Generate(first));
    }

    /// <summary>A rewriter removes the RETURNING clause, with its INTO, by returning null for it.</summary>
    [Fact]
    public void ARewriterCanDropTheReturningClause()
    {
        var script = Parsed("UPDATE t SET a = 1 RETURNING a INTO v");
        new DropReturning().RewriteTree(script);

        Assert.Equal("UPDATE t SET a = 1;", Generate(script));
    }

    private sealed class DropReturning : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(ReturningClause node) => null;
    }
}
