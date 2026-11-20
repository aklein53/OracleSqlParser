using OracleSqlParser.Tests;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// The grammar of the 1.0 API consistency pass, checked against Oracle in both directions, as in
/// <see cref="ClauseGrammarOracleTests"/>. <see cref="Rejected"/> and <see cref="RejectedPlsql"/> are forms the parser rejects,
/// and Oracle must reject their syntax. <see cref="Accepted"/> are forms the parser accepts, and Oracle must not reject their
/// syntax (a missing object is fine). <see cref="AcceptedPlsql"/> are PL/SQL blocks that must run without an error.
/// </summary>
[Collection(OracleCollection.Name)]
public class ApiConsistencyOracleTests(OracleFixture db)
{
    /// <summary>Forms the parser rejects, in SQL: a MERGE action that is not UPDATE or INSERT, and CALL with BULK COLLECT or two targets, and PARTITION BY without a list.</summary>
    private static readonly string[] Rejected =
    [
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN MATCHED THEN INSERT (a) VALUES (1)",
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN NOT MATCHED THEN UPDATE SET t.a = 1",
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN MATCHED THEN DELETE",
        "CALL f() BULK COLLECT INTO :a",
        "CALL f() INTO :a, :b",
        "SELECT SUM(a) OVER (PARTITION BY ORDER BY b) FROM t",
        "SELECT * FROM t FOR UPDATE WAIT",
        "LOCK TABLE t IN SHARE MODE WAIT",
        "SELECT LAG(x) IGNORE NULLS RESPECT NULLS OVER (ORDER BY y) FROM t",
        "SELECT LAG(x RESPECT NULLS) IGNORE NULLS OVER (ORDER BY y) FROM t",
    ];

    /// <summary>Forms the parser accepts in SQL, which Oracle must accept at syntax analysis.</summary>
    private static readonly string[] Accepted =
    [
        // INSERT: VALUES forms, qualified column lists
        "INSERT INTO t VALUES (1, 2, 3, 4)",
        "INSERT INTO t (id, a) VALUES (1, 2), (3, 4)",
        "INSERT INTO t x (x.id, x.a) VALUES (1, 2)",
        "INSERT INTO t (t.id) VALUES (1)",
        "INSERT INTO t (id) SELECT 1 FROM dual",
        "INSERT ALL INTO t (id) VALUES (1) SELECT 1 FROM dual",
        "INSERT ALL INTO t (t.id, t.a) VALUES (1, 2) INTO t (id) VALUES (3) SELECT 1 FROM dual",
        "INSERT INTO t (id) VALUES (1) RETURNING id INTO :x",
        // MERGE: every action form
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.a = s.a",
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.a = s.a DELETE WHERE t.b = 0",
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.a = s.a WHERE s.b > 1 DELETE WHERE t.b = 0",
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN NOT MATCHED THEN INSERT (t.id, t.a) VALUES (s.id, s.a)",
        "MERGE INTO t USING t s ON (t.id = s.id) WHEN NOT MATCHED THEN INSERT VALUES (s.id, s.a, s.b, s.col_a)",
        "MERGE INTO t x USING t s ON (x.id = s.id) WHEN NOT MATCHED THEN INSERT (x.id) VALUES (s.id)",
        // UPDATE SET, including the row-value form and RETURNING
        "UPDATE t SET (a, b) = (SELECT 1, 2 FROM dual) WHERE id = -1",
        "UPDATE t SET a = 1, (b, col_a) = (SELECT 2, 3 FROM dual) WHERE id = -1",
        "UPDATE t SET a = 1 WHERE id = -1 RETURNING a, b INTO :x, :y",
        "UPDATE t SET a = 1 WHERE id = -1 RETURNING OLD a, NEW b INTO :x, :y",
        // PARTITION BY in OVER, MATCH_RECOGNIZE and partitioned outer joins
        "SELECT SUM(a) OVER (PARTITION BY b ORDER BY id) FROM t",
        "SELECT ROW_NUMBER() OVER (PARTITION BY a, b ORDER BY id) FROM t",
        "SELECT * FROM t MATCH_RECOGNIZE (PARTITION BY a ORDER BY b PATTERN (p) DEFINE p AS a > 0)",
        "SELECT * FROM t PARTITION BY (t.a) RIGHT OUTER JOIN t t2 ON (t.id = t2.id)",
        "SELECT * FROM t LEFT OUTER JOIN t t2 PARTITION BY (t2.a) ON (t.id = t2.id)",
        // IGNORE NULLS / RESPECT NULLS and NTH_VALUE direction
        "SELECT LAG(a) IGNORE NULLS OVER (ORDER BY id) FROM t",
        "SELECT LAG(a) RESPECT NULLS OVER (ORDER BY id) FROM t",
        "SELECT NTH_VALUE(a, 2) FROM FIRST OVER (ORDER BY id) FROM t",
        "SELECT NTH_VALUE(a, 2) FROM LAST OVER (ORDER BY id) FROM t",
        "SELECT NTH_VALUE(a, 2 FROM LAST) OVER (ORDER BY id) FROM t",
        // FOR UPDATE and LOCK TABLE waits
        "SELECT * FROM t FOR UPDATE NOWAIT",
        "SELECT * FROM t FOR UPDATE WAIT 3",
        "SELECT * FROM t FOR UPDATE OF t.a WAIT 3",
        "SELECT * FROM t FOR UPDATE SKIP LOCKED",
        "LOCK TABLE t IN EXCLUSIVE MODE NOWAIT",
        "LOCK TABLE t IN SHARE MODE WAIT 2",
        // FETCH NEXT and FETCH FIRST are synonyms
        "SELECT id FROM t FETCH NEXT 2 ROWS ONLY",
        "SELECT id FROM t FETCH FIRST 2 ROWS ONLY",
        // CALL INTO with one target
        "CALL f() INTO :x",
        "CALL f(1) INTO :x",
        // EXISTS and IN subqueries
        "SELECT * FROM t WHERE EXISTS (SELECT 1 FROM t t2)",
        "SELECT * FROM t WHERE id IN (SELECT id FROM t t2)",
    ];

    /// <summary>PL/SQL forms the parser rejects, and Oracle must reject at PL/SQL syntax analysis (PLS-00103).</summary>
    private static readonly string[] RejectedPlsql =
    [
        "DECLARE TYPE n IS TABLE OF NUMBER; v n := n(1, 2); BEGIN FORALL i IN REVERSE 1 .. 2 UPDATE t SET a = 1 WHERE id = v(i); END;",
        "DECLARE TYPE n IS TABLE OF NUMBER; v n := n(1, 2); BEGIN FORALL i IN INDICES OF v BETWEEN 1 UPDATE t SET a = 1 WHERE id = v(i); END;",
    ];

    /// <summary>
    /// PL/SQL blocks that must run without an error: RETURNING and VALUES in PL/SQL, every FORALL bound, SET ROW, labels and
    /// anchors. FORALL bodies use the index in a bind, as PLS-00435 requires.
    /// </summary>
    private static readonly string[] AcceptedPlsql =
    [
        // RETURNING INTO, including BULK COLLECT inside FORALL
        "DECLARE x NUMBER; BEGIN INSERT INTO t (id, a) VALUES (-7, 1) RETURNING a INTO x; END;",
        "DECLARE TYPE n IS TABLE OF NUMBER; v n := n(1, 2); r n := n(); BEGIN " +
            "FORALL i IN 1 .. 2 UPDATE t SET a = a + 1 WHERE id = v(i) RETURNING a BULK COLLECT INTO r; END;",
        "DECLARE TYPE n IS TABLE OF NUMBER; r n := n(); BEGIN DELETE FROM t WHERE id = -99 RETURNING a BULK COLLECT INTO r; END;",
        // VALUES: the record form, and the row form
        "DECLARE r t%ROWTYPE; BEGIN r.id := -5; INSERT INTO t VALUES r; END;",
        "BEGIN INSERT INTO t (id, a, b, col_a) VALUES (-6, 1, 1, 1); END;",
        // UPDATE SET ROW
        "DECLARE r t%ROWTYPE; BEGIN r.id := -5; UPDATE t SET ROW = r WHERE id = -5; END;",
        // FORALL: range, INDICES OF with and without BETWEEN, VALUES OF (an index collection of PLS_INTEGER), SAVE EXCEPTIONS
        "DECLARE TYPE n IS TABLE OF NUMBER; v n := n(1, 2); BEGIN FORALL i IN 1 .. 2 UPDATE t SET a = 1 WHERE id = v(i); END;",
        "DECLARE TYPE n IS TABLE OF NUMBER; v n := n(1, 2); BEGIN " +
            "FORALL i IN INDICES OF v BETWEEN 1 AND 2 UPDATE t SET a = 1 WHERE id = v(i); END;",
        "DECLARE TYPE n IS TABLE OF NUMBER; v n := n(1, 2); BEGIN FORALL i IN INDICES OF v UPDATE t SET a = 1 WHERE id = v(i); END;",
        "DECLARE TYPE n IS TABLE OF NUMBER; TYPE ix IS TABLE OF PLS_INTEGER; v n := n(1, 2); k ix := ix(1); BEGIN " +
            "FORALL i IN VALUES OF k UPDATE t SET a = 1 WHERE id = v(i); END;",
        "DECLARE TYPE n IS TABLE OF NUMBER; v n := n(1, 2); BEGIN " +
            "FORALL i IN 1 .. 2 SAVE EXCEPTIONS UPDATE t SET a = 1 WHERE id = v(i); END;",
        // Labels: quoted and unquoted, and END with a label
        "BEGIN <<\"My Lbl\">> NULL; END;",
        "BEGIN <<lbl>> NULL; END lbl;",
        // Anchored declarations
        "DECLARE x t.a%TYPE; y t%ROWTYPE; BEGIN NULL; END;",
        // A cursor attribute is an attribute reference, not a column
        "DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR SELECT * FROM t; IF c%ISOPEN THEN CLOSE c; END IF; END;",
    ];

    public static IEnumerable<object[]> RejectedCases() => Rejected.Select((s, i) => new object[] { i, s });

    public static IEnumerable<object[]> AcceptedCases() => Accepted.Select((s, i) => new object[] { i, s });

    public static IEnumerable<object[]> RejectedPlsqlCases() => RejectedPlsql.Select((s, i) => new object[] { i, s });

    public static IEnumerable<object[]> AcceptedPlsqlCases() => AcceptedPlsql.Select((s, i) => new object[] { i, s });

    [SkippableTheory]
    [MemberData(nameof(RejectedCases))]
    public async Task OracleRejectsWhatTheParserRejects(int index, string sql)
    {
        db.SkipIfUnavailable();
        Assert.False(SqlParser.Parse(sql).Success, $"the parser accepted:\n{sql}");
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"APIR{index}", OracleHarness.Prepare(sql), true);
        Assert.True(outcome == Outcome.Rejected, $"expected Oracle to reject the syntax of:\n{sql}\nbut the outcome was {outcome}: {detail}");
    }

    [SkippableTheory]
    [MemberData(nameof(AcceptedCases))]
    public async Task OracleAcceptsWhatTheParserAccepts(int index, string sql)
    {
        db.SkipIfUnavailable();
        Assert.True(SqlParser.Parse(sql).Success, $"the parser rejected:\n{sql}");
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"APIA{index}", OracleHarness.Prepare(sql), true);
        Assert.True(outcome != Outcome.Rejected, $"Oracle rejected the syntax of:\n{sql}\n{detail}");
    }

    [SkippableTheory]
    [MemberData(nameof(RejectedPlsqlCases))]
    public async Task OracleRejectsThisPlsqlForm(int index, string sql)
    {
        db.SkipIfUnavailable();
        Assert.False(SqlParser.Parse(sql).Success, $"the parser accepted:\n{sql}");
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"APIRP{index}", OracleHarness.Prepare(sql), true);
        Assert.True(outcome == Outcome.Rejected, $"expected Oracle to reject the syntax of:\n{sql}\nbut the outcome was {outcome}: {detail}");
    }

    [SkippableTheory]
    [MemberData(nameof(AcceptedPlsqlCases))]
    public async Task OracleRunsThisPlsqlForm(int index, string sql)
    {
        db.SkipIfUnavailable();
        Assert.True(SqlParser.Parse(sql).Success, $"the parser rejected:\n{sql}");
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"APIAP{index}", OracleHarness.Prepare(sql), true);
        Assert.True(outcome == Outcome.Executed, $"expected a clean run of:\n{sql}\nbut the outcome was {outcome}: {detail}");
    }
}
