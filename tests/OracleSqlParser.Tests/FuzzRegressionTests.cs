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
}
