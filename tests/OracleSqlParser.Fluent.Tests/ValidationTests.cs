using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

/// <summary>Checks that ToSql and Validate refuse incomplete trees and SQL that does not parse or read back, and print the rest.</summary>
public class ValidationTests
{
    private static string Reparse(string sql) => new SqlScriptGenerator().Generate(SqlParser.ParseStatement(sql));

    // ---- completeness ----
    // Each check must fire from the completeness walk, not from the reparse: the printed text of an incomplete tree does not
    // parse, so every assertion also rules out the "printed SQL" messages.

    private static void AssertRejected(Action action, string part)
    {
        var ex = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains(part, ex.Message);
        Assert.DoesNotContain("printed SQL", ex.Message);
    }

    [Fact]
    public void UpdateWithNoSetItemsThrowsNamingSet() =>
        AssertRejected(() => Sql.Update("t").ToSql(), "has no SET items");

    [Fact]
    public void MergeWithoutSourceThrowsNamingUsing() =>
        AssertRejected(() => Sql.MergeInto("t").ToSql(), "MERGE has no USING source");

    [Fact]
    public void MergeWithoutOnThrowsNamingOn() =>
        AssertRejected(() => Sql.MergeInto("t").Using("s").WhenMatchedUpdate(("a", Sql.Lit(1))).ToSql(), "has no ON condition");

    [Fact]
    public void MergeWithNoClausesThrowsNamingWhen() =>
        AssertRejected(
            () => Sql.MergeInto("t").Using("s").On(Sql.Col("t.id").Eq(Sql.Col("s.id"))).ToSql(),
            "has no WHEN MATCHED or WHEN NOT MATCHED clause");

    [Fact]
    public void InsertWithNeitherValuesNorQueryThrows() =>
        AssertRejected(() => Sql.InsertInto("t").Columns("a").ToSql(), "has neither VALUES nor a query");

    [Fact]
    public void QuerySpecificationWithEmptySelectListThrows() =>
        AssertRejected(() => new SelectStatement { Body = new QuerySpecification() }.ToSql(), "A SELECT list is empty");

    [Fact]
    public void InsertTableWithEmptyNameThrows() =>
        AssertRejected(() => Sql.InsertInto(new NamedTable()).Values(Sql.Lit(1)).ToSql(), "A table name is empty");

    [Fact]
    public void HandMadeEmptyColumnReferenceInSetThrows() =>
        AssertRejected(() => Sql.Update("t").Set("a", new ColumnReference()).ToSql(), "A column reference has no name");

    [Fact]
    public void ClonedPlaceholderMergeStillThrows() =>
        AssertRejected(() => Sql.MergeInto("t").DeepClone<MergeStatement>().ToSql(), "MERGE has no USING source");

    [Fact]
    public void EmptyTableNestedInsideASubqueryThrows()
    {
        // The generic walk reaches nodes below the statement, not only the statement's own parts.
        var inner = new SelectStatement
        {
            Body = new QuerySpecification
            {
                SelectList = [new ExpressionSelectItem { Expression = Sql.Col("a") }],
                From = new FromClause { Tables = [new NamedTable()] },
            },
        };
        AssertRejected(() => Sql.Select("a").From("t").Where(Sql.Exists(inner)).ToSql(), "A table name is empty");
    }

    // ---- INSERT width ----

    [Fact]
    public void InsertColumnCountMustMatchQuerySelectList()
    {
        var insert = Sql.InsertInto("t").Columns("a", "b").Query(Sql.Select("b").From("u"));
        AssertRejected(() => insert.ToSql(), "INSERT lists 2 column(s) but its query selects 1 item(s)");
    }

    [Fact]
    public void InsertWithStarQuerySkipsWidthCheck()
    {
        var insert = Sql.InsertInto("t").Columns("a", "b").Query(Sql.Select("*").From("u"));
        Assert.Equal("INSERT INTO t (a, b) SELECT * FROM u", insert.ToSql());
    }

    [Fact]
    public void InsertWidthIsCheckedOnTheLeftmostBlockOfASetOperation()
    {
        var bad = Sql.InsertInto("t").Columns("a", "b").Query(Sql.Select("x").From("u").Union(Sql.Select("y", "z").From("v")));
        Assert.Throws<InvalidOperationException>(() => bad.ToSql());

        var good = Sql.InsertInto("t").Columns("a", "b").Query(Sql.Select("x", "y").From("u").Union(Sql.Select("y").From("v")));
        Assert.Equal("INSERT INTO t (a, b) SELECT x, y FROM u UNION SELECT y FROM v", good.ToSql());
    }

    // ---- round trip ----

    [Fact]
    public void HandBuiltTreeThatDoesNotReadBackThrows()
    {
        // (a = 1) AND ((b = 2) AND (c = 3)) with the right side unwrapped: prints as a = 1 AND b = 2 AND c = 3, which reads back left-deep.
        var inner = new BinaryExpression
        {
            Left = Sql.Col("b").Eq(Sql.Lit(2)),
            Operator = BinaryOperator.And,
            Right = Sql.Col("c").Eq(Sql.Lit(3)),
        };
        var condition = new BinaryExpression
        {
            Left = Sql.Col("a").Eq(Sql.Lit(1)),
            Operator = BinaryOperator.And,
            Right = inner,
        };
        var select = Sql.Select("a").From("t").Where(condition);
        var ex = Assert.Throws<InvalidOperationException>(() => select.ToSql());
        Assert.Contains("does not read back", ex.Message);
    }

    [Fact]
    public void ParsedStatementWithCommentsAndOddLayoutPasses()
    {
        var parsed = SqlParser.ParseStatement("SELECT /* c */ a,   b\n  FROM   t -- trailing\n WHERE  a=1");
        string sql = parsed.ToSql();
        Assert.Equal(Reparse("SELECT a, b FROM t WHERE a = 1"), sql);
    }

    [Fact]
    public void ValidateReturnsTheSameNode()
    {
        var select = Sql.Select("a").From("t");
        Assert.Same(select, select.Validate());
    }

    [Fact]
    public void ScriptRoundTripsThroughParse()
    {
        var script = Sql.Script(
            Sql.Select("a").From("t"),
            Sql.DeleteFrom("u").Where(Sql.Col("u.id").Eq(Sql.Bind("id"))));
        string sql = script.ToSql();
        var parsed = SqlParser.Parse(sql);
        Assert.True(parsed.Success);
        Assert.True(SqlNodeComparer.StructurallyEqual(script, parsed.Script));
    }

    [Fact]
    public void GeneratorOptionsAreHonouredAndStillChecked()
    {
        var select = Sql.Select("a", "b").From("t");
        string plain = select.ToSql();
        string multiline = select.ToSql(new SqlScriptGeneratorOptions { MultilineSelectElementsList = true });
        Assert.NotEqual(plain, multiline);
        Assert.True(SqlNodeComparer.StructurallyEqual(select, SqlParser.ParseStatement(multiline)));
    }

    // ---- target version ----

    [Fact]
    public void MultiRowValuesThrowsOnOracle19AndPassesOnOracle23()
    {
        var insert = Sql.InsertInto("t").Values(Sql.Lit(1)).Values(Sql.Lit(2));
        var ex = Assert.Throws<InvalidOperationException>(() => insert.ToSql(target: OracleVersion.Oracle19));
        Assert.Contains("23", ex.Message);

        Assert.Equal("INSERT INTO t VALUES (1), (2)", insert.ToSql(target: OracleVersion.Oracle23));
    }

    [Fact]
    public void BooleanLiteralThrowsOnOracle19()
    {
        var select = Sql.Select(Sql.Lit(true).As("b")).From("dual");
        var ex = Assert.Throws<InvalidOperationException>(() => select.ToSql(target: OracleVersion.Oracle19));
        Assert.Contains("23", ex.Message);
        Assert.Equal("SELECT TRUE AS b FROM dual", select.ToSql(target: OracleVersion.Oracle23));
    }

    [Fact]
    public void BooleanExpressionThrowsOnOracle19WhenCheckedAlone()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Lit(true).ToSql(target: OracleVersion.Oracle19));
        Assert.Equal("TRUE", Sql.Lit(true).ToSql());
    }

    [Fact]
    public void PlainQueryPassesOnOracle19()
    {
        var select = Sql.Select("e.name").From("emp", "e").Where(Sql.Col("e.sal").Gt(Sql.Bind("min"))).OrderByDesc("e.sal");
        Assert.Equal("SELECT e.name FROM emp e WHERE e.sal > :min ORDER BY e.sal DESC", select.ToSql(target: OracleVersion.Oracle19));
    }
}
