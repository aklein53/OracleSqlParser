using System.Diagnostics;
using OracleSqlParser;
using OracleSqlParser.Fuzzing;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Span invariants on every parse, successful or not: every node and token satisfies 0 &lt;= Start &lt;= End &lt;= source length.
/// On a successful parse every child's span also lies within its parent's span.
/// </summary>
public class SpanInvariantTests
{
    /// <summary>Number of generated inputs in the deterministic run (sized to finish well under 20 s).</summary>
    private const int FuzzInputCount = 3000;

    public static IEnumerable<object[]> CorpusCases() => CorpusSeeds.All().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void Corpus_SpansHold(string sql)
    {
        var problems = SpanProblems(SqlParser.Parse(sql));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    [Trait("Category", "Fuzz")]
    public void Deterministic_Fuzz_SpansHold()
    {
        var gen = new FuzzInputs(CorpusSeeds.All().ToList(), new Random(20260602)) { MaxChainTerms = 300, MaxSize = 600 };
        var clock = Stopwatch.StartNew();
        var problems = new List<string>();
        for (int i = 0; i < FuzzInputCount && problems.Count < 5; i++)
        {
            var c = gen.Next();
            List<string>? found = null;
            var worker = new Thread(() => found = CheckOne(c.Sql, c.Options), 1024 * 1024) { IsBackground = true };
            worker.Start();
            if (!worker.Join(TimeSpan.FromSeconds(10)))
                problems.Add("hang (no answer in 10 s): " + Show(c.Sql));
            else if (found is { Count: > 0 })
                problems.Add($"[variant {c.Variant}] {Show(c.Sql)}\n  " + string.Join("\n  ", found));
        }
        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
        Assert.True(clock.Elapsed < TimeSpan.FromMinutes(1), $"span fuzz took {clock.Elapsed}");
    }

    /// <summary>Runs the span checks for one input on the calling (1 MB stack) thread.</summary>
    private static List<string> CheckOne(string sql, ParseOptions? options)
    {
        try
        {
            return SpanProblems(SqlParser.Parse(sql, options));
        }
        catch (Exception e)
        {
            return ["parse threw " + e.GetType().Name + ": " + e.Message];
        }
    }

    /// <summary>
    /// One message per violated invariant (empty when the parse satisfies them all). Traversal is iterative
    /// (SqlNode.Descendants) so deep trees cannot overflow the stack.
    /// </summary>
    internal static List<string> SpanProblems(ParseResult r)
    {
        var problems = new List<string>();
        int len = r.Source.Length;
        foreach (var t in r.Tokens)
            if (!(0 <= t.Start && t.Start <= t.End && t.End <= len))
                problems.Add($"token {t.Type} '{t.Value}' [{t.Start},{t.End}) outside 0..{len}");

        foreach (var n in r.Script.Descendants())
        {
            var name = n.GetType().Name;
            if (!(0 <= n.Start && n.Start <= n.End && n.End <= len))
                problems.Add($"{name} [{n.Start},{n.End}) outside 0..{len} (success={r.Success})");
            if (!r.Success) continue;
            foreach (var child in n.GetChildren())
                if (child.Start < n.Start || child.End > n.End)
                    problems.Add($"{name} [{n.Start},{n.End}) does not contain child {child.GetType().Name} [{child.Start},{child.End})");
        }
        return problems;
    }

    private static string Show(string sql) => (sql.Length > 300 ? sql[..300] + "..." : sql).Replace("\n", "\\n");

    // ---- regression tests for the three fuzz-found repros ----

    [Fact]
    public void TemporaryTablespaceTempfileDrop_EmptyDatafileSpecIsZeroWidth()
    {
        const string sql = "CREATE TEMPORARY TABLESPACE tmp TEMPFILE DROP 't.dbf' SIZE 5M REUSE";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var file = Assert.Single(((CreateTablespaceStatement)r.Script.Statements[0]).Files);
        // The file spec consumed no tokens: it sits where DROP starts (offset 41), not at the end of TEMPFILE (40).
        Assert.Equal(41, file.Start);
        Assert.Equal(41, file.End);
        Assert.Empty(SpanProblems(r));
    }

    [Fact]
    public void TablespaceDatafileParens_NoNodeHasNegativeWidth()
    {
        // The fuzz repro had a long run of parentheses after DATAFILE. Check that shape across a range of lengths.
        for (int n = 1; n <= 250; n += 7)
        {
            var sql = "CREATE TABLESPACE ts DATAFILE " + new string('(', n);
            Assert.Empty(SpanProblems(SqlParser.Parse(sql)));
        }
    }

    [Fact]
    public void GrantToProcedureWithoutName_PrivilegeIsZeroWidth()
    {
        const string sql = "GRANT TO PROCEDURE /*+ hr & . p hr . q";
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success);
        var gp = Assert.Single(((GrantStatement)r.Script.Statements[0]).Privileges);
        Assert.Equal(6, gp.Start);
        Assert.Equal(6, gp.End);
        Assert.Empty(SpanProblems(r));
    }
}
