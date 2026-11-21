using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

public class DmlTests
{
    [Fact]
    public void InsertWithColumnsAndValues()
    {
        RoundTrip.AssertStatement(
            Sql.InsertInto("emp").Columns("id", "name").Values(Sql.Bind("id"), Sql.Bind("name")),
            "INSERT INTO emp (id, name) VALUES (:id, :name)");
    }

    [Fact]
    public void InsertWithTwoRowValues()
    {
        RoundTrip.AssertStatement(
            Sql.InsertInto("t").Columns("a").Values(Sql.Lit(1)).Values(Sql.Lit(2)),
            "INSERT INTO t (a) VALUES (1), (2)");
    }

    [Fact]
    public void InsertFromQuery()
    {
        RoundTrip.AssertStatement(
            Sql.InsertInto("t").Query((SelectStatement)SqlParser.ParseStatement("SELECT a FROM u")),
            "INSERT INTO t SELECT a FROM u");
    }

    [Fact]
    public void InsertReturningInto()
    {
        RoundTrip.AssertStatement(
            Sql.InsertInto("emp").Columns("name").Values(Sql.Bind("name"))
                .Returning(["id"], [":out_id"]),
            "INSERT INTO emp (name) VALUES (:name) RETURNING id INTO :out_id");
    }

    [Fact]
    public void UpdateWithSetAndWhere()
    {
        RoundTrip.AssertStatement(
            Sql.Update("emp", "e").Set("sal", Sql.Col("e.sal").Times(Sql.Lit(1.1m)))
                .Where(Sql.Col("e.id").Eq(Sql.Bind("id"))),
            "UPDATE emp e SET sal = e.sal * 1.1 WHERE e.id = :id");
    }

    [Fact]
    public void UpdateWithTwoSetItems()
    {
        RoundTrip.AssertStatement(
            Sql.Update("emp").Set("a", Sql.Lit(1)).Set("b", Sql.Lit("x")),
            "UPDATE emp SET a = 1, b = 'x'");
    }

    [Fact]
    public void DeleteWithWhere()
    {
        RoundTrip.AssertStatement(
            Sql.DeleteFrom("emp").Where(Sql.Col("id").Eq(Sql.Bind("id"))),
            "DELETE FROM emp WHERE id = :id");
    }

    [Fact]
    public void DeleteWithoutWhere()
    {
        RoundTrip.AssertStatement(Sql.DeleteFrom("emp"), "DELETE FROM emp");
    }

    [Fact]
    public void MergeWithMatchedUpdateAndNotMatchedInsert()
    {
        RoundTrip.AssertStatement(
            Sql.MergeInto("tgt", "t")
                .Using("src", "s")
                .On(Sql.Col("t.id").Eq(Sql.Col("s.id")))
                .WhenMatchedUpdate(("name", Sql.Col("s.name")))
                .WhenNotMatchedInsert(["id", "name"], [Sql.Col("s.id"), Sql.Col("s.name")]),
            "MERGE INTO tgt t USING src s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET name = s.name WHEN NOT MATCHED THEN INSERT (id, name) VALUES (s.id, s.name)");
    }

    [Fact]
    public void MergeWithSubqueryUsingAndWhereClauses()
    {
        RoundTrip.AssertStatement(
            Sql.MergeInto("tgt")
                .Using((SelectStatement)SqlParser.ParseStatement("SELECT id, qty FROM u"), "s")
                .On(Sql.Col("tgt.id").Eq(Sql.Col("s.id")))
                .WhenMatchedUpdate(("qty", Sql.Col("s.qty")))
                .WhenMatchedUpdateWhere(Sql.Col("s.qty").Gt(Sql.Lit(0)))
                .WhenMatchedDeleteWhere(Sql.Col("s.qty").Eq(Sql.Lit(0)))
                .WhenNotMatchedInsert(["id"], [Sql.Col("s.id")])
                .WhenNotMatchedWhere(Sql.Col("s.qty").Gt(Sql.Lit(0))),
            "MERGE INTO tgt USING (SELECT id, qty FROM u) s ON (tgt.id = s.id) WHEN MATCHED THEN UPDATE SET qty = s.qty WHERE s.qty > 0 DELETE WHERE s.qty = 0 WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id) WHERE s.qty > 0");
    }

    [Fact]
    public void MergeOnConditionIsParenthesized()
    {
        var merge = Sql.MergeInto("tgt").Using("src").On(Sql.Col("tgt.id").Eq(Sql.Col("src.id")))
            .WhenNotMatchedInsert(["id"], [Sql.Col("src.id")]);
        Assert.Equal("MERGE INTO tgt USING src ON (tgt.id = src.id) WHEN NOT MATCHED THEN INSERT (id) VALUES (src.id)",
            new SqlScriptGenerator().Generate(merge));
    }

    [Fact]
    public void InsertValuesAfterQueryThrows()
    {
        var insert = Sql.InsertInto("t").Query((SelectStatement)SqlParser.ParseStatement("SELECT a FROM u"));
        Assert.Throws<InvalidOperationException>(() => insert.Values(Sql.Lit(1)));
    }

    [Fact]
    public void InsertQueryAfterValuesThrows()
    {
        var insert = Sql.InsertInto("t").Values(Sql.Lit(1));
        Assert.Throws<InvalidOperationException>(() => insert.Query((SelectStatement)SqlParser.ParseStatement("SELECT a FROM u")));
    }

    [Fact]
    public void ReturningIntoRejectsArbitraryText()
    {
        var insert = Sql.InsertInto("t").Values(Sql.Lit(1));
        Assert.Throws<ArgumentException>(() => insert.Returning(["id"], ["x + 1"]));
        Assert.Throws<ArgumentException>(() => insert.Returning(["id"], ["count(*)"]));
    }

    [Fact]
    public void ColumnStringsRejectSqlText()
    {
        Assert.Throws<ArgumentException>(() => Sql.InsertInto("t").Columns("a; drop table t"));
        Assert.Throws<ArgumentException>(() => Sql.Update("t").Set("count(*)", Sql.Lit(1)));
    }

    [Fact]
    public void MergeMatchedWhereWithoutMatchedClauseThrows()
    {
        var merge = Sql.MergeInto("t").Using("s").On(Sql.Col("t.id").Eq(Sql.Col("s.id")))
            .WhenNotMatchedInsert(["id"], [Sql.Col("s.id")]);
        Assert.Throws<InvalidOperationException>(() => merge.WhenMatchedUpdateWhere(Sql.Col("s.id").Gt(Sql.Lit(0))));
        Assert.Throws<InvalidOperationException>(() => merge.WhenMatchedDeleteWhere(Sql.Col("s.id").Gt(Sql.Lit(0))));
    }

    [Fact]
    public void MergeNotMatchedWhereWithoutInsertThrows()
    {
        var merge = Sql.MergeInto("t").Using("s").On(Sql.Col("t.id").Eq(Sql.Col("s.id")))
            .WhenMatchedUpdate(("a", Sql.Lit(1)));
        Assert.Throws<InvalidOperationException>(() => merge.WhenNotMatchedWhere(Sql.Col("s.id").Gt(Sql.Lit(0))));
    }

    [Fact]
    public void EmptyValuesAndEmptySetThrow()
    {
        Assert.Throws<ArgumentException>(() => Sql.InsertInto("t").Values());
        Assert.Throws<ArgumentException>(() => Sql.MergeInto("t").Using("s").On(Sql.Lit(1).Eq(Sql.Lit(1)))
            .WhenMatchedUpdate());
    }
}
