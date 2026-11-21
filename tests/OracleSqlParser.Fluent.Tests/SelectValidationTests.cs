using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

/// <summary>Checks that the SELECT builders reject SQL their own methods would introduce, and leave the statement unchanged when they do.</summary>
public class SelectValidationTests
{
    private static Expression Cond() => Sql.Col("a.x").Eq(Sql.Col("b.x"));

    private static SelectStatement Base() => Sql.Select("a").From("t");

    private static string Printed(Statement s) => new SqlScriptGenerator().Generate(s);

    // ---- FOR UPDATE ----

    [Fact]
    public void ForUpdateOnSetOperationThrows()
    {
        var s = Sql.Select("a").From("t").Union(Sql.Select("b").From("u"));
        Assert.Throws<InvalidOperationException>(() => s.ForUpdate());
        Assert.Null(s.ForUpdate);
    }

    [Fact]
    public void ForUpdateOnParenthesizedBodyThrows()
    {
        var s = new SelectStatement { Body = new ParenthesizedQuery { Query = Base() } };
        Assert.Throws<InvalidOperationException>(() => s.ForUpdate());
        Assert.Null(s.ForUpdate);
    }

    [Fact]
    public void ForUpdateOnValuesBodyThrows()
    {
        var s = new SelectStatement { Body = new ValuesQuery() };
        Assert.Throws<InvalidOperationException>(() => s.ForUpdate());
    }

    [Fact]
    public void ForUpdateWithDistinctThrows()
    {
        var s = Sql.Select("a").From("t").Distinct();
        Assert.Throws<InvalidOperationException>(() => s.ForUpdate());
        Assert.Null(s.ForUpdate);
    }

    [Fact]
    public void ForUpdateWithGroupByThrows()
    {
        var s = Sql.Select("a").From("t").GroupBy("a");
        Assert.Throws<InvalidOperationException>(() => s.ForUpdate());
        Assert.Null(s.ForUpdate);
    }

    [Fact]
    public void DistinctAfterForUpdateThrowsAndLeavesItUnchanged()
    {
        var s = Sql.Select("a").From("t").ForUpdate();
        Assert.Throws<InvalidOperationException>(() => s.Distinct());
        RoundTrip.AssertStatement(s, "SELECT a FROM t FOR UPDATE");
    }

    [Fact]
    public void GroupByAfterForUpdateThrowsAndLeavesItUnchanged()
    {
        var s = Sql.Select("a").From("t").ForUpdate();
        Assert.Throws<InvalidOperationException>(() => s.GroupBy("a"));
        RoundTrip.AssertStatement(s, "SELECT a FROM t FOR UPDATE");
    }

    [Fact]
    public void ForUpdateWithoutDistinctOrGroupByStillWorks()
    {
        RoundTrip.AssertStatement(Sql.Select("a").From("t").ForUpdate(), "SELECT a FROM t FOR UPDATE");
    }

    // ---- aliases ----

    [Theory]
    [InlineData("level")]
    [InlineData("sysdate")]
    [InlineData("rownum")]
    [InlineData("rowid")]
    [InlineData("user")]
    [InlineData("offset")]
    [InlineData("join")]
    [InlineData("using")]
    [InlineData("cross")]
    [InlineData("natural")]
    [InlineData("outer")]
    [InlineData("inner")]
    [InlineData("when")]
    [InlineData("left")]
    [InlineData("select")]
    public void TableAliasThatOracleReadsAsSomethingElseThrows(string alias)
    {
        Assert.Throws<ArgumentException>(() => Sql.Table("t", alias));
        Assert.Throws<ArgumentException>(() => Base().From("u", alias));
    }

    [Theory]
    [InlineData("level")]
    [InlineData("rowid")]
    [InlineData("join")]
    public void JoinAliasThatOracleReadsAsSomethingElseThrows(string alias)
    {
        var s = Base();
        Assert.Throws<ArgumentException>(() => s.Join("b", alias, Cond()));
        RoundTrip.AssertStatement(s, "SELECT a FROM t");
    }

    [Fact]
    public void SubqueryAliasThatOracleReadsAsSomethingElseThrows()
    {
        var s = Base();
        Assert.Throws<ArgumentException>(() => s.From(Sql.Select("b").From("u"), "level"));
        RoundTrip.AssertStatement(s, "SELECT a FROM t");
    }

    [Fact]
    public void QuotedLevelAliasRoundTrips()
    {
        RoundTrip.AssertStatement(Sql.Select("*").From("t", "\"LEVEL\""), "SELECT * FROM t \"LEVEL\"");
    }

    [Fact]
    public void QuotedJoinKeywordAliasRoundTrips()
    {
        RoundTrip.AssertStatement(Sql.Select("*").From("t").Join("u", "\"join\"", Cond()), "SELECT * FROM t JOIN u \"join\" ON a.x = b.x");
    }

    // ---- table names ----

    [Theory]
    [InlineData("a.b.c")]
    [InlineData("a.b.c.d")]
    public void TableNameWithMoreThanTwoPartsThrows(string name)
    {
        Assert.Throws<ArgumentException>(() => Sql.Table(name));
        var s = Base();
        Assert.Throws<ArgumentException>(() => s.From(name));
        Assert.Throws<ArgumentException>(() => s.Join(name, null, Cond()));
        RoundTrip.AssertStatement(s, "SELECT a FROM t");
    }

    [Fact]
    public void TwoPartTableNameStillWorks()
    {
        RoundTrip.AssertStatement(Sql.Select("*").From("hr.emp"), "SELECT * FROM hr.emp");
    }

    [Fact]
    public void NamedTableReferenceWithThreePartsThrows()
    {
        var table = new NamedTable
        {
            Name = [new Identifier { Value = "A" }, new Identifier { Value = "B" }, new Identifier { Value = "C" }],
        };
        var s = Base();
        Assert.Throws<ArgumentException>(() => s.From(table));
        RoundTrip.AssertStatement(s, "SELECT a FROM t");
    }

    [Fact]
    public void EmptyNamedTableReferenceThrows()
    {
        Assert.Throws<ArgumentException>(() => Base().From(new NamedTable()));
    }

    // ---- VALUES bodies ----

    [Fact]
    public void ClauseMethodsThatPrintAfterAValuesBodyThrow()
    {
        var calls = new (string Name, Func<SelectStatement, SelectStatement> Call)[]
        {
            ("OrderBy", s => s.OrderBy("a")),
            ("OrderByDesc", s => s.OrderByDesc("a")),
            ("Offset", s => s.Offset(1)),
            ("FetchFirst", s => s.FetchFirst(1)),
            ("With", s => s.With("x", Base())),
        };
        foreach (var (name, call) in calls)
        {
            var s = new SelectStatement { Body = new ValuesQuery() };
            var ex = Assert.Throws<InvalidOperationException>(() => call(s));
            Assert.Contains("VALUES", ex.Message);
            Assert.Null(s.OrderBy);
            Assert.Null(s.Offset);
            Assert.Null(s.Fetch);
            Assert.Null(s.With);
            Assert.True(s.Body is ValuesQuery, name);
        }
    }

    // ---- bare star ----

    [Fact]
    public void SelectBareStarWithAnotherItemThrows()
    {
        Assert.Throws<ArgumentException>(() => Sql.Select("*", "x"));
    }

    [Fact]
    public void SelectBareStarExpressionWithAnotherItemThrows()
    {
        Assert.Throws<ArgumentException>(() => Sql.Select(Sql.Star(), Sql.Col("a")));
    }

    [Fact]
    public void QualifiedStarMixedWithColumnsIsAllowed()
    {
        RoundTrip.AssertStatement(Sql.Select("t.*", "a").From("t"), "SELECT t.*, a FROM t");
    }

    [Fact]
    public void TwoQualifiedStarsAreAllowed()
    {
        RoundTrip.AssertStatement(Sql.Select("t.*", "u.*").From("t").CrossJoin("u"), "SELECT t.*, u.* FROM t CROSS JOIN u");
    }

    [Fact]
    public void AddingAColumnToABareStarThrowsAndLeavesItUnchanged()
    {
        var s = Sql.Select("*").From("t");
        Assert.Throws<InvalidOperationException>(() => s.AddColumn("a"));
        RoundTrip.AssertStatement(s, "SELECT * FROM t");
    }

    [Fact]
    public void AddingABareStarToAColumnThrowsAndLeavesItUnchanged()
    {
        var s = Base();
        Assert.Throws<InvalidOperationException>(() => s.AddColumn("*"));
        Assert.Throws<InvalidOperationException>(() => s.AddColumn(Sql.Star()));
        RoundTrip.AssertStatement(s, "SELECT a FROM t");
    }

    [Fact]
    public void AddingBareStarNamesInAListThrowsAndLeavesItUnchanged()
    {
        var s = Base();
        Assert.Throws<InvalidOperationException>(() => s.AddColumns("b", "*"));
        RoundTrip.AssertStatement(s, "SELECT a FROM t");
    }

    [Fact]
    public void AddingAQualifiedStarToAColumnIsAllowed()
    {
        RoundTrip.AssertStatement(Base().AddColumn("t.*"), "SELECT a, t.* FROM t");
    }

    // ---- WITH names ----

    [Fact]
    public void WithInvalidNameLeavesNoEmptyWith()
    {
        var s = Sql.Select("*").From("x");
        Assert.Throws<ArgumentException>(() => s.With("1", Base()));
        Assert.Null(s.With);
        RoundTrip.AssertStatement(s, "SELECT * FROM x");
    }

    [Fact]
    public void WithDuplicateNameThrows()
    {
        var s = Sql.Select("*").From("x").With("x", Base());
        Assert.Throws<ArgumentException>(() => s.With("X", Base()));
        Assert.Single(s.With!.Ctes);
    }

    [Fact]
    public void WithQuotedNameThatFoldsToTheSameNameThrows()
    {
        // Oracle reads "X" and x as the same name.
        var s = Sql.Select("*").From("x").With("x", Base());
        Assert.Throws<ArgumentException>(() => s.With("\"X\"", Base()));
    }

    [Fact]
    public void WithDifferentlyCasedQuotedNameIsAllowed()
    {
        var s = Sql.Select("*").From("x").With("x", Base()).With("\"x\"", Base());
        Assert.Equal(2, s.With!.Ctes.Count);
    }

    [Fact]
    public void AddColumnsWithAnInvalidNameAddsNothing()
    {
        var s = Base();
        Assert.Throws<ArgumentException>(() => s.AddColumns("b", "1"));
        RoundTrip.AssertStatement(s, "SELECT a FROM t");
    }

    // ---- every throwing call leaves the statement as it was ----

    [Fact]
    public void ThrowingCallsLeaveTheStatementUnchanged()
    {
        var calls = new (string Name, Action<SelectStatement> Call)[]
        {
            ("From bad alias", s => s.From("u", "level")),
            ("From bad table", s => s.From("a.b.c")),
            ("Join bad alias", s => s.Join("b", "level", Cond())),
            ("Join bad table", s => s.Join("a.b.c", null, Cond())),
            ("Join without condition", s => s.Join(Sql.Table("b"), null!)),
            ("GroupBy empty", s => s.GroupBy(new Expression[0])),
            ("GroupBy empty names", s => s.GroupBy(new string[0])),
            ("GroupBy bad name", s => s.GroupBy("a", "1")),
            ("Offset negative", s => s.Offset(-1)),
            ("FetchFirst negative", s => s.FetchFirst(-1)),
            ("With bad name", s => s.With("1", Base())),
            ("AddColumn bad name", s => s.AddColumn("1")),
            ("AddColumns partly bad", s => s.AddColumns("b", "1")),
            ("AddColumn star mix", s => s.AddColumn("*")),
            ("OrderBy bad name", s => s.OrderBy("1; drop table t")),
            ("Union with itself", s => s.Union(s)),
        };
        foreach (var (name, call) in calls)
        {
            var s = Base();
            var before = Printed(s);
            Assert.ThrowsAny<Exception>(() => call(s));
            Assert.True(SqlNodeComparer.StructurallyEqual(s, Base()), $"{name} changed the statement");
            Assert.Equal(before, Printed(s));
        }
    }
}
