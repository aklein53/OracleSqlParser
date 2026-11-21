using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

public class SelectEditTests
{
    private static SelectStatement Parse(string sql) => (SelectStatement)SqlParser.ParseStatement(sql);

    [Fact]
    public void AndWhereOnStatementWithoutWhereActsAsWhere()
    {
        var s = Parse("SELECT * FROM t");
        s.AndWhere(Sql.Col("x").Eq(Sql.Lit(1)));
        RoundTrip.AssertStatement(s, "SELECT * FROM t WHERE x = 1");
    }

    [Fact]
    public void AndWhereGroupsAnExistingOrCondition()
    {
        var s = Parse("SELECT * FROM t WHERE a = 1 OR b = 2");
        s.AndWhere(Sql.Col("c").Eq(Sql.Lit(3)));
        RoundTrip.AssertStatement(s, "SELECT * FROM t WHERE (a = 1 OR b = 2) AND c = 3");
    }

    [Fact]
    public void AndWhereKeepsAnExistingAndCondition()
    {
        var s = Parse("SELECT * FROM t WHERE a = 1 AND b = 2");
        s.AndWhere(Sql.Col("c").Eq(Sql.Lit(3)));
        RoundTrip.AssertStatement(s, "SELECT * FROM t WHERE a = 1 AND b = 2 AND c = 3");
    }

    [Fact]
    public void AndWhereWithAnOrConditionGroupsTheAddedCondition()
    {
        var s = Parse("SELECT * FROM t WHERE a = 1");
        s.AndWhere(Sql.Col("b").Eq(Sql.Lit(2)).Or(Sql.Col("c").Eq(Sql.Lit(3))));
        RoundTrip.AssertStatement(s, "SELECT * FROM t WHERE a = 1 AND (b = 2 OR c = 3)");
    }

    [Fact]
    public void OrWhereOnAnOrConditionDoesNotRegroup()
    {
        var s = Parse("SELECT * FROM t WHERE a = 1 OR b = 2");
        s.OrWhere(Sql.Col("c").Eq(Sql.Lit(3)));
        RoundTrip.AssertStatement(s, "SELECT * FROM t WHERE a = 1 OR b = 2 OR c = 3");
    }

    [Fact]
    public void OrWhereGroupsAnExistingAndCondition()
    {
        var s = Parse("SELECT * FROM t WHERE a = 1 AND b = 2");
        s.OrWhere(Sql.Col("c").Eq(Sql.Lit(3)));
        RoundTrip.AssertStatement(s, "SELECT * FROM t WHERE a = 1 AND b = 2 OR c = 3");
    }

    [Fact]
    public void WhereReplacesTheCondition()
    {
        var s = Parse("SELECT * FROM t WHERE a = 1");
        s.Where(Sql.Col("z").Eq(Sql.Bind("z")));
        RoundTrip.AssertStatement(s, "SELECT * FROM t WHERE z = :z");
    }

    [Fact]
    public void RemoveWhereClearsTheCondition()
    {
        var s = Parse("SELECT * FROM t WHERE a = 1 OR b = 2");
        s.RemoveWhere();
        RoundTrip.AssertStatement(s, "SELECT * FROM t");
    }

    [Fact]
    public void AddColumnWithAlias()
    {
        var s = Parse("SELECT a FROM t");
        s.AddColumn(Sql.Col("b"), "bb");
        RoundTrip.AssertStatement(s, "SELECT a, b AS bb FROM t");
    }

    [Fact]
    public void AddColumnByName()
    {
        var s = Parse("SELECT a FROM t");
        s.AddColumn("c").AddColumns("d", "e.*");
        RoundTrip.AssertStatement(s, "SELECT a, c, d, e.* FROM t");
    }

    [Fact]
    public void AddStarColumnWithAliasThrows()
    {
        var s = Parse("SELECT a FROM t");
        Assert.Throws<InvalidOperationException>(() => s.AddColumn(Sql.Star(), "x"));
    }

    [Fact]
    public void AddLiteralColumnToParsedQuery()
    {
        var s = Parse("SELECT a FROM t");
        s.AddColumn(Sql.Lit("x'y"), "q");
        RoundTrip.AssertStatement(s, "SELECT a, 'x''y' AS q FROM t");
    }

    [Fact]
    public void FromAppendsASecondTableAfterAParsedJoin()
    {
        var s = Parse("SELECT * FROM a JOIN b ON a.x = b.x");
        s.From("c");
        RoundTrip.AssertStatement(s, "SELECT * FROM a JOIN b ON a.x = b.x, c");
    }

    [Fact]
    public void JoinAddedToParsedJoinIsLeftDeep()
    {
        var s = Parse("SELECT * FROM a JOIN b ON a.x = b.x");
        s.Join("c", null, Sql.Col("b.y").Eq(Sql.Col("c.y")));
        RoundTrip.AssertStatement(s, "SELECT * FROM a JOIN b ON a.x = b.x JOIN c ON b.y = c.y");
    }

    [Fact]
    public void JoinWithOrConditionIsPrintedWithoutExtraParentheses()
    {
        var s = Parse("SELECT * FROM a");
        s.Join("b", null, Sql.Col("a.x").Eq(Sql.Col("b.x")).Or(Sql.Col("a.y").Eq(Sql.Col("b.y"))));
        RoundTrip.AssertStatement(s, "SELECT * FROM a JOIN b ON a.x = b.x OR a.y = b.y");
    }

    [Fact]
    public void OrderByAppendsToParsedOrderBy()
    {
        var s = Parse("SELECT a FROM t ORDER BY a");
        s.OrderByDesc("b");
        RoundTrip.AssertStatement(s, "SELECT a FROM t ORDER BY a, b DESC");
    }

    [Fact]
    public void FetchFirstReplacesParsedFetch()
    {
        var s = Parse("SELECT a FROM t FETCH FIRST 5 ROWS ONLY");
        s.FetchFirst(7);
        RoundTrip.AssertStatement(s, "SELECT a FROM t FETCH FIRST 7 ROWS ONLY");
    }

    [Fact]
    public void OrderByAppliesToParsedUnion()
    {
        var s = Parse("SELECT a FROM t UNION SELECT b FROM u");
        s.OrderBy("a");
        RoundTrip.AssertStatement(s, "SELECT a FROM t UNION SELECT b FROM u ORDER BY a");
    }

    [Fact]
    public void WhereOnParsedUnionThrowsNamingSetOperation()
    {
        var s = Parse("SELECT a FROM t UNION SELECT b FROM u");
        var ex = Assert.Throws<InvalidOperationException>(() => s.Where(Sql.Col("a").Eq(Sql.Lit(1))));
        Assert.Contains("SetOperation", ex.Message);
    }

    [Fact]
    public void AndWhereOnParsedUnionThrows()
    {
        var s = Parse("SELECT a FROM t UNION SELECT b FROM u");
        var ex = Assert.Throws<InvalidOperationException>(() => s.AndWhere(Sql.Col("a").Eq(Sql.Lit(1))));
        Assert.Contains("SetOperation", ex.Message);
    }

    [Fact]
    public void AddColumnOnValuesBodyThrowsNamingValuesQuery()
    {
        // The parser has no standalone VALUES statement, so the body is built directly.
        var s = new SelectStatement { Body = new ValuesQuery() };
        var ex = Assert.Throws<InvalidOperationException>(() => s.AddColumn("a"));
        Assert.Contains("ValuesQuery", ex.Message);
    }

    [Fact]
    public void ForUpdateOnParsedUnionThrowsAndLeavesItUnchanged()
    {
        var s = Parse("SELECT a FROM t UNION SELECT b FROM u");
        var ex = Assert.Throws<InvalidOperationException>(() => s.ForUpdate());
        Assert.Contains("SetOperation", ex.Message);
        Assert.Null(s.ForUpdate);
        RoundTrip.AssertStatement(s, "SELECT a FROM t UNION SELECT b FROM u");
    }

    [Fact]
    public void UnionOnParsedQueryWithWhereKeepsTheLeftWhere()
    {
        var s = Parse("SELECT a FROM t WHERE x = 1");
        s.Union(Sql.Select("b").From("u"));
        RoundTrip.AssertStatement(s, "SELECT a FROM t WHERE x = 1 UNION SELECT b FROM u");
    }

    [Fact]
    public void UnionWithItselfIsRejected()
    {
        var s = Parse("SELECT a FROM t");
        Assert.Throws<ArgumentException>(() => s.Union(s));
    }
}
