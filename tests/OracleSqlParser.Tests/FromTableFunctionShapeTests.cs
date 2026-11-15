using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>A table function named in a query's FROM clause without TABLE(...) (Oracle 18c+), and the forms that must not change.</summary>
public class FromTableFunctionShapeTests
{
    [Theory]
    [InlineData("SELECT * FROM dbms_xplan.display()", "(TableFunction expression=(FunctionCall name=[DBMS_XPLAN DISPLAY]) withoutTableKeyword)")]
    [InlineData("SELECT * FROM hr.pkg.f(1, 2) t", "(TableFunction expression=(FunctionCall name=[HR PKG F] arguments=[(Argument value=1) (Argument value=2)]) withoutTableKeyword alias=T)")]
    [InlineData("SELECT * FROM f(1) x, g(2) y", "(TableFunction expression=(FunctionCall name=[F] arguments=[(Argument value=1)]) withoutTableKeyword alias=X)")]
    public void TableFunctionWithoutTable(string sql, string expected) =>
        Assert.Equal(expected, Shape.Of<TableFunction>(sql));

    [Theory]
    [InlineData("SELECT * FROM dbms_xplan.display()", "SELECT * FROM dbms_xplan.display()")]
    [InlineData("SELECT * FROM hr.pkg.f(1, 2) t", "SELECT * FROM hr.pkg.f(1, 2) t")]
    [InlineData("SELECT * FROM (pkg.match_paths(a, b)) m", "SELECT * FROM (pkg.match_paths(a, b)) m")]
    [InlineData("SELECT * FROM t a JOIN pkg.f(1) b ON a.id = b.id", "SELECT * FROM t a JOIN pkg.f(1) b ON a.id = b.id")]
    [InlineData("SELECT * FROM (&&ut3_owner..ut_suite_cache_macros.match_paths(schema_items, path_items)) m",
        "SELECT * FROM (&&ut3_owner..ut_suite_cache_macros.match_paths(schema_items, path_items)) m")]
    public void TableFunctionWithoutTableRoundTrips(string sql, string generated)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var text = new SqlScriptGenerator().Generate(r.Script.Statements[0]);
        Assert.Equal(generated, text);
        var again = SqlParser.Parse(text);
        Assert.True(again.Success, string.Join("; ", again.Errors));
        Assert.Equal(AstPrinter.Print(r.Script.Statements[0]), AstPrinter.Print(again.Script.Statements[0]));
    }

    [Fact]
    public void ParenthesisedTableFunctionKeepsItsParentheses()
    {
        var r = SqlParser.Parse("SELECT * FROM (pkg.match_paths(a, b)) m");
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var pt = Assert.IsType<ParenthesizedTable>(((QuerySpecification)((SelectStatement)r.Script.Statements[0]).Body).From[0]);
        Assert.IsType<TableFunction>(pt.Inner);
        Assert.Equal("m", pt.Alias!.Value.ToLowerInvariant());
    }

    // The TABLE(...) form is unchanged: no flag, and the generator still writes TABLE.
    [Fact]
    public void TableKeywordFormIsUnchanged()
    {
        Assert.Equal("(TableFunction expression=(FunctionCall name=[F] arguments=[(Argument value=1)]) alias=T)", Shape.Of<TableFunction>("SELECT * FROM TABLE(f(1)) t"));
        var r = SqlParser.Parse("SELECT * FROM TABLE(f(1)) t");
        Assert.Contains("TABLE(f(1))", new SqlScriptGenerator().Generate(r.Script.Statements[0]));
    }

    // Forms that look similar but must keep their trees: partition, database link, schema-qualified table, subquery, AS OF.
    [Theory]
    [InlineData("SELECT * FROM t PARTITION (p1)", "(NamedTable name=[T] partition=P1)")]
    [InlineData("SELECT * FROM t@dblink", "(NamedTable name=[T] dbLink=[DBLINK])")]
    [InlineData("SELECT * FROM scott.emp", "(NamedTable name=[SCOTT EMP])")]
    public void NamedTablesAreUnchanged(string sql, string expected) =>
        Assert.Equal(expected, Shape.Of<NamedTable>(sql));

    [Theory]
    [InlineData("SELECT * FROM dbms_xplan.display() partition (p)")]
    [InlineData("SELECT * FROM app.f(1) sample (10)")]
    [InlineData("SELECT * FROM dbms_xplan.display()@nolink")]
    [InlineData("SELECT * FROM f(1) + 2")]
    [InlineData("SELECT * FROM f(1) || x")]
    [InlineData("SELECT * FROM dual d(a)")]
    [InlineData("SELECT * FROM dual d (a, b)")]
    public void TheCallMayNotBeFollowedByTableClauses(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success, sql);
    }

    // Outside a query's FROM clause a name followed by '(' is still a column list or an error, as before.
    [Theory]
    [InlineData("INSERT INTO t (a, b) VALUES (1, 2)", "(InsertStatement target=(NamedTable name=[T]) columns=[A B] valueRows=[[1 2]])")]
    public void DmlTargetsAreUnchanged(string sql, string expected) =>
        Assert.Equal(expected, Shape.Of<InsertStatement>(sql));

    [Theory]
    [InlineData("DELETE FROM t (1)")]
    [InlineData("LOCK TABLE pkg.f(1) IN SHARE MODE")]
    public void DmlTargetsDoNotTakeACall(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success, sql);
    }
}
