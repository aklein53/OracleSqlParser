using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Tests for the nodes and members of the 1.0 API consistency pass: each new clause node spans its keyword, is taken as
/// written or replaced by a rewriter, and round trips through the generator.
/// </summary>
public class ApiConsistencyTests
{
    private static SqlScript Parsed(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return r.Script;
    }

    private static string Text(string sql, SqlNode node) => sql[node.Start..node.End];

    private static string Generate(SqlScript script) => new SqlScriptGenerator().Generate(script).Trim();

    /// <summary>The DML RETURNING clause spans its keyword through the last INTO target; the INTO keeps its own span.</summary>
    [Theory]
    [InlineData("UPDATE t SET a = 1 RETURNING a INTO v", "RETURNING a INTO v", "INTO v")]
    [InlineData("DELETE FROM t RETURNING a, b BULK COLLECT INTO x, y", "RETURNING a, b BULK COLLECT INTO x, y", "BULK COLLECT INTO x, y")]
    [InlineData("INSERT INTO t VALUES (1) RETURN a INTO v", "RETURN a INTO v", "INTO v")]
    [InlineData("INSERT INTO t VALUES (1) RETURNING /* c */ a INTO v", "RETURNING /* c */ a INTO v", "INTO v")]
    public void DmlReturningSpansItsKeywordThroughTheInto(string sql, string expected, string expectedInto)
    {
        var returning = Assert.IsAssignableFrom<DmlStatement>(Parsed(sql).Statements[0]).Returning!;

        Assert.Equal(expected, Text(sql, returning));
        Assert.Equal(expectedInto, Text(sql, returning.Into!));
    }

    /// <summary>A DML statement without RETURNING has no returning clause, rather than an empty one.</summary>
    [Fact]
    public void AStatementWithoutReturningHasNoReturningClause()
    {
        Assert.Null(Assert.IsAssignableFrom<DmlStatement>(Parsed("DELETE FROM t WHERE a = 1").Statements[0]).Returning);
    }

    [Theory]
    [InlineData("UPDATE t SET a = 1 RETURNING a INTO v")]
    [InlineData("DELETE FROM t RETURNING a, b BULK COLLECT INTO x, y")]
    [InlineData("INSERT INTO t VALUES (1) RETURNING a INTO v")]
    public void DmlReturningRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>NEXT and FIRST are synonyms: the spelling is kept for generation, but the two compare structurally equal.</summary>
    [Fact]
    public void FetchNextAndFirstCompareEqualButKeepTheirSpelling()
    {
        var next = Parsed("SELECT a FROM t FETCH NEXT 10 ROWS ONLY");
        var first = Parsed("SELECT a FROM t FETCH FIRST 10 ROWS ONLY");

        Assert.True(SqlNodeComparer.StructurallyEqual(next, first));
        Assert.True(Assert.IsType<FetchClause>(Assert.IsType<SelectStatement>(next.Statements[0]).Fetch).Next);
        Assert.Contains("FETCH NEXT 10", Generate(next));
        Assert.Contains("FETCH FIRST 10", Generate(first));
    }

    /// <summary>The VALUES clause spans its keyword through its last row; each row spans its parentheses.</summary>
    [Fact]
    public void ValuesClauseAndItsRowsSpanTheirKeywordAndParentheses()
    {
        const string sql = "INSERT INTO t VALUES /* c */ (1, 2), (3, 4)";
        var values = Assert.IsAssignableFrom<InsertStatement>(Parsed(sql).Statements[0]).Values!;

        Assert.Equal("VALUES /* c */ (1, 2), (3, 4)", Text(sql, values));
        Assert.Equal(["(1, 2)", "(3, 4)"], values.Rows.Select(r => Text(sql, r)));
        Assert.Null(values.Record);
    }

    /// <summary>PL/SQL's <c>VALUES rec</c> has no rows; the record is the clause's expression.</summary>
    [Fact]
    public void ValuesRecordHasNoRows()
    {
        const string sql = "BEGIN INSERT INTO t VALUES rec; END;";
        var insert = Assert.Single(Parsed(sql).Descendants().OfType<InsertStatement>());

        Assert.Empty(insert.Values!.Rows);
        Assert.Equal("rec", Text(sql, insert.Values!.Record!));
        Assert.Equal("VALUES rec", Text(sql, insert.Values!));
    }

    /// <summary>An INSERT has exactly one of VALUES and a query: the other is null.</summary>
    [Fact]
    public void InsertSelectHasNoValuesClause()
    {
        var insert = Assert.IsType<InsertStatement>(Parsed("INSERT INTO t SELECT 1, 2 FROM dual").Statements[0]);

        Assert.Null(insert.Values);
        Assert.NotNull(insert.Query);
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1, 2), (3, 4)")]
    [InlineData("INSERT INTO t (a, b) VALUES (1, DEFAULT)")]
    [InlineData("INSERT ALL INTO a (x) VALUES (1) INTO b VALUES (2) SELECT 1 FROM dual")]
    [InlineData("WITH x(a, b) AS (VALUES (1, 2), (3, 4)) SELECT * FROM x")]
    [InlineData("SELECT * FROM (VALUES (1, 'a'), (2, 'b')) v (c1, c2)")]
    public void ValuesRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>A rewriter can replace a row list, and the generator prints the new rows.</summary>
    [Fact]
    public void ARewriterCanReplaceTheValuesRows()
    {
        var script = Parsed("INSERT INTO t VALUES (1), (2)");
        new OneRow().RewriteTree(script);

        Assert.Equal("INSERT INTO t VALUES (9);", Generate(script));
    }

    private sealed class OneRow : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(RowValue node) =>
            node.Values[0] is Literal { Value: "1" } ? new RowValue { Values = [new Literal { Kind = LiteralKind.Number, Value = "9" }] } : null;
    }

    /// <summary>An INSERT's column list holds column references, so a qualified name such as <c>x.a</c> is kept (Oracle accepts it).</summary>
    [Fact]
    public void InsertColumnsAreQualifiedColumnReferences()
    {
        const string sql = "INSERT INTO t x (x.a, b) VALUES (1, 2)";
        var columns = Assert.IsType<InsertStatement>(Parsed(sql).Statements[0]).Columns;

        Assert.Equal(["x.a", "b"], columns.Select(c => Text(sql, c)));
        Assert.Equal(2, columns[0].Parts.Count);
    }

    [Theory]
    [InlineData("INSERT INTO t x (x.a, b) VALUES (1, 2)")]
    [InlineData("INSERT INTO t (t.a) VALUES (1)")]
    [InlineData("INSERT ALL INTO t (t.a) VALUES (1) SELECT 1 FROM dual")]
    public void InsertColumnListRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>Each MERGE clause spans WHEN through the end of its action, and the two kinds are separate node types.</summary>
    [Fact]
    public void MergeClausesSpanWhenThroughTheirAction()
    {
        const string sql = "MERGE INTO t USING s ON (t.id = s.id) " +
            "WHEN MATCHED AND s.v > 0 THEN UPDATE SET t.v = s.v WHERE s.v > 1 DELETE WHERE t.v = 0 " +
            "WHEN NOT MATCHED THEN INSERT (t.id) VALUES (s.id) WHERE s.id > 1";
        var clauses = Assert.IsType<MergeStatement>(Parsed(sql).Statements[0]).Clauses;

        Assert.Equal("WHEN MATCHED AND s.v > 0 THEN UPDATE SET t.v = s.v WHERE s.v > 1 DELETE WHERE t.v = 0", Text(sql, clauses[0]));
        Assert.Equal("WHEN NOT MATCHED THEN INSERT (t.id) VALUES (s.id) WHERE s.id > 1", Text(sql, clauses[1]));
        Assert.IsType<MergeMatchedClause>(clauses[0]);
        Assert.Equal("s.v > 0", Text(sql, clauses[0].AndCondition!));
        Assert.Equal("VALUES (s.id)", Text(sql, Assert.IsType<MergeNotMatchedClause>(clauses[1]).Values));
    }

    [Theory]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN DELETE")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN INSERT (id) VALUES (1)")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN NOT MATCHED THEN UPDATE SET t.v = 1")]
    public void AMergeActionThatIsNotUpdateOrInsertIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    [Theory]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v WHERE s.v > 1 DELETE WHERE t.v = 0")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN NOT MATCHED AND s.id > 0 THEN INSERT (t.id) VALUES (s.id)")]
    public void MergeClausesRoundTrip(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>The SET clause spans SET through its last assignment; each assignment is a SetItem.</summary>
    [Fact]
    public void SetClauseSpansSetThroughItsLastItem()
    {
        const string sql = "UPDATE t SET /* c */ a = 1, (b, c) = (SELECT 2, 3 FROM dual) WHERE x = 1";
        var set = Assert.IsType<UpdateStatement>(Parsed(sql).Statements[0]).Set;

        Assert.Equal("SET /* c */ a = 1, (b, c) = (SELECT 2, 3 FROM dual)", Text(sql, set));
        Assert.Equal(["a = 1", "(b, c) = (SELECT 2, 3 FROM dual)"], set.Items.Select(i => Text(sql, i)));
        Assert.Equal(2, set.Items[1].Columns.Count);
    }

    /// <summary>A MERGE update action's SET clause starts at its SET keyword, after the UPDATE keyword.</summary>
    [Fact]
    public void MergeUpdateSetClauseStartsAtSet()
    {
        const string sql = "MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v";
        var matched = Assert.IsType<MergeMatchedClause>(Assert.Single(Assert.IsType<MergeStatement>(Parsed(sql).Statements[0]).Clauses));

        Assert.Equal("SET t.v = s.v", Text(sql, matched.Set));
        Assert.Single(matched.Set.Items);
    }

    [Theory]
    [InlineData("UPDATE t SET a = 1, (b, c) = (SELECT 2, 3 FROM dual) WHERE x = 1")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v, t.w = 1 DELETE WHERE t.v = 0")]
    public void SetClauseRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>A rewriter can drop one assignment of a SET clause and keep the rest.</summary>
    [Fact]
    public void ARewriterCanDropOneSetItem()
    {
        var script = Parsed("UPDATE t SET a = 1, b = 2");
        new DropFirstSetItem().RewriteTree(script);

        Assert.Equal("UPDATE t SET b = 2;", Generate(script));
    }

    private sealed class DropFirstSetItem : SqlNodeRewriter
    {
        private bool _dropped;

        public override SqlNode? Rewrite(SetItem node)
        {
            if (_dropped) return node;
            _dropped = true;
            return null;
        }
    }

    /// <summary>A query's PARTITION BY spans its keyword through the last expression, wherever it is written.</summary>
    [Theory]
    [InlineData("SELECT SUM(a) OVER (PARTITION BY /* c */ b, c ORDER BY d) FROM t", "PARTITION BY /* c */ b, c")]
    [InlineData("SELECT * FROM a PARTITION BY (a.x, a.y) LEFT OUTER JOIN b ON (a.id = b.id)", "PARTITION BY (a.x, a.y)")]
    [InlineData("SELECT * FROM a LEFT OUTER JOIN b PARTITION BY (b.x) ON (a.id = b.id)", "PARTITION BY (b.x)")]
    [InlineData("SELECT * FROM t MATCH_RECOGNIZE (PARTITION BY a ORDER BY b PATTERN (x) DEFINE x AS a > 0)", "PARTITION BY a")]
    public void PartitionByClauseSpansItsKeyword(string sql, string expected)
    {
        var clause = Assert.Single(Parsed(sql).Descendants().OfType<PartitionByClause>());

        Assert.Equal(expected, Text(sql, clause));
    }

    [Theory]
    [InlineData("SELECT SUM(a) OVER (PARTITION BY b, c ORDER BY d) FROM t")]
    [InlineData("SELECT * FROM a PARTITION BY (a.x) LEFT OUTER JOIN b ON (a.id = b.id)")]
    [InlineData("SELECT * FROM t MATCH_RECOGNIZE (PARTITION BY a ORDER BY b PATTERN (x) DEFINE x AS a > 0)")]
    public void PartitionByRoundTrips(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    [Theory]
    [InlineData("SELECT SUM(a) OVER (PARTITION BY ORDER BY d) FROM t")]
    public void AnEmptyPartitionByIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>Each FORALL bound form spans from its first bound (or its INDICES or VALUES keyword) through the last.</summary>
    [Theory]
    [InlineData("BEGIN FORALL i IN 1 .. n DELETE FROM t WHERE id = c(i); END;", "1 .. n", typeof(ForallRange))]
    [InlineData("BEGIN FORALL i IN INDICES OF c BETWEEN 1 AND 5 DELETE FROM t WHERE id = c(i); END;", "INDICES OF c BETWEEN 1 AND 5", typeof(ForallIndicesOf))]
    [InlineData("BEGIN FORALL i IN INDICES OF c DELETE FROM t WHERE id = c(i); END;", "INDICES OF c", typeof(ForallIndicesOf))]
    [InlineData("BEGIN FORALL i IN VALUES OF idx UPDATE t SET a = 1; END;", "VALUES OF idx", typeof(ForallValuesOf))]
    public void ForallBoundsSpanTheirForm(string sql, string expected, Type type)
    {
        var forall = Assert.Single(Parsed(sql).Descendants().OfType<ForallStatement>());

        Assert.IsType(type, forall.Bounds);
        Assert.Equal(expected, Text(sql, forall.Bounds));
        Assert.True(SqlNodeComparer.StructurallyEqual(Parsed(sql), Parsed(Generate(Parsed(sql)))), Generate(Parsed(sql)));
    }

    /// <summary>Oracle has no REVERSE in FORALL (PLS-00103), and the parser does not accept it either.</summary>
    [Fact]
    public void ForallReverseIsRejected()
    {
        Assert.False(SqlParser.Parse("BEGIN FORALL i IN REVERSE 1 .. n DELETE FROM t WHERE id = c(i); END;").Success);
    }

    /// <summary>A rewriter that drops the lower bound of a FORALL range fails, as it does for any required child.</summary>
    [Fact]
    public void ARewriterCannotDropTheLowerBoundOfAForallRange()
    {
        var script = Parsed("BEGIN FORALL i IN 1 .. 2 DELETE FROM t WHERE id = i; END;");

        var ex = Assert.Throws<InvalidOperationException>(() => new DropLiterals().RewriteTree(script));
        Assert.Contains("ForallRange.Lower", ex.Message);
    }

    private sealed class DropLiterals : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(Literal node) => null;
    }

    /// <summary>CALL's INTO spans its keyword through its one target, and holds exactly that target.</summary>
    [Fact]
    public void CallIntoSpansItsKeywordThroughItsTarget()
    {
        const string sql = "CALL pkg.fn(1) INTO :x";
        var call = Assert.IsType<CallStatement>(Parsed(sql).Statements[0]);

        Assert.Equal("INTO :x", Text(sql, call.Into!));
        Assert.Single(call.Into!.Targets);
        Assert.False(call.Into!.BulkCollect);
    }

    /// <summary>Oracle rejects BULK COLLECT and a second target in CALL (ORA-03049, ORA-03048).</summary>
    [Theory]
    [InlineData("CALL f() BULK COLLECT INTO :a")]
    [InlineData("CALL f() INTO :a, :b")]
    public void CallIntoAcceptsOneTargetOnly(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    [Fact]
    public void CallIntoRoundTrips()
    {
        var script = Parsed("CALL f(1) INTO :x");

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>IGNORE NULLS and RESPECT NULLS are one enum-valued option, and absent means null.</summary>
    [Theory]
    [InlineData("SELECT LAG(x, 1) IGNORE NULLS OVER (ORDER BY y) FROM t", NullTreatment.Ignore)]
    [InlineData("SELECT LAG(x, 1) RESPECT NULLS OVER (ORDER BY y) FROM t", NullTreatment.Respect)]
    public void NullTreatmentIsAnEnum(string sql, NullTreatment expected)
    {
        var call = Assert.Single(Parsed(sql).Descendants().OfType<FunctionCall>());

        Assert.Equal(expected, call.NullTreatment);
        Assert.True(SqlNodeComparer.StructurallyEqual(Parsed(sql), Parsed(Generate(Parsed(sql)))));
    }

    [Fact]
    public void AFunctionWithoutNullTreatmentHasNone()
    {
        var call = Assert.Single(Parsed("SELECT LAG(x, 1) OVER (ORDER BY y) FROM t").Descendants().OfType<FunctionCall>());

        Assert.Null(call.NullTreatment);
    }

    /// <summary>NTH_VALUE's FROM FIRST or FROM LAST is an enum, and the direction round trips.</summary>
    [Theory]
    [InlineData("SELECT NTH_VALUE(x, 2) FROM LAST OVER (ORDER BY y) FROM t", NthValueFrom.Last)]
    [InlineData("SELECT NTH_VALUE(x, 2 FROM FIRST) OVER (ORDER BY y) FROM t", NthValueFrom.First)]
    public void NthValueFromIsAnEnum(string sql, NthValueFrom expected)
    {
        var call = Assert.Single(Parsed(sql).Descendants().OfType<FunctionCall>());

        Assert.Equal(expected, call.NthFrom);
        Assert.True(SqlNodeComparer.StructurallyEqual(Parsed(sql), Parsed(Generate(Parsed(sql)))));
    }

    /// <summary>FOR UPDATE and LOCK TABLE share one lock-wait enum; the seconds of WAIT are a separate expression.</summary>
    [Theory]
    [InlineData("SELECT 1 FROM a FOR UPDATE NOWAIT", LockWait.NoWait, null)]
    [InlineData("SELECT 1 FROM a FOR UPDATE WAIT 5", LockWait.Wait, "5")]
    [InlineData("SELECT 1 FROM a FOR UPDATE SKIP LOCKED", LockWait.SkipLocked, null)]
    public void ForUpdateLockWait(string sql, LockWait wait, string? seconds)
    {
        var forUpdate = Assert.IsType<SelectStatement>(Parsed(sql).Statements[0]).ForUpdate!;

        Assert.Equal(wait, forUpdate.Wait);
        Assert.Equal(seconds, forUpdate.WaitSeconds is null ? null : Text(sql, forUpdate.WaitSeconds));
        Assert.True(SqlNodeComparer.StructurallyEqual(Parsed(sql), Parsed(Generate(Parsed(sql)))));
    }

    [Theory]
    [InlineData("BEGIN LOCK TABLE a IN SHARE MODE NOWAIT; END;", LockWait.NoWait, null)]
    [InlineData("BEGIN LOCK TABLE a IN SHARE MODE WAIT 5; END;", LockWait.Wait, "5")]
    public void LockTableWait(string sql, LockWait wait, string? seconds)
    {
        var lock_ = Assert.Single(Parsed(sql).Descendants().OfType<LockTableStatement>());

        Assert.Equal(wait, lock_.Wait);
        Assert.Equal(seconds, lock_.WaitSeconds is null ? null : Text(sql, lock_.WaitSeconds));
        Assert.True(SqlNodeComparer.StructurallyEqual(Parsed(sql), Parsed(Generate(Parsed(sql)))));
    }

    /// <summary>WAIT takes its seconds; without them Oracle rejects the statement.</summary>
    [Theory]
    [InlineData("SELECT 1 FROM a FOR UPDATE WAIT")]
    [InlineData("BEGIN LOCK TABLE a IN SHARE MODE WAIT; END;")]
    public void WaitWithoutSecondsIsRejected(string sql)
    {
        Assert.False(SqlParser.Parse(sql).Success);
    }

    /// <summary>A statement label is an identifier, so it keeps its spelling and its quoting.</summary>
    [Fact]
    public void StatementLabelsAreIdentifiers()
    {
        const string sql = "BEGIN <<lbl>> NULL; <<\"a b\">> NULL; END lbl;";
        var block = Assert.Single(Parsed(sql).Descendants().OfType<BlockStatement>());
        var labels = block.Statements.Select(s => s.Labels.Single()).ToList();

        Assert.Equal("LBL", labels[0].Value);
        Assert.Equal("lbl", labels[0].Text);
        Assert.False(labels[0].Quoted);
        Assert.Equal("a b", labels[1].Value);
        Assert.True(labels[1].Quoted);
        Assert.Equal("lbl", Text(sql, block.EndLabel!));
    }

    [Theory]
    [InlineData("BEGIN <<\"a b\">> NULL; END;")]
    [InlineData("BEGIN <<abc>> NULL; END;")]
    public void StatementLabelsRoundTrip(string sql)
    {
        var script = Parsed(sql);

        Assert.True(SqlNodeComparer.StructurallyEqual(script, Parsed(Generate(script))), Generate(script));
    }

    /// <summary>An anchored type is a TypeAnchor, and the declaration keeps its anchor through a round trip.</summary>
    [Theory]
    [InlineData("DECLARE x t.a%TYPE; BEGIN NULL; END;", TypeAnchor.Type)]
    [InlineData("DECLARE y t%ROWTYPE; BEGIN NULL; END;", TypeAnchor.RowType)]
    public void TypeAnchorIsAnEnum(string sql, TypeAnchor expected)
    {
        var anchored = Assert.Single(Parsed(sql).Descendants().OfType<DataType>());

        Assert.Equal(expected, anchored.Anchor);
        Assert.True(SqlNodeComparer.StructurallyEqual(Parsed(sql), Parsed(Generate(Parsed(sql)))));
    }

    /// <summary>SQL%FOUND and c%ROWCOUNT are column references with an attribute, so they are not reported as columns.</summary>
    [Fact]
    public void CursorAttributesAreNotReportedAsColumns()
    {
        var script = Parsed("BEGIN IF SQL%FOUND AND c%ROWCOUNT > 0 THEN NULL; END IF; END;");

        var attributes = script.Descendants().OfType<ColumnReference>().Where(c => c.Attribute is not null).Select(c => c.Attribute).ToList();
        Assert.Equal(["FOUND", "ROWCOUNT"], attributes);
        Assert.Empty(SqlAnalysis.GetColumns(script));
    }

    /// <summary>A rewriter removes the RETURNING clause, with its INTO, by returning null for it.</summary>
    [Fact]
    public void ARewriterCanDropTheReturningClause()
    {
        var script = Parsed("UPDATE t SET a = 1 RETURNING a INTO v");
        new DropReturning().RewriteTree(script);

        Assert.Equal("UPDATE t SET a = 1;", Generate(script));
    }

    private sealed class DropReturning : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(ReturningClause node) => null;
    }
}
