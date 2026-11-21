using OracleSqlParser;
using OracleSqlParser.Fluent;

namespace OracleSqlParser.Fluent.Tests;

public class DmlEditTests
{
    /// <summary>Asserts an edited tree prints as <paramref name="expectedSql"/> and is structurally equal to its reparse.</summary>
    private static void AssertEdited(Statement edited, string expectedSql)
    {
        Assert.Equal(expectedSql, new SqlScriptGenerator().Generate(edited));
        Assert.True(SqlNodeComparer.StructurallyEqual(edited, SqlParser.ParseStatement(expectedSql)),
            $"Edited tree is not structurally equal to the parse of: {expectedSql}");
    }

    [Fact]
    public void AndWhereOnUpdateWithOrConditionGroupsExisting()
    {
        var update = (UpdateStatement)SqlParser.ParseStatement("UPDATE t SET a = 1 WHERE b = 2 OR c = 3");
        update.AndWhere(Sql.Col("d").Eq(Sql.Lit(4)));
        AssertEdited(update, "UPDATE t SET a = 1 WHERE (b = 2 OR c = 3) AND d = 4");
    }

    [Fact]
    public void AndWhereWithoutExistingWhereActsAsWhere()
    {
        var update = (UpdateStatement)SqlParser.ParseStatement("UPDATE t SET a = 1");
        update.AndWhere(Sql.Col("b").Eq(Sql.Lit(2)));
        AssertEdited(update, "UPDATE t SET a = 1 WHERE b = 2");
    }

    [Fact]
    public void OrWhereOnDeleteGroupsExisting()
    {
        var delete = (DeleteStatement)SqlParser.ParseStatement("DELETE FROM t WHERE a = 1 AND b = 2");
        delete.OrWhere(Sql.Col("c").Eq(Sql.Lit(3)));
        AssertEdited(delete, "DELETE FROM t WHERE a = 1 AND b = 2 OR c = 3");
    }

    [Fact]
    public void RemoveWhereOnParsedDelete()
    {
        var delete = (DeleteStatement)SqlParser.ParseStatement("DELETE FROM t WHERE a = 1");
        delete.RemoveWhere();
        AssertEdited(delete, "DELETE FROM t");
    }

    [Fact]
    public void SetAppendsToParsedUpdate()
    {
        var update = (UpdateStatement)SqlParser.ParseStatement("UPDATE t SET a = 1 WHERE b = 2");
        update.Set("c", Sql.Lit(3));
        AssertEdited(update, "UPDATE t SET a = 1, c = 3 WHERE b = 2");
    }

    [Fact]
    public void ReturningReplacesParsedClause()
    {
        var insert = (InsertStatement)SqlParser.ParseStatement("INSERT INTO t (a) VALUES (1) RETURNING id INTO :x");
        insert.Returning(["b"], [":y"]);
        AssertEdited(insert, "INSERT INTO t (a) VALUES (1) RETURNING b INTO :y");
    }

    [Fact]
    public void WhenMatchedUpdateWhereReplacesParsedCondition()
    {
        var merge = (MergeStatement)SqlParser.ParseStatement(
            "MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET a = s.a WHERE s.a > 0");
        merge.WhenMatchedUpdateWhere(Sql.Col("s.a").Lt(Sql.Lit(10)));
        AssertEdited(merge, "MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET a = s.a WHERE s.a < 10");
    }

    [Fact]
    public void OnReplacesParsedConditionAndKeepsParentheses()
    {
        var merge = (MergeStatement)SqlParser.ParseStatement(
            "MERGE INTO t USING s ON (t.id = s.id) WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id)");
        merge.On(Sql.Col("t.id").Eq(Sql.Col("s.key")));
        AssertEdited(merge, "MERGE INTO t USING s ON (t.id = s.key) WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id)");
    }
}
