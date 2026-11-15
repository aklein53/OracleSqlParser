using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>SQL*Plus HOST with a line-end '-' that joins its arguments onto the next line (ParseOptions.SqlPlusScript only).</summary>
public class SqlPlusHostShapeTests
{
    private static readonly ParseOptions Script = new() { SqlPlusScript = true };

    [Fact]
    public void Host_HyphenAtLineEndJoinsTheNextLineIntoOneCommand()
    {
        var sql = "HOST sqlldr pm/&pm_pass -\ncontrol=&ctl_file data=&dat_file log=&log_file";
        var r = SqlParser.Parse(sql, Script);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        // One command: the hyphen and the line break are not arguments, and the arguments of both lines run on in source order.
        var cmd = Assert.IsType<SqlPlusCommandStatement>(Assert.Single(r.Script.Statements));
        Assert.Equal("HOST", cmd.Command);
        Assert.Equal(Shape.Of<SqlPlusCommandStatement>("HOST sqlldr pm/&pm_pass control=&ctl_file data=&dat_file log=&log_file"), AstPrinter.Print(cmd));
    }

    [Fact]
    public void Host_ContinuedCommandRoundTripsToTheSameTree()
    {
        var r = SqlParser.Parse("HOST echo a -\nb -\nc\nSELECT 1 FROM dual", Script);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal(2, r.Script.Statements.Count);
        var gen = new SqlScriptGenerator().Generate(r.Script.Statements[0]);
        var again = SqlParser.Parse(gen, Script);
        Assert.True(again.Success, string.Join("; ", again.Errors));
        Assert.Equal(AstPrinter.Print(r.Script.Statements[0]), AstPrinter.Print(again.Script.Statements[0]));
    }

    [Fact]
    public void Host_WithoutAContinuationStillEndsAtTheLineBreak()
    {
        var r = SqlParser.Parse("HOST echo a\nb", Script);
        Assert.False(r.Success);
    }

    [Fact]
    public void Host_HyphenWithoutSqlPlusScriptIsNotAContinuation()
    {
        // Plain SQL reads the '-' as a minus operator; the command does not run on to the next line.
        var r = SqlParser.Parse("HOST echo a -\nb");
        Assert.False(r.Success);
    }

    [Fact]
    public void Host_DoubleHyphenIsACommentNotAContinuation()
    {
        var r = SqlParser.Parse("HOST echo a --\nb", Script);
        Assert.False(r.Success);
    }

    [Fact]
    public void OtherSqlPlusCommands_DoNotJoinLines()
    {
        // Only HOST joins its lines in the parser: other commands keep their trees as they were.
        var r = SqlParser.Parse("SET serveroutput -\non", Script);
        Assert.False(r.Success);
    }
}
