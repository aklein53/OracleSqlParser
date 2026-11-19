using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// The clause nodes as edit anchors: each one removed or replaced by its span, with the edited text checked to parse.
/// </summary>
public class SourceEditorClauseTests
{
    private static T Find<T>(ParseResult r) where T : SqlNode => r.Script.Descendants().OfType<T>().First();

    private static string Reparsed(string text)
    {
        var r = SqlParser.Parse(text);
        Assert.True(r.Success, $"edited SQL does not parse: {text}\n{string.Join("; ", r.Errors)}");
        return text;
    }

    [Fact]
    public void RemoveOffsetKeepsTheFetchClause()
    {
        var r = SqlParser.Parse("SELECT a FROM t ORDER BY a OFFSET 2 ROWS FETCH FIRST 1 ROWS ONLY");
        var editor = new SourceEditor(r);
        editor.Remove(Find<OffsetClause>(r));
        var text = Reparsed(editor.Apply());

        Assert.DoesNotContain("OFFSET", text);
        Assert.Contains("FETCH FIRST 1 ROWS ONLY", text);
    }

    [Fact]
    public void ReplaceOffsetReplacesItsKeywordAndCount()
    {
        var r = SqlParser.Parse("SELECT a FROM t OFFSET 2 ROWS");
        var editor = new SourceEditor(r);
        editor.Replace(Find<OffsetClause>(r), "OFFSET 9 ROWS");

        Assert.Equal("SELECT a FROM t OFFSET 9 ROWS", Reparsed(editor.Apply()));
    }

    [Fact]
    public void RemoveWithClauseDropsTheCtes()
    {
        var r = SqlParser.Parse("WITH x AS (SELECT 1 n FROM dual) SELECT n FROM dual");
        var editor = new SourceEditor(r);
        editor.Remove(Find<WithClause>(r));
        var text = Reparsed(editor.Apply());

        Assert.DoesNotContain("WITH", text);
        Assert.Contains("SELECT n FROM dual", text);
    }

    [Fact]
    public void ReplaceWithClauseSwapsTheCteDefinition()
    {
        var r = SqlParser.Parse("WITH x AS (SELECT 1 n FROM dual) SELECT n FROM x");
        var editor = new SourceEditor(r);
        editor.Replace(Find<WithClause>(r), "WITH x AS (SELECT 2 n FROM dual)");

        Assert.Equal("WITH x AS (SELECT 2 n FROM dual) SELECT n FROM x", Reparsed(editor.Apply()));
    }

    [Fact]
    public void RemoveGroupByItemRemovesItsCommaToo()
    {
        var r = SqlParser.Parse("SELECT a, b, COUNT(*) FROM t GROUP BY a, b");
        var editor = new SourceEditor(r);
        editor.Remove(Find<GroupByClause>(r).Items[0]);
        var text = Reparsed(editor.Apply());

        Assert.Contains("GROUP BY b", text);
        Assert.DoesNotContain("GROUP BY a", text);
    }

    [Fact]
    public void ReplaceGroupByItemWithARollup()
    {
        var r = SqlParser.Parse("SELECT a, b, COUNT(*) FROM t GROUP BY a, b");
        var editor = new SourceEditor(r);
        editor.Replace(Find<GroupByClause>(r).Items[1], "ROLLUP(b)");

        Assert.Equal("SELECT a, b, COUNT(*) FROM t GROUP BY a, ROLLUP(b)", Reparsed(editor.Apply()));
    }

    [Fact]
    public void RemoveIntoClauseLeavesTheSelect()
    {
        var r = SqlParser.Parse("BEGIN SELECT a INTO x FROM t; END;");
        var editor = new SourceEditor(r);
        editor.Remove(Find<IntoClause>(r));
        var text = Reparsed(editor.Apply());

        Assert.DoesNotContain("INTO", text);
        Assert.Equal("BEGIN SELECT a  FROM t; END;", text); // the removed span leaves its surrounding spaces, as the editor does elsewhere
    }

    [Fact]
    public void ReplaceBulkIntoClause()
    {
        var r = SqlParser.Parse("BEGIN SELECT a INTO x FROM t; END;");
        var editor = new SourceEditor(r);
        editor.Replace(Find<IntoClause>(r), "BULK COLLECT INTO x");

        Assert.Equal("BEGIN SELECT a BULK COLLECT INTO x FROM t; END;", Reparsed(editor.Apply()));
    }

    /// <summary>A query with a WITH, an INTO and an OFFSET/FETCH: AndWhere adds the predicate and keeps every clause.</summary>
    [Fact]
    public void AndWhereOnAQueryWithWithIntoAndOffsetFetchKeepsThemAll()
    {
        const string sql = "BEGIN WITH c AS (SELECT a FROM t) SELECT a INTO v FROM c OFFSET 1 ROW FETCH FIRST 2 ROWS ONLY; END;";
        var r = SqlParser.Parse(sql);
        var editor = new SourceEditor(r);
        editor.AndWhere(r.Script.Descendants().OfType<QuerySpecification>().Single(q => q.Into is not null), SqlParser.ParseExpression("a > 1"));
        var text = Reparsed(editor.Apply());

        Assert.Contains("WITH c AS (SELECT a FROM t)", text);
        Assert.Contains("INTO v", text);
        Assert.Contains("WHERE a > 1", text);
        Assert.Contains("OFFSET 1 ROW", text);
        Assert.Contains("FETCH FIRST 2 ROWS ONLY", text);
    }

    [Fact]
    public void AndWhereWithAnExistingWhereOnAQueryWithWithAndInto()
    {
        const string sql = "BEGIN WITH c AS (SELECT a FROM t) SELECT a INTO v FROM c WHERE a > 1 OFFSET 1 ROWS; END;";
        var r = SqlParser.Parse(sql);
        var editor = new SourceEditor(r);
        editor.AndWhere(r.Script.Descendants().OfType<QuerySpecification>().Single(q => q.Into is not null), SqlParser.ParseExpression("a < 9"));
        var text = Reparsed(editor.Apply());

        Assert.Contains("WHERE (a > 1) AND (a < 9)", text);
        Assert.Contains("OFFSET 1 ROWS", text);
        Assert.Contains("INTO v", text);
    }
}
