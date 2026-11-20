using System.Collections;
using System.Reflection;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Traversal covers the tree: GetChildren returns every child node, root.Accept(visitor) reaches every node, and every
/// concrete node type has its visitor and rewriter methods. Corpus inputs only (no fuzz), so the recursive visitor is safe.
/// </summary>
public class TraversalCompletenessTests
{
    public static IEnumerable<object[]> CorpusCases() => CorpusSeeds.All().Select(s => new object[] { s });

    /// <summary>Records every node the visitor is called for, by reference.</summary>
    private sealed class CountingVisitor : SqlNodeVisitor
    {
        public readonly List<SqlNode> Seen = [];
        public override void Visit(SqlNode node) => Seen.Add(node);
    }

    /// <summary>Every SqlNode reachable through the public instance properties of <paramref name="node"/>, flattening lists.</summary>
    private static List<SqlNode> PropertyNodes(SqlNode node)
    {
        var found = new List<SqlNode>();
        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var value = p.GetValue(node);
            AddNodes(value, found);
        }
        return found;
    }

    private static void AddNodes(object? value, List<SqlNode> found)
    {
        switch (value)
        {
            case null or string: return;
            case SqlNode n: found.Add(n); return;
            case IEnumerable items:
                foreach (var item in items) AddNodes(item, found);
                return;
        }
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void GetChildren_ReturnsEveryNodeProperty(string sql)
    {
        var r = SqlParser.Parse(sql);
        foreach (var node in r.Script.Descendants())
        {
            var children = new HashSet<SqlNode>(node.GetChildren(), ReferenceEqualityComparer.Instance);
            foreach (var value in PropertyNodes(node))
                Assert.True(children.Contains(value),
                    $"{node.GetType().Name} has a {value.GetType().Name} property value that GetChildren() does not return");
        }
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void Accept_ReachesEveryNode(string sql)
    {
        var r = SqlParser.Parse(sql);
        var expected = r.Script.Descendants().ToList();
        var counter = new CountingVisitor();
        // Descendants() includes the root, so the visitor (started at the root) must see the same nodes, each once.
        r.Script.Accept(counter);
        Assert.Equal(expected.Count, counter.Seen.Count);
        Assert.Equal(expected.Count, counter.Seen.Distinct(ReferenceEqualityComparer.Instance).Count());
        var reached = new HashSet<SqlNode>(counter.Seen, ReferenceEqualityComparer.Instance);
        Assert.All(expected, n => Assert.Contains(n, reached));
    }

    [Fact]
    public void EveryConcreteNodeType_HasVisitorAndRewriterMethods()
    {
        var asm = typeof(SqlNode).Assembly;
        var nodeTypes = asm.GetTypes()
            .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && t.IsSubclassOf(typeof(SqlNode)))
            .ToList();
        Assert.True(nodeTypes.Count > 100, $"expected the full node set, found {nodeTypes.Count}");

        var problems = new List<string>();
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var t in nodeTypes)
        {
            if (!HasMethod(typeof(SqlNodeVisitor), "Visit", t, declared)) problems.Add($"{t.Name}: no SqlNodeVisitor.Visit({t.Name})");
            if (!HasMethod(typeof(SqlNodeVisitor), "ExplicitVisit", t, declared)) problems.Add($"{t.Name}: no SqlNodeVisitor.ExplicitVisit({t.Name})");
            if (!HasMethod(typeof(SqlNodeRewriter), "Rewrite", t, declared)) problems.Add($"{t.Name}: no SqlNodeRewriter.Rewrite({t.Name})");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static bool HasMethod(Type owner, string name, Type param, BindingFlags flags) =>
        owner.GetMethods(flags).Any(m => m.Name == name && m.GetParameters() is [{ ParameterType: var p }] && p == param);
}
