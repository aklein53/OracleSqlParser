using System.Globalization;
using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

public class ExpressionTests
{
    [Fact]
    public void ComparisonWithBindVariable()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Eq(Sql.Bind("x")), "a = :x");
    }

    [Fact]
    public void OrInsideAndIsParenthesized()
    {
        RoundTrip.AssertExpression(
            Sql.Col("a").Eq(Sql.Lit(1)).Or(Sql.Col("b").Eq(Sql.Lit(2))).And(Sql.Col("c").Eq(Sql.Lit(3))),
            "(a = 1 OR b = 2) AND c = 3");
    }

    [Fact]
    public void RightOperandOfSameSubtractIsParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Minus(Sql.Col("b").Minus(Sql.Col("c"))), "a - (b - c)");
    }

    [Fact]
    public void LeftOperandOfSameSubtractIsNotParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Minus(Sql.Col("b")).Minus(Sql.Col("c")), "a - b - c");
    }

    [Fact]
    public void RightOperandOfSameAndIsParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").And(Sql.Col("b").And(Sql.Col("c"))), "a AND (b AND c)");
    }

    [Fact]
    public void RightAndUnderOrIsNotParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Or(Sql.Col("b").And(Sql.Col("c"))), "a OR b AND c");
    }

    [Fact]
    public void RightOrUnderAndIsParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").And(Sql.Col("b").Or(Sql.Col("c"))), "a AND (b OR c)");
    }

    [Fact]
    public void LowerPrecedenceOperandOfTimesIsParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Plus(Sql.Col("b")).Times(Sql.Col("c")), "(a + b) * c");
    }

    [Fact]
    public void NotOverOrIsParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Eq(Sql.Lit(1)).Or(Sql.Col("b").Eq(Sql.Lit(2))).Not(), "NOT (a = 1 OR b = 2)");
    }

    [Fact]
    public void NotOfColumn()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Not(), "NOT a");
    }

    [Fact]
    public void BetweenOperandThatIsOrIsParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Between(Sql.Col("b").Or(Sql.Col("c")), Sql.Lit(2)), "a BETWEEN (b OR c) AND 2");
    }

    [Fact]
    public void BetweenWithPlainBounds()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Between(Sql.Lit(1), Sql.Lit(2)), "a BETWEEN 1 AND 2");
    }

    [Fact]
    public void BindNameWithSpaceThrows()
    {
        Assert.Throws<ArgumentException>(() => Sql.Bind("a b"));
    }

    [Fact]
    public void BindNameAcceptsLeadingColon()
    {
        var withColon = Assert.IsType<BindVariable>(Sql.Bind(":x"));
        var without = Assert.IsType<BindVariable>(Sql.Bind("x"));
        Assert.Equal(without.Name, withColon.Name);
        Assert.Equal(without.Quoted, withColon.Quoted);
    }

    [Fact]
    public void BindNameKeepsTheTypedSpelling()
    {
        RoundTrip.AssertExpression(Sql.Bind("MinSal"), ":MinSal");
    }

    [Fact]
    public void NegativeLiteralIsUnaryMinus()
    {
        RoundTrip.AssertExpression(Sql.Lit(-1), "-1");
    }

    [Fact]
    public void IntMinAndLongMinRoundTrip()
    {
        RoundTrip.AssertExpression(Sql.Lit(int.MinValue), "-2147483648");
        RoundTrip.AssertExpression(Sql.Lit(long.MinValue), "-9223372036854775808");
    }

    [Fact]
    public void NegativeLiteralAsRightOperand()
    {
        RoundTrip.AssertExpression(Sql.Lit(1).Plus(Sql.Lit(-2)), "1 + -2");
    }

    [Fact]
    public void DecimalLiteralIsInvariantUnderGermanCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            RoundTrip.AssertExpression(Sql.Lit(1.5m), "1.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void DoubleLiteralRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Lit(1.5d), "1.5");
    }

    [Fact]
    public void StringWithQuoteIsEscaped()
    {
        RoundTrip.AssertExpression(Sql.Lit("O'Brien"), "'O''Brien'");
    }

    [Fact]
    public void NonAsciiStringRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Lit("naïve €"), "'naïve €'");
    }

    [Fact]
    public void NullStringThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => Sql.Lit((string)null!));
    }

    [Fact]
    public void NanAndInfinityThrowOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sql.Lit(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sql.Lit(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sql.Lit(double.NegativeInfinity));
    }

    [Fact]
    public void BooleanLiteral()
    {
        RoundTrip.AssertExpression(Sql.Lit(true), "TRUE");
    }

    [Fact]
    public void NullLiteral()
    {
        RoundTrip.AssertExpression(Sql.Null, "NULL");
    }

    [Fact]
    public void FunctionCall()
    {
        RoundTrip.AssertExpression(Sql.Func("NVL", Sql.Col("a"), Sql.Lit(0)), "NVL(a, 0)");
    }

    [Fact]
    public void FunctionCallWithStarArgument()
    {
        RoundTrip.AssertExpression(Sql.Func("COUNT", Sql.Star()), "COUNT(*)");
    }

    [Theory]
    [InlineData("CAST")]
    [InlineData("TRIM")]
    [InlineData("EXTRACT")]
    [InlineData("TREAT")]
    [InlineData("CASE")]
    [InlineData("EXISTS")]
    public void FunctionNamesWithSpecialFormsAreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => Sql.Func(name, Sql.Col("a")));
    }

    [Fact]
    public void InList()
    {
        RoundTrip.AssertExpression(Sql.Col("a").In(Sql.Lit(1), Sql.Lit(2)), "a IN (1, 2)");
    }

    [Fact]
    public void NotInList()
    {
        RoundTrip.AssertExpression(Sql.Col("a").NotIn(Sql.Lit(1), Sql.Lit(2)), "a NOT IN (1, 2)");
    }

    [Fact]
    public void LikeAndNotLike()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Like(Sql.Lit("x%")), "a LIKE 'x%'");
        RoundTrip.AssertExpression(Sql.Col("a").NotLike(Sql.Lit("x%")), "a NOT LIKE 'x%'");
    }

    [Fact]
    public void IsNullAndIsNotNull()
    {
        RoundTrip.AssertExpression(Sql.Col("a").IsNull(), "a IS NULL");
        RoundTrip.AssertExpression(Sql.Col("a").IsNotNull(), "a IS NOT NULL");
    }

    [Fact]
    public void SearchedCase()
    {
        RoundTrip.AssertExpression(
            Sql.Case().When(Sql.Col("a").Eq(Sql.Lit(1)), Sql.Lit("one")).Else(Sql.Lit("other")).End(),
            "CASE WHEN a = 1 THEN 'one' ELSE 'other' END");
    }

    [Fact]
    public void SimpleCase()
    {
        RoundTrip.AssertExpression(
            Sql.Case(Sql.Col("a")).When(Sql.Lit(1), Sql.Lit("one")).End(),
            "CASE a WHEN 1 THEN 'one' END");
    }

    [Theory]
    [InlineData("")]
    [InlineData(":")]
    [InlineData("x.y")]
    public void BindNameThatIsEmptyOrDottedIsRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => Sql.Bind(name));
    }

    [Theory]
    [InlineData("SOME")]
    [InlineData("ANY")]
    [InlineData("ALL")]
    [InlineData("PRIOR")]
    [InlineData("CONNECT_BY_ROOT")]
    [InlineData("some")]
    public void FunctionNamesRoutedByTheParserInAnyContextAreRejected(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => Sql.Func(name, Sql.Col("b")));
        Assert.Contains("special form", ex.Message);
        Assert.Contains("Sql.Expr", ex.Message);
    }

    [Theory]
    [InlineData("CASE")]
    [InlineData("PRIOR")]
    [InlineData("CONNECT_BY_ROOT")]
    [InlineData("TRIM")]
    public void SpecialFormNamesGiveTheSpecialFormMessage(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => Sql.Func(name));
        Assert.Contains("special form", ex.Message);
        Assert.DoesNotContain("not a valid name", ex.Message);
    }

    [Fact]
    public void QuotedFunctionNameIsAPlainCall()
    {
        RoundTrip.AssertExpression(Sql.Func("\"SOME\"", Sql.Col("b")), "\"SOME\"(b)");
    }

    [Fact]
    public void StarPrintsAsStarAndQualifiedStar()
    {
        RoundTrip.AssertExpression(Sql.Star(), "*");
        RoundTrip.AssertExpression(Sql.Star("e"), "e.*");
    }

    [Fact]
    public void NotEqualsPrintsAndRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Ne(Sql.Lit(1)), "a <> 1");
    }

    [Fact]
    public void LessThanPrintsAndRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Lt(Sql.Lit(1)), "a < 1");
    }

    [Fact]
    public void LessThanOrEqualPrintsAndRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Le(Sql.Lit(1)), "a <= 1");
    }

    [Fact]
    public void GreaterThanOrEqualPrintsAndRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Ge(Sql.Lit(1)), "a >= 1");
    }

    [Fact]
    public void GreaterThanPrintsAndRoundTrips()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Gt(Sql.Lit(1)), "a > 1");
    }

    [Fact]
    public void ConcatThenPlusIsLeftAssociative()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Concat(Sql.Col("b")).Plus(Sql.Col("c")), "a || b + c");
    }

    [Fact]
    public void PlusOverConcatIsParenthesized()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Plus(Sql.Col("b").Concat(Sql.Col("c"))), "a + (b || c)");
    }

    [Fact]
    public void ComparisonChainIsLeftAssociativeWithoutParentheses()
    {
        RoundTrip.AssertExpression(Sql.Col("a").Eq(Sql.Col("b")).Eq(Sql.Col("c")), "a = b = c");
    }

    [Fact]
    public void ZeroArgumentFunctionPrintsEmptyParentheses()
    {
        // SYSDATE() is not valid Oracle, so the zero-argument case uses a real function (changed in the review fix round).
        RoundTrip.AssertExpression(Sql.Func("SYS_GUID"), "SYS_GUID()");
    }

    [Fact]
    public void DecimalKeepsItsScale()
    {
        RoundTrip.AssertExpression(Sql.Lit(1.10m), "1.10");
    }

    [Fact]
    public void ExistsOverSubquery()
    {
        var query = (SelectStatement)SqlParser.ParseStatement("SELECT 1 FROM dual");
        RoundTrip.AssertExpression(Sql.Exists(query), "EXISTS (SELECT 1 FROM dual)");
    }

    [Fact]
    public void ScalarSubqueryInComparison()
    {
        var query = (SelectStatement)SqlParser.ParseStatement("SELECT MAX(x) FROM u");
        RoundTrip.AssertExpression(Sql.Col("a").Gt(Sql.Subquery(query)), "a > (SELECT MAX(x) FROM u)");
    }

    [Fact]
    public void ExprParsesSqlText()
    {
        Assert.True(SqlNodeComparer.StructurallyEqual(Sql.Expr("a > :b"), SqlParser.ParseExpression("a > :b")));
    }

    [Fact]
    public void ExprWithSyntaxErrorThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Sql.Expr("a >"));
    }

    [Fact]
    public void EmptyCaseThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.Case().End());
    }

    [Fact]
    public void CastFunctionNameThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Sql.Func("CAST", Sql.Col("a")));
    }

    [Fact]
    public void AliasedExpressionSelectItem()
    {
        var item = Sql.Col("a").Plus(Sql.Lit(1)).As("b");
        Assert.Equal("B", item.Alias!.Value);
        Assert.IsType<BinaryExpression>(item.Expression);
    }
}
