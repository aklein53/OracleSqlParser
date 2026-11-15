using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Conditional compilation around the clauses of a subprogram heading (between the RETURN type and IS/AS or ';').</summary>
public class ConditionalHeadingShapeTests
{
    private const string NestedResultCache =
        "create or replace package body p as\n" +
        "  function get_level_number\n" +
        "    return number\n" +
        "    $if $$rac_lt_11_2 $then\n" +
        "      $if not dbms_db_version.ver_le_10_2 $then\n" +
        "        result_cache relies_on (logger_prefs, logger_prefs_by_client_id)\n" +
        "      $end\n" +
        "    $end\n" +
        "  is\n" +
        "    l_level number;\n" +
        "  begin\n" +
        "    return 1;\n" +
        "  end;\n" +
        "end;";

    private const string SpecResultCache =
        "create or replace package p as\n" +
        "  function get_pref(p_pref_name in varchar2) return varchar2\n" +
        "    $if not dbms_db_version.ver_le_10_2  $then\n" +
        "      result_cache\n" +
        "    $end;\n" +
        "end;";

    [Fact]
    public void NestedConditionalResultCacheInABody()
    {
        var r = SqlParser.Parse(NestedResultCache);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var all = Shape.Parse(NestedResultCache).Descendants().OfType<ConditionalProgramOptions>().ToList();
        Assert.Equal(2, all.Count);
        var outer = all[0]; // pre-order: the outer group first
        var text = AstPrinter.Print(outer);
        Assert.StartsWith("(ConditionalProgramOptions condition=(InquiryDirective", text);
        Assert.Contains("(ConditionalProgramOptions condition=(Not DBMS_DB_VERSION.VER_LE_10_2) thenOptions=(ProgramOptions resultCache relyOn=[[LOGGER_PREFS] [LOGGER_PREFS_BY_CLIENT_ID]]))", text);
    }

    [Fact]
    public void ConditionalResultCacheInASpec()
    {
        Assert.Equal("(ConditionalProgramOptions condition=(Not DBMS_DB_VERSION.VER_LE_10_2) thenOptions=(ProgramOptions resultCache))",
            Shape.Of<ConditionalProgramOptions>(SpecResultCache));
    }

    [Theory]
    [InlineData(NestedResultCache)]
    [InlineData(SpecResultCache)]
    public void ConditionalHeadingRoundTrips(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var text = new SqlScriptGenerator().Generate(r.Script);
        Assert.Contains("$IF", text);
        var again = SqlParser.Parse(text);
        Assert.True(again.Success, string.Join("; ", again.Errors));
        Assert.Equal(AstPrinter.Print(r.Script), AstPrinter.Print(again.Script));
    }

    [Fact]
    public void ElsifAndElseBranchesHoldTheirClauses()
    {
        const string sql = "create or replace package p as function f1 return number $if 1 = 1 $then deterministic " +
                           "$elsif 1 = 2 $then pipelined $else parallel_enable $end; end;";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var c = Assert.Single(Shape.Parse(sql).Descendants().OfType<ConditionalProgramOptions>());
        Assert.Single(c.ElsifBranches);
        Assert.NotNull(c.ThenOptions);
        Assert.NotNull(c.ElseOptions!.ParallelEnable);
        Assert.Contains("$ELSIF 1 = 2 $THEN PIPELINED $ELSE PARALLEL_ENABLE $END", new SqlScriptGenerator().Generate(r.Script));
    }

    [Fact]
    public void EmptyBranchesAreAllowedAsOracleAllowsThem()
    {
        var r = SqlParser.Parse("create or replace package p as function f1 return number $if 1 = 1 $then $end; end;");
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var c = Assert.Single(Shape.Parse("create or replace package p as function f1 return number $if 1 = 1 $then $end; end;")
            .Descendants().OfType<ConditionalProgramOptions>());
        Assert.Null(c.ThenOptions);
    }

    [Theory]
    // a $IF group with no $END: an error, not a hang
    [InlineData("create or replace package p as function f1 return number $if 1 = 1 $then deterministic; end;")]
    [InlineData("create or replace package p as function f1 return number $if 1 = 1 $then deterministic")]
    [InlineData("create or replace package body p as function f1 return number $if 1 = 1 $then deterministic is begin return 1; end; end;")]
    // Oracle separates clauses with spaces, not commas
    [InlineData("create or replace package p as function f1 return number $if 1 = 1 $then deterministic, pipelined $end; end;")]
    // a $ERROR directive is not a heading clause (Oracle raises it when selected; the parser does not model it here)
    [InlineData("create or replace package p as function f1 return number $error 'x' $end; end;")]
    // an unknown clause inside a branch
    [InlineData("create or replace package p as function f1 return number $if 1 = 1 $then bogus $end; end;")]
    public void MalformedConditionalHeadingsAreErrors(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success, sql);
    }

    [Fact]
    public void ALoneEndDirectiveInAHeadingIsAnError()
    {
        Assert.False(SqlParser.Parse("create or replace package p as function f1 return number $end; end;").Success);
    }
}
