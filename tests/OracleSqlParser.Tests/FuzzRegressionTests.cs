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
}
