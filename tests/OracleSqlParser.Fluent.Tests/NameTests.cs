using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

public class NameTests
{
    [Fact]
    public void UnquotedNamePartsAreUpperCasedAndNotQuoted()
    {
        var col = Sql.Col("e.sal");
        Assert.Equal(["E", "SAL"], col.Parts.Select(p => p.Value));
        Assert.All(col.Parts, p => Assert.False(p.Quoted));
    }

    [Fact]
    public void QuotedNamePartsKeepCaseAndAreQuoted()
    {
        var col = Sql.Col("\"Order\".\"Id\"");
        Assert.Equal(["Order", "Id"], col.Parts.Select(p => p.Value));
        Assert.All(col.Parts, p => Assert.True(p.Quoted));
    }

    [Fact]
    public void TableSplitsSchemaAndAlias()
    {
        var table = Sql.Table("hr.emp", "e");
        Assert.Equal(["HR", "EMP"], table.Name.Select(p => p.Value));
        Assert.NotNull(table.Alias);
        Assert.Equal("E", table.Alias.Value);
    }

    [Fact]
    public void IdIsOnePart()
    {
        var id = Sql.Id("\"MixedCase\"");
        Assert.Equal("MixedCase", id.Value);
        Assert.True(id.Quoted);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("x(+)")]
    [InlineData("c%FOUND")]
    public void InvalidNamesThrowArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() => Sql.Col(name));
    }

    [Fact]
    public void QuotedKeywordLikeNamePrintsAndRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Col("e.\"date\""), "e.\"date\"");
    }
}
