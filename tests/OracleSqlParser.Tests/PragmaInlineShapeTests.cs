using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Statement-level PRAGMA INLINE in an executable section (the declaration form is PragmaDeclaration).</summary>
public class PragmaInlineShapeTests
{
    [Theory]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'YES'); x := get_logical_suite(1); END;", "(InlinePragmaStatement subprogram=GET_LOGICAL_SUITE enabled)")]
    [InlineData("BEGIN pragma inline(get_logical_suite, 'NO'); x := 1; END;", "(InlinePragmaStatement subprogram=GET_LOGICAL_SUITE)")]
    [InlineData("BEGIN pragma inline(get_logical_suite, 'yes'); NULL; END;", "(InlinePragmaStatement subprogram=GET_LOGICAL_SUITE enabled)")]
    [InlineData("BEGIN pragma inline(\"get_logical_suite\", 'YES'); NULL; END;", "(InlinePragmaStatement subprogram=\"get_logical_suite\" enabled)")]
    public void InlinePragmaStatementShape(string sql, string expected) =>
        Assert.Equal(expected, Shape.Of<InlinePragmaStatement>(sql));

    [Theory]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'YES'); x := get_logical_suite(1); END;", "BEGIN PRAGMA INLINE (get_logical_suite, 'YES'); x := get_logical_suite(1); END")]
    [InlineData("BEGIN NULL; PRAGMA INLINE(f, 'NO'); END;", "BEGIN NULL; PRAGMA INLINE (f, 'NO'); END")]
    public void InlinePragmaRoundTrips(string sql, string generated)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var text = new SqlScriptGenerator().Generate(r.Script.Statements[0]);
        Assert.Equal(generated, text.TrimEnd(';', ' ', '/', '\n'));
        var again = SqlParser.Parse(text);
        Assert.True(again.Success, string.Join("; ", again.Errors));
        Assert.Equal(AstPrinter.Print(r.Script.Statements[0]), AstPrinter.Print(again.Script.Statements[0]));
    }

    [Theory]
    [InlineData("BEGIN PRAGMA INLINE get_logical_suite; END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite); NULL; END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, YES); NULL; END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'MAYBE'); NULL; END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 1); NULL; END;")]
    [InlineData("BEGIN PRAGMA INLINE(hr.get_logical_suite, 'YES'); NULL; END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'YES', 'NO'); NULL; END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'YES') NULL; END;")]
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'YES'; NULL; END;")]
    [InlineData("BEGIN PRAGMA AUTONOMOUS_TRANSACTION; NULL; END;")]
    [InlineData("BEGIN NULL; PRAGMA EXCEPTION_INIT(e, -20001); END;")]
    // Oracle counts PRAGMA INLINE as no statement: a block with only the pragma does not compile.
    [InlineData("BEGIN PRAGMA INLINE(get_logical_suite, 'YES'); END;")]
    [InlineData("BEGIN PRAGMA INLINE(f, 'YES'); PRAGMA INLINE(g, 'NO'); END;")]
    [InlineData("BEGIN PRAGMA INLINE(f, 'YES'); EXCEPTION WHEN OTHERS THEN NULL; END;")]
    public void InvalidInlinePragmasAreErrors(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success, sql);
    }

    [Fact]
    public void PragmaDeclarationIsUnchanged()
    {
        Assert.Equal("(PragmaDeclaration name=INLINE arguments=[GET_LOGICAL_SUITE 'YES'])",
            Shape.Of<PragmaDeclaration>("DECLARE PRAGMA INLINE(get_logical_suite, 'YES'); BEGIN NULL; END;"));
    }
}
