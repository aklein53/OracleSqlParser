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

    /// <summary>The VALUES clause spans its keyword through its last row; each row spans its parentheses.</summary>
    [Fact]
    public void ValuesClauseAndItsRowsSpanTheirKeywordAndParentheses()
    {
        const string sql = "INSERT INTO t VALUES /* c */ (1, 2), (3, 4)";
        var values = Assert.IsAssignableFrom<InsertStatement>(Parsed(sql).Statements[0]).Values!;

        Assert.Equal("VALUES /* c */ (1, 2), (3, 4)", Text(sql, values));
        Assert.Equal(["(1, 2)", "(3, 4)"], values.Rows.Select(r => Text(sql, r)));
        Assert.Null(values.Record);
    }

    /// <summary>PL/SQL's <c>VALUES rec</c> has no rows; the record is the clause's expression.</summary>
    [Fact]
    public void ValuesRecordHasNoRows()
    {
        const string sql = "BEGIN INSERT INTO t VALUES rec; END;";
        var insert = Assert.Single(Parsed(sql).Descendants().OfType<InsertStatement>());

        Assert.Empty(insert.Values!.Rows);
        Assert.Equal("rec", Text(sql, insert.Values!.Record!));
        Assert.Equal("VALUES rec", Text(sql, insert.Values!));
    }

    /// <summary>An INSERT has exactly one of VALUES and a query: the other is null.</summary>
    [Fact]
    public void InsertSelectHasNoValuesClause()
    {
        var insert = Assert.IsType<InsertStatement>(Parsed("INSERT INTO t SELECT 1, 2 FROM dual").Statements[0]);

        Assert.Null(insert.Values);
        Assert.NotNull(insert.Query);
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1, 2), (3, 4)")]
    [InlineData("INSERT INTO t (a, b) VALUES (1, DEFAULT)")]
    [InlineData("INSERT ALL INTO a (x) VALUES (1) INTO b VALUES (2) SELECT 1 FROM dual")]
    [InlineData("WITH x(a, b) AS (VALUES (1, 2), (3, 4)) SELECT * FROM x")]
    [InlineData("SELECT * FROM (VALUES (1, 'a'), (2, 'b')) v (c1, c2)")]
    public void ValuesRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>A rewriter can replace a row list, and the generator prints the new rows.</summary>
    [Fact]
    public void ARewriterCanReplaceTheValuesRows()
    {
        var script = Parsed("INSERT INTO t VALUES (1), (2)");
        new OneRow().RewriteTree(script);

        Assert.Equal("INSERT INTO t VALUES (9);", Generate(script));
    }

    private sealed class OneRow : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(RowValue node) =>
            node.Values[0] is Literal { Value: "1" } ? new RowValue { Values = [new Literal { Kind = LiteralKind.Number, Value = "9" }] } : null;
    }

    /// <summary>An INSERT's column list holds column references, so a qualified name such as <c>x.a</c> is kept (Oracle accepts it).</summary>
    [Fact]
    public void InsertColumnsAreQualifiedColumnReferences()
    {
        const string sql = "INSERT INTO t x (x.a, b) VALUES (1, 2)";
        var columns = Assert.IsType<InsertStatement>(Parsed(sql).Statements[0]).Columns;

        Assert.Equal(["x.a", "b"], columns.Select(c => Text(sql, c)));
        Assert.Equal(2, columns[0].Parts.Count);
    }

    [Theory]
    [InlineData("INSERT INTO t x (x.a, b) VALUES (1, 2)")]
    [InlineData("INSERT INTO t (t.a) VALUES (1)")]
    [InlineData("INSERT ALL INTO t (t.a) VALUES (1) SELECT 1 FROM dual")]
    public void InsertColumnListRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>Each MERGE clause spans WHEN through the end of its action, and the two kinds are separate node types.</summary>
    [Fact]
    public void MergeClausesSpanWhenThroughTheirAction()
    {
        const string sql = "MERGE INTO t USING s ON (t.id = s.id) " +
            "WHEN MATCHED AND s.v > 0 THEN UPDATE SET t.v = s.v WHERE s.v > 1 DELETE WHERE t.v = 0 " +
            "WHEN NOT MATCHED THEN INSERT (t.id) VALUES (s.id) WHERE s.id > 1";
        var clauses = Assert.IsType<MergeStatement>(Parsed(sql).Statements[0]).Clauses;

        Assert.Equal("WHEN MATCHED AND s.v > 0 THEN UPDATE SET t.v = s.v WHERE s.v > 1 DELETE WHERE t.v = 0", Text(sql, clauses[0]));
        Assert.Equal("WHEN NOT MATCHED THEN INSERT (t.id) VALUES (s.id) WHERE s.id > 1", Text(sql, clauses[1]));
        Assert.IsType<MergeMatchedClause>(clauses[0]);
        Assert.Equal("s.v > 0", Text(sql, clauses[0].AndCondition!));
        Assert.Equal("VALUES (s.id)", Text(sql, Assert.IsType<MergeNotMatchedClause>(clauses[1]).Values));
    }

    [Theory]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN DELETE")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN INSERT (id) VALUES (1)")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN NOT MATCHED THEN UPDATE SET t.v = 1")]
    public void AMergeActionThatIsNotUpdateOrInsertIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    [Theory]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v WHERE s.v > 1 DELETE WHERE t.v = 0")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN NOT MATCHED AND s.id > 0 THEN INSERT (t.id) VALUES (s.id)")]
    public void MergeClausesRoundTrip(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
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
