using System.Collections;
using System.Runtime.ExceptionServices;

namespace OracleSqlParser;

/// <summary>
/// Measurements and deep-tree support. The parser accepts left-deep chains of any length (a 20,000-term AND, a 10,000-join
/// FROM), and several recursive algorithms (printing, cloning, analysis, generation) descend once per level. Public
/// entry points of those algorithms call <see cref="RunForDepth{T}"/>, so a deep tree runs on a thread whose stack is sized
/// for its depth instead of overflowing the default stack. Shallow trees run inline.
/// </summary>
internal static class TreeShape
{
    /// <summary>Trees up to this many levels run on the caller's thread.</summary>
    private const int InlineDepth = 256;

    /// <summary>Stack reserved per tree level on a deep run (a few frames of a few hundred bytes each, with margin).</summary>
    private const int StackBytesPerLevel = 4 * 1024;

    /// <summary>The largest stack a deep run reserves. Address space only: pages are committed as they are used.</summary>
    private const long MaxStackBytes = 1L << 30;

    /// <summary>True on a thread started by <see cref="RunForDepth{T}"/>, so nested calls do not start more threads.</summary>
    [ThreadStatic] private static bool _onDeepStack;

    /// <summary>The number of nested nodes on the longest path from <paramref name="root"/> (1 for a leaf). Iterative.</summary>
    public static int NestingDepth(SqlNode root)
    {
        int max = 0;
        var stack = new Stack<(object? Value, int Depth)>();
        stack.Push((root, 1));
        while (stack.Count > 0)
        {
            var (value, depth) = stack.Pop();
            switch (value)
            {
                case SqlNode n:
                    max = Math.Max(max, depth);
                    foreach (var p in n.NodeProperties()) stack.Push((p.Value, depth + 1));
                    break;
                case string:
                    break;
                case IEnumerable e:
                    foreach (var item in e) stack.Push((item, depth));
                    break;
            }
        }
        return max;
    }

    /// <summary>
    /// Runs <paramref name="work"/>, which recurses once per level of <paramref name="root"/>. A deep tree is run on a new
    /// thread with a stack of <see cref="StackBytesPerLevel"/> per level; any exception is rethrown on the caller.
    /// </summary>
    public static T RunForDepth<T>(SqlNode root, Func<T> work)
    {
        if (_onDeepStack) return work();
        int depth = NestingDepth(root);
        if (depth <= InlineDepth) return work();

        long bytes = Math.Min(MaxStackBytes, (long)depth * StackBytesPerLevel);
        T? result = default;
        Exception? error = null;
        var worker = new Thread(() =>
        {
            _onDeepStack = true;
            try { result = work(); }
            catch (Exception e) { error = e; }
        }, (int)bytes);
        worker.Start();
        worker.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }
}
