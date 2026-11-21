using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

/// <summary>Editing parsed SQL to add a common table expression and a condition that uses it.</summary>
public class CteEditTests
{
    private const string AllowedCte = "allowed AS (SELECT id FROM allowed_depts WHERE user_id = :uid)";

    private static SelectStatement Parse(string sql) => (SelectStatement)SqlParser.ParseStatement(sql);

    private static SelectStatement Allowed() =>
        Sql.Select("id").From("allowed_depts").Where(Sql.Col("user_id").Eq(Sql.Bind("uid")));

    [Fact]
    public void AddCteAndInFilterToParsedSelect()
    {
        var s = Parse("SELECT e.name FROM emp e WHERE e.active = 1 OR e.admin = 1");
        s.With("allowed", Allowed()).AndWhere(Sql.Col("e.dept_id").In(Sql.Select("id").From("allowed")));
        RoundTrip.AssertStatement(s,
            $"WITH {AllowedCte} SELECT e.name FROM emp e WHERE (e.active = 1 OR e.admin = 1) AND e.dept_id IN (SELECT id FROM allowed)");
        s.ToSql(target: OracleVersion.Oracle19);
    }

    [Fact]
    public void AddSecondCteAndExistsFilterToParsedSelectWithWith()
    {
        var s = Parse("WITH big AS (SELECT * FROM emp WHERE sal > 1000) SELECT b.name FROM big b");
        s.With("allowed", Allowed())
            .AndWhere(Sql.Exists(Sql.Select(Sql.Lit(1)).From("allowed", "a").Where(Sql.Col("a.id").Eq(Sql.Col("b.dept_id")))));
        RoundTrip.AssertStatement(s,
            $"WITH big AS (SELECT * FROM emp WHERE sal > 1000), {AllowedCte} SELECT b.name FROM big b WHERE EXISTS (SELECT 1 FROM allowed a WHERE a.id = b.dept_id)");
    }

    [Fact]
    public void AddCteAndJoinToParsedSelect()
    {
        var s = Parse("SELECT e.name FROM emp e");
        s.With("allowed", Allowed()).Join("allowed", "a", Sql.Col("a.id").Eq(Sql.Col("e.dept_id")));
        RoundTrip.AssertStatement(s, $"WITH {AllowedCte} SELECT e.name FROM emp e JOIN allowed a ON a.id = e.dept_id");
    }

    [Fact]
    public void EditOneStatementOfAParsedScript()
    {
        var script = SqlParser.Parse("SELECT name FROM emp;\nSELECT id FROM dept;").Script;
        var s = (SelectStatement)script.Statements[1];
        s.With("allowed", Allowed()).AndWhere(Sql.Col("id").In(Sql.Select("id").From("allowed")));
        Assert.Equal(
            $"SELECT name FROM emp;\nWITH {AllowedCte} SELECT id FROM dept WHERE id IN (SELECT id FROM allowed);",
            script.ToSql(target: OracleVersion.Oracle19).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void EditAClonedStatementLeavesTheParsedOneUnchanged()
    {
        var original = Parse("SELECT name FROM emp");
        var copy = (SelectStatement)original.DeepClone();
        copy.With("allowed", Allowed()).AndWhere(Sql.Col("dept_id").In(Sql.Select("id").From("allowed")));
        RoundTrip.AssertStatement(original, "SELECT name FROM emp");
        RoundTrip.AssertStatement(copy, $"WITH {AllowedCte} SELECT name FROM emp WHERE dept_id IN (SELECT id FROM allowed)");
    }

    [Fact]
    public void UpdateFilterUsesCteInsideTheSubquery()
    {
        // Oracle has no WITH before UPDATE, so the common table expression goes on the subquery.
        var u = (UpdateStatement)SqlParser.ParseStatement("UPDATE emp e SET e.bonus = 0");
        u.AndWhere(Sql.Col("e.dept_id").In(Sql.Select("id").From("allowed").With("allowed", Allowed())));
        RoundTrip.AssertStatement(u, $"UPDATE emp e SET e.bonus = 0 WHERE e.dept_id IN (WITH {AllowedCte} SELECT id FROM allowed)");
    }

    [Fact]
    public void CteOnParsedUnionWorksButAndWhereThrows()
    {
        var s = Parse("SELECT id FROM a UNION SELECT id FROM b");
        s.With("allowed", Allowed());
        RoundTrip.AssertStatement(s, $"WITH {AllowedCte} SELECT id FROM a UNION SELECT id FROM b");
        var before = s.ToSql();
        Assert.Throws<InvalidOperationException>(() => s.AndWhere(Sql.Col("id").In(Sql.Select("id").From("allowed"))));
        Assert.Equal(before, s.ToSql());
    }
}
