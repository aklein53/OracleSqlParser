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
    [InlineData("SELECT * FROM dbms_xplan.display()")]
    [InlineData("SELECT * FROM hr.pkg.f(1, 2) t")]
    [InlineData("SELECT * FROM (pkg.match_paths(a, b)) m")]
    [InlineData("create or replace package body p as\n  function get_level_number\n    return number\n    $if $$rac_lt_11_2 $then\n      $if not dbms_db_version.ver_le_10_2 $then\n        result_cache relies_on (logger_prefs, logger_prefs_by_client_id)\n      $end\n    $end\n  is\n    l_level number;\n  begin\n    return 1;\n  end;\nend;")]
    [InlineData("create or replace package p as\n  function get_pref(p_pref_name in varchar2) return varchar2\n    $if not dbms_db_version.ver_le_10_2  $then\n      result_cache\n    $end;\nend;")]
    [InlineData("create or replace package body p as\n  function get_pref(p_pref_name in varchar2) return varchar2\n    $if not dbms_db_version.ver_le_10_2 $then\n      result_cache\n      $if $$no_op is null or not $$no_op $then\n        relies_on (logger_prefs, logger_prefs_by_client_id)\n      $end\n    $end\n  is\n    l_pref_value varchar2(100);\n  begin\n    return l_pref_value;\n  end;\nend;")]
    // ANSI VARYING types: the length follows VARYING (the generator once printed CHARACTER(10) VARYING, which Oracle rejects).
    [InlineData("CREATE TABLE varying_types (a CHARACTER VARYING(10), b NATIONAL CHARACTER VARYING(5), c NCHAR VARYING(3), d CHAR VARYING(2))")]
    public void Parses(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
    }
}
