using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

/// <summary>Checks for the findings of the final review: Validate on non-printable nodes, Oracle validity of the builders, and the new paging and Negate calls.</summary>
public class ReviewRoundTests
{
    private static void AssertThrowsWith<TEx>(Action action, string part) where TEx : Exception
    {
        var ex = Assert.Throws<TEx>(action);
        Assert.Contains(part, ex.Message);
    }

    // ---- 1. Validate on nodes the generator cannot print alone ----

    [Fact]
    public void ValidateOnWhereClauseChecksCompletenessOnly()
    {
        var where = new WhereClause { Condition = Sql.Col("a").Eq(Sql.Lit(1)) };
        Assert.Same(where, where.Validate());
    }

    [Fact]
    public void ValidateOnFromClauseChecksCompletenessOnly()
    {
        var from = new FromClause { Tables = [Sql.Table("t")] };
        Assert.Same(from, from.Validate());
    }

    [Fact]
    public void ValidateOnIncompleteWhereClauseStillThrows() =>
        AssertThrowsWith<InvalidOperationException>(
            () => new WhereClause { Condition = new ColumnReference() }.Validate(), "A column reference has no name");

    [Fact]
    public void ValidateOnEmptySetClauseThrows() =>
        AssertThrowsWith<InvalidOperationException>(() => new SetClause { Items = [] }.Validate(), "SET");

    [Fact]
    public void EmptySetClauseInsideMergeThrows()
    {
        var merge = Sql.MergeInto("t").Using("s").On(Sql.Col("t.id").Eq(Sql.Col("s.id")));
        merge.Clauses.Add(new MergeMatchedClause { Set = new SetClause { Items = [] } });
        AssertThrowsWith<InvalidOperationException>(() => merge.ToSql(), "SET");
    }

    // ---- 2. Sql.Func message points to Sql.Expr ----

    [Fact]
    public void SpecialFormMessagePointsToSqlExpr()
    {
        var ex = Assert.Throws<ArgumentException>(() => Sql.Func("CAST", Sql.Col("a")));
        Assert.Contains("Sql.Expr", ex.Message);
        Assert.DoesNotContain("dedicated factory", ex.Message);
    }

    // ---- 4. FOR UPDATE with OFFSET / FETCH ----

    [Fact]
    public void ForUpdateAfterOffsetThrowsAndLeavesItUnchanged()
    {
        var s = Sql.Select("a").From("t").Offset(1);
        AssertThrowsWith<InvalidOperationException>(() => s.ForUpdate(), "OFFSET");
        Assert.Null(s.ForUpdate);
    }

    [Fact]
    public void ForUpdateAfterFetchThrowsAndLeavesItUnchanged()
    {
        var s = Sql.Select("a").From("t").FetchFirst(1);
        AssertThrowsWith<InvalidOperationException>(() => s.ForUpdate(), "FETCH");
        Assert.Null(s.ForUpdate);
    }

    [Fact]
    public void OffsetAfterForUpdateThrowsAndLeavesItUnchanged()
    {
        var s = Sql.Select("a").From("t").ForUpdate();
        AssertThrowsWith<InvalidOperationException>(() => s.Offset(1), "FOR UPDATE");
        Assert.Null(s.Offset);
    }

    [Fact]
    public void FetchAfterForUpdateThrowsAndLeavesItUnchanged()
    {
        var s = Sql.Select("a").From("t").ForUpdate();
        AssertThrowsWith<InvalidOperationException>(() => s.FetchFirst(1), "FOR UPDATE");
        Assert.Null(s.Fetch);
    }

    [Fact]
    public void ExpressionOffsetAfterForUpdateThrows()
    {
        var s = Sql.Select("a").From("t").ForUpdate();
        AssertThrowsWith<InvalidOperationException>(() => s.Offset(Sql.Bind("off")), "FOR UPDATE");
        Assert.Null(s.Offset);
    }

    // ---- 5. Duplicate columns ----

    [Fact]
    public void InsertColumnListWithDuplicateThrowsAndLeavesItUnchanged()
    {
        var insert = Sql.InsertInto("t").Columns("a");
        AssertThrowsWith<ArgumentException>(() => insert.Columns("a", "a"), "more than once");
        Assert.Single(insert.Columns);
    }

    [Fact]
    public void InsertColumnDuplicateComparesFoldedLastPart()
    {
        var insert = Sql.InsertInto("t");
        Assert.Throws<ArgumentException>(() => insert.Columns("x.id", "ID"));
        Assert.Throws<ArgumentException>(() => insert.Columns("\"ID\"", "id"));
    }

    [Fact]
    public void UpdateSetOnSameColumnReplacesTheValue()
    {
        var update = Sql.Update("t").Set("sal", Sql.Lit(1)).Set("e.SAL", Sql.Lit(2));
        Assert.Single(update.Set.Items);
        RoundTrip.AssertStatement(update, "UPDATE t SET sal = 2");
    }

    [Fact]
    public void UpdateSetOnQuotedSameFoldedColumnReplacesTheValue()
    {
        var update = Sql.Update("t").Set("sal", Sql.Lit(1)).Set("\"SAL\"", Sql.Lit(3));
        Assert.Single(update.Set.Items);
    }

    [Fact]
    public void UpdateSetOnColumnInsideRowValueAssignmentThrowsAndLeavesItUnchanged()
    {
        var update = Sql.Update("t");
        update.Set.Items.Add(new SetItem
        {
            Columns = [Sql.Col("a"), Sql.Col("b")],
            Value = Sql.Subquery(Sql.Select(Sql.Lit(1), Sql.Lit(2))),
        });
        AssertThrowsWith<InvalidOperationException>(() => update.Set("a", Sql.Lit(9)), "row-value");
        Assert.Single(update.Set.Items);
    }

    [Fact]
    public void UpdateSetAfterSetRowThrowsAndLeavesItUnchanged()
    {
        var update = Sql.Update("t");
        update.Set.Items.Add(new SetItem { Row = true, Value = Sql.Col("rec") });
        AssertThrowsWith<InvalidOperationException>(() => update.Set("a", Sql.Lit(1)), "SET ROW");
        Assert.Single(update.Set.Items);
    }

    [Fact]
    public void WhenMatchedUpdateWithDuplicateColumnsThrowsAndLeavesItUnchanged()
    {
        var merge = Sql.MergeInto("t").Using("s").On(Sql.Col("t.id").Eq(Sql.Col("s.id")));
        AssertThrowsWith<ArgumentException>(() => merge.WhenMatchedUpdate(("a", Sql.Lit(1)), ("A", Sql.Lit(2))), "more than once");
        Assert.Empty(merge.Clauses);
    }

    // ---- 6. Pseudo-columns in Sql.Func ----

    [Theory]
    [InlineData("SYSDATE")]
    [InlineData("sysdate")]
    [InlineData("SYSTIMESTAMP")]
    [InlineData("CURRENT_DATE")]
    [InlineData("CURRENT_TIMESTAMP")]
    [InlineData("LOCALTIMESTAMP")]
    [InlineData("DBTIMEZONE")]
    [InlineData("SESSIONTIMEZONE")]
    [InlineData("USER")]
    [InlineData("UID")]
    [InlineData("ROWNUM")]
    [InlineData("ROWID")]
    [InlineData("ORA_ROWSCN")]
    [InlineData("LEVEL")]
    public void PseudoColumnNamesAreRefusedAsFunctionsForAnyArgumentCount(string name)
    {
        AssertThrowsWith<ArgumentException>(() => Sql.Func(name), "Sql.Col");
        AssertThrowsWith<ArgumentException>(() => Sql.Func(name, Sql.Lit(1)), "Sql.Col");
    }

    [Fact]
    public void QuotedPseudoColumnNameIsStillAllowedAsAFunction() =>
        Assert.NotNull(Sql.Func("\"SYSDATE\""));

    [Fact]
    public void PseudoColumnAsColumnReferenceRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Col("SYSDATE"), "SYSDATE");
        RoundTrip.AssertExpression(Sql.Col("ROWNUM"), "ROWNUM");
    }

    // ---- 7. Paging with bind expressions ----

    [Fact]
    public void ExpressionOffsetAndFetchPrintAndRoundTrip() =>
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").OrderBy("a").Offset(Sql.Bind("off")).FetchFirst(Sql.Bind("n")),
            "SELECT a FROM t ORDER BY a OFFSET :off ROWS FETCH FIRST :n ROWS ONLY");

    [Fact]
    public void ExpressionOffsetAndFetchWorkOnOracle19()
    {
        var s = Sql.Select("a").From("t").OrderBy("a").Offset(Sql.Bind("off")).FetchFirst(Sql.Bind("n"));
        Assert.Equal("SELECT a FROM t ORDER BY a OFFSET :off ROWS FETCH FIRST :n ROWS ONLY", s.ToSql(target: OracleVersion.Oracle19));
    }

    [Fact]
    public void ExpressionOffsetAndFetchRejectNullAndKeepStatementUnchanged()
    {
        var s = Sql.Select("a").From("t");
        Assert.Throws<ArgumentNullException>(() => s.Offset((Expression)null!));
        Assert.Throws<ArgumentNullException>(() => s.FetchFirst((Expression)null!));
        Assert.Null(s.Offset);
        Assert.Null(s.Fetch);
    }

    [Fact]
    public void ExpressionOffsetWorksOnSetOperationBody() =>
        RoundTrip.AssertStatement(
            Sql.Select("a").From("t").Union(Sql.Select("b").From("u")).Offset(Sql.Bind("off")),
            "SELECT a FROM t UNION SELECT b FROM u OFFSET :off ROWS");

    [Fact]
    public void ReadmePagingExamplePrintsTheSameTextOnBothTargets()
    {
        var page = Sql.Select("e.name").From("emp", "e").OrderBy("e.name").Offset(Sql.Bind("off")).FetchFirst(Sql.Bind("n"));
        const string expected = "SELECT e.name FROM emp e ORDER BY e.name OFFSET :off ROWS FETCH FIRST :n ROWS ONLY";
        Assert.Equal(expected, page.ToSql(target: OracleVersion.Oracle19));
        Assert.Equal(expected, page.ToSql(target: OracleVersion.Oracle23));
    }

    // ---- 8. Negate ----

    [Fact]
    public void NegateOfColumnPrintsUnaryMinus() =>
        RoundTrip.AssertExpression(Sql.Col("a").Negate(), "-a");

    [Fact]
    public void NegateOfSumWrapsTheSum() =>
        RoundTrip.AssertExpression(Sql.Col("a").Plus(Sql.Col("b")).Negate(), "-(a + b)");

    [Fact]
    public void NegateOfProductThenTimesKeepsTheGrouping() =>
        RoundTrip.AssertExpression(Sql.Col("a").Negate().Times(Sql.Col("b")), "-a * b");

    [Fact]
    public void NegateOfNegativeLiteralIsParenthesized() =>
        RoundTrip.AssertExpression(Sql.Lit(-1).Negate(), "-(-1)");

    [Fact]
    public void NegateOfPositiveLiteralEqualsNegativeLiteral() =>
        RoundTrip.AssertExpression(Sql.Lit(1).Negate(), "-1");

    // ---- 1 (continued): ToSql leaves printing of non-statement nodes to the generator ----

    [Fact]
    public void ToSqlOnWhereClauseLetsTheGeneratorDecide() =>
        Assert.Throws<NotSupportedException>(() => new WhereClause { Condition = Sql.Col("a").Eq(Sql.Lit(1)) }.ToSql());

    [Fact]
    public void NegateRejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => ((Expression)null!).Negate());
}
