using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Corpus of forms found in real open-source PL/SQL (see CHANGELOG). Like <see cref="SmokeCorpusTests"/> each must parse;
/// the integration suite also sends these to Oracle (CorpusAcceptanceTests reads this class too). They live apart from the
/// smoke corpus so the seeds of the deterministic fuzz run (FuzzSmokeTests) stay the same.
/// </summary>
public class RealWorldGapCorpusTests
{
    [Theory]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg")]
    [InlineData("GRANT my_role TO FUNCTION hr.f")]
    [InlineData("GRANT my_role TO PROCEDURE hr.p")]
    [InlineData("REVOKE my_role FROM PACKAGE hr.my_pkg")]
    [InlineData("REVOKE my_role FROM FUNCTION hr.f")]
    [InlineData("REVOKE my_role FROM PROCEDURE hr.p")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'YES'); x := get_logical_suite(1); END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'NO'); x := get_logical_suite(1); END;")]
    public void Parses(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
    }
}
