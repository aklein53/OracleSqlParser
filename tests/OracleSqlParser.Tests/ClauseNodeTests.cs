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

    /// <summary>The OFFSET clause spans its keyword through ROW or ROWS, and a comment inside it stays inside.</summary>
    [Theory]
    [InlineData("SELECT a FROM t OFFSET 5 ROWS", "OFFSET 5 ROWS")]
    [InlineData("SELECT a FROM t OFFSET /*c*/ 5 ROWS", "OFFSET /*c*/ 5 ROWS")]
    [InlineData("SELECT a FROM t OFFSET  5  ROW", "OFFSET  5  ROW")]
    [InlineData("SELECT a FROM t ORDER BY a\n OFFSET 2 ROWS\n FETCH FIRST 1 ROWS ONLY", "OFFSET 2 ROWS")]
    public void OffsetSpansItsKeywordThroughRowOrRows(string sql, string expected)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal(expected, Text(sql, Assert.IsType<SelectStatement>(r.Script.Statements[0]).Offset!));
    }

    /// <summary>The count is the clause's only child, so the offset and the FETCH clause stay independent.</summary>
    [Fact]
    public void OffsetIsIndependentOfFetch()
    {
        const string sql = "SELECT a FROM t OFFSET 5 ROWS";
        var stmt = Assert.IsType<SelectStatement>(SqlParser.Parse(sql).Script.Statements[0]);

        Assert.Equal("5", Text(sql, stmt.Offset!.Offset));
        Assert.Null(stmt.Fetch);
    }

    /// <summary>The generator writes ROWS whichever spelling was read, and the result parses back to the same tree.</summary>
    [Fact]
    public void OffsetRoundTripsAndWritesRows()
    {
        var script = SqlParser.Parse("SELECT a FROM t OFFSET 5 ROW").Script;
        var text = new SqlScriptGenerator().Generate(script).Trim();

        Assert.Equal("SELECT a FROM t OFFSET 5 ROWS;", text);
        Assert.True(SqlNodeComparer.StructurallyEqual(script, SqlParser.Parse(text).Script));
    }

    /// <summary>A rewriter can replace the offset, or drop it by returning null for the clause.</summary>
    [Fact]
    public void ARewriterCanReplaceOrDropTheOffset()
    {
        var replaced = SqlParser.Parse("SELECT a FROM t OFFSET 5 ROWS").Script;
        new ReplaceOffset().RewriteTree(replaced);
        Assert.Equal("SELECT a FROM t OFFSET 7 ROWS;", new SqlScriptGenerator().Generate(replaced).Trim());

        var dropped = SqlParser.Parse("SELECT a FROM t OFFSET 5 ROWS").Script;
        new DropOffset().RewriteTree(dropped);
        Assert.Equal("SELECT a FROM t;", new SqlScriptGenerator().Generate(dropped).Trim());
    }

    private sealed class ReplaceOffset : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(OffsetClause node) =>
            new OffsetClause { Offset = new Literal { Kind = LiteralKind.Number, Value = "7" } };
    }

    private sealed class DropOffset : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(OffsetClause node) => null;
    }

    /// <summary>Oracle requires ROW or ROWS after the count (ORA-03047), so a bare count is a parse error, as are a missing count and a stray ROWS.</summary>
    [Theory]
    [InlineData("SELECT a FROM t OFFSET")]
    [InlineData("SELECT a FROM t OFFSET ROWS")]
    [InlineData("SELECT a FROM t OFFSET 5")]
    [InlineData("SELECT a FROM t OFFSET 5 FETCH FIRST 2 ROWS ONLY")]
    public void AnOffsetWithoutRowOrRowsIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>The WITHIN GROUP clause starts at WITHIN and ends at its closing parenthesis; the ORDER BY inside is its child.</summary>
    [Fact]
    public void WithinGroupSpansWithinThroughItsParenthesis()
    {
        const string sql = "SELECT LISTAGG(n, ',') WITHIN /*c*/ GROUP (ORDER BY n DESC) FROM t";
        var call = Assert.IsType<FunctionCall>(Assert.IsType<ExpressionSelectItem>(Query(sql).SelectList[0]).Expression);

        Assert.Equal("WITHIN /*c*/ GROUP (ORDER BY n DESC)", Text(sql, call.WithinGroup!));
        Assert.Equal("ORDER BY n DESC", Text(sql, call.WithinGroup!.OrderBy));
    }

    /// <summary>The generator writes WITHIN GROUP back, and the output parses to the same tree.</summary>
    [Fact]
    public void WithinGroupRoundTrips()
    {
        const string text = "SELECT LISTAGG(n, ',') WITHIN GROUP (ORDER BY n DESC) FROM t;";
        var script = SqlParser.Parse(text).Script;

        Assert.Equal(text, new SqlScriptGenerator().Generate(script).Trim());
        Assert.True(SqlNodeComparer.StructurallyEqual(script, SqlParser.Parse(new SqlScriptGenerator().Generate(script)).Script));
    }

    /// <summary>A rewriter removes the whole WITHIN GROUP clause by returning null for it.</summary>
    [Fact]
    public void ARewriterCanDropWithinGroup()
    {
        var script = SqlParser.Parse("SELECT LISTAGG(n, ',') WITHIN GROUP (ORDER BY n) FROM t").Script;
        new DropWithinGroup().RewriteTree(script);

        Assert.Equal("SELECT LISTAGG(n, ',') FROM t;", new SqlScriptGenerator().Generate(script).Trim());
    }

    private sealed class DropWithinGroup : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(WithinGroupClause node) => null;
    }

    [Theory]
    [InlineData("SELECT LISTAGG(n, ',') WITHIN GROUP (ORDER BY) FROM t")]
    [InlineData("SELECT LISTAGG(n, ',') WITHIN GROUP FROM t")]
    [InlineData("SELECT LISTAGG(n, ',') WITHIN GROUP (ORDER BY n FROM t")]
    public void AWithinGroupWithoutAnOrderByIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>The INTO clause spans its keyword, or BULK COLLECT INTO from its BULK, through the last target.</summary>
    [Theory]
    [InlineData("SELECT a INTO x, y FROM t", "INTO x, y", false)]
    [InlineData("SELECT a BULK  COLLECT INTO /*c*/ tab FROM t", "BULK  COLLECT INTO /*c*/ tab", true)]
    public void SelectIntoSpansItsKeywords(string sql, string expected, bool bulk)
    {
        var into = Query(sql).Into!;

        Assert.Equal(expected, Text(sql, into));
        Assert.Equal(bulk, into.BulkCollect);
    }

    [Theory]
    [InlineData("FETCH c1 INTO a, b", "INTO a, b", false)]
    [InlineData("FETCH c1 BULK COLLECT INTO tab LIMIT 5", "BULK COLLECT INTO tab", true)]
    [InlineData("FETCH c1 BULK  COLLECT INTO /*c*/ tab", "BULK  COLLECT INTO /*c*/ tab", true)]
    public void FetchIntoSpansItsKeywords(string statement, string expected, bool bulk)
    {
        var sql = InBlock(statement);
        var into = Assert.Single(Parsed(sql).Script.Descendants().OfType<FetchStatement>()).Into;

        Assert.Equal(expected, Text(sql, into));
        Assert.Equal(bulk, into.BulkCollect);
    }

    /// <summary>FETCH and EXECUTE IMMEDIATE are PL/SQL statements, so the tests parse them inside a block.</summary>
    private static string InBlock(string statement) => "BEGIN " + statement + "; END;";

    private static ParseResult Parsed(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return r;
    }

    /// <summary>EXECUTE IMMEDIATE has an INTO and a RETURNING INTO clause; the RETURNING keyword is not inside either span.</summary>
    [Theory]
    [InlineData("EXECUTE IMMEDIATE 'x' INTO v USING 1", "INTO v", false)]
    [InlineData("EXECUTE IMMEDIATE 'x' BULK COLLECT INTO v", "BULK COLLECT INTO v", true)]
    public void ExecuteImmediateIntoSpansItsKeywords(string statement, string expected, bool bulk)
    {
        var sql = InBlock(statement);
        var into = Assert.Single(Parsed(sql).Script.Descendants().OfType<ExecuteImmediateStatement>()).Into!;

        Assert.Equal(expected, Text(sql, into));
        Assert.Equal(bulk, into.BulkCollect);
    }

    /// <summary>The RETURNING INTO clause starts at its INTO (or BULK), not at the RETURNING keyword before it.</summary>
    [Fact]
    public void ReturningIntoStartsAtInto()
    {
        var sql = InBlock("EXECUTE IMMEDIATE 'x' USING 1 RETURNING BULK COLLECT INTO r");
        var stmt = Assert.Single(Parsed(sql).Script.Descendants().OfType<ExecuteImmediateStatement>());

        Assert.Null(stmt.Into);
        Assert.Equal("BULK COLLECT INTO r", Text(sql, stmt.ReturningInto!));
        Assert.True(stmt.ReturningInto!.BulkCollect);
    }

    [Theory]
    [InlineData("UPDATE t SET a = 1 RETURNING a INTO v", "INTO v", false)]
    [InlineData("DELETE FROM t RETURNING a BULK  COLLECT INTO v", "BULK  COLLECT INTO v", true)]
    [InlineData("INSERT INTO t VALUES (1) RETURNING a, b BULK COLLECT INTO x, y", "BULK COLLECT INTO x, y", true)]
    public void DmlReturningIntoSpansItsKeywords(string sql, string expected, bool bulk)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var into = Assert.IsAssignableFrom<DmlStatement>(r.Script.Statements[0]).ReturningInto!;

        Assert.Equal(expected, Text(sql, into));
        Assert.Equal(bulk, into.BulkCollect);
    }

    /// <summary>A rewriter can drop a query's INTO, or replace it with a BULK COLLECT INTO over the same targets.</summary>
    [Fact]
    public void ARewriterCanDropOrReplaceAnInto()
    {
        var dropped = SqlParser.Parse("SELECT a INTO x FROM t").Script;
        new DropInto().RewriteTree(dropped);
        Assert.Equal("SELECT a FROM t;", new SqlScriptGenerator().Generate(dropped).Trim());

        var replaced = SqlParser.Parse("SELECT a INTO x FROM t").Script;
        new BulkInto().RewriteTree(replaced);
        Assert.Equal("SELECT a BULK COLLECT INTO x FROM t;", new SqlScriptGenerator().Generate(replaced).Trim());
    }

    private sealed class DropInto : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(IntoClause node) => null;
    }

    private sealed class BulkInto : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(IntoClause node) => new IntoClause { BulkCollect = true, Targets = node.Targets };
    }

    [Theory]
    [InlineData("SELECT a INTO x, y FROM t")]
    [InlineData("SELECT a BULK COLLECT INTO tab FROM t")]
    [InlineData("FETCH c1 BULK COLLECT INTO a LIMIT 5")]
    [InlineData("EXECUTE IMMEDIATE 'x' INTO v USING 1 RETURNING INTO r")]
    [InlineData("UPDATE t SET a = 1 RETURNING a BULK COLLECT INTO v")]
    public void IntoRoundTrips(string sql)
    {
        var script = SqlParser.Parse(sql).Script;
        var text = new SqlScriptGenerator().Generate(script);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, SqlParser.Parse(text).Script), text);
    }

    [Theory]
    [InlineData("FETCH c1 INTO")]
    [InlineData("FETCH c1 BULK COLLECT a")]
    [InlineData("UPDATE t SET a = 1 RETURNING a BULK INTO v")]
    public void AnIntoWithoutItsTargetsOrKeywordIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>
    /// The WITH clause spans WITH through its last common table expression. Without CTEs it ends at the <c>;</c> that
    /// closes its last WITH FUNCTION or WITH PROCEDURE declaration.
    /// </summary>
    [Theory]
    [InlineData("WITH x AS (SELECT 1 FROM dual) SELECT * FROM x", "WITH x AS (SELECT 1 FROM dual)")]
    [InlineData("WITH /*c*/ a AS (SELECT 1 FROM dual),\n b (c) AS (SELECT 2 FROM dual) SELECT * FROM a",
        "WITH /*c*/ a AS (SELECT 1 FROM dual),\n b (c) AS (SELECT 2 FROM dual)")]
    [InlineData("WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END; SELECT f FROM dual",
        "WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END;")]
    [InlineData("WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END; x AS (SELECT 1 FROM dual) SELECT f FROM x",
        "WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END; x AS (SELECT 1 FROM dual)")]
    public void WithSpansItsKeywordThroughItsLastItem(string sql, string expected)
    {
        var with = Assert.Single(Parsed(sql).Script.Descendants().OfType<WithClause>());

        Assert.Equal(expected, Text(sql, with));
    }

    /// <summary>A WITH in a subquery is that subquery's own clause, so the outer query has none.</summary>
    [Fact]
    public void ASubqueryHasItsOwnWithClause()
    {
        const string sql = "SELECT * FROM (WITH x AS (SELECT 1 FROM dual) SELECT * FROM x)";
        var with = Assert.Single(Parsed(sql).Script.Descendants().OfType<WithClause>());

        Assert.Null(Assert.IsType<SelectStatement>(Parsed(sql).Script.Statements[0]).With);
        Assert.Equal("WITH x AS (SELECT 1 FROM dual)", Text(sql, with));
    }

    /// <summary>A rewriter removes the WITH clause, the CTEs and declarations with it, by returning null for it.</summary>
    [Fact]
    public void ARewriterCanDropTheWithClause()
    {
        var script = SqlParser.Parse("WITH x AS (SELECT 1 FROM dual) SELECT * FROM x").Script;
        new DropWith().RewriteTree(script);

        Assert.Equal("SELECT * FROM x;", new SqlScriptGenerator().Generate(script).Trim());
    }

    private sealed class DropWith : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(WithClause node) => null;
    }

    [Theory]
    [InlineData("WITH x AS (SELECT 1 FROM dual) SELECT * FROM x")]
    [InlineData("WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END; SELECT f FROM dual")]
    public void WithRoundTrips(string sql)
    {
        var script = SqlParser.Parse(sql).Script;
        var text = new SqlScriptGenerator().Generate(script);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, SqlParser.Parse(text).Script), text);
    }

    [Theory]
    [InlineData("WITH SELECT 1 FROM dual")]
    [InlineData("WITH AS (SELECT 1 FROM dual) SELECT 1 FROM dual")]
    [InlineData("WITH x AS SELECT 1 FROM dual")]
    public void AWithWithoutItsNameOrQueryIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>
    /// Each grouping kind spans the text it was written as: ROLLUP and its keyword, a parenthesized list, the empty set,
    /// and a whole GROUPING SETS.
    /// </summary>
    [Fact]
    public void GroupingSpecsSpanWhatWasWritten()
    {
        const string sql = "SELECT a FROM t GROUP BY ROLLUP /*c*/ (a, (b, c)), GROUPING SETS ((d, e), ROLLUP(f), ()), (g, h), (), i";
        var items = Query(sql).GroupBy!.Items;

        var rollup = Assert.IsType<RollupGrouping>(items[0]);
        Assert.Equal("ROLLUP /*c*/ (a, (b, c))", Text(sql, rollup));
        Assert.Equal("(b, c)", Text(sql, rollup.Items[1]));
        var sets = Assert.IsType<GroupingSetsGrouping>(items[1]);
        Assert.Equal("GROUPING SETS ((d, e), ROLLUP(f), ())", Text(sql, sets));
        Assert.Equal("ROLLUP(f)", Text(sql, sets.Sets[1]));
        Assert.Equal("()", Text(sql, sets.Sets[2]));
        Assert.Equal("(g, h)", Text(sql, items[2]));
        Assert.Equal("()", Text(sql, items[3]));
        Assert.Equal("i", Text(sql, items[4]));
        Assert.IsType<ExpressionGrouping>(items[4]);
    }

    /// <summary>A single parenthesized expression is an expression grouping that keeps its parentheses, as elsewhere.</summary>
    [Fact]
    public void ASingleParenthesizedGroupingIsAnExpression()
    {
        var eg = Assert.IsType<ExpressionGrouping>(Query("SELECT a FROM t GROUP BY (a)").GroupBy!.Items[0]);

        Assert.IsType<ParenthesizedExpression>(eg.Expression);
    }

    [Fact]
    public void AGroupingSpecRoundTripsAsWritten()
    {
        const string text = "SELECT a FROM t GROUP BY ROLLUP(a, (b, c)), CUBE(d), GROUPING SETS((e), (f, g), ROLLUP(h), ()), (i, j), ();";
        var script = SqlParser.Parse(text).Script;

        Assert.Equal(text, new SqlScriptGenerator().Generate(script).Trim());
        Assert.True(SqlNodeComparer.StructurallyEqual(script, SqlParser.Parse(new SqlScriptGenerator().Generate(script)).Script));
    }

    /// <summary>A rewriter can turn a ROLLUP into a CUBE, and remove one set from a GROUPING SETS by returning null for it.</summary>
    [Fact]
    public void ARewriterCanReplaceOrRemoveAGroupingSpec()
    {
        var replaced = SqlParser.Parse("SELECT a FROM t GROUP BY ROLLUP(a, b)").Script;
        new RollupToCube().RewriteTree(replaced);
        Assert.Equal("SELECT a FROM t GROUP BY CUBE(a, b);", new SqlScriptGenerator().Generate(replaced).Trim());

        var removed = SqlParser.Parse("SELECT a FROM t GROUP BY GROUPING SETS ((a), ())").Script;
        new DropGrandTotal().RewriteTree(removed);
        Assert.Equal("SELECT a FROM t GROUP BY GROUPING SETS((a));", new SqlScriptGenerator().Generate(removed).Trim());
    }

    private sealed class RollupToCube : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(RollupGrouping node) => new CubeGrouping { Items = node.Items };
    }

    private sealed class DropGrandTotal : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(GrandTotalGrouping node) => null;
    }

    /// <summary>Oracle rejects these at parse time, so the parser does too.</summary>
    [Theory]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP()")]
    [InlineData("SELECT a FROM t GROUP BY CUBE()")]
    [InlineData("SELECT a FROM t GROUP BY GROUPING SETS()")]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(())")]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(a, ())")]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(GROUPING SETS(a))")]
    [InlineData("SELECT a FROM t GROUP BY CUBE(GROUPING SETS(a))")]
    [InlineData("SELECT a FROM t GROUP BY GROUPING SETS(GROUPING SETS(a))")]
    [InlineData("SELECT a FROM t GROUP BY GROUPING SETS(((a, b)))")]
    [InlineData("SELECT a FROM t GROUP BY GROUPING SETS((a), ((a, b)))")]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(((a, b)))")]
    [InlineData("SELECT a FROM t GROUP BY ((a, b), c)")]
    [InlineData("SELECT a FROM t GROUP BY (a, (b, c))")]
    [InlineData("SELECT a FROM t GROUP BY (a, ())")]
    [InlineData("SELECT a FROM t GROUP BY (a, ROLLUP(b))")]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(a) + 1")]
    public void AGroupingSpecOracleRejectsIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>
    /// Oracle parses a ROLLUP or CUBE written where a grouping item is expected as an ordinary function call, and rejects
    /// the nonsense only later, so these parse. They are FunctionCall nodes, as Oracle sees them.
    /// </summary>
    [Theory]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(ROLLUP(a))")]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(CUBE(a))")]
    [InlineData("SELECT a FROM t GROUP BY (ROLLUP(a))")]
    [InlineData("SELECT a FROM t GROUP BY GROUPING SETS((ROLLUP(a)))")]
    public void ARollupCallWhereAnItemIsExpectedIsAFunctionCall(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains(r.Script.Descendants().OfType<FunctionCall>(), f => f.Name[0].Value is "ROLLUP" or "CUBE");
    }

    /// <summary>ROLLUP, CUBE and GROUPING are ordinary names outside a GROUP BY list, and GROUPING() stays a function call.</summary>
    [Fact]
    public void GroupingWordsOutsideAGroupByListAreOrdinary()
    {
        const string sql = "SELECT rollup, cube, GROUPING(a) g, GROUPING_ID(a, b) gid FROM t GROUP BY ROLLUP(a, b)";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));

        Assert.Equal(new[] { "GROUPING", "GROUPING_ID" }, r.Script.Descendants().OfType<FunctionCall>().Select(f => f.Name[0].Value).ToArray());
        Assert.Equal(new[] { "ROLLUP", "CUBE" }, Query(sql).SelectList.OfType<ExpressionSelectItem>().Take(2).Select(i => ((ColumnReference)i.Expression).Parts[0].Value).ToArray());
    }

    /// <summary>
    /// Each MERGE action has its own WHERE clause. The DELETE part's clause starts at its WHERE, not at the DELETE before it.
    /// </summary>
    [Fact]
    public void EachMergeActionSpansItsOwnWhere()
    {
        const string sql = "MERGE INTO t USING s ON (t.id = s.id) " +
            "WHEN MATCHED THEN UPDATE SET t.v = s.v WHERE s.v > 0 DELETE  WHERE t.v = 0 " +
            "WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) WHERE s.id > 1";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var clauses = Assert.IsType<MergeStatement>(r.Script.Statements[0]).Clauses;

        Assert.Equal("WHERE s.v > 0", Text(sql, clauses[0].UpdateWhere!));
        Assert.Equal("WHERE t.v = 0", Text(sql, clauses[0].DeleteWhere!));
        Assert.Null(clauses[0].InsertWhere);
        Assert.Equal("WHERE s.id > 1", Text(sql, clauses[1].InsertWhere!));
    }

    /// <summary>The DELETE WHERE of an update action starts at its own WHERE, past a comment.</summary>
    [Fact]
    public void TheMergeDeleteWhereStartsAtItsWhere()
    {
        const string sql = "MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = 1 DELETE /* d */ WHERE /* e */ t.y = 2";
        var clause = Assert.Single(Assert.IsType<MergeStatement>(SqlParser.Parse(sql).Script.Statements[0]).Clauses);

        Assert.Equal("WHERE /* e */ t.y = 2", Text(sql, clause.DeleteWhere!));
        Assert.Null(clause.UpdateWhere);
    }

    /// <summary>Oracle has no delete-only MERGE action (ORA-02000), so DELETE without UPDATE is a parse error, matched or not.</summary>
    [Theory]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN DELETE WHERE t.x = 1")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN DELETE")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN NOT MATCHED THEN DELETE WHERE s.x = 1")]
    public void AMergeDeleteActionWithoutUpdateIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>The generator writes the MERGE wheres back, and the output parses to the same tree.</summary>
    [Fact]
    public void MergeWheresRoundTrip()
    {
        const string sql = "MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v WHERE s.v > 0 " +
            "DELETE WHERE t.v = 0 WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) WHERE s.id > 1";
        var script = SqlParser.Parse(sql).Script;
        var text = new SqlScriptGenerator().Generate(script).Trim();

        Assert.Equal(sql + ";", text);
        Assert.True(SqlNodeComparer.StructurallyEqual(script, SqlParser.Parse(text).Script));
    }

    /// <summary>A rewriter that returns null for every WHERE removes the MERGE actions' WHEREs and nothing else.</summary>
    [Fact]
    public void ARewriterCanDropTheMergeWheres()
    {
        var script = SqlParser.Parse("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v WHERE s.v > 0 DELETE WHERE t.v = 0").Script;
        new DropWhere().RewriteTree(script);

        Assert.Equal("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v;", new SqlScriptGenerator().Generate(script).Trim());
    }

    [Theory]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v DELETE t.v = 0")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) WHERE")]
    public void AMergeWhereWithoutItsConditionIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>KEEP always has its ORDER BY, so a KEEP without one is a parse error and the clause never has a null order.</summary>
    [Fact]
    public void KeepSpansItsOrderBy()
    {
        const string sql = "SELECT MAX(a) KEEP (DENSE_RANK LAST /* c */ ORDER BY b) FROM t";
        var keep = Assert.IsType<FunctionCall>(Assert.IsType<ExpressionSelectItem>(Query(sql).SelectList[0]).Expression).Keep!;

        Assert.Equal("KEEP (DENSE_RANK LAST /* c */ ORDER BY b)", Text(sql, keep));
        Assert.Equal("ORDER BY b", Text(sql, keep.OrderBy));
        Assert.False(keep.First);
    }

    [Fact]
    public void KeepWithoutOrderByIsRejected()
    {
        Assert.False(SqlParser.Parse("SELECT MAX(a) KEEP (DENSE_RANK LAST) FROM t").Success);
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
                OffsetClause => "OFFSET",
                WithinGroupClause => "WITHIN",
                IntoClause into => into.BulkCollect ? "BULK" : "INTO",
                WithClause => "WITH",
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
