using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class DmlAndSessionShapeTests
{
    private const string SelDual = "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])]))";

    [Theory]
    [InlineData("INSERT INTO t (a, b) VALUES (1, DEFAULT) RETURNING a INTO :x",
        "(InsertStatement target=(NamedTable name=[T]) columns=[A B] valueRows=[[1 DEFAULT]] returning=[(ReturningItem expression=A)] returningInto=[:x])")]
    [InlineData("INSERT INTO t VALUES (1), (2)", "(InsertStatement target=(NamedTable name=[T]) valueRows=[[1] [2]])")]
    [InlineData("INSERT /*+ APPEND */ INTO t SELECT 1 FROM dual",
        "(InsertStatement target=(NamedTable name=[T]) query=" + SelDual + " hint=(HintClause text=APPEND items=[(HintItem name=APPEND)]))")]
    [InlineData("INSERT INTO t (a) (SELECT 1 FROM dual)",
        "(InsertStatement target=(NamedTable name=[T]) columns=[A] query=(SelectStatement body=(ParenthesizedQuery query=" + SelDual + ")))")]
    [InlineData("INSERT INTO t SELECT 1 FROM dual LOG ERRORS INTO err_t ('tag') REJECT LIMIT UNLIMITED",
        "(InsertStatement target=(NamedTable name=[T]) query=" + SelDual + " errorLogging=(ErrorLogging table=[ERR_T] tag='tag' rejectUnlimited))")]
    [InlineData("INSERT INTO t SELECT 1 FROM dual LOG ERRORS REJECT LIMIT 10",
        "(InsertStatement target=(NamedTable name=[T]) query=" + SelDual + " errorLogging=(ErrorLogging rejectLimit=10))")]
    [InlineData("INSERT INTO t VALUES (1) RETURNING a, b BULK COLLECT INTO v1, v2",
        "(InsertStatement target=(NamedTable name=[T]) valueRows=[[1]] returning=[(ReturningItem expression=A) (ReturningItem expression=B)] returningBulkCollect returningInto=[V1 V2])")]
    public void Inserts(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("INSERT ALL INTO a (x) VALUES (1) INTO b VALUES (2) SELECT 1 FROM dual",
        "(MultiTableInsertStatement branches=[(MultiInsertBranch targets=[(InsertIntoClause table=(NamedTable name=[A]) columns=[X] values=[1]) (InsertIntoClause table=(NamedTable name=[B]) values=[2])])] query=" + SelDual + ")")]
    [InlineData("INSERT FIRST WHEN x > 1 THEN INTO a WHEN x > 0 THEN INTO b ELSE INTO c SELECT 1 FROM dual",
        "(MultiTableInsertStatement first conditional branches=[(MultiInsertBranch when=(GreaterThan X 1) targets=[(InsertIntoClause table=(NamedTable name=[A]))]) (MultiInsertBranch when=(GreaterThan X 0) targets=[(InsertIntoClause table=(NamedTable name=[B]))])] else=(MultiInsertBranch targets=[(InsertIntoClause table=(NamedTable name=[C]))]) query=" + SelDual + ")")]
    public void MultitableInserts(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("UPDATE t SET a = 1, (b, c) = (SELECT 1, 2 FROM dual) WHERE id = 5",
        "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1) (SetClause columns=[B C] value=(SubqueryExpression query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1) (ExpressionSelectItem expression=2)] from=[(NamedTable name=[DUAL])]))))] where=(Equals ID 5))")]
    [InlineData("UPDATE t SET a = 1 WHERE CURRENT OF c1", "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1)] where=(CurrentOfExpression cursor=C1))")]
    [InlineData("UPDATE t SET a = DEFAULT RETURNING a INTO v", "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=DEFAULT)] returning=[(ReturningItem expression=A)] returningInto=[V])")]
    [InlineData("DELETE FROM t x WHERE x.a = 1 RETURNING a BULK COLLECT INTO tab",
        "(DeleteStatement target=(NamedTable name=[T] alias=X) where=(Equals X.A 1) returning=[(ReturningItem expression=A)] returningBulkCollect returningInto=[TAB])")]
    [InlineData("DELETE t", "(DeleteStatement target=(NamedTable name=[T]))")]
    [InlineData("UPDATE t SET a = 1 RETURNING OLD a, NEW a INTO :o, :n",
        "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1)] returning=[(ReturningItem image=Old expression=A) (ReturningItem image=New expression=A)] returningInto=[:o :n])")]
    [InlineData("UPDATE t SET a = 1 RETURNING old, new INTO :o, :n",
        "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1)] returning=[(ReturningItem expression=OLD) (ReturningItem expression=NEW)] returningInto=[:o :n])")]
    [InlineData("UPDATE t SET a = 1 RETURNING NEW SUM(a) INTO :n",
        "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1)] returning=[(ReturningItem image=New expression=(FunctionCall name=[SUM] arguments=[(Argument value=A)]))] returningInto=[:n])")]
    [InlineData("UPDATE t SET t.a = u.a FROM u WHERE t.id = u.id",
        "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[T.A] value=U.A)] from=[(NamedTable name=[U])] where=(Equals T.ID U.ID))")]
    [InlineData("DELETE FROM t FROM u WHERE t.id = u.id",
        "(DeleteStatement target=(NamedTable name=[T]) from=[(NamedTable name=[U])] where=(Equals T.ID U.ID))")]
    [InlineData("DELETE FROM t x FROM t y JOIN u ON y.id = u.id WHERE x.id = y.id",
        "(DeleteStatement target=(NamedTable name=[T] alias=X) from=[(JoinTable left=(NamedTable name=[T] alias=Y) kind=Inner right=(NamedTable name=[U]) on=(Equals Y.ID U.ID))] where=(Equals X.ID Y.ID))")]
    [InlineData("DELETE FROM t RETURNING OLD a INTO :o",
        "(DeleteStatement target=(NamedTable name=[T]) returning=[(ReturningItem image=Old expression=A)] returningInto=[:o])")]
    public void UpdatesAndDeletes(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Fact]
    public void Merge_Shape() =>
        Assert.Equal(
            "(MergeStatement target=(NamedTable name=[T]) source=(SubqueryTable query=(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[S])])) alias=S) on=(paren (Equals T.ID S.ID)) clauses=[" +
            "(MergeClause matched set=[(SetClause columns=[T.V] value=S.V)] updateWhere=(GreaterThan S.V 0) deleteWhere=(Equals T.V 0)) " +
            "(MergeClause insertColumns=[ID V] insertValues=[S.ID S.V] insertWhere=(GreaterThan S.V 1))])",
            Shape.Of("MERGE INTO t USING (SELECT * FROM s) s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v WHERE s.v > 0 DELETE WHERE t.v = 0 WHEN NOT MATCHED THEN INSERT (id, v) VALUES (s.id, s.v) WHERE s.v > 1")
                );

    [Fact]
    public void Merge_LogErrors_Shape() =>
        Assert.Equal(
            "(MergeStatement target=(NamedTable name=[T]) source=(SubqueryTable query=(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[S])])) alias=S) on=(paren (Equals T.ID S.ID)) clauses=[" +
            "(MergeClause matched set=[(SetClause columns=[T.V] value=S.V)])] errorLogging=(ErrorLogging table=[ERR_T] tag='x' rejectLimit=5))",
            Shape.Of("MERGE INTO t USING (SELECT * FROM s) s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v LOG ERRORS INTO err_t ('x') REJECT LIMIT 5"));

    [Theory]
    [InlineData("COMMIT", "(CommitStatement)")]
    [InlineData("COMMIT WORK COMMENT 'c'", "(CommitStatement work comment='c')")]
    [InlineData("COMMIT WRITE BATCH NOWAIT", "(CommitStatement write=(CommitWrite wait=NoWait mode=Batch))")]
    [InlineData("COMMIT FORCE 'tx', 5", "(CommitStatement force='tx' forceScn=5)")]
    [InlineData("ROLLBACK", "(RollbackStatement)")]
    [InlineData("ROLLBACK WORK TO SAVEPOINT sp1", "(RollbackStatement work toSavepoint=SP1)")]
    [InlineData("ROLLBACK FORCE 'tx'", "(RollbackStatement force='tx')")]
    [InlineData("SAVEPOINT sp1", "(SavepointStatement name=SP1)")]
    [InlineData("SET TRANSACTION READ ONLY NAME 'rpt'", "(SetTransactionStatement access=ReadOnly name='rpt')")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", "(SetTransactionStatement isolation=Serializable)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ COMMITTED", "(SetTransactionStatement isolation=ReadCommitted)")]
    [InlineData("SET TRANSACTION READ WRITE USE ROLLBACK SEGMENT rbs1", "(SetTransactionStatement access=ReadWrite rollbackSegment=RBS1)")]
    [InlineData("SET CONSTRAINTS ALL DEFERRED", "(SetConstraintsStatement all deferred)")]
    [InlineData("SET CONSTRAINT a, s.b IMMEDIATE", "(SetConstraintsStatement constraints=[[A] [S B]])")]
    [InlineData("SET ROLE NONE", "(SetRoleStatement none)")]
    [InlineData("SET ROLE ALL EXCEPT r1, r2", "(SetRoleStatement all except=[R1 R2])")]
    [InlineData("SET ROLE r1, r2 IDENTIFIED BY pw", "(SetRoleStatement roles=[(RoleSpec name=R1) (RoleSpec name=R2 identifiedBy=PW)])")]
    public void TransactionControl(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD' CURRENT_SCHEMA = hr",
        "(AlterSessionStatement kind=Set settings=[(SessionSetting name=[NLS_DATE_FORMAT] value='YYYY-MM-DD') (SessionSetting name=[CURRENT_SCHEMA] value=HR)])")]
    [InlineData("ALTER SESSION ENABLE PARALLEL DML PARALLEL 4", "(AlterSessionStatement kind=Enable feature=ParallelDml parallel=4)")]
    [InlineData("ALTER SESSION DISABLE PARALLEL QUERY", "(AlterSessionStatement kind=Disable feature=ParallelQuery)")]
    [InlineData("ALTER SESSION FORCE PARALLEL DDL", "(AlterSessionStatement kind=Force feature=ParallelDdl)")]
    [InlineData("ALTER SESSION ENABLE COMMIT IN PROCEDURE", "(AlterSessionStatement kind=Enable feature=CommitInProcedure)")]
    [InlineData("ALTER SESSION ENABLE RESUMABLE TIMEOUT 60 NAME 'job'", "(AlterSessionStatement kind=Enable feature=Resumable timeout=60 resumableName='job')")]
    [InlineData("ALTER SESSION CLOSE DATABASE LINK lnk", "(AlterSessionStatement kind=CloseDatabaseLink databaseLink=LNK)")]
    [InlineData("ALTER SESSION ADVISE ROLLBACK", "(AlterSessionStatement kind=Advise advise=Rollback)")]
    [InlineData("ALTER SESSION SYNC WITH PRIMARY", "(AlterSessionStatement kind=SyncWithPrimary)")]
    public void AlterSession(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("CALL proc(1, 2)", "(CallStatement routine=(FunctionCall name=[PROC] arguments=[(Argument value=1) (Argument value=2)]))")]
    [InlineData("CALL pkg.fn(1) INTO :x", "(CallStatement routine=(FunctionCall name=[PKG FN] arguments=[(Argument value=1)]) into=:x)")]
    [InlineData("EXPLAIN PLAN SET STATEMENT_ID = 'q' INTO plan_table FOR SELECT 1 FROM dual", "(ExplainPlanStatement statementId='q' intoTable=[PLAN_TABLE] statement=" + SelDual + ")")]
    [InlineData("LOCK TABLE a, b IN EXCLUSIVE MODE NOWAIT", "(LockTableStatement tables=[(NamedTable name=[A]) (NamedTable name=[B])] mode=Exclusive noWait)")]
    [InlineData("LOCK TABLE a IN ROW SHARE MODE WAIT 5", "(LockTableStatement tables=[(NamedTable name=[A])] mode=RowShare wait=5)")]
    [InlineData("LOCK TABLE a IN ROW EXCLUSIVE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=RowExclusive)")]
    [InlineData("LOCK TABLE a IN SHARE UPDATE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=ShareUpdate)")]
    [InlineData("LOCK TABLE a IN SHARE ROW EXCLUSIVE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=ShareRowExclusive)")]
    [InlineData("LOCK TABLE a IN SHARE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=Share)")]
    [InlineData("TRUNCATE TABLE s.t PRESERVE MATERIALIZED VIEW LOG DROP ALL STORAGE CASCADE",
        "(TruncateStatement target=Table name=[S T] materializedViewLog=Preserve storage=DropAllStorage cascade)")]
    [InlineData("TRUNCATE CLUSTER c REUSE STORAGE", "(TruncateStatement target=Cluster name=[C] storage=ReuseStorage)")]
    [InlineData("COMMENT ON COLUMN t.c IS 'it''s'", "(CommentStatement target=Column name=[T C] text=it's)")]
    [InlineData("COMMENT ON MATERIALIZED VIEW mv IS 'x'", "(CommentStatement target=MaterializedView name=[MV] text=x)")]
    [InlineData("RENAME a TO b", "(RenameStatement from=[A] to=[B])")]
    [InlineData("PURGE RECYCLEBIN", "(PurgeStatement target=Recyclebin)")]
    [InlineData("PURGE TABLE t", "(PurgeStatement target=Table name=[T])")]
    [InlineData("PURGE TABLESPACE ts USER u", "(PurgeStatement target=Tablespace name=[TS] user=U)")]
    [InlineData("FLASHBACK TABLE t TO BEFORE DROP RENAME TO t2", "(FlashbackTableStatement tables=[[T]] target=BeforeDrop renameTo=T2 before)")]
    [InlineData("FLASHBACK TABLE t1, s.t2 TO TIMESTAMP SYSDATE - 1 ENABLE TRIGGERS", "(FlashbackTableStatement tables=[[T1] [S T2]] target=Timestamp point=(Subtract SYSDATE 1) enableTriggers)")]
    [InlineData("FLASHBACK TABLE t TO BEFORE SCN 123", "(FlashbackTableStatement tables=[[T]] target=Scn point=123 before)")]
    [InlineData("FLASHBACK TABLE t TO RESTORE POINT rp", "(FlashbackTableStatement tables=[[T]] target=RestorePoint point=RP)")]
    public void OtherStatements(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("GRANT SELECT, INSERT (a, b) ON hr.emp TO scott, PUBLIC WITH GRANT OPTION",
        "(GrantStatement privileges=[(GrantPrivilege name=SELECT) (GrantPrivilege name=INSERT columns=[A B])] object=[HR EMP] grantees=[(Grantee name=SCOTT) (Grantee public)] withGrantOption)")]
    [InlineData("GRANT ALL PRIVILEGES TO u", "(GrantStatement allPrivileges grantees=[(Grantee name=U)])")]
    [InlineData("GRANT CREATE ANY TABLE, dba TO u WITH ADMIN OPTION", "(GrantStatement privileges=[(GrantPrivilege name=CREATE ANY TABLE) (GrantPrivilege name=DBA)] grantees=[(Grantee name=U)] withAdminOption)")]
    [InlineData("GRANT READ ON DIRECTORY d TO u", "(GrantStatement privileges=[(GrantPrivilege name=READ)] objectKind=Directory object=[D] grantees=[(Grantee name=U)])")]
    [InlineData("GRANT SELECT ANY TABLE ON SCHEMA hr TO u", "(GrantStatement privileges=[(GrantPrivilege name=SELECT ANY TABLE)] objectKind=Schema object=[HR] grantees=[(Grantee name=U)])")]
    [InlineData("REVOKE CREATE ANY TABLE ON SCHEMA hr FROM u", "(GrantStatement revoke privileges=[(GrantPrivilege name=CREATE ANY TABLE)] objectKind=Schema object=[HR] grantees=[(Grantee name=U)])")]
    [InlineData("GRANT SELECT ON schema.t TO u", "(GrantStatement privileges=[(GrantPrivilege name=SELECT)] object=[SCHEMA T] grantees=[(Grantee name=U)])")]
    [InlineData("REVOKE SELECT ON emp FROM scott CASCADE CONSTRAINTS", "(GrantStatement revoke privileges=[(GrantPrivilege name=SELECT)] object=[EMP] grantees=[(Grantee name=SCOTT)] cascadeConstraints)")]
    [InlineData("GRANT SELECT ON t TO u CONTAINER = ALL", "(GrantStatement privileges=[(GrantPrivilege name=SELECT)] object=[T] grantees=[(Grantee name=U)] container=All)")]
    public void Grants(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("AUDIT SELECT ON emp BY ACCESS", "(AuditStatement options=[(AuditOption name=SELECT)] on=(AuditObject name=[EMP]) by=Access)")]
    [InlineData("ADMINISTER KEY MANAGEMENT CREATE KEYSTORE 'k' IDENTIFIED BY pw", "(AdminStatement verb=ADMINISTER body=[(WordItem value=KEY) (WordItem value=MANAGEMENT) (WordItem value=CREATE) (WordItem value=KEYSTORE) (LiteralItem kind=String value=k) (WordItem value=IDENTIFIED) (WordItem value=BY) (WordItem value=PW)])")]
    public void AdminTokenTreeStatements(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("ANALYZE TABLE emp COMPUTE STATISTICS", "(AnalyzeStatement target=Table name=[EMP] action=ComputeStatistics)")]
    [InlineData("ANALYZE INDEX ix COMPUTE SYSTEM STATISTICS", "(AnalyzeStatement target=Index name=[IX] action=ComputeStatistics systemStatistics)")]
    [InlineData("ANALYZE CLUSTER c DELETE STATISTICS", "(AnalyzeStatement target=Cluster name=[C] action=DeleteStatistics)")]
    [InlineData("ANALYZE TABLE t PARTITION (p1) COMPUTE STATISTICS FOR ALL COLUMNS SIZE 10",
        "(AnalyzeStatement target=Table name=[T] partition=(PartitionReference name=P1) action=ComputeStatistics otherClauses=[(WordItem value=FOR) (WordItem value=ALL) (WordItem value=COLUMNS) (WordItem value=SIZE) (LiteralItem kind=Number value=10)])")]
    [InlineData("ANALYZE TABLE t SUBPARTITION (s1) ESTIMATE STATISTICS SAMPLE 10 PERCENT",
        "(AnalyzeStatement target=Table name=[T] partition=(PartitionReference subpartition name=S1) action=EstimateStatistics sample=10 samplePercent)")]
    [InlineData("ANALYZE TABLE t ESTIMATE STATISTICS SAMPLE 500 ROWS", "(AnalyzeStatement target=Table name=[T] action=EstimateStatistics sample=500)")]
    [InlineData("ANALYZE TABLE t VALIDATE STRUCTURE CASCADE ONLINE", "(AnalyzeStatement target=Table name=[T] action=ValidateStructure cascade online=true)")]
    [InlineData("ANALYZE TABLE t VALIDATE STRUCTURE OFFLINE", "(AnalyzeStatement target=Table name=[T] action=ValidateStructure online=false)")]
    [InlineData("ANALYZE TABLE t LIST CHAINED ROWS INTO chained_rows", "(AnalyzeStatement target=Table name=[T] action=ListChainedRows intoTable=[CHAINED_ROWS])")]
    [InlineData("ANALYZE TABLE t VALIDATE REF UPDATE",
        "(AnalyzeStatement target=Table name=[T] action=Other otherClauses=[(WordItem value=VALIDATE) (WordItem value=REF) (WordItem value=UPDATE)])")]
    public void AnalyzeStatements(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Fact]
    public void SqlPlus_Commands_And_Terminators()
    {
        var script = Shape.Parse("SET SERVEROUTPUT ON SIZE 1000000\nSPOOL out.log\nPROMPT it's fine\nDEFINE x = 5\n@child.sql\nWHENEVER SQLERROR EXIT FAILURE ROLLBACK\nEXEC dbms_output.put_line('x')\nSELECT &x FROM dual;\n/\n");
        var printed = script.Statements.Select(s => AstPrinter.Print(s)).ToList();
        Assert.Equal(
        [
            "(SqlPlusCommandStatement command=SET arguments=[(WordItem value=SERVEROUTPUT) (WordItem value=ON) (WordItem value=SIZE) (LiteralItem kind=Number value=1000000)])",
            "(SqlPlusCommandStatement command=SPOOL arguments=[(WordItem value=OUT) (SymbolItem symbol=.) (WordItem value=LOG)])",
            "(SqlPlusCommandStatement command=PROMPT arguments=[(LiteralItem kind=String value=it's fine)])",
            "(SqlPlusCommandStatement command=DEFINE arguments=[(WordItem value=X) (SymbolItem symbol==) (LiteralItem kind=Number value=5)])",
            "(SqlPlusCommandStatement command=@ arguments=[(LiteralItem kind=String value=child.sql)])",
            "(SqlPlusCommandStatement command=WHENEVER arguments=[(WordItem value=SQLERROR) (WordItem value=EXIT) (WordItem value=FAILURE) (WordItem value=ROLLBACK)])",
            "(ExecStatement body=(ProcedureCallStatement call=(FunctionCall name=[DBMS_OUTPUT PUT_LINE] arguments=[(Argument value='x')])))",
            "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=(SubstitutionVariable name=x))] from=[(NamedTable name=[DUAL])]))",
        ], printed);
    }

    // '@file' and '@@file' at the start of a line take the rest of the line as the file name, verbatim: '/*' in a path is not a comment.
    [Fact]
    public void SqlPlus_FileRun_TakesRestOfLine() =>
        Assert.Equal(
            "(SqlPlusCommandStatement command=@ arguments=[(LiteralItem kind=String value=../packages/*.pks)]) ; " +
            "(SqlPlusCommandStatement command=@@ arguments=[(LiteralItem kind=String value=sub/x--y.sql)]) ; " +
            "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])]))",
            Shape.Of("@../packages/*.pks\n@@sub/x--y.sql\nSELECT 1 FROM dual"));

    // A substitution variable can stand for a name (CREATE USER &owner ...), and it keeps the text that continues it.
    [Fact]
    public void SubstitutionVariable_CanBeAName()
    {
        var sql = "CREATE USER &LOGGER_USER IDENTIFIED BY &PASSWD;\nGRANT SELECT ON &&ut3_owner._cbac_role TO &&ut3_owner;";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.True(r.Script.Statements.Count == 2, "two statements");
        var printed = new SqlScriptGenerator().Generate(r.Script);
        Assert.Contains("CREATE USER &LOGGER_USER IDENTIFIED BY &PASSWD", printed);
        Assert.Contains("&&ut3_owner._cbac_role", printed);
    }

    // In SQL*Plus a '.' alone on its line ends the statement in the buffer, like a blank line does.
    [Fact]
    public void SqlPlusScript_LoneDotEndsAStatement()
    {
        var r = SqlParser.Parse("COMMENT ON TABLE t IS 'x'\n.\nSELECT 2 FROM dual", new ParseOptions { SqlPlusScript = true });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.True(r.Script.Statements.Count == 2, "two statements");
    }

    // A synthesized ';' after a blank line must not become the head of the next line: '@&d' after a DEFINE line is still a command.
    [Fact]
    public void SqlPlusScript_FileRunAfterBlankLineAndDefine()
    {
        var r = SqlParser.Parse("@a.sql\n\nDEFINE d = 1\n@&d\n", new ParseOptions { SqlPlusScript = true });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("(SqlPlusCommandStatement command=@ arguments=[(LiteralItem kind=String value=&d)])",
            r.Script.Statements.Select(st => AstPrinter.Print(st)));
    }

    // The MERGE insert column list may be qualified by the target alias (Oracle accepts INSERT (p.a, p.b)).
    [Fact]
    public void MergeInsertColumnsMayBeQualified()
    {
        var sql = "MERGE INTO t p USING s d ON (p.a = d.a) WHEN NOT MATCHED THEN INSERT (p.a, p.b) VALUES (d.a, d.b)";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("INSERT (p.a, p.b)", new SqlScriptGenerator().Generate(r.Script));
    }

    // SQL*Plus script reading (ParseOptions.SqlPlusScript): a '-' at the end of a line continues the command, and a blank
    // line ends an unterminated statement unless SET SQLBLANKLINES ON. Plain SQL keeps both as ordinary text.
    [Fact]
    public void SqlPlusScript_HyphenAtLineEndContinuesTheCommand()
    {
        var script = new ParseOptions { SqlPlusScript = true };
        var r = SqlParser.Parse("SELECT 1726-\n,'AR'-\n,'x' FROM dual", script);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Single(r.Script.Statements);
        Assert.True(SqlParser.Parse("SELECT 1-\n2 FROM dual").Success);
    }

    // A '$' or '!' that starts a command line is the SQL*Plus host command (the rest of the line is an operating-system command);
    // '!=' and '$name' are not.
    [Fact]
    public void SqlPlus_HostShellEscapeTakesRestOfLine()
    {
        var r = SqlParser.Parse("$ del params.sql.tmp\nSELECT 1 FROM dual WHERE 1\n!= 2", new ParseOptions { SqlPlusScript = true });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal("(SqlPlusCommandStatement command=$ arguments=[(LiteralItem kind=String value=del params.sql.tmp)])", AstPrinter.Print(r.Script.Statements[0]));
        Assert.Equal(2, r.Script.Statements.Count);
    }

    // In PL/SQL a '-' at a line end is still a minus (checked on Oracle 23: v := 5 - newline 3 gives 2).
    [Fact]
    public void SqlPlusScript_HyphenInsidePlsqlIsAMinus()
    {
        var r = SqlParser.Parse("DECLARE\n  v NUMBER;\nBEGIN\n  v := 5 -\n3;\nEND;\n/", new ParseOptions { SqlPlusScript = true });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Contains("5 - 3", new SqlScriptGenerator().Generate(r.Script));
    }

    [Fact]
    public void SqlPlusScript_BlankLineEndsAnUnterminatedStatement()
    {
        var script = new ParseOptions { SqlPlusScript = true };
        var r = SqlParser.Parse("SELECT 1 AS a1\nFROM dual\n\nBEGIN NULL; END;", script);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.True(r.Script.Statements.Count == 2, "two statements");
        Assert.True(SqlParser.Parse("SELECT 1\n\nFROM dual").Script.Statements.Count == 1); // plain SQL: a blank line is just whitespace
    }

    // Blank lines inside a PL/SQL unit (anonymous block, CREATE PACKAGE/TYPE ...) do not end it: SQL*Plus runs it at its lone '/'.
    [Theory]
    [InlineData("CREATE OR REPLACE PACKAGE p AS\n\n  x NUMBER;\nEND;\n/\nSELECT 1 FROM dual")]
    [InlineData("DECLARE\n  x NUMBER;\n\nBEGIN\n\n  NULL;\nEND;\n/\nSELECT 1 FROM dual")]
    [InlineData("PROMPT start\nCREATE TYPE t AS OBJECT (\n  a NUMBER\n\n);\n/\nSELECT 1 FROM dual")]
    public void SqlPlusScript_BlankLinesInsidePlsqlUnitsAreKept(string sql)
    {
        var r = SqlParser.Parse(sql, new ParseOptions { SqlPlusScript = true });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal(2, r.Script.Statements.Count(st => st is not SqlPlusCommandStatement));
    }

    [Fact]
    public void SqlPlusScript_SetSqlBlankLinesOnKeepsBlankLines()
    {
        var r = SqlParser.Parse("SET SQLBLANKLINES ON\nSELECT 1\n\nFROM dual", new ParseOptions { SqlPlusScript = true });
        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.True(r.Script.Statements.Count == 2, "two statements");
    }

    // A '/' alone on its line ends a query or view; it is not a division (the next statement starts with SELECT).
    [Theory]
    [InlineData("CREATE VIEW v AS SELECT * FROM t WHERE a > 1\n/\nSELECT 2 FROM dual")]
    [InlineData("SELECT * FROM t WHERE a > (5\n/1440)\n/\nSELECT 2 FROM dual")]
    public void LoneSlashEndsAQueryStatement(string sql) => Assert.Equal(2, Shape.Parse(sql).Statements.Count);

    // A line starting with '@' after a table name is a database link, not a SQL*Plus file run.
    [Fact]
    public void Line_Starting_With_At_After_Table_Is_DbLink() =>
        Assert.Equal("(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[EMP] dbLink=[REMOTE])]))",
            Shape.Of("SELECT * FROM emp\n@remote"));

    [Theory]
    [InlineData("EXEC :x := 1;", "(ExecStatement body=(AssignmentStatement target=:x value=1))")]
    [InlineData("EXECUTE :x := f(1);", "(ExecStatement body=(AssignmentStatement target=:x value=(FunctionCall name=[F] arguments=[(Argument value=1)])))")]
    [InlineData("EXEC pkg.p(1);", "(ExecStatement body=(ProcedureCallStatement call=(FunctionCall name=[PKG P] arguments=[(Argument value=1)])))")]
    [InlineData("EXEC :x := 1\nEXEC :y := 2", "(ExecStatement body=(AssignmentStatement target=:x value=1)) ; (ExecStatement body=(AssignmentStatement target=:y value=2))")]
    public void Exec_CallOrAssignment(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));
}
