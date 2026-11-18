using System.Reflection;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// The clause nodes. What they add over the bare body they wrap is a span that starts at the keyword, so a caller can
/// take the clause as written, or replace it, without looking for the keyword in the token stream.
/// </summary>
public class ClauseNodeTests
{
    private static QuerySpecification Query(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return Assert.IsType<QuerySpecification>(Assert.IsType<SelectStatement>(r.Script.Statements[0]).Body);
    }

    private static string Text(string sql, SqlNode node) => sql[node.Start..node.End];

    [Fact]
    public void EachClauseSpansItsKeywordAndBody()
    {
        const string sql = "SELECT a FROM t WHERE x = 1 GROUP BY a, b HAVING COUNT(*) > 1";
        var q = Query(sql);

        Assert.Equal("FROM t", Text(sql, q.From!));
        Assert.Equal("WHERE x = 1", Text(sql, q.Where!));
        Assert.Equal("GROUP BY a, b", Text(sql, q.GroupBy!));
        Assert.Equal("HAVING COUNT(*) > 1", Text(sql, q.Having!));
    }

    [Fact]
    public void AClauseWrittenOverSeveralLinesIsSpannedWhole()
    {
        const string sql = "SELECT a\n  FROM t\n  WHERE x = 1\n     AND y = 2";

        Assert.Equal("WHERE x = 1\n     AND y = 2", Text(sql, Query(sql).Where!));
    }

    /// <summary>A comment between the keyword and the body is inside the clause, so cutting the span keeps it.</summary>
    [Fact]
    public void ACommentAfterTheKeywordIsInsideTheClause()
    {
        const string sql = "SELECT a FROM t WHERE /* note */ x = 1";

        Assert.Equal("WHERE /* note */ x = 1", Text(sql, Query(sql).Where!));
    }

    [Fact]
    public void TheHierarchicalClausesSpanTheirKeywords()
    {
        const string sql = "SELECT a FROM t START WITH p IS NULL CONNECT BY NOCYCLE PRIOR a = p";
        var q = Query(sql);

        Assert.Equal("START WITH p IS NULL", Text(sql, q.StartWith!));
        Assert.Equal("CONNECT BY NOCYCLE PRIOR a = p", Text(sql, q.ConnectBy!));
        Assert.True(q.ConnectBy!.NoCycle);
    }

    [Fact]
    public void NoCycleIsFalseWhenItIsNotWritten()
    {
        Assert.False(Query("SELECT a FROM t CONNECT BY PRIOR a = p").ConnectBy!.NoCycle);
    }

    [Theory]
    [InlineData("SELECT 1 FROM t ORDER BY a DESC", "ORDER BY a DESC", false)]
    [InlineData("SELECT 1 FROM t CONNECT BY PRIOR a = p ORDER SIBLINGS BY a", "ORDER SIBLINGS BY a", true)]
    public void TheOrderByClauseSpansItsKeyword(string sql, string expected, bool siblings)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var order = Assert.IsType<SelectStatement>(r.Script.Statements[0]).OrderBy!;

        Assert.Equal(expected, Text(sql, order));
        Assert.Equal(siblings, order.Siblings);
    }

    [Fact]
    public void AnAbsentClauseIsNull()
    {
        var q = Query("SELECT a FROM t");

        Assert.Null(q.Where);
        Assert.Null(q.GroupBy);
        Assert.Null(q.Having);
        Assert.Null(q.StartWith);
        Assert.Null(q.ConnectBy);
    }

    /// <summary>A SELECT without FROM is Oracle 23c; the clause is absent rather than an empty one.</summary>
    [Fact]
    public void AnAbsentFromClauseIsNull()
    {
        Assert.Null(Query("SELECT 1").From);
    }

    [Theory]
    [InlineData("UPDATE t SET a = 1 WHERE b = 2", "WHERE b = 2")]
    [InlineData("DELETE FROM t WHERE b = 2", "WHERE b = 2")]
    public void DmlCarriesTheSameWhereClause(string sql, string expected)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var where = Assert.Single(r.Script.Descendants().OfType<WhereClause>());

        Assert.Equal(expected, Text(sql, where));
    }

    [Theory]
    [InlineData("SELECT SUM(x) OVER (PARTITION BY d ORDER BY s) FROM t", "ORDER BY s")]
    [InlineData("SELECT LISTAGG(n, ',') WITHIN GROUP (ORDER BY n) FROM t", "ORDER BY n")]
    [InlineData("SELECT MAX(a) KEEP (DENSE_RANK FIRST ORDER BY b) FROM t", "ORDER BY b")]
    [InlineData("SELECT XMLAGG(x ORDER BY y) FROM t", "ORDER BY y")]
    public void ANestedOrderBySpansItsKeywordToo(string sql, string expected)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var order = Assert.Single(r.Script.Descendants().OfType<OrderByClause>());

        Assert.Equal(expected, Text(sql, order));
    }

    /// <summary>Each query block reports its own clauses, so a subquery does not report the outer one.</summary>
    [Fact]
    public void ASubqueryReportsItsOwnClauses()
    {
        const string sql = "SELECT a FROM (SELECT b FROM t WHERE c = 1) WHERE a = 2";
        var outer = Query(sql);
        var inner = outer.Descendants().OfType<QuerySpecification>().Single(q => q != outer);

        Assert.Equal("WHERE a = 2", Text(sql, outer.Where!));
        Assert.Equal("WHERE c = 1", Text(sql, inner.Where!));
    }

    /// <summary>The clause is a node, so a rewriter removes a whole clause by returning null for it.</summary>
    [Fact]
    public void ARewriterCanDropAWholeClause()
    {
        var script = SqlParser.Parse("SELECT a FROM t WHERE x = 1 ORDER BY a").Script;
        new DropWhere().RewriteTree(script);

        Assert.Equal("SELECT a FROM t ORDER BY a;", new SqlScriptGenerator().Generate(script).Trim());
    }

    private sealed class DropWhere : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(WhereClause node) => null;
    }

    public static IEnumerable<object[]> Corpus() =>
        typeof(SmokeCorpusTests).GetMethod("Parses")!.GetCustomAttributes<InlineDataAttribute>()
            .SelectMany(a => a.GetData(null!))
            .Select(d => new object[] { (string)d[0] });

    /// <summary>
    /// Across the smoke corpus: every clause node starts at the keyword it is named for, and ends at or after the
    /// content it holds. This is what catches a parse path that builds a clause without giving it the keyword's span.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryClauseNodeStartsAtItsKeyword(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));

        foreach (var node in r.Script.Descendants())
        {
            string? keyword = node switch
            {
                FromClause => "FROM",
                WhereClause => "WHERE",
                StartWithClause => "START",
                ConnectByClause => "CONNECT",
                GroupByClause => "GROUP",
                HavingClause => "HAVING",
                OrderByClause => "ORDER",
                _ => null,
            };
            if (keyword is null)
                continue;

            Assert.InRange(node.Start, 0, sql.Length - keyword.Length);
            Assert.Equal(keyword, sql.Substring(node.Start, keyword.Length), ignoreCase: true);
            foreach (var child in node.GetChildren())
            {
                Assert.True(child.Start >= node.Start, $"{node.GetType().Name} starts after its content in: {sql}");
                Assert.True(child.End <= node.End, $"{node.GetType().Name} ends before its content in: {sql}");
            }
        }
    }
}
