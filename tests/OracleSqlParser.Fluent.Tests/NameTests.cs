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

    [Fact]
    public void UnquotedIdFoldsToUpperCaseAndPrintsAsTyped()
    {
        var id = Sql.Id("emp");
        Assert.Equal("EMP", id.Value);
        Assert.False(id.Quoted);
        RoundTrip.AssertExpression(Sql.Col(id), "emp");
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

    [Theory]
    [InlineData("a--x")]
    [InlineData("a/*x*/")]
    [InlineData("a /*x*/ .b")]
    [InlineData("--x")]
    public void CommentsInColumnNamesAreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => Sql.Col(name));
    }

    [Fact]
    public void CommentsInTableAliasAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Table("hr.emp", "e--x"));
    }

    [Fact]
    public void CommentsInSelectAliasAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col("a").As("x--"));
    }

    [Theory]
    [InlineData("x--")]
    [InlineData("x/*c*/")]
    public void CommentsInBindNamesAreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => Sql.Bind(name));
    }

    [Fact]
    public void CommentMarkersInsideQuotedNameAreKept()
    {
        var col = Sql.Col("\"a--b\"");
        Assert.Equal("a--b", col.Parts[0].Value);
        Assert.True(col.Parts[0].Quoted);
        RoundTrip.AssertExpression(col, "\"a--b\"");
    }

    [Fact]
    public void WhitespaceAroundTheWholeNameIsAccepted()
    {
        Assert.Equal(["E", "SAL"], Sql.Col("  e.sal  ").Parts.Select(p => p.Value));
    }

    [Fact]
    public void WhitespaceInsideDottedNameIsAcceptedAndPrintsCompact()
    {
        // Oracle allows spaces around the dot. The tree is the same as e.sal, so it prints as e.sal.
        Assert.Equal(["E", "SAL"], Sql.Col("e . sal").Parts.Select(p => p.Value));
        RoundTrip.AssertExpression(Sql.Col("e . sal"), "e.sal");
    }

    [Fact]
    public void EmptyQuotedNameIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col("\"\""));
        Assert.Throws<ArgumentException>(() => Sql.Col("\"\".sal"));
    }

    [Fact]
    public void DoubleQuoteInsideQuotedNameIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col("\"a\"\"b\""));
    }

    [Fact]
    public void ColPartsHappyPathRoundTrips()
    {
        RoundTrip.AssertExpression(
            Sql.Col(new Identifier { Value = "E" }, new Identifier { Value = "SAL" }),
            "E.SAL");
    }

    [Fact]
    public void ColPartsQuotedRoundTrips()
    {
        RoundTrip.AssertExpression(
            Sql.Col(new Identifier { Value = "Order", Quoted = true }, new Identifier { Value = "Id", Quoted = true }),
            "\"Order\".\"Id\"");
    }

    [Fact]
    public void QuotedPartWithSqlTextIsAcceptedAndQuoted()
    {
        RoundTrip.AssertExpression(Sql.Col(new Identifier { Value = "a;b", Quoted = true }), "\"a;b\"");
    }

    [Fact]
    public void UnquotedPartWithSqlTextIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col(new Identifier { Value = "a; drop table t" }));
    }

    [Fact]
    public void UnquotedPartNotInUpperCaseIsRejected()
    {
        // Printed as abc, it would reparse as ABC, so the tree would differ.
        Assert.Throws<ArgumentException>(() => Sql.Col(new Identifier { Value = "abc" }));
    }

    [Fact]
    public void UnquotedPartWithDotIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col(new Identifier { Value = "A.B" }));
    }

    [Fact]
    public void NullPartValueIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col(new Identifier { Value = null! }));
    }

    [Fact]
    public void QuotedPartWithDoubleQuoteIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col(new Identifier { Value = "a\"b", Quoted = true }));
    }

    [Fact]
    public void EmptyQuotedPartIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Sql.Col(new Identifier { Value = "", Quoted = true }));
    }
}
