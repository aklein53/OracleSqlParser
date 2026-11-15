using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// User-defined and anchored type names keep their quoting and dot boundaries in <see cref="DataType.NameParts"/>; the
/// generator prints those parts, so a quoted type name survives a parse, generate, reparse round trip.
/// </summary>
public class QuotedTypeNameTests
{
    private static DataType FirstDataType(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return r.Script.Descendants().OfType<DataType>().First();
    }

    [Theory]
    [InlineData("DECLARE x \"ab\"; BEGIN NULL; END;", "(DataType name=ab nameParts=[\"ab\"])")]
    [InlineData("DECLARE x hr.\"my type\"; BEGIN NULL; END;", "(DataType name=HR.my type nameParts=[HR \"my type\"])")]
    [InlineData("DECLARE x \"a.b\".t; BEGIN NULL; END;", "(DataType name=a.b.T nameParts=[\"a.b\" T])")]
    [InlineData("DECLARE x t.\"c\"%TYPE; BEGIN NULL; END;", "(DataType name=T.c attribute=TYPE nameParts=[T \"c\"])")]
    [InlineData("DECLARE x emp_t; BEGIN NULL; END;", "(DataType name=EMP_T nameParts=[EMP_T])")]
    [InlineData("DECLARE x REF \"t\"; BEGIN NULL; END;", "(DataType name=t ref nameParts=[\"t\"])")]
    public void UserDefinedNamesCarryTheirParts(string sql, string expected) =>
        Assert.Equal(expected, AstPrinter.Print(FirstDataType(sql)));

    [Fact]
    public void QuotedPartKeepsItsSpelling()
    {
        var dt = FirstDataType("DECLARE x t.\"c\"%TYPE; BEGIN NULL; END;");
        Assert.Equal("T.c", dt.Name);
        Assert.Equal(new[] { "T", "c" }, dt.NameParts.Select(p => p.Value));
        Assert.Equal(new[] { false, true }, dt.NameParts.Select(p => p.Quoted));
        Assert.Equal("c", dt.NameParts[1].Text);
    }

    [Theory]
    [InlineData("DECLARE x VARCHAR2(10); BEGIN NULL; END;")]
    [InlineData("DECLARE x NUMBER; BEGIN NULL; END;")]
    [InlineData("DECLARE x TIMESTAMP WITH TIME ZONE; BEGIN NULL; END;")]
    [InlineData("DECLARE x INTERVAL DAY TO SECOND; BEGIN NULL; END;")]
    [InlineData("DECLARE x DOUBLE PRECISION; BEGIN NULL; END;")]
    [InlineData("DECLARE x PLS_INTEGER; BEGIN NULL; END;")]
    [InlineData("DECLARE x BOOLEAN; BEGIN NULL; END;")]
    public void BuiltInTypesHaveNoNameParts(string sql)
    {
        var dt = FirstDataType(sql);
        Assert.NotEmpty(dt.Name);
        Assert.Empty(dt.NameParts);
    }

    [Theory]
    // Each form, as typed, must reach the generated text with its quoting intact.
    [InlineData("DECLARE x \"ab\"; BEGIN NULL; END;", "x \"ab\"")]
    [InlineData("DECLARE x hr.\"my type\"; BEGIN NULL; END;", "hr.\"my type\"")]
    [InlineData("DECLARE x \"a.b\".t; BEGIN NULL; END;", "\"a.b\".t")]
    [InlineData("DECLARE x t.\"c\"%TYPE; BEGIN NULL; END;", "t.\"c\"%TYPE")]
    [InlineData("CREATE TYPE o AS OBJECT (x \"ab\")", "x \"ab\"")]
    [InlineData("CREATE TABLE t (c \"ab\")", "c \"ab\"")]
    [InlineData("CREATE TABLE t (c hr.\"my type\" NOT NULL)", "hr.\"my type\"")]
    [InlineData("SELECT CAST(x AS \"t\") FROM dual", "AS \"t\"")]
    [InlineData("SELECT TREAT(x AS \"t\") FROM dual", "AS \"t\"")]
    [InlineData("SELECT TREAT(x AS REF \"t\") FROM dual", "REF \"t\"")]
    [InlineData("CREATE PROCEDURE p (a \"t\") AS BEGIN NULL; END;", "a \"t\"")]
    [InlineData("CREATE FUNCTION f RETURN \"t\" IS BEGIN RETURN NULL; END;", "RETURN \"t\"")]
    [InlineData("DECLARE TYPE tt IS TABLE OF \"t\" INDEX BY PLS_INTEGER; BEGIN NULL; END;", "OF \"t\"")]
    [InlineData("DECLARE TYPE va IS VARRAY(3) OF \"t\"; BEGIN NULL; END;", "OF \"t\"")]
    [InlineData("CREATE TYPE tt AS TABLE OF REF \"c\"", "REF \"c\"")]
    public void QuotedTypeNameRoundTrips(string sql, string fragment)
    {
        var first = SqlParser.Parse(sql);
        Assert.True(first.Success, string.Join("; ", first.Errors));
        var printed = new SqlScriptGenerator().Generate(first.Script);
        Assert.Contains(fragment, printed);

        var second = SqlParser.Parse(printed);
        Assert.True(second.Success, $"generated SQL does not parse: {string.Join("; ", second.Errors)}\n--- generated ---\n{printed}");
        Assert.True(SqlNodeComparer.StructurallyEqual(first.Script, second.Script), printed);
        Assert.Equal(AstPrinter.Print(first.Script), AstPrinter.Print(second.Script));
    }

    [Fact]
    public void UppercaseOptionStillQuotesOnlyWhereQuoted()
    {
        var first = SqlParser.Parse("DECLARE x hr.\"my type\"; BEGIN NULL; END;");
        Assert.True(first.Success, string.Join("; ", first.Errors));
        var printed = new SqlScriptGenerator(new SqlScriptGeneratorOptions { IdentifierCasing = IdentifierCasing.Uppercase }).Generate(first.Script);
        Assert.Contains("HR.\"my type\"", printed);
    }

    [Fact]
    public void NameDescribesTheSamePartsAsNameParts()
    {
        var dt = FirstDataType("DECLARE x \"a.b\".t; BEGIN NULL; END;");
        Assert.Equal("a.b.T", dt.Name);
        Assert.Equal(2, dt.NameParts.Count);
        Assert.Equal("a.b", dt.NameParts[0].Value);
        Assert.Equal("T", dt.NameParts[1].Value);
    }

    [Fact]
    public void ExternalParameterWithQuotedTypeParses()
    {
        var r = SqlParser.Parse("CREATE PROCEDURE p (x NUMBER) AS LANGUAGE C NAME \"p\" LIBRARY lib PARAMETERS (x \"ab\");");
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("\"ab\"", new SqlScriptGenerator().Generate(r.Script));
    }
}
