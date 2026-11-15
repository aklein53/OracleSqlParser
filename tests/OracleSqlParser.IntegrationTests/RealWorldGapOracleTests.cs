using OracleSqlParser.Tests;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// The negative side of the real-world gap forms: each statement is one the parser must reject, and Oracle must reject it
/// at syntax analysis (not with a name-resolution error, which would mean Oracle accepted the syntax). Positive forms are in
/// the corpus (RealWorldGapCorpusTests). The forms that Oracle accepts but the parser still rejects (such as a table function
/// followed by an operator) are not listed: only rejections are asserted here.
/// </summary>
[Collection(OracleCollection.Name)]
public class RealWorldGapOracleTests(OracleFixture db)
{
    private static readonly string[] Rejected =
    [
        "GRANT my_role TO PACKAGE hr.my_pkg WITH ADMIN OPTION",
        "GRANT my_role TO PACKAGE hr.my_pkg CASCADE CONSTRAINTS",
        "GRANT my_role TO PACKAGE hr.my_pkg, scott",
        "GRANT my_role TO PACKAGE hr.my_pkg.f",
        "GRANT my_role TO PACKAGE hr.",
        "GRANT my_role TO PACKAGE 'x'",
        "GRANT my_role TO PACKAGE hr my_pkg",
        "GRANT my_role TO PROCEDURE hr.p hr.q",
        "BEGIN PRAGMA INLINE get_logical_suite; END;",
        "BEGIN PRAGMA INLINE(get_logical_suite); NULL; END;",
        "BEGIN PRAGMA INLINE(hr.get_logical_suite, 'YES'); NULL; END;",
        "SELECT * FROM dbms_xplan.display() partition (p)",
        "SELECT * FROM app.f(1) sample (10)",
        "SELECT * FROM dbms_xplan.display()@nolink",
        "SELECT * FROM dual d(a)",
    ];

    public static IEnumerable<object[]> OracleRejects() => Rejected.Select((s, i) => new object[] { i, s });

    [SkippableTheory]
    [MemberData(nameof(OracleRejects))]
    public async Task OracleRejectsThisSyntax(int index, string sql)
    {
        db.SkipIfUnavailable();
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"RGN{index}", OracleHarness.Prepare(sql), false);
        Assert.True(outcome == Outcome.Rejected, $"expected Oracle to reject the syntax of:\n{sql}\nbut the outcome was {outcome}: {detail}");
    }
}
