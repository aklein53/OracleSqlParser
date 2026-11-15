using System.Diagnostics;
using OracleSqlParser.Fuzzing;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Regression tests for defects found by tools/Fuzz (one per bug). Each bug has a test that reproduces the failing input
/// and checks the fixed behaviour.
/// </summary>
public class FuzzRegressionTests
{
    // ---------- JSON: deep trees are linear in size, not quadratic ----------

    private static string AndChain(int n) => "SELECT 1 FROM t WHERE " + string.Join(" AND ", Enumerable.Range(0, n).Select(i => "a" + i + " = 1"));

    [Fact]
    public void AstJson_IndentedSerialize_OfA8000TermChain_DoesNotExhaustMemory()
    {
        // Indenting a 8,000-level tree wrote quadratically many spaces and ran out of memory.
        var script = SqlParser.Parse(AndChain(8000)).Script;
        string json = AstJson.Serialize(script);
        Assert.Contains("\"type\":\"BinaryExpression\"", json);
        Assert.True(json.Length < 5_000_000, $"json is {json.Length} chars");
    }

    [Fact]
    public void AstJson_ShallowTrees_StillIndent()
    {
        var script = SqlParser.Parse(AndChain(50)).Script;
        Assert.Contains("\n  \"type\": \"SqlScript\"", AstJson.Serialize(script));
    }

    // ---------- SqlAnalysis: qualified names on long join chains ----------

    private static string JoinChain(int n) => "SELECT t0.c FROM t0 " + string.Concat(Enumerable.Range(1, n).Select(i => $"JOIN t{i} ON t{i}.id = t{i - 1}.id "));

    [Fact]
    public void Analysis_QualifiedColumnsOnLongJoinChain_ResolveAndStayFast()
    {
        // Column resolution scanned every FROM item for every qualified column: quadratic in the number of joins (12 s at 16,000).
        var script = SqlParser.Parse(JoinChain(10_000)).Script;
        var sw = Stopwatch.StartNew();
        var columns = SqlAnalysis.GetColumns(script);
        sw.Stop();
        Assert.Equal(20_001, columns.Count); // t0.c, then two columns in each of the 10,000 ON clauses
        Assert.All(columns, c => Assert.Equal(ColumnResolution.Resolved, c.Resolution));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"GetColumns took {sw.Elapsed}");
    }

    [Fact]
    public void Analysis_QualifiedNames_MatchAliasOrTrailingNamePartsAsBefore()
    {
        // s.t.c and t.c reach the unaliased table through its name parts; x.c through its alias.
        var cols = SqlAnalysis.GetColumns(SqlParser.Parse("SELECT s.t.c, x.c, t.c FROM s.t JOIN u x ON x.c = t.c").Script);
        Assert.Equal(5, cols.Count);
        Assert.All(cols, c => Assert.Equal(ColumnResolution.Resolved, c.Resolution));
        var dup = SqlAnalysis.GetColumns(SqlParser.Parse("SELECT a.c FROM t a JOIN t a ON a.c = 1").Script);
        Assert.Equal(ColumnResolution.Ambiguous, dup[0].Resolution);
    }

    private static void AssertContract(string sql, ParseOptions? options = null)
    {
        var f = ContractChecker.Check(sql, options);
        Assert.True(f is null, f is null ? "" : $"{f.Category} {f.Signature}\n{f.Detail}");
    }

    [Fact]
    public void Printer_DeepChain_IsGeneratedAndRoundTrips()
    {
        // The printer, generator and clone recurse once per level, so an 8,000-term chain overflowed the default stack.
        AssertContract(AndChain(8000));
    }

    // ---------- round-trip: chained comparison-level operators ----------

    [Theory]
    [InlineData("SELECT 1 FROM dual WHERE a IN (1) IN (2)")]
    [InlineData("SELECT 1 FROM dual WHERE a NOT IN (1) IN (2)")]
    [InlineData("SELECT 1 FROM dual WHERE a LIKE 'x' LIKE 'y'")]
    [InlineData("SELECT 1 FROM dual WHERE a = 1 IN (1)")]
    [InlineData("SELECT 1 FROM dual WHERE a BETWEEN 1 AND 2 IN (1)")]
    [InlineData("SELECT 1 FROM dual WHERE a IS NULL IN (1)")]
    [InlineData("SELECT 1 FROM dual WHERE (a IN (1)) IN (2)")]
    public void ChainedComparisonOperands_RoundTripWithoutChangingTheTree(string sql)
    {
        AssertContract(sql);
    }

    [Fact]
    public void ChainedIn_IsParsedLeftAssociative_AndPrintedWithoutParentheses()
    {
        Assert.Equal("(InExpression value=(InExpression value=A list=[1]) list=[2])", Shape.Expr("a IN (1) IN (2)"));
        Assert.Contains("WHERE A IN (1) IN (2)", SqlWriter.ToSql(Shape.Parse("SELECT 1 FROM dual WHERE a IN (1) IN (2)").Statements[0]).ToUpperInvariant());
    }

    // ---------- round-trip: audit option words ----------

    [Theory]
    [InlineData("AUDIT t\"\"''R")]
    [InlineData("AUDIT 'x' ON t")]
    [InlineData("AUDIT \"CREATE SESSION\"")]
    [InlineData("AUDIT CREATE SESSION, 1")]
    public void AuditOptions_AreBareWordsOnly(string sql)
    {
        // A quoted name or a literal used to be joined into an option name that did not survive printing (T   R became T R).
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success);
        AssertContract(sql);
    }

    [Theory]
    [InlineData("CREATE AUDIT POLICY p ACTIONS 'x'")]
    [InlineData("CREATE AUDIT POLICY p ACTIONS \"x\" ON hr.emp")]
    public void AuditPolicyActions_AreBareWordsOnly(string sql)
    {
        // The literal 'x' was printed as the identifier x, which reparses as X.
        Assert.False(SqlParser.Parse(sql).Success);
        AssertContract(sql);
    }

    [Fact]
    public void AuditOptions_KeywordsStillParse()
    {
        Assert.Contains("CREATE SESSION", Shape.Of("AUDIT CREATE SESSION"));
        AssertContract("AUDIT CREATE SESSION, ALTER SYSTEM BY SCOTT WHENEVER NOT SUCCESSFUL");
    }

    // ---------- generated SQL must print the same tree for every production (fuzz finds of earlier rounds) ----------

    [Theory]
    [InlineData("SELECT 1 FROM dual WHERE a = 1 = 1")]
    [InlineData("SELECT -(-1) FROM dual")]
    [InlineData("SELECT 1 FROM dual WHERE NOT a IN (1)")]
    public void Contract_HoldsForOrdinaryStatements(string sql)
    {
        AssertContract(sql);
    }

    [Fact]
    public void Contract_HoldsAtTheDepthLimitForEveryNestingConstruct()
    {
        const int limit = 200;
        string Parens(int d) => "SELECT " + new string('(', d) + "1" + new string(')', d) + " FROM dual";
        AssertContract(Parens(limit - 1));
        AssertContract(Parens(limit));
        AssertContract(Parens(limit + 1));
        AssertContract("SELECT " + string.Concat(Enumerable.Repeat("CASE WHEN 1=1 THEN ", 199)) + "1" + string.Concat(Enumerable.Repeat(" END", 199)) + " FROM dual");
    }

    // ---------- round-trip: column constraints and other properties keep their order ----------

    [Theory]
    [InlineData("CREATE TABLE t(a R USING UNIQUE)")]
    [InlineData("CREATE TABLE t(a NUMBER UNIQUE FOO BAR)")]
    [InlineData("CREATE TABLE t(a NUMBER FOO UNIQUE)")]
    public void ColumnProperties_RoundTripInTheirSourceOrder(string sql)
    {
        AssertContract(sql);
    }

    [Fact]
    public void ColumnProperties_PrintedInTheirSourceOrder()
    {
        // The word before UNIQUE was printed after it, where the constraint absorbed it as USING INDEX.
        var sql = SqlWriter.ToSql(Shape.Parse("CREATE TABLE t(a R USING UNIQUE)").Statements[0]);
        Assert.Contains("A R USING UNIQUE", sql.ToUpperInvariant());
    }

    // ---------- round-trip: statement labels keep their quoting ----------

    [Theory]
    [InlineData("BEGIN <<\"a b\">> NULL; END;")]
    [InlineData("BEGIN <<\"abc\">> NULL; END;")]
    [InlineData("BEGIN <<abc>> NULL; END;")]
    public void StatementLabels_RoundTrip(string sql)
    {
        AssertContract(sql);
    }

    [Fact]
    public void StatementLabel_QuotedName_IsPrintedQuoted()
    {
        // The label is kept as its value; printed bare, "a b" was not even a single name.
        string gen = new SqlScriptGenerator().Generate(SqlParser.Parse("BEGIN <<\"a b\">> NULL; END;").Script);
        Assert.Contains("<<\"a b\">>", gen);
    }
}
