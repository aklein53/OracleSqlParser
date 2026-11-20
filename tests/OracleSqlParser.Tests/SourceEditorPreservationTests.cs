using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// SourceEditor keeps every byte it does not edit. For corpus inputs, seeded random nodes (identifiers, expressions and
/// clauses) are replaced or removed, and the text outside the edited ranges is checked byte for byte.
/// </summary>
public class SourceEditorPreservationTests
{
    private const int PerCategory = 2;

    public static IEnumerable<object[]> CorpusCases() => CorpusSeeds.All().Select(s => new object[] { s });

    /// <summary>A stable seed derived from the input text, so each input gets the same random choices on every run.</summary>
    private static int SeedOf(string sql)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in sql) h = (h ^ c) * 16777619;
            return (int)(h & 0x7fffffff);
        }
    }

    /// <summary>Nodes that can be edited: they have a source position and a non-empty range.</summary>
    private static List<SqlNode> EditableNodes(ParseResult r) =>
        r.Script.Descendants()
            .Where(n => n.FirstTokenIndex >= 0 && n.FirstTokenIndex <= n.LastTokenIndex && n.Start < n.End)
            .ToList();

    /// <summary>Up to <see cref="PerCategory"/> nodes of each kind (identifiers, expressions, clauses), chosen with the seeded generator.</summary>
    private static List<SqlNode> PickNodes(List<SqlNode> nodes, Random rng)
    {
        var picked = new List<SqlNode>();
        var categories = new[]
        {
            nodes.Where(n => n is Identifier).ToList(),
            nodes.Where(n => n is Expression).ToList(),
            nodes.Where(n => n.GetType().Name.EndsWith("Clause", StringComparison.Ordinal)).ToList(),
        };
        foreach (var cat in categories)
        {
            var pool = cat.ToList();
            for (int k = 0; k < PerCategory && pool.Count > 0; k++)
            {
                int i = rng.Next(pool.Count);
                picked.Add(pool[i]);
                pool.RemoveAt(i);
            }
        }
        return picked;
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void Replace_KeepsTextOutsideTheEditedRange(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var rng = new Random(SeedOf(sql));
        foreach (var node in PickNodes(EditableNodes(r), rng))
        {
            var editor = new SourceEditor(r);
            editor.Replace(node, "X");
            var expected = sql[..node.Start] + "X" + sql[node.End..];
            Assert.Equal(expected, editor.Apply());
        }
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void ReplaceTwo_KeepsTextOutsideBothRanges(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var rng = new Random(SeedOf(sql) + 1);
        var picked = PickNodes(EditableNodes(r), rng)
            .OrderBy(n => n.Start).ThenBy(n => n.End).ToList();
        // Two nodes that do not nest or overlap: the first one that ends before the next one starts.
        for (int i = 0; i + 1 < picked.Count; i++)
        {
            var a = picked[i];
            var b = picked[i + 1];
            if (a.End > b.Start) continue;
            var editor = new SourceEditor(r);
            editor.Replace(a, "X");
            editor.Replace(b, "Y");
            var expected = sql[..a.Start] + "X" + sql[a.End..b.Start] + "Y" + sql[b.End..];
            Assert.Equal(expected, editor.Apply());
            return;
        }
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void Remove_KeepsTextOutsideTheRemovedRange(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        var rng = new Random(SeedOf(sql) + 2);
        foreach (var node in PickNodes(EditableNodes(r), rng))
        {
            var editor = new SourceEditor(r);
            editor.Remove(node);
            var output = editor.Apply();
            Assert.True(IsOneRangeRemoved(sql, output, node.Start, node.End),
                $"removing {node.GetType().Name} [{node.Start},{node.End}) changed text outside a single removed range");
        }
    }

    /// <summary>
    /// True when <paramref name="output"/> is <paramref name="sql"/> with one contiguous range deleted, and that range
    /// covers <c>[start, end)</c>. The candidate deletions are bounded by the common prefix and suffix, so no
    /// quadratic search is needed.
    /// </summary>
    private static bool IsOneRangeRemoved(string sql, string output, int start, int end)
    {
        int d = sql.Length - output.Length;
        if (d < 0) return false;
        int prefix = 0;
        while (prefix < output.Length && sql[prefix] == output[prefix]) prefix++;
        int suffix = 0;
        while (suffix < output.Length && sql[sql.Length - 1 - suffix] == output[output.Length - 1 - suffix]) suffix++;
        // A deletion at offset i (length d) is valid when i <= prefix and the text after it lines up with the suffix.
        int lo = Math.Max(0, sql.Length - suffix - d);
        int hi = Math.Min(prefix, sql.Length - d);
        if (lo > hi) return false;
        // Covering [start, end) needs i <= start and i + d >= end.
        int needLo = Math.Max(lo, end - d), needHi = Math.Min(hi, start);
        if (needLo > needHi) return false;
        return sql[..needLo] + sql[(needLo + d)..] == output;
    }
}
