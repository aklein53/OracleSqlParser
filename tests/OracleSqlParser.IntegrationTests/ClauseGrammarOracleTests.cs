using OracleSqlParser.Tests;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// The grammar decisions of the clause nodes, checked against Oracle in both directions. Each form in <see cref="Rejected"/>
/// is one the parser rejects, and Oracle must reject it at syntax analysis (not with a name-resolution error, which would
/// mean Oracle accepted the syntax). Each form in <see cref="Accepted"/> is one the parser accepts, and Oracle must not
/// reject it at parse time (a name or semantic error is fine: the syntax was accepted).
/// </summary>
[Collection(OracleCollection.Name)]
public class ClauseGrammarOracleTests(OracleFixture db)
{
    /// <summary>Oracle takes these only as a whole element, so the parser rejects them: nested lists, empty lists, nested GROUPING SETS.</summary>
    private static readonly string[] Rejected =
    [
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP()",
        "SELECT COUNT(*) FROM t GROUP BY CUBE()",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS()",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(())",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(a, ())",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(GROUPING SETS(a))",
        "SELECT COUNT(*) FROM t GROUP BY CUBE(GROUPING SETS(a))",
        "SELECT COUNT(*) FROM t GROUP BY CUBE(a, GROUPING SETS(b))",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS(GROUPING SETS(a))",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS(((a, b)))",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS((a), ((a, b)))",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(((a, b)))",
        "SELECT COUNT(*) FROM t GROUP BY ((a, b), a)",
        "SELECT COUNT(*) FROM t GROUP BY (a, (b, a))",
        "SELECT COUNT(*) FROM t GROUP BY (a, ())",
        "SELECT COUNT(*) FROM t GROUP BY (a, ROLLUP(b))",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(a) + 1",
        "WITH SELECT 1 FROM dual",
        "WITH (SELECT 1 FROM dual) SELECT 1 FROM dual",
        "SELECT a FROM t OFFSET ROWS",
    ];

    /// <summary>
    /// Oracle accepts these at parse time. A ROLLUP or CUBE where a grouping item is expected is a function call to Oracle,
    /// so the nonsense ROLLUP(ROLLUP(a)) fails only later, as an unknown function.
    /// </summary>
    private static readonly string[] Accepted =
    [
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(ROLLUP(a))",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(CUBE(a))",
        "SELECT COUNT(*) FROM t GROUP BY (ROLLUP(a))",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS((ROLLUP(a)))",
        "SELECT COUNT(*) FROM t GROUP BY ((a))",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP((a))",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS(((a)))",
        "SELECT COUNT(*) FROM t GROUP BY (a), (b)",
        "SELECT COUNT(*) FROM t GROUP BY ()",
        "SELECT COUNT(*) FROM t GROUP BY (), a",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS(())",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(a, (b, a))",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(a + 1, b)",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS(ROLLUP(a), CUBE((a, b)))",
        "SELECT COUNT(*) FROM t GROUP BY GROUPING SETS((a), (a), ())",
        "SELECT COUNT(*) FROM t GROUP BY rollup",
        "SELECT COUNT(*) FROM t GROUP BY ROLLUP(a), CUBE(b), GROUPING SETS((a), ())",
        "WITH x AS (SELECT 1 n FROM dual) SELECT * FROM x",
        "SELECT COUNT(*) FROM t OFFSET 1 ROW",
        "SELECT COUNT(*) FROM t OFFSET 1 ROWS",
    ];

    public static IEnumerable<object[]> RejectedCases() => Rejected.Select((s, i) => new object[] { i, s });

    public static IEnumerable<object[]> AcceptedCases() => Accepted.Select((s, i) => new object[] { i, s });

    [SkippableTheory]
    [MemberData(nameof(RejectedCases))]
    public async Task OracleRejectsWhatTheParserRejects(int index, string sql)
    {
        db.SkipIfUnavailable();
        Assert.False(SqlParser.Parse(sql).Success, $"the parser accepted:\n{sql}");
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"CGR{index}", OracleHarness.Prepare(sql), true);
        Assert.True(outcome == Outcome.Rejected, $"expected Oracle to reject the syntax of:\n{sql}\nbut the outcome was {outcome}: {detail}");
    }

    [SkippableTheory]
    [MemberData(nameof(AcceptedCases))]
    public async Task OracleAcceptsWhatTheParserAccepts(int index, string sql)
    {
        db.SkipIfUnavailable();
        Assert.True(SqlParser.Parse(sql).Success, $"the parser rejected:\n{sql}");
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"CGA{index}", OracleHarness.Prepare(sql), true);
        Assert.True(outcome != Outcome.Rejected, $"Oracle rejected the syntax of:\n{sql}\n{detail}");
    }
}
