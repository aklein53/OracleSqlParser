using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Code-based access control: GRANT/REVOKE of a role to or from a PACKAGE, FUNCTION or PROCEDURE (Oracle 12c+).</summary>
public class GrantProgramUnitShapeTests
{
    [Theory]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg", "(Grantee name=MY_PKG programKind=Package programSchema=HR)")]
    [InlineData("GRANT my_role TO FUNCTION hr.f", "(Grantee name=F programKind=Function programSchema=HR)")]
    [InlineData("GRANT my_role TO PROCEDURE hr.p", "(Grantee name=P programKind=Procedure programSchema=HR)")]
    [InlineData("REVOKE my_role FROM PACKAGE hr.my_pkg", "(Grantee name=MY_PKG programKind=Package programSchema=HR)")]
    [InlineData("GRANT my_role TO PACKAGE my_pkg", "(Grantee name=MY_PKG programKind=Package)")]
    public void ProgramUnitGrantee(string sql, string grantee) =>
        Assert.Equal(grantee, Shape.Of<Grantee>(sql));

    [Theory]
    [InlineData("grant my_role to package hr.my_pkg;", "GRANT MY_ROLE TO PACKAGE hr.my_pkg")]
    [InlineData("grant my_role to function hr.f;", "GRANT MY_ROLE TO FUNCTION hr.f")]
    [InlineData("grant my_role to procedure hr.p;", "GRANT MY_ROLE TO PROCEDURE hr.p")]
    [InlineData("revoke my_role from package hr.my_pkg;", "REVOKE MY_ROLE FROM PACKAGE hr.my_pkg")]
    [InlineData("grant &&owner._role to package &&owner..pkg", "GRANT &&owner._role TO PACKAGE &&owner..pkg")]
    [InlineData("grant my_role to package \"HR\".\"my_pkg\"", "GRANT MY_ROLE TO PACKAGE \"HR\".\"my_pkg\"")]
    public void ProgramUnitGrantRoundTrips(string sql, string generated)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var text = new SqlScriptGenerator().Generate(r.Script.Statements[0]);
        Assert.Equal(generated, text);
        var again = SqlParser.Parse(text);
        Assert.True(again.Success, string.Join("; ", again.Errors));
        Assert.Equal(AstPrinter.Print(r.Script.Statements[0]), AstPrinter.Print(again.Script.Statements[0]));
    }

    // A bare PACKAGE, FUNCTION or PROCEDURE is not reserved in Oracle (it names a user), so it stays a plain grantee.
    [Fact]
    public void BareKeywordIsStillAUserGrantee()
    {
        var g = Assert.Single(Shape.Parse("GRANT my_role TO package").Descendants().OfType<Grantee>());
        Assert.Null(g.ProgramKind);
        Assert.Equal("PACKAGE", g.Name!.Value);
    }

    [Theory]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg WITH ADMIN OPTION")]
    [InlineData("GRANT my_role TO PROCEDURE hr.p WITH GRANT OPTION")]
    [InlineData("REVOKE my_role FROM PACKAGE hr.my_pkg CASCADE CONSTRAINTS")]
    [InlineData("REVOKE my_role FROM PACKAGE hr.my_pkg FORCE")]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg CONTAINER = ALL")]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg, PUBLIC")]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg, scott")]
    [InlineData("GRANT SELECT ON t TO PACKAGE hr.my_pkg")]
    [InlineData("GRANT ALL PRIVILEGES TO PACKAGE hr.my_pkg")]
    [InlineData("GRANT my_role(a) TO PACKAGE hr.my_pkg")]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg.f")]
    [InlineData("GRANT my_role TO PACKAGE hr.")]
    [InlineData("GRANT my_role TO PACKAGE 'x'")]
    [InlineData("GRANT my_role TO PACKAGE hr my_pkg")]
    [InlineData("GRANT my_role TO PROCEDURE hr.p hr.q")]
    [InlineData("GRANT my_role TO PACKAGE hr.my_pkg, PACKAGE hr.other")]
    public void OracleRejectsTheseGrantForms(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success, sql);
    }

    [Fact]
    public void AnUnterminatedProgramGrantIsAnErrorNotAHang()
    {
        Assert.False(SqlParser.Parse("GRANT my_role TO PACKAGE hr.").Success);
        Assert.False(SqlParser.Parse("GRANT my_role TO PACKAGE hr.my_pkg,").Success);
        Assert.False(SqlParser.Parse("GRANT my_role TO PACKAGE hr.my_pkg,\nPACKAGE").Success);
    }
}
