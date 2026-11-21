using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

public class SelectTests
{
    [Fact]
    public void SpecExampleQuery()
    {
        var q = Sql.Select("e.name", "e.sal")
            .From("emp", "e")
            .LeftJoin("dept", "d", Sql.Col("e.dept_id").Eq(Sql.Col("d.id")))
            .Where(Sql.Col("e.sal").Gt(Sql.Bind("min")))
            .OrderByDesc("e.sal")
            .FetchFirst(10);
        RoundTrip.AssertStatement(q,
            "SELECT e.name, e.sal FROM emp e LEFT OUTER JOIN dept d ON e.dept_id = d.id WHERE e.sal > :min ORDER BY e.sal DESC FETCH FIRST 10 ROWS ONLY");
    }

    [Fact]
    public void SelectStarFromDual()
    {
        RoundTrip.AssertStatement(Sql.Select("*").From("dual"), "SELECT * FROM dual");
    }

    [Fact]
    public void QualifiedStarItem()
    {
        RoundTrip.AssertStatement(Sql.Select("e.*").From("emp", "e"), "SELECT e.* FROM emp e");
    }

    [Fact]
    public void StarExpressionBecomesStarItem()
    {
        RoundTrip.AssertStatement(Sql.Select(Sql.Star()).From("t"), "SELECT * FROM t");
    }

    [Fact]
    public void AliasedSelectItem()
    {
        RoundTrip.AssertStatement(Sql.Select(Sql.Col("a").As("x")).From("t"), "SELECT a AS x FROM t");
    }

    [Fact]
    public void DistinctQuery()
    {
        RoundTrip.AssertStatement(Sql.Select("a").Distinct().From("t"), "SELECT DISTINCT a FROM t");
    }

    [Fact]
    public void GroupByAndHaving()
    {
        RoundTrip.AssertStatement(
            Sql.Select(Sql.Col("a"), Sql.Func("COUNT", Sql.Star()))
                .From("t")
                .GroupBy("a")
                .Having(Sql.Func("COUNT", Sql.Star()).Gt(Sql.Lit(1))),
            "SELECT a, COUNT(*) FROM t GROUP BY a HAVING COUNT(*) > 1");
    }

    [Fact]
    public void GroupByMultipleColumns()
    {
        RoundTrip.AssertStatement(Sql.Select("a").From("t").GroupBy("a", "b"), "SELECT a FROM t GROUP BY a, b");
    }

    [Fact]
    public void CommaJoinOfTwoTables()
    {
        RoundTrip.AssertStatement(Sql.Select("a").From("t1").From("t2", "b"), "SELECT a FROM t1, t2 b");
    }

    [Fact]
    public void CrossJoin()
    {
        RoundTrip.AssertStatement(Sql.Select("*").From("a").CrossJoin("b"), "SELECT * FROM a CROSS JOIN b");
    }

    [Fact]
    public void InnerJoinWithAlias()
    {
        RoundTrip.AssertStatement(
            Sql.Select("*").From("a", "x").Join("b", "y", Sql.Col("x.id").Eq(Sql.Col("y.id"))),
            "SELECT * FROM a x JOIN b y ON x.id = y.id");
    }

    [Fact]
    public void ChainedJoinsAreLeftDeep()
    {
        RoundTrip.AssertStatement(
            Sql.Select("*")
                .From("a", "x")
                .Join("b", "y", Sql.Col("x.id").Eq(Sql.Col("y.id")))
                .FullJoin("c", "z", Sql.Col("y.id").Eq(Sql.Col("z.id"))),
            "SELECT * FROM a x JOIN b y ON x.id = y.id FULL OUTER JOIN c z ON y.id = z.id");
    }

    [Fact]
    public void RightJoin()
    {
        RoundTrip.AssertStatement(
            Sql.Select("*").From("a").RightJoin("b", null, Sql.Col("a.id").Eq(Sql.Col("b.id"))),
            "SELECT * FROM a RIGHT OUTER JOIN b ON a.id = b.id");
    }

    [Fact]
    public void SubqueryInFromClause()
    {
        RoundTrip.AssertStatement(
            Sql.Select("*").From(Sql.Select("a").From("t"), "s"),
            "SELECT * FROM (SELECT a FROM t) s");
    }

    [Fact]
    public void CommonTableExpression()
    {
        RoundTrip.AssertStatement(
            Sql.Select("*").From("x").With("x", Sql.Select(Sql.Lit(1)).From("dual")),
            "WITH x AS (SELECT 1 FROM dual) SELECT * FROM x");
    }

    [Fact]
    public void UnionOfTwoQueriesThenOrderBy()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Union(Sql.Select("b").From("u")).OrderBy(Sql.Lit(1)),
            "SELECT a FROM t UNION SELECT b FROM u ORDER BY 1");
    }

    [Fact]
    public void UnionOrderByNamedColumn()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Union(Sql.Select("b").From("u")).OrderBy("a"),
            "SELECT a FROM t UNION SELECT b FROM u ORDER BY a");
    }

    [Fact]
    public void UnionAllOperandWithFetchIsWrapped()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").UnionAll(Sql.Select("b").From("u").FetchFirst(1)),
            "SELECT a FROM t UNION ALL (SELECT b FROM u FETCH FIRST 1 ROWS ONLY)");
    }

    [Fact]
    public void IntersectAndMinus()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Intersect(Sql.Select("b").From("u")),
            "SELECT a FROM t INTERSECT SELECT b FROM u");
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Minus(Sql.Select("b").From("u")),
            "SELECT a FROM t MINUS SELECT b FROM u");
    }

    [Fact]
    public void ChainedUnionsAreLeftDeep()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t")
                .Union(Sql.Select("b").From("u"))
                .Union(Sql.Select("c").From("v")),
            "SELECT a FROM t UNION SELECT b FROM u UNION SELECT c FROM v");
    }

    [Fact]
    public void UnionOperandThatIsAUnionIsParenthesized()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Union(Sql.Select("b").From("u").Union(Sql.Select("c").From("v"))),
            "SELECT a FROM t UNION (SELECT b FROM u UNION SELECT c FROM v)");
    }

    [Fact]
    public void UnionOperandWithOrderByIsParenthesized()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Union(Sql.Select("b").From("u").OrderBy("b")),
            "SELECT a FROM t UNION (SELECT b FROM u ORDER BY b)");
    }

    [Fact]
    public void UnionOperandWithWithIsParenthesized()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Union(Sql.Select("*").From("x").With("x", Sql.Select(Sql.Lit(1)).From("dual"))),
            "SELECT a FROM t UNION (WITH x AS (SELECT 1 FROM dual) SELECT * FROM x)");
    }

    [Fact]
    public void OffsetAndFetch()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Offset(5).FetchFirst(10),
            "SELECT a FROM t OFFSET 5 ROWS FETCH FIRST 10 ROWS ONLY");
    }

    [Fact]
    public void ForUpdate()
    {
        RoundTrip.AssertStatement(Sql.Select("*").From("t").ForUpdate(), "SELECT * FROM t FOR UPDATE");
    }

    [Fact]
    public void OrderByNullsLast()
    {
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").OrderBy("a", NullsOrdering.Last),
            "SELECT a FROM t ORDER BY a NULLS LAST");
    }

    [Fact]
    public void JoinWithoutFromThrows()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("a").Join("b", null, Sql.Col("a.x").Eq(Sql.Col("b.x"))));
        Assert.Contains("FROM", ex.Message);
    }

    [Fact]
    public void OrderByAfterUnionOnTheLeftThrows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("a").From("t").OrderBy("a").Union(Sql.Select("b").From("u")));
    }

    [Fact]
    public void UnionOnLeftWithFetchThrows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("a").From("t").FetchFirst(1).UnionAll(Sql.Select("b").From("u")));
    }

    [Fact]
    public void SelectingAFunctionCallByNameThrows()
    {
        Assert.Throws<ArgumentException>(() => Sql.Select("count(*)"));
    }

    [Fact]
    public void OrderByNameWithSqlTextThrows()
    {
        Assert.Throws<ArgumentException>(() => Sql.Select("a").From("t").OrderBy("1; drop table t"));
    }

    [Fact]
    public void NegativeOffsetAndFetchThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sql.Select("a").From("t").Offset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sql.Select("a").From("t").FetchFirst(-1));
    }

    [Fact]
    public void ClauseMethodsOnASetOperationBodyThrowNamingTheBody()
    {
        var union = Sql.Select("a").From("t").Union(Sql.Select("b").From("u"));
        var ex = Assert.Throws<InvalidOperationException>(() => union.Where(Sql.Col("a").Eq(Sql.Lit(1))));
        Assert.Contains("SetOperation", ex.Message);
    }

    [Fact]
    public void JoinAsFromItemIsParenthesized()
    {
        var join = new JoinTable { Left = Sql.Table("b"), Kind = JoinKind.Inner, Right = Sql.Table("c"), On = Sql.Col("b.x").Eq(Sql.Col("c.x")) };
        RoundTrip.AssertStatement(Sql.Select("*").From(join), "SELECT * FROM (b JOIN c ON b.x = c.x)");
    }

    [Fact]
    public void UnionOperandWithForUpdateIsParenthesized()
    {
        RoundTrip.AssertStatement(Sql.Select("a").From("t").Union(Sql.Select("b").From("u").ForUpdate()), "SELECT a FROM t UNION (SELECT b FROM u FOR UPDATE)");
    }

    [Fact]
    public void OffsetZeroAndNullsFirst()
    {
        RoundTrip.AssertStatement(Sql.Select("a").From("t").Offset(0).OrderBy("a", NullsOrdering.First), "SELECT a FROM t ORDER BY a NULLS FIRST OFFSET 0 ROWS");
    }

    [Fact]
    public void CteWithUnionBody()
    {
        RoundTrip.AssertStatement(
            Sql.Select("*").From("x").With("x", Sql.Select("a").From("t").Union(Sql.Select("b").From("u"))),
            "WITH x AS (SELECT a FROM t UNION SELECT b FROM u) SELECT * FROM x");
    }

    [Fact]
    public void CommaSubquery()
    {
        RoundTrip.AssertStatement(Sql.Select("*").From("t").From(Sql.Select("b").From("u"), "s"), "SELECT * FROM t, (SELECT b FROM u) s");
    }

    [Fact]
    public void ParsedUnionOperandKeepsParenthesesOnRoundTrip()
    {
        var parsed = (SelectStatement)SqlParser.ParseStatement("SELECT b FROM u UNION SELECT c FROM v");
        RoundTrip.AssertStatement(Sql.Select("a").From("t").Union(parsed), "SELECT a FROM t UNION (SELECT b FROM u UNION SELECT c FROM v)");
    }

    [Fact]
    public void ExtraWhereOnParsedWithSelect()
    {
        var parsed = (SelectStatement)SqlParser.ParseStatement("WITH x AS (SELECT 1 FROM dual) SELECT * FROM x WHERE a = 1 OR b = 2");
        parsed.AndWhere(Sql.Col("c").Eq(Sql.Lit(3)));
        RoundTrip.AssertStatement(parsed, "WITH x AS (SELECT 1 FROM dual) SELECT * FROM x WHERE (a = 1 OR b = 2) AND c = 3");
    }

    [Fact]
    public void QualifiedStarWithSpacesAroundTheDot()
    {
        RoundTrip.AssertStatement(Sql.Select("e . *").From("emp", "e"), "SELECT e.* FROM emp e");
    }

    [Fact]
    public void JoinOnTheRightIsParenthesized()
    {
        var right = new JoinTable { Left = Sql.Table("c"), Kind = JoinKind.Inner, Right = Sql.Table("d"), On = Sql.Col("c.x").Eq(Sql.Col("d.x")) };
        RoundTrip.AssertStatement(Sql.Select("*").From("a").Join(right, Sql.Col("a.y").Eq(Sql.Col("c.y"))), "SELECT * FROM a JOIN (c JOIN d ON c.x = d.x) ON a.y = c.y");
    }
}
