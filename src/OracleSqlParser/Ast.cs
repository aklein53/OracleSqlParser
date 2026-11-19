namespace OracleSqlParser;

/// <summary>Marks a property that records source spelling only (not meaning); <see cref="AstPrinter"/> leaves it out so shapes compare semantically.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed partial class SpellingOnlyAttribute : Attribute { }

/// <summary>
/// Base class of every AST node. Start/End are character offsets into the source text; Line/Column are 1-based.
/// Nodes created in code (not by the parser) have zero spans; the span setters are internal.
/// Parsing (<see cref="SqlParser"/>) is thread-safe: every call uses its own state. Trees are mutable and
/// are NOT thread-safe; do not share a tree between threads while any of them may modify it.
/// </summary>
/// <remarks>
/// The per-type members (<c>GetChildren</c>, <c>Accept</c>, <c>AcceptChildren</c>, rewriting, cloning and comparison)
/// are generated into <c>Generated/*.g.cs</c> by <c>tools/AstGen</c>; the virtual members below are only the
/// fallbacks that let the library compile while that code is being (re)generated.
/// </remarks>
public abstract partial class SqlNode
{
    /// <summary>Character offset of the first character of this node in the source text.</summary>
    public int Start { get; internal set; }
    /// <summary>Character offset just past the last character of this node.</summary>
    public int End { get; internal set; }
    /// <summary>1-based line of <see cref="Start"/>.</summary>
    public int Line { get; internal set; }
    /// <summary>1-based column of <see cref="Start"/>.</summary>
    public int Column { get; internal set; }
    /// <summary>
    /// Index into <c>ParseResult.Tokens</c> of the first token this node covers, or -1 when the node was built in code
    /// (or came from a parse that has no token stream). <c>Tokens[FirstTokenIndex].Start == Start</c> when set.
    /// </summary>
    public int FirstTokenIndex { get; internal set; } = -1;
    /// <summary>
    /// Index into <c>ParseResult.Tokens</c> of the last token this node covers, or -1 (see <see cref="FirstTokenIndex"/>).
    /// <c>Tokens[LastTokenIndex].End == End</c> when set.
    /// </summary>
    public int LastTokenIndex { get; internal set; } = -1;

    /// <summary>Direct child nodes, in property declaration order (nested lists are flattened). Generated per node type; no reflection.</summary>
    public virtual IEnumerable<SqlNode> GetChildren() => [];

    /// <summary>Calls <c>visitor.ExplicitVisit(this)</c> (the typed overload for this node's concrete type).</summary>
    public virtual void Accept(SqlNodeVisitor visitor) => visitor.Visit(this);

    /// <summary>Accepts <paramref name="visitor"/> on every direct child, in declaration order.</summary>
    public virtual void AcceptChildren(SqlNodeVisitor visitor) { }

    internal virtual SqlNode? AcceptRewriter(SqlNodeRewriter rewriter) => rewriter.Rewrite(this);

    internal virtual void RewriteChildren(SqlNodeRewriter rewriter) { }

    internal virtual SqlNode CloneCore() => (SqlNode)MemberwiseClone();

    /// <summary>The node's own properties (not the span members) in declaration order; generated per node type.</summary>
    internal virtual NodeProperty[] NodeProperties() => [];

    internal virtual bool ShapeEquals(SqlNode other, Stack<(SqlNode, SqlNode)> pending) => true;

    /// <summary>Deep copy of this node and everything below it (scalars, spelling-only text, spans and children). A deep tree is copied on a thread with a stack sized for its depth.</summary>
    public SqlNode DeepClone() => TreeShape.RunForDepth(this, CloneCore);

    /// <summary>Deep copy typed as <typeparamref name="TNode"/>. Throws <see cref="InvalidCastException"/> if this node is not a <typeparamref name="TNode"/>.</summary>
    public TNode DeepClone<TNode>() where TNode : SqlNode => (TNode)DeepClone();

    /// <summary>This node and all descendants, depth-first pre-order. Uses an explicit stack (safe on very deep trees).</summary>
    public IEnumerable<SqlNode> Descendants()
    {
        var stack = new Stack<SqlNode>();
        stack.Push(this);
        var tmp = new List<SqlNode>();
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            tmp.Clear();
            tmp.AddRange(n.GetChildren());
            for (int i = tmp.Count - 1; i >= 0; i--) stack.Push(tmp[i]);
        }
    }

    // ---- helpers called by generated code ----

    internal static void CopySpan(SqlNode from, SqlNode to)
    {
        to.Start = from.Start; to.End = from.End; to.Line = from.Line; to.Column = from.Column;
        to.FirstTokenIndex = from.FirstTokenIndex; to.LastTokenIndex = from.LastTokenIndex;
    }

    internal static T? CloneChild<T>(T? node) where T : SqlNode => node is null ? null : (T)node.CloneCore();

    internal static List<T> CloneList<T>(List<T>? list) where T : SqlNode
    {
        if (list is null) return null!;
        var r = new List<T>(list.Count);
        foreach (var n in list) r.Add(CloneChild(n)!);
        return r;
    }

    internal static List<List<T>> CloneNested<T>(List<List<T>>? list) where T : SqlNode
    {
        if (list is null) return null!;
        var r = new List<List<T>>(list.Count);
        foreach (var l in list) r.Add(CloneList(l));
        return r;
    }

    internal static List<string> CloneStrings(List<string>? list) => list is null ? null! : [.. list];

    internal static bool SameStrings(List<string>? a, List<string>? b) =>
        a is null || b is null ? a is null && b is null : a.SequenceEqual(b);

    internal static bool Pair(SqlNode? a, SqlNode? b, Stack<(SqlNode, SqlNode)> pending)
    {
        if (a is null || b is null) return a is null && b is null;
        pending.Push((a, b));
        return true;
    }

    internal static bool PairList<T>(List<T>? a, List<T>? b, Stack<(SqlNode, SqlNode)> pending) where T : SqlNode
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!Pair(a[i], b[i], pending)) return false;
        return true;
    }

    internal static bool PairNested<T>(List<List<T>>? a, List<List<T>>? b, Stack<(SqlNode, SqlNode)> pending) where T : SqlNode
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!PairList(a[i], b[i], pending)) return false;
        return true;
    }
}

// ---------- Script & statements ----------

/// <summary>A parsed script: the statements in source order.</summary>
public sealed partial class SqlScript : SqlNode
{
    /// <summary>The statements, in source order; empty for an empty script.</summary>
    public List<Statement> Statements { get; set; } = [];
}

/// <summary>A statement: the base of every SQL and PL/SQL statement node.</summary>
public abstract partial class Statement : SqlNode
{
    /// <summary>PL/SQL labels (&lt;&lt;name&gt;&gt;) attached to this statement, in source order; quoted labels keep their quotes.</summary>
    public List<Identifier> Labels { get; set; } = [];
}

/// <summary>INSERT / UPDATE / DELETE / MERGE; carries an optional optimizer hint.</summary>
public abstract partial class DmlStatement : Statement
{
    /// <summary>The optimizer hint comment after the keyword, such as <c>/*+ APPEND */</c>, or null when absent.</summary>
    public HintClause? Hint { get; set; }
    /// <summary>The <c>RETURNING</c> clause, such as <c>RETURNING a, b INTO x, y</c>, or null when absent.</summary>
    public ReturningClause? Returning { get; set; }
    /// <summary>The <c>LOG ERRORS</c> clause, or null when absent.</summary>
    public ErrorLoggingClause? ErrorLogging { get; set; }
}

/// <summary>
/// The <c>RETURNING</c> clause of a DML statement: the returned items and the <c>INTO</c> that receives them.
/// </summary>
/// <remarks>
/// Spans the <c>RETURNING</c> (or <c>RETURN</c>) keyword through the last target of <see cref="Into"/>; see
/// <see cref="FromClause"/>. <see cref="IntoClause"/> keeps its own span, from <c>INTO</c> or <c>BULK</c>.
/// </remarks>
public sealed partial class ReturningClause : SqlNode
{
    /// <summary>The returned items, in source order.</summary>
    public required List<ReturningItem> Items { get; set; }
    /// <summary>
    /// The <c>INTO</c> clause that receives the returned values. Required: Oracle needs the <c>INTO</c> after a DML
    /// <c>RETURNING</c> list, so a RETURNING clause always has one.
    /// </summary>
    public required IntoClause Into { get; set; }
}

/// <summary>One item of a RETURNING list: a column or expression, optionally qualified with the row image OLD or NEW (23ai).</summary>
public sealed partial class ReturningItem : SqlNode
{
    /// <summary>Null when neither OLD nor NEW is written: the value after the change is returned.</summary>
    public ReturningImage? Image { get; set; }
    /// <summary>The column or expression returned.</summary>
    public required Expression Expression { get; set; }
}

/// <summary>The row image named by a <c>RETURNING OLD</c> or <c>RETURNING NEW</c> item.</summary>
public enum ReturningImage
{
    /// <summary><c>OLD</c>: the row as it was before the change.</summary>
    Old,
    /// <summary><c>NEW</c>: the row as it is after the change.</summary>
    New,
}

/// <summary>A <c>LOG ERRORS [INTO t] [('tag')] [REJECT LIMIT n]</c> clause, which sends row errors of a DML statement to an error table.</summary>
public sealed partial class ErrorLoggingClause : SqlNode
{
    /// <summary>The error table name parts from <c>INTO</c>; empty when absent.</summary>
    public List<Identifier> Table { get; set; } = [];
    /// <summary>The tag expression in parentheses, or null when absent.</summary>
    public Expression? Tag { get; set; }
    /// <summary>The number after <c>REJECT LIMIT</c>, or null when absent.</summary>
    public Expression? RejectLimit { get; set; }
    /// <summary>True for <c>REJECT LIMIT UNLIMITED</c>.</summary>
    public bool RejectUnlimited { get; set; }
}

/// <summary>A full query: [WITH] body [ORDER BY] [OFFSET] [FETCH] [FOR UPDATE]. Also used for subqueries.</summary>
public sealed partial class SelectStatement : Statement
{
    /// <summary>The <c>WITH</c> clause, with its PL/SQL declarations and common table expressions; null when absent.</summary>
    public WithClause? With { get; set; }
    /// <summary>The query body: a query specification, a set operation, a <c>VALUES</c> list or a parenthesized query.</summary>
    public required QueryExpression Body { get; set; }
    /// <summary>The <c>ORDER BY</c> clause, or null when absent.</summary>
    public OrderByClause? OrderBy { get; set; }
    /// <summary>The <c>OFFSET</c> clause, or null when absent.</summary>
    public OffsetClause? Offset { get; set; }
    /// <summary>The <c>FETCH</c> clause, or null when absent.</summary>
    public FetchClause? Fetch { get; set; }
    /// <summary>The <c>FOR UPDATE</c> clause, or null when absent.</summary>
    public ForUpdateClause? ForUpdate { get; set; }
}

/// <summary>
/// The <c>WITH</c> clause of a query: 12c <c>WITH FUNCTION</c> / <c>WITH PROCEDURE</c> declarations, then common table
/// expressions. Either list may be empty, but not both.
/// </summary>
/// <remarks>
/// Spans the <c>WITH</c> keyword through the end of the last item: the last CTE, or, when there are no CTEs, the last
/// declaration's <c>;</c>. See <see cref="FromClause"/>.
/// </remarks>
public sealed partial class WithClause : SqlNode
{
    /// <summary>The <c>WITH FUNCTION</c> and <c>WITH PROCEDURE</c> declarations, in source order; empty when absent.</summary>
    public List<Declaration> Declarations { get; set; } = [];
    /// <summary>The common table expressions, in source order; empty when absent.</summary>
    public List<CommonTableExpression> Ctes { get; set; } = [];
}

/// <summary>One named query of a <c>WITH</c> clause, such as <c>x (a) AS (SELECT 1 FROM dual)</c>.</summary>
public sealed partial class CommonTableExpression : SqlNode
{
    /// <summary>The name of the common table expression.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The column aliases in parentheses after the name; empty when absent.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The query that defines the common table expression.</summary>
    public required SelectStatement Query { get; set; }
    /// <summary>The <c>SEARCH</c> clause of a recursive expression, or null when absent.</summary>
    public SearchClause? Search { get; set; }
    /// <summary>The <c>CYCLE</c> clause of a recursive expression, or null when absent.</summary>
    public CycleClause? Cycle { get; set; }
}

/// <summary>A <c>SEARCH DEPTH FIRST BY</c> or <c>SEARCH BREADTH FIRST BY</c> clause, such as <c>SEARCH DEPTH FIRST BY id SET ord</c>.</summary>
public sealed partial class SearchClause : SqlNode
{
    /// <summary>True for <c>DEPTH FIRST</c>; false for <c>BREADTH FIRST</c>.</summary>
    public bool DepthFirst { get; set; }
    /// <summary>The ordering items after <c>BY</c>, in source order.</summary>
    public List<OrderByItem> By { get; set; } = [];
    /// <summary>The pseudo column that receives the sort sequence after <c>SET</c>.</summary>
    public required Identifier SetColumn { get; set; }
}

/// <summary>A <c>CYCLE</c> clause of a recursive expression, such as <c>CYCLE id SET is_cycle TO 'Y' DEFAULT 'N'</c>.</summary>
public sealed partial class CycleClause : SqlNode
{
    /// <summary>The columns that identify a row when detecting a cycle.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The pseudo column that records whether a row closes a cycle.</summary>
    public required Identifier SetColumn { get; set; }
    /// <summary>The value after <c>TO</c> for a row that closes a cycle, or null when absent.</summary>
    public Expression? CycleValue { get; set; }
    /// <summary>The value after <c>DEFAULT</c> for a row that does not, or null when absent.</summary>
    public Expression? DefaultValue { get; set; }
}

/// <summary>A query body: a query specification, a set operation, a <c>VALUES</c> list or a parenthesized query.</summary>
public abstract partial class QueryExpression : SqlNode { }

/// <summary>A <c>SELECT</c> block, such as <c>SELECT a FROM t WHERE b = 1</c>.</summary>
public sealed partial class QuerySpecification : QueryExpression
{
    /// <summary>The optimizer hint after <c>SELECT</c>, or null when absent.</summary>
    public HintClause? Hint { get; set; }
    /// <summary>The <c>ALL</c>, <c>DISTINCT</c> or <c>UNIQUE</c> keyword after <c>SELECT</c>, or null when absent.</summary>
    public QuantifierKind? Quantifier { get; set; }
    /// <summary>The select items, in source order.</summary>
    public List<SelectItem> SelectList { get; set; } = [];
    /// <summary>The <c>FROM</c> clause, or null when absent (Oracle 23c allows a <c>SELECT</c> without one).</summary>
    public FromClause? From { get; set; }
    /// <summary>The <c>WHERE</c> clause, or null when absent.</summary>
    public WhereClause? Where { get; set; }
    /// <summary>The <c>START WITH</c> clause of a hierarchical query, or null when absent.</summary>
    public StartWithClause? StartWith { get; set; }
    /// <summary>The <c>CONNECT BY</c> clause, or null when absent.</summary>
    public ConnectByClause? ConnectBy { get; set; }
    /// <summary>The <c>GROUP BY</c> clause, or null when absent.</summary>
    public GroupByClause? GroupBy { get; set; }
    /// <summary>The <c>HAVING</c> clause, or null when absent.</summary>
    public HavingClause? Having { get; set; }
    /// <summary>The <c>INTO</c> clause of <c>SELECT ... INTO</c> in a PL/SQL block, or null when absent.</summary>
    public IntoClause? Into { get; set; }
    /// <summary>The <c>MODEL</c> clause, or null when absent.</summary>
    public ModelClause? Model { get; set; }
}

/// <summary>A set quantifier, such as <c>DISTINCT</c> after <c>SELECT</c> or inside an aggregate.</summary>
public enum QuantifierKind
{
    /// <summary><c>ALL</c>: keep duplicate rows.</summary>
    All,
    /// <summary><c>DISTINCT</c>: remove duplicate rows.</summary>
    Distinct,
    /// <summary><c>UNIQUE</c>: a synonym of <c>DISTINCT</c>.</summary>
    Unique,
}

/// <summary>Two queries combined by a set operator, such as <c>SELECT a FROM t UNION ALL SELECT b FROM u</c>.</summary>
public sealed partial class SetOperation : QueryExpression
{
    /// <summary>The query on the left of the operator.</summary>
    public required QueryExpression Left { get; set; }
    /// <summary>The set operator.</summary>
    public SetOperator Operator { get; set; }
    /// <summary>True when <c>ALL</c> is written, keeping duplicate rows.</summary>
    public bool All { get; set; }
    /// <summary>The query on the right of the operator.</summary>
    public required QueryExpression Right { get; set; }
}

/// <summary>The operator of a set operation.</summary>
public enum SetOperator
{
    /// <summary><c>UNION</c>.</summary>
    Union,
    /// <summary><c>INTERSECT</c>.</summary>
    Intersect,
    /// <summary><c>MINUS</c>, Oracle's name for <c>EXCEPT</c>.</summary>
    Minus,
}

/// <summary>A query in parentheses, such as <c>(SELECT 1 FROM dual)</c>, used as a query body.</summary>
public sealed partial class ParenthesizedQuery : QueryExpression
{
    /// <summary>The query inside the parentheses.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>One item of a <c>SELECT</c> list.</summary>
public abstract partial class SelectItem : SqlNode { }

/// <summary>A column or expression in a <c>SELECT</c> list, such as <c>sal * 12 AS annual</c>.</summary>
public sealed partial class ExpressionSelectItem : SelectItem
{
    /// <summary>The expression that produces the value.</summary>
    public required Expression Expression { get; set; }
    /// <summary>The alias after the expression, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

/// <summary><c>*</c> or <c>qualifier.*</c> in a <c>SELECT</c> list.</summary>
public sealed partial class StarSelectItem : SelectItem
{
    /// <summary>The name parts before <c>.*</c>; empty for a plain <c>*</c>.</summary>
    public List<Identifier> Qualifier { get; set; } = [];
}

/// <summary>
/// The <c>FROM</c> clause of a query or of an <c>UPDATE</c> / <c>DELETE</c>, such as <c>FROM a JOIN b ON a.x = b.x</c>.
/// </summary>
/// <remarks>
/// A clause node spans its keyword, which the table references alone do not: <c>Tables[0].Start</c> is the first table,
/// while this node's <see cref="SqlNode.Start"/> is the <c>FROM</c>. That is what lets a caller cut the clause out of
/// the source, or replace it, without hunting for the keyword in the token stream.
/// </remarks>
public sealed partial class FromClause : SqlNode
{
    /// <summary>The table references, in source order.</summary>
    public List<TableReference> Tables { get; set; } = [];
}

/// <summary>The <c>WHERE</c> clause of a query or of an <c>UPDATE</c> / <c>DELETE</c>, such as <c>WHERE a = 1</c>.</summary>
/// <remarks>Spans the <c>WHERE</c> keyword; see <see cref="FromClause"/>.</remarks>
public sealed partial class WhereClause : SqlNode
{
    /// <summary>The search condition.</summary>
    public required Expression Condition { get; set; }
}

/// <summary>The <c>START WITH</c> clause of a hierarchical query, such as <c>START WITH mgr IS NULL</c>.</summary>
/// <remarks>Spans the <c>START</c> keyword; see <see cref="FromClause"/>.</remarks>
public sealed partial class StartWithClause : SqlNode
{
    /// <summary>The condition that selects the root rows.</summary>
    public required Expression Condition { get; set; }
}

/// <summary>The <c>CONNECT BY</c> clause of a hierarchical query, such as <c>CONNECT BY NOCYCLE PRIOR id = mgr</c>.</summary>
/// <remarks>Spans the <c>CONNECT</c> keyword; see <see cref="FromClause"/>.</remarks>
public sealed partial class ConnectByClause : SqlNode
{
    /// <summary>True when <c>NOCYCLE</c> is written.</summary>
    public bool NoCycle { get; set; }
    /// <summary>The condition that joins a row to its parent.</summary>
    public required Expression Condition { get; set; }
}

/// <summary>The <c>GROUP BY</c> clause, such as <c>GROUP BY a, ROLLUP(b, c)</c>.</summary>
/// <remarks>Spans the <c>GROUP</c> keyword; see <see cref="FromClause"/>.</remarks>
public sealed partial class GroupByClause : SqlNode
{
    /// <summary>The grouping elements, in source order: expressions, composites, <c>()</c>, <c>ROLLUP</c>, <c>CUBE</c> or <c>GROUPING SETS</c>.</summary>
    public List<GroupingSpec> Items { get; set; } = [];
}

/// <summary>
/// One element of a <c>GROUP BY</c> list, or an item inside <c>ROLLUP</c>, <c>CUBE</c> or <c>GROUPING SETS</c>. The concrete
/// kinds are <see cref="ExpressionGrouping"/>, <see cref="CompositeGrouping"/>, <see cref="GrandTotalGrouping"/>,
/// <see cref="RollupGrouping"/>, <see cref="CubeGrouping"/> and <see cref="GroupingSetsGrouping"/>.
/// </summary>
/// <remarks>Each kind spans the text it was written as, as ScriptDom's grouping specifications do; see <see cref="FromClause"/>.</remarks>
public abstract partial class GroupingSpec : SqlNode { }

/// <summary>A single grouping expression, such as <c>deptno</c> or <c>(deptno)</c> in <c>GROUP BY deptno</c>.</summary>
public sealed partial class ExpressionGrouping : GroupingSpec
{
    /// <summary>The grouping expression. A parenthesized single expression keeps its <see cref="ParenthesizedExpression"/>.</summary>
    public required Expression Expression { get; set; }
}

/// <summary>
/// A parenthesized list of two or more expressions, such as <c>(a, b)</c>: its items group together as one set.
/// </summary>
/// <remarks>
/// Oracle takes only expressions inside it: a nested list, <c>()</c>, or a <c>ROLLUP</c>, <c>CUBE</c> or <c>GROUPING SETS</c>
/// call is a parse error. A single parenthesized expression, <c>(a)</c>, is an <see cref="ExpressionGrouping"/>.
/// </remarks>
public sealed partial class CompositeGrouping : GroupingSpec
{
    /// <summary>The expressions of the list, in source order.</summary>
    public List<GroupingSpec> Items { get; set; } = [];
}

/// <summary>The empty grouping set <c>()</c>, which groups every row into one group (the grand total).</summary>
public sealed partial class GrandTotalGrouping : GroupingSpec { }

/// <summary><c>ROLLUP (items)</c>, such as <c>ROLLUP(a, (b, c))</c>.</summary>
public sealed partial class RollupGrouping : GroupingSpec
{
    /// <summary>The items in the parentheses, each an expression or a <see cref="CompositeGrouping"/>; at least one.</summary>
    public List<GroupingSpec> Items { get; set; } = [];
}

/// <summary><c>CUBE (items)</c>, such as <c>CUBE(a, (b, c))</c>.</summary>
public sealed partial class CubeGrouping : GroupingSpec
{
    /// <summary>The items in the parentheses, each an expression or a <see cref="CompositeGrouping"/>; at least one.</summary>
    public List<GroupingSpec> Items { get; set; } = [];
}

/// <summary><c>GROUPING SETS (sets)</c>, such as <c>GROUPING SETS((a, b), ROLLUP(c), ())</c>.</summary>
public sealed partial class GroupingSetsGrouping : GroupingSpec
{
    /// <summary>The grouping sets in the parentheses, each a <see cref="RollupGrouping"/>, <see cref="CubeGrouping"/>, <see cref="CompositeGrouping"/>, <see cref="GrandTotalGrouping"/> or expression; at least one.</summary>
    public List<GroupingSpec> Sets { get; set; } = [];
}

/// <summary>
/// The PL/SQL <c>INTO</c> targets of a query, a <c>FETCH</c>, an <c>EXECUTE IMMEDIATE</c> or a DML <c>RETURNING</c>, such as
/// <c>INTO a, b</c> or <c>BULK COLLECT INTO l</c>.
/// </summary>
/// <remarks>
/// Spans the <c>INTO</c> keyword, or the <c>BULK</c> keyword of <c>BULK COLLECT INTO</c>, through the last target; see
/// <see cref="FromClause"/>. The <c>RETURNING</c> keyword before a DML's <c>INTO</c> is not part of it.
/// </remarks>
public sealed partial class IntoClause : SqlNode
{
    /// <summary>True for <c>BULK COLLECT INTO</c>.</summary>
    public bool BulkCollect { get; set; }
    /// <summary>The variables that receive the values, in source order.</summary>
    public List<Expression> Targets { get; set; } = [];
}

/// <summary>The <c>HAVING</c> clause, such as <c>HAVING COUNT(*) > 1</c>.</summary>
/// <remarks>Spans the <c>HAVING</c> keyword; see <see cref="FromClause"/>.</remarks>
public sealed partial class HavingClause : SqlNode
{
    /// <summary>The search condition.</summary>
    public required Expression Condition { get; set; }
}

/// <summary>
/// An <c>ORDER BY</c> clause, such as <c>ORDER BY a DESC</c>. Used wherever a sort list is written: on a query, inside
/// <c>OVER (...)</c>, <c>WITHIN GROUP (...)</c> and <c>KEEP (...)</c>, and in <c>MATCH_RECOGNIZE</c> and <c>MODEL</c> rules.
/// </summary>
/// <remarks>Spans the <c>ORDER</c> keyword; see <see cref="FromClause"/>.</remarks>
public sealed partial class OrderByClause : SqlNode
{
    /// <summary>True for <c>ORDER SIBLINGS BY</c>, which keeps the hierarchy order.</summary>
    public bool Siblings { get; set; }
    /// <summary>The sort items, in source order.</summary>
    public List<OrderByItem> Items { get; set; } = [];
}

/// <summary>
/// The <c>WITHIN GROUP (ORDER BY ...)</c> clause of an ordered-set aggregate, such as <c>LISTAGG(n, ',') WITHIN GROUP (ORDER BY n)</c>.
/// </summary>
/// <remarks>Spans the <c>WITHIN</c> keyword through the closing parenthesis; see <see cref="FromClause"/>.</remarks>
public sealed partial class WithinGroupClause : SqlNode
{
    /// <summary>The <c>ORDER BY</c> inside the parentheses.</summary>
    public required OrderByClause OrderBy { get; set; }
}

/// <summary>One item of an <c>ORDER BY</c> list, such as <c>sal DESC NULLS LAST</c>.</summary>
public sealed partial class OrderByItem : SqlNode
{
    /// <summary>The sort key.</summary>
    public required Expression Expression { get; set; }
    /// <summary>True for <c>DESC</c>; false for <c>ASC</c> or no direction.</summary>
    public bool Descending { get; set; }
    /// <summary>The <c>NULLS FIRST</c> or <c>NULLS LAST</c> setting, or null when absent.</summary>
    public NullsOrdering? Nulls { get; set; }
}

/// <summary>Where nulls sort in an <c>ORDER BY</c> item.</summary>
public enum NullsOrdering
{
    /// <summary><c>NULLS FIRST</c>.</summary>
    First,
    /// <summary><c>NULLS LAST</c>.</summary>
    Last,
}

/// <summary>The <c>OFFSET</c> clause, such as <c>OFFSET 5 ROWS</c>. It is independent of <see cref="FetchClause"/>, which Oracle also allows alone.</summary>
/// <remarks>
/// Spans the <c>OFFSET</c> keyword through <c>ROW</c> or <c>ROWS</c>; see <see cref="FromClause"/>. The spelling is not kept:
/// the generator writes <c>ROWS</c>.
/// </remarks>
public sealed partial class OffsetClause : SqlNode
{
    /// <summary>The number of rows to skip.</summary>
    public required Expression Offset { get; set; }
}

/// <summary>A row limiting clause, such as <c>FETCH NEXT 10 ROWS ONLY</c> or <c>FETCH FIRST 5 PERCENT ROWS WITH TIES</c>.</summary>
public sealed partial class FetchClause : SqlNode
{
    /// <summary>
    /// True when <c>NEXT</c> is written; false for <c>FIRST</c>. <c>NEXT</c> and <c>FIRST</c> are synonyms, so this records
    /// the spelling only and is left out of structural comparison: <c>FETCH NEXT</c> and <c>FETCH FIRST</c> with the same
    /// count compare equal.
    /// </summary>
    [SpellingOnly] public bool Next { get; set; }
    /// <summary>The row or percent count, or null when absent.</summary>
    public Expression? Count { get; set; }
    /// <summary>True when <c>PERCENT</c> is written.</summary>
    public bool Percent { get; set; }
    /// <summary>True for <c>WITH TIES</c>.</summary>
    public bool WithTies { get; set; }
    /// <summary>23ai vector search: EXACT, or APPROX / APPROXIMATE (synonyms). Null when the keyword is omitted.</summary>
    public FetchAccuracy? Accuracy { get; set; }
    /// <summary>23ai approximate fetch: WITH TARGET ACCURACY p PERCENT (the percentage expression); null when absent.</summary>
    public Expression? TargetAccuracy { get; set; }
}

/// <summary>The accuracy keyword of a <c>FETCH</c> clause, used by 23ai vector search.</summary>
public enum FetchAccuracy
{
    /// <summary><c>EXACT</c>.</summary>
    Exact,
    /// <summary><c>APPROX</c> or <c>APPROXIMATE</c>.</summary>
    Approximate,
}

/// <summary>A <c>FOR UPDATE</c> clause, such as <c>FOR UPDATE OF t.a NOWAIT</c>.</summary>
public sealed partial class ForUpdateClause : SqlNode
{
    /// <summary>The columns after <c>OF</c>; empty when the lock applies to every table.</summary>
    public List<ColumnReference> Of { get; set; } = [];
    /// <summary>How a locked row is handled: <see cref="LockWait.NoWait"/>, <see cref="LockWait.Wait"/> or <see cref="LockWait.SkipLocked"/>; null when absent (wait forever).</summary>
    public LockWait? Wait { get; set; }
    /// <summary>The number of seconds after <c>WAIT</c> (<see cref="Wait"/> is <see cref="LockWait.Wait"/>), or null otherwise.</summary>
    public Expression? WaitSeconds { get; set; }
}

/// <summary>How a lock request handles a row that another session holds: in <c>FOR UPDATE</c> and <c>LOCK TABLE</c>.</summary>
public enum LockWait
{
    /// <summary><c>NOWAIT</c>: fail at once.</summary>
    NoWait,
    /// <summary><c>WAIT n</c>: wait up to n seconds, given by the statement's <c>WaitSeconds</c>.</summary>
    Wait,
    /// <summary><c>SKIP LOCKED</c>: leave out the locked rows (<c>FOR UPDATE</c> only).</summary>
    SkipLocked,
}

/// <summary>The direction of <c>NTH_VALUE</c>: <c>FROM FIRST</c> or <c>FROM LAST</c>.</summary>
public enum NthValueFrom
{
    /// <summary><c>FROM FIRST</c>.</summary>
    First,
    /// <summary><c>FROM LAST</c>.</summary>
    Last,
}

/// <summary>The <c>IGNORE NULLS</c> or <c>RESPECT NULLS</c> option of a window function such as <c>LAG</c>.</summary>
public enum NullTreatment
{
    /// <summary><c>IGNORE NULLS</c>.</summary>
    Ignore,
    /// <summary><c>RESPECT NULLS</c>.</summary>
    Respect,
}

// ---------- DML ----------

/// <summary><c>INSERT INTO target [(columns)] VALUES (...)</c> or <c>INSERT INTO target SELECT ...</c>.</summary>
public sealed partial class InsertStatement : DmlStatement
{
    /// <summary>The table or view that receives the rows.</summary>
    public required TableReference Target { get; set; }
    /// <summary>The column list in parentheses, each optionally qualified by the target name or alias (<c>INSERT INTO t x (x.a)</c>); empty when absent.</summary>
    public List<ColumnReference> Columns { get; set; } = [];
    /// <summary>
    /// The <c>VALUES</c> clause, or null for the <c>SELECT</c> form. Exactly one of <see cref="Values"/> and
    /// <see cref="Query"/> is set.
    /// </summary>
    public ValuesClause? Values { get; set; }
    /// <summary>
    /// The query that supplies the rows of <c>INSERT ... SELECT</c>, or null for the <c>VALUES</c> form. Exactly one of
    /// <see cref="Values"/> and <see cref="Query"/> is set.
    /// </summary>
    public SelectStatement? Query { get; set; }
}

/// <summary>One row of a <c>VALUES</c> list: the expressions in its parentheses, such as <c>(1, 'a')</c>.</summary>
/// <remarks>Spans the parentheses; see <see cref="FromClause"/>.</remarks>
public sealed partial class RowValue : SqlNode
{
    /// <summary>The expressions of the row, in source order.</summary>
    public required List<Expression> Values { get; set; }
}

/// <summary>
/// A <c>VALUES</c> clause of an <c>INSERT</c>, such as <c>VALUES (1, 2), (3, 4)</c>, or the PL/SQL record form
/// <c>VALUES rec</c>.
/// </summary>
/// <remarks>Spans the <c>VALUES</c> keyword through its last row or record; see <see cref="FromClause"/>.</remarks>
public sealed partial class ValuesClause : SqlNode
{
    /// <summary>The rows, in source order; empty for the PL/SQL record form.</summary>
    public List<RowValue> Rows { get; set; } = [];
    /// <summary>The PL/SQL record variable of <c>VALUES rec</c>, or null for the row form.</summary>
    public Expression? Record { get; set; }
}

/// <summary><c>UPDATE target SET col = value [FROM ...] [WHERE condition]</c>.</summary>
public sealed partial class UpdateStatement : DmlStatement
{
    /// <summary>The table or view that is updated.</summary>
    public required TableReference Target { get; set; }
    /// <summary>The <c>SET</c> clause, such as <c>SET a = 1, b = 2</c>.</summary>
    public required SetClause Set { get; set; }
    /// <summary>23ai: UPDATE ... SET ... FROM table_references. Null when absent.</summary>
    public FromClause? From { get; set; }
    /// <summary>The <c>WHERE</c> clause, or null when absent.</summary>
    public WhereClause? Where { get; set; }
}

/// <summary>The <c>SET</c> clause of an <c>UPDATE</c> or of a <c>MERGE</c> update action: its assignments, in source order.</summary>
/// <remarks>Spans the <c>SET</c> keyword through the last assignment; see <see cref="FromClause"/>.</remarks>
public sealed partial class SetClause : SqlNode
{
    /// <summary>The assignments, in source order.</summary>
    public List<SetItem> Items { get; set; } = [];
}

/// <summary>One assignment of a <c>SET</c> clause: <c>col = expr</c> or <c>(c1, c2) = (subquery)</c>.</summary>
public sealed partial class SetItem : SqlNode
{
    /// <summary>PL/SQL: SET ROW = record (Columns is empty).</summary>
    public bool Row { get; set; }
    /// <summary>The assigned columns: one column, or several in parentheses for a row-value assignment.</summary>
    public List<ColumnReference> Columns { get; set; } = [];
    /// <summary>The value assigned to the column or columns.</summary>
    public required Expression Value { get; set; }
}

/// <summary><c>DELETE FROM target [FROM ...] [WHERE condition]</c>.</summary>
public sealed partial class DeleteStatement : DmlStatement
{
    /// <summary>The table or view that rows are deleted from.</summary>
    public required TableReference Target { get; set; }
    /// <summary>23ai: DELETE FROM target FROM table_references (direct joins). Null when absent.</summary>
    public FromClause? From { get; set; }
    /// <summary>The <c>WHERE</c> clause, or null when absent.</summary>
    public WhereClause? Where { get; set; }
}

/// <summary><c>MERGE INTO target USING source ON (condition) WHEN ...</c>.</summary>
public sealed partial class MergeStatement : DmlStatement
{
    /// <summary>The table that is merged into.</summary>
    public required TableReference Target { get; set; }
    /// <summary>The table or query whose rows are merged in.</summary>
    public required TableReference Source { get; set; }
    /// <summary>The join condition after <c>ON</c>.</summary>
    public required Expression On { get; set; }
    /// <summary>The <c>WHEN MATCHED</c> and <c>WHEN NOT MATCHED</c> clauses, in source order.</summary>
    public List<MergeClause> Clauses { get; set; } = [];
}

/// <summary>One <c>WHEN [NOT] MATCHED THEN</c> clause of a <c>MERGE</c> statement: <see cref="MergeMatchedClause"/> or <see cref="MergeNotMatchedClause"/>.</summary>
/// <remarks>
/// The base of the two kinds. Spans <c>WHEN</c> through the end of its action (<c>UPDATE SET ...</c> or <c>INSERT ... VALUES ...</c>,
/// with their <c>WHERE</c> clauses); see <see cref="FromClause"/>. Oracle has no condition after <c>MATCHED</c>
/// (<c>WHEN MATCHED AND cond</c> is rejected), so the base carries no members.
/// </remarks>
public abstract partial class MergeClause : SqlNode
{
}

/// <summary><c>WHEN MATCHED THEN UPDATE SET ... [WHERE ...] [DELETE WHERE ...]</c>.</summary>
/// <remarks>Spans <c>WHEN</c> through the end of its action; see <see cref="FromClause"/>.</remarks>
public sealed partial class MergeMatchedClause : MergeClause
{
    /// <summary>The <c>UPDATE SET</c> clause of the action.</summary>
    public required SetClause Set { get; set; }
    /// <summary>The <c>WHERE</c> clause of the update, or null when absent.</summary>
    public WhereClause? UpdateWhere { get; set; }
    /// <summary>
    /// The <c>WHERE</c> clause of the <c>DELETE</c> part of an <c>UPDATE SET ... DELETE WHERE</c> action, or null when absent.
    /// Oracle has no delete-only action (<c>WHEN MATCHED THEN DELETE</c> is a syntax error), so this is set only on an update.
    /// </summary>
    public WhereClause? DeleteWhere { get; set; }
}

/// <summary><c>WHEN NOT MATCHED THEN INSERT (cols) VALUES (...) [WHERE ...]</c>.</summary>
/// <remarks>Spans <c>WHEN</c> through the end of its action; see <see cref="FromClause"/>.</remarks>
public sealed partial class MergeNotMatchedClause : MergeClause
{
    /// <summary>The column list of the insert, each optionally qualified by the target alias (<c>INSERT (p.a, p.b)</c>); empty when absent.</summary>
    public List<ColumnReference> Columns { get; set; } = [];
    /// <summary>The <c>VALUES</c> clause of the insert: one row.</summary>
    public required ValuesClause Values { get; set; }
    /// <summary>The <c>WHERE</c> clause of the insert, or null when absent.</summary>
    public WhereClause? Where { get; set; }
}

// ---------- Table references ----------

/// <summary>A table source in a <c>FROM</c> clause: a named table, a subquery, a join, a function or another table form.</summary>
public abstract partial class TableReference : SqlNode
{
    /// <summary>The alias after the table source, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

/// <summary>A table or view name in a <c>FROM</c> clause, such as <c>scott.emp e PARTITION (p1)</c>.</summary>
public sealed partial class NamedTable : TableReference
{
    /// <summary>The table name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>Database link name after '@' (dotted parts).</summary>
    public List<Identifier> DbLink { get; set; } = [];
    /// <summary>The partition name in <c>PARTITION (p)</c>, or null when absent.</summary>
    public Identifier? Partition { get; set; }
    /// <summary>The <c>SAMPLE</c> clause, or null when absent.</summary>
    public SampleClause? Sample { get; set; }
    /// <summary>The <c>AS OF</c> or <c>VERSIONS BETWEEN</c> clause, or null when absent.</summary>
    public FlashbackClause? Flashback { get; set; }
    /// <summary>JSON duality view annotations written after the table (WITH INSERT UPDATE DELETE, WITH NOCHECK, ...).</summary>
    public List<DualityAnnotationItem> DualityAnnotations { get; set; } = [];
}

/// <summary>A subquery in a <c>FROM</c> clause, such as <c>(SELECT 1 FROM dual) s</c>.</summary>
public sealed partial class SubqueryTable : TableReference
{
    /// <summary>True for <c>LATERAL</c>.</summary>
    public bool Lateral { get; set; }
    /// <summary>The subquery.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>TABLE(expr) collection expression, or a table function named in FROM without TABLE (see <see cref="WithoutTableKeyword"/>).</summary>
public sealed partial class TableFunction : TableReference
{
    /// <summary>The collection expression, such as <c>f(1)</c> in <c>TABLE(f(1))</c>; a function call in the form without TABLE.</summary>
    public required Expression Expression { get; set; }
    /// <summary>
    /// True when the function is named in FROM without the <c>TABLE</c> keyword (Oracle 18c and later), as in
    /// <c>FROM hr.pkg.f(1, 2) t</c>; false for <c>TABLE(expr)</c>.
    /// </summary>
    public bool WithoutTableKeyword { get; set; }
}

/// <summary>A table reference in parentheses, such as <c>(a JOIN b ON a.id = b.id)</c>.</summary>
public sealed partial class ParenthesizedTable : TableReference
{
    /// <summary>The table reference inside the parentheses.</summary>
    public required TableReference Inner { get; set; }
}

/// <summary>A join of two table references, such as <c>a LEFT JOIN b ON a.id = b.id</c>.</summary>
public sealed partial class JoinTable : TableReference
{
    /// <summary>The table reference on the left of the join.</summary>
    public required TableReference Left { get; set; }
    /// <summary>The kind of join.</summary>
    public JoinKind Kind { get; set; }
    /// <summary>True for a <c>NATURAL</c> join.</summary>
    public bool Natural { get; set; }
    /// <summary>The table reference on the right of the join.</summary>
    public required TableReference Right { get; set; }
    /// <summary>The <c>ON</c> condition, or null when absent.</summary>
    public Expression? On { get; set; }
    /// <summary>The columns of a <c>USING</c> clause; empty when absent.</summary>
    public List<Identifier> Using { get; set; } = [];
    /// <summary>Partitioned outer join: the <c>PARTITION BY (...)</c> clause written before the join keyword, on the left operand; null when absent.</summary>
    public PartitionByClause? LeftPartitionBy { get; set; }
    /// <summary>Partitioned outer join: the <c>PARTITION BY (...)</c> clause written after the join keyword, on the right operand; null when absent.</summary>
    public PartitionByClause? RightPartitionBy { get; set; }
}

/// <summary>The kind of a join.</summary>
public enum JoinKind
{
    /// <summary><c>[INNER] JOIN</c>.</summary>
    Inner,
    /// <summary><c>LEFT [OUTER] JOIN</c>.</summary>
    Left,
    /// <summary><c>RIGHT [OUTER] JOIN</c>.</summary>
    Right,
    /// <summary><c>FULL [OUTER] JOIN</c>.</summary>
    Full,
    /// <summary><c>CROSS JOIN</c>.</summary>
    Cross,
    /// <summary><c>CROSS APPLY</c>.</summary>
    CrossApply,
    /// <summary><c>OUTER APPLY</c>.</summary>
    OuterApply,
}

// ---------- Expressions ----------

/// <summary>An expression: a value, a column, an operator application, a function call or another expression form.</summary>
public abstract partial class Expression : SqlNode { }

/// <summary>A name, such as a column, table or alias.</summary>
public sealed partial class Identifier : SqlNode
{
    /// <summary>Normalized: unquoted names upper-cased; quoted names verbatim.</summary>
    public string Value { get; set; } = "";
    /// <summary>True when the name was written in double quotes.</summary>
    public bool Quoted { get; set; }
    /// <summary>The name exactly as typed (without quotes); null for synthesized identifiers. Used by <see cref="SqlScriptGenerator"/> to preserve case.</summary>
    [SpellingOnly] public string? Text { get; set; }
    /// <summary>The normalized name.</summary>
    public override string ToString() => Value;
}

/// <summary>A column reference, such as <c>e.ename</c>.</summary>
public sealed partial class ColumnReference : Expression
{
    /// <summary>The name parts, qualifier first: for <c>e.ename</c>, the table alias and then the column name.</summary>
    public List<Identifier> Parts { get; set; } = [];
    /// <summary>Oracle (+) outer-join marker.</summary>
    public bool OuterJoin { get; set; }
    /// <summary>
    /// The attribute written after <c>%</c>, as spelled, or null when there is none. Common values: <c>FOUND</c>,
    /// <c>NOTFOUND</c>, <c>ROWCOUNT</c> and <c>ISOPEN</c> for a cursor (<c>SQL%FOUND</c>, <c>c%ROWCOUNT</c>), and
    /// <c>BULK_ROWCOUNT</c> and <c>BULK_EXCEPTIONS</c> for <c>SQL%BULK_EXCEPTIONS</c>.
    /// </summary>
    /// <remarks>
    /// Caution: <c>SQL%FOUND</c> and <c>c%ROWCOUNT</c> parse as a <see cref="ColumnReference"/> with this property set. Code
    /// that treats every column reference as a column must check <c>Attribute is null</c>; <see cref="SqlAnalysis"/> does.
    /// </remarks>
    public string? Attribute { get; set; }
}

/// <summary>The kind of a literal value.</summary>
public enum LiteralKind
{
    /// <summary>A number, such as <c>42</c> or <c>1.5e3</c>.</summary>
    Number,
    /// <summary>A character string, such as <c>'text'</c>.</summary>
    String,
    /// <summary>The <c>NULL</c> literal.</summary>
    Null,
    /// <summary>A <c>DATE</c> literal, such as <c>DATE '2020-01-01'</c>.</summary>
    Date,
    /// <summary>A <c>TIMESTAMP</c> literal, such as <c>TIMESTAMP '2020-01-01 10:00:00'</c>.</summary>
    Timestamp,
    /// <summary><c>TRUE</c> or <c>FALSE</c>.</summary>
    Boolean,
    /// <summary>The <c>DEFAULT</c> keyword used as a value.</summary>
    Default,
    /// <summary>The <c>ANY</c> keyword used as a value.</summary>
    Any,
}

/// <summary>A literal value, such as <c>'x'</c>, <c>42</c>, <c>DATE '2020-01-01'</c> or <c>NULL</c>.</summary>
public sealed partial class Literal : Expression
{
    /// <summary>The kind of literal.</summary>
    public LiteralKind Kind { get; set; }
    /// <summary>N'...' / NQ'...' national character literal.</summary>
    public bool National { get; set; }
    /// <summary>Opening delimiter of a q'…' alternative-quoting literal (e.g. '[' for q'[it's]'); null for ordinary 'strings'.</summary>
    [SpellingOnly] public char? QuoteDelimiter { get; set; }
    /// <summary>The literal text as written, without the quotes of a string; null for <c>NULL</c>.</summary>
    public string? Value { get; set; }
}

/// <summary>An <c>INTERVAL</c> literal, such as <c>INTERVAL '3' DAY</c> or <c>INTERVAL '1-2' YEAR(3) TO MONTH</c>.</summary>
public sealed partial class IntervalLiteral : Expression
{
    /// <summary>The interval string expression.</summary>
    public required Expression Value { get; set; }
    /// <summary>The leading datetime field, such as <c>DAY</c>.</summary>
    public IntervalUnit From { get; set; }
    /// <summary>The leading precision, such as the <c>3</c> in <c>YEAR(3)</c>, or null when absent.</summary>
    public int? FromPrecision { get; set; }
    /// <summary>The trailing field after <c>TO</c>, or null for a single-field interval.</summary>
    public IntervalUnit? To { get; set; }
    /// <summary>The precision of the trailing field, or null when absent.</summary>
    public int? ToPrecision { get; set; }
}

/// <summary>
/// Postfix interval over a parenthesized datetime difference: <c>(a - b) DAY(2) TO SECOND(6)</c> or
/// <c>(a - b) YEAR TO MONTH</c>. Unlike <see cref="IntervalLiteral"/> there is no INTERVAL keyword or string.
/// </summary>
public sealed partial class IntervalExpression : Expression
{
    /// <summary>The parenthesized datetime difference, such as <c>(a - b)</c>.</summary>
    public required Expression Value { get; set; }
    /// <summary>The leading datetime field, such as <c>DAY</c>.</summary>
    public IntervalUnit From { get; set; }
    /// <summary>The leading precision, or null when absent.</summary>
    public int? FromPrecision { get; set; }
    /// <summary>The trailing datetime field after <c>TO</c>, such as <c>SECOND</c>.</summary>
    public IntervalUnit To { get; set; }
    /// <summary>The precision of the trailing field, or null when absent.</summary>
    public int? ToPrecision { get; set; }
}

/// <summary>A datetime field of an interval.</summary>
public enum IntervalUnit
{
    /// <summary><c>YEAR</c>.</summary>
    Year,
    /// <summary><c>MONTH</c>.</summary>
    Month,
    /// <summary><c>DAY</c>.</summary>
    Day,
    /// <summary><c>HOUR</c>.</summary>
    Hour,
    /// <summary><c>MINUTE</c>.</summary>
    Minute,
    /// <summary><c>SECOND</c>.</summary>
    Second,
}

/// <summary>A bind variable, such as <c>:id</c>.</summary>
public sealed partial class BindVariable : Expression
{
    /// <summary>The bind name without the colon.</summary>
    public string Name { get; set; } = "";
    /// <summary>The name was written quoted (:"name").</summary>
    public bool Quoted { get; set; }
}

/// <summary>A star, such as the <c>*</c> in <c>COUNT(*)</c> or <c>t.*</c>.</summary>
public sealed partial class StarExpression : Expression
{
    /// <summary>The name parts before <c>.*</c>; empty for a plain <c>*</c>.</summary>
    public List<Identifier> Qualifier { get; set; } = [];
}

/// <summary>An expression in parentheses, such as <c>(a + b)</c>; kept so that the written shape is preserved.</summary>
public sealed partial class ParenthesizedExpression : Expression
{
    /// <summary>The expression inside the parentheses.</summary>
    public required Expression Inner { get; set; }
}

/// <summary>(a, b, c) row/list value.</summary>
public sealed partial class ExpressionList : Expression
{
    /// <summary>The expressions in the list, in source order.</summary>
    public List<Expression> Items { get; set; } = [];
}

/// <summary>The operator of a <see cref="BinaryExpression"/> (and the comparison of a <see cref="QuantifiedComparison"/>).</summary>
/// <remarks>Spellings that mean the same operator share a member: <c>!=</c>, <c>^=</c> and <c>~=</c> are all <see cref="NotEquals"/>. Use <see cref="OperatorExtensions.ToSql(BinaryOperator)"/> for the text.</remarks>
public enum BinaryOperator
{
    /// <summary><c>+</c></summary>
    Add,
    /// <summary><c>-</c></summary>
    Subtract,
    /// <summary><c>*</c></summary>
    Multiply,
    /// <summary><c>/</c></summary>
    Divide,
    /// <summary><c>**</c>, PL/SQL only.</summary>
    Power,
    /// <summary><c>||</c></summary>
    Concatenate,
    /// <summary><c>=</c></summary>
    Equals,
    /// <summary><c>&lt;&gt;</c>, also written <c>!=</c>, <c>^=</c> or <c>~=</c>.</summary>
    NotEquals,
    /// <summary><c>&lt;</c></summary>
    LessThan,
    /// <summary><c>&gt;</c></summary>
    GreaterThan,
    /// <summary><c>&lt;=</c></summary>
    LessThanOrEqual,
    /// <summary><c>&gt;=</c></summary>
    GreaterThanOrEqual,
    /// <summary><c>AND</c></summary>
    And,
    /// <summary><c>OR</c></summary>
    Or,
    /// <summary><c>x MEMBER OF nt</c></summary>
    MemberOf,
    /// <summary><c>x NOT MEMBER OF nt</c></summary>
    NotMemberOf,
    /// <summary><c>x SUBMULTISET OF nt</c></summary>
    SubmultisetOf,
    /// <summary><c>x NOT SUBMULTISET OF nt</c></summary>
    NotSubmultisetOf,
    /// <summary><c>MULTISET UNION</c></summary>
    MultisetUnion,
    /// <summary><c>MULTISET UNION ALL</c></summary>
    MultisetUnionAll,
    /// <summary><c>MULTISET UNION DISTINCT</c></summary>
    MultisetUnionDistinct,
    /// <summary><c>MULTISET INTERSECT</c></summary>
    MultisetIntersect,
    /// <summary><c>MULTISET INTERSECT ALL</c></summary>
    MultisetIntersectAll,
    /// <summary><c>MULTISET INTERSECT DISTINCT</c></summary>
    MultisetIntersectDistinct,
    /// <summary><c>MULTISET EXCEPT</c></summary>
    MultisetExcept,
    /// <summary><c>MULTISET EXCEPT ALL</c></summary>
    MultisetExceptAll,
    /// <summary><c>MULTISET EXCEPT DISTINCT</c></summary>
    MultisetExceptDistinct,
}

/// <summary>A binary operator application, such as <c>a + b</c> or <c>a || b</c>.</summary>
public sealed partial class BinaryExpression : Expression
{
    /// <summary>The left operand.</summary>
    public required Expression Left { get; set; }
    /// <summary>The operator. Spellings that mean the same operator (<c>!=</c>, <c>^=</c>, <c>~=</c>) share a member; see <see cref="BinaryOperator"/>.</summary>
    public required BinaryOperator Operator { get; set; }
    /// <summary>The source spelling of <see cref="Operator"/> when it is <c>!=</c>, <c>^=</c> or <c>~=</c>; null for the canonical spelling. Records spelling only, not meaning.</summary>
    [SpellingOnly] public string? OperatorSpelling { get; set; }
    /// <summary>The right operand.</summary>
    public required Expression Right { get; set; }
}

/// <summary>The quantifier of a quantified comparison, such as <c>= ANY (...)</c>.</summary>
public enum ComparisonQuantifier
{
    /// <summary><c>ANY</c>.</summary>
    Any,
    /// <summary><c>SOME</c>, a synonym of <c>ANY</c>.</summary>
    Some,
    /// <summary><c>ALL</c>.</summary>
    All,
}

/// <summary>A comparison with a quantified list or subquery, such as <c>x = ANY (SELECT y FROM u)</c>.</summary>
public sealed partial class QuantifiedComparison : Expression
{
    /// <summary>The value on the left of the comparison.</summary>
    public required Expression Left { get; set; }
    /// <summary>The comparison operator. Only the comparison members of <see cref="BinaryOperator"/> are valid here: Equals, NotEquals, LessThan, GreaterThan, LessThanOrEqual and GreaterThanOrEqual.</summary>
    public required BinaryOperator Operator { get; set; }
    /// <summary>The source spelling of <see cref="Operator"/> when it is <c>!=</c>, <c>^=</c> or <c>~=</c>; null for the canonical spelling. Records spelling only, not meaning.</summary>
    [SpellingOnly] public string? OperatorSpelling { get; set; }
    /// <summary>The quantifier: <c>ANY</c>, <c>SOME</c> or <c>ALL</c>.</summary>
    public ComparisonQuantifier Quantifier { get; set; }
    /// <summary>The subquery or list on the right.</summary>
    public required Expression Right { get; set; }
}

/// <summary>The operator of a <see cref="UnaryExpression"/>.</summary>
public enum UnaryOperator
{
    /// <summary><c>+</c></summary>
    Plus,
    /// <summary><c>-</c></summary>
    Minus,
    /// <summary><c>NOT</c></summary>
    Not,
    /// <summary><c>PRIOR</c></summary>
    Prior,
    /// <summary><c>CONNECT_BY_ROOT</c></summary>
    ConnectByRoot,
}

/// <summary>A prefix operator applied to one operand, such as <c>NOT a</c>, <c>-a</c> or <c>PRIOR a</c>.</summary>
public sealed partial class UnaryExpression : Expression
{
    /// <summary>The prefix operator.</summary>
    public required UnaryOperator Operator { get; set; }
    /// <summary>The operand.</summary>
    public required Expression Operand { get; set; }
}

/// <summary>A <c>BETWEEN</c> test, such as <c>a BETWEEN 1 AND 2</c>.</summary>
public sealed partial class BetweenExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>NOT BETWEEN</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The lower bound.</summary>
    public required Expression Low { get; set; }
    /// <summary>The upper bound.</summary>
    public required Expression High { get; set; }
}

/// <summary>An <c>IN</c> test, such as <c>a IN (1, 2)</c> or <c>a IN (SELECT ...)</c>.</summary>
public sealed partial class InExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>NOT IN</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The listed values, in source order; empty when the test uses a subquery.</summary>
    public List<Expression> List { get; set; } = [];
    /// <summary>The subquery of <c>IN (SELECT ...)</c>, or null for a list.</summary>
    public SelectStatement? Query { get; set; }
}

/// <summary>The form of a <c>LIKE</c> operator.</summary>
public enum LikeKind
{
    /// <summary><c>LIKE</c>.</summary>
    Like,
    /// <summary><c>LIKEC</c>: character-length semantics.</summary>
    Likec,
    /// <summary><c>LIKE2</c>: UCS2 code-point semantics.</summary>
    Like2,
    /// <summary><c>LIKE4</c>: UCS4 code-point semantics.</summary>
    Like4,
}

/// <summary>A <c>LIKE</c> test, such as <c>name LIKE 'A%' ESCAPE '\'</c>.</summary>
public sealed partial class LikeExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>NOT LIKE</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The form of the operator.</summary>
    public LikeKind Kind { get; set; }
    /// <summary>The pattern expression.</summary>
    public required Expression Pattern { get; set; }
    /// <summary>The <c>ESCAPE</c> character expression, or null when absent.</summary>
    public Expression? Escape { get; set; }
}

/// <summary>The test written after <c>IS</c>, such as <c>IS NULL</c>.</summary>
public enum IsTest
{
    /// <summary><c>NULL</c>.</summary>
    Null,
    /// <summary><c>NAN</c>.</summary>
    Nan,
    /// <summary><c>INFINITE</c>.</summary>
    Infinite,
    /// <summary><c>EMPTY</c>.</summary>
    Empty,
    /// <summary><c>PRESENT</c>.</summary>
    Present,
    /// <summary><c>A SET</c>.</summary>
    ASet,
    /// <summary><c>JSON</c>.</summary>
    Json,
    /// <summary><c>OF [TYPE]</c>.</summary>
    OfType,
    /// <summary><c>DANGLING</c>.</summary>
    Dangling,
    /// <summary><c>TRUE</c>.</summary>
    True,
    /// <summary><c>FALSE</c>.</summary>
    False,
}

/// <summary>One type in an <c>IS OF</c> test, such as <c>ONLY t</c>.</summary>
public sealed partial class IsOfType : SqlNode
{
    /// <summary>True for <c>ONLY</c>: the exact type, not its subtypes.</summary>
    public bool Only { get; set; }
    /// <summary>The type tested.</summary>
    public required DataType Type { get; set; }
}

/// <summary>An <c>IS</c> test, such as <c>a IS NOT NULL</c>.</summary>
public sealed partial class IsExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>IS NOT</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The test written after <c>IS</c>.</summary>
    public IsTest Test { get; set; }
    /// <summary>IS JSON options (FORMAT JSON, STRICT/LAX, WITH/WITHOUT UNIQUE KEYS ...).</summary>
    public List<FunctionClause> JsonClauses { get; set; } = [];
    /// <summary>IS OF [TYPE] (ONLY t, ...).</summary>
    public List<IsOfType> OfTypes { get; set; } = [];
}

/// <summary>An <c>EXISTS</c> test, such as <c>EXISTS (SELECT 1 FROM u)</c>.</summary>
public sealed partial class ExistsExpression : Expression
{
    /// <summary>The subquery.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>A scalar subquery used as an expression, such as <c>(SELECT MAX(x) FROM u)</c>.</summary>
public sealed partial class SubqueryExpression : Expression
{
    /// <summary>The subquery.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>A <c>CASE</c> expression, simple or searched, such as <c>CASE WHEN a &gt; 1 THEN 'x' ELSE 'y' END</c>.</summary>
public sealed partial class CaseExpression : Expression
{
    /// <summary>The operand of a simple <c>CASE</c>, or null for the searched form.</summary>
    public Expression? Operand { get; set; }
    /// <summary>The <c>WHEN</c> branches, in source order.</summary>
    public List<WhenClause> Whens { get; set; } = [];
    /// <summary>The <c>ELSE</c> result, or null when absent.</summary>
    public Expression? Else { get; set; }
}

/// <summary>One <c>WHEN ... THEN</c> branch of a <c>CASE</c> expression.</summary>
public sealed partial class WhenClause : SqlNode
{
    /// <summary>The condition, or the value compared with the operand, after <c>WHEN</c>.</summary>
    public required Expression When { get; set; }
    /// <summary>The result after <c>THEN</c>.</summary>
    public required Expression Then { get; set; }
}

/// <summary>The conversion that a <c>CastExpression</c> performs.</summary>
public enum CastKind
{
    /// <summary><c>CAST (expr AS type)</c>.</summary>
    Cast,
    /// <summary><c>XMLCAST</c>.</summary>
    XmlCast,
    /// <summary><c>TREAT (expr AS type)</c>.</summary>
    Treat,
    /// <summary><c>VALIDATE_CONVERSION</c>.</summary>
    ValidateConversion,
    /// <summary><c>XMLSERIALIZE</c>.</summary>
    XmlSerialize,
    /// <summary>The parenthesized downcast <c>(expr AS type)</c>, as in <c>(SELF AS parent_t).method()</c>: generalized invocation
    /// calls the named supertype's method without dynamic dispatch, which <c>TREAT</c> does not do.</summary>
    GeneralizedInvocation,
}

/// <summary>CAST / XMLCAST / TREAT / VALIDATE_CONVERSION (all of the form f(expr AS type ...)).</summary>
public sealed partial class CastExpression : Expression
{
    /// <summary>The conversion function written.</summary>
    public CastKind Kind { get; set; }
    /// <summary>True for <c>CAST (MULTISET (subquery) AS type)</c>.</summary>
    public bool Multiset { get; set; }
    /// <summary>The expression being converted.</summary>
    public required Expression Value { get; set; }
    /// <summary>TREAT(x AS REF type).</summary>
    public bool Ref { get; set; }
    /// <summary>The target type.</summary>
    public DataType? Type { get; set; }
    /// <summary>DEFAULT x ON CONVERSION ERROR.</summary>
    public OnBehaviorClause? OnConversionError { get; set; }
    /// <summary>The format model written after a comma in the trailing options, or null when absent.</summary>
    public Expression? Format { get; set; }
    /// <summary>The NLS parameter written after the format model, or null when absent.</summary>
    public Expression? NlsParam { get; set; }
    /// <summary>XMLSERIALIZE options (DOCUMENT/CONTENT, ENCODING, VERSION, INDENT, DEFAULTS).</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
}

/// <summary>The field named in an <c>EXTRACT</c> expression.</summary>
public enum ExtractField
{
    /// <summary><c>YEAR</c>.</summary>
    Year,
    /// <summary><c>MONTH</c>.</summary>
    Month,
    /// <summary><c>DAY</c>.</summary>
    Day,
    /// <summary><c>HOUR</c>.</summary>
    Hour,
    /// <summary><c>MINUTE</c>.</summary>
    Minute,
    /// <summary><c>SECOND</c>.</summary>
    Second,
    /// <summary><c>TIMEZONE_HOUR</c>.</summary>
    TimezoneHour,
    /// <summary><c>TIMEZONE_MINUTE</c>.</summary>
    TimezoneMinute,
    /// <summary><c>TIMEZONE_REGION</c>.</summary>
    TimezoneRegion,
    /// <summary><c>TIMEZONE_ABBR</c>.</summary>
    TimezoneAbbr,
}

/// <summary>An <c>EXTRACT (field FROM source)</c> expression, such as <c>EXTRACT(YEAR FROM d)</c>.</summary>
public sealed partial class ExtractExpression : Expression
{
    /// <summary>The field extracted.</summary>
    public ExtractField Field { get; set; }
    /// <summary>The datetime or interval expression.</summary>
    public required Expression Source { get; set; }
}

/// <summary>The side of the string that <c>TRIM</c> removes characters from.</summary>
public enum TrimSpec
{
    /// <summary><c>LEADING</c>.</summary>
    Leading,
    /// <summary><c>TRAILING</c>.</summary>
    Trailing,
    /// <summary><c>BOTH</c>.</summary>
    Both,
}

/// <summary>A <c>TRIM</c> expression, such as <c>TRIM(BOTH 'x' FROM s)</c>.</summary>
public sealed partial class TrimExpression : Expression
{
    /// <summary>The side to trim, or null when none is written.</summary>
    public TrimSpec? Spec { get; set; }
    /// <summary>The characters to remove, or null when the default (blanks) applies.</summary>
    public Expression? Characters { get; set; }
    /// <summary>The string being trimmed.</summary>
    public required Expression Source { get; set; }
}

/// <summary>A substitution variable, such as <c>&amp;name</c> in SQL*Plus.</summary>
public sealed partial class SubstitutionVariable : Expression
{
    /// <summary>The variable name without the ampersand.</summary>
    public string Name { get; set; } = "";
    /// <summary>&amp;&amp;name (persistent).</summary>
    public bool Persistent { get; set; }
    /// <summary>The prefix character as typed: '&amp;' by default, or the character set by SQL*Plus <c>SET DEFINE</c> (e.g. '^').</summary>
    [SpellingOnly] public char Prefix { get; set; } = '&';
}

/// <summary>A <c>COLLATE</c> expression, such as <c>name COLLATE BINARY_CI</c>.</summary>
public sealed partial class CollateExpression : Expression
{
    /// <summary>The expression being collated.</summary>
    public required Expression Value { get; set; }
    /// <summary>The collation name.</summary>
    public required Identifier Collation { get; set; }
}

/// <summary>FOR dim IN (...) / FOR dim [LIKE p] FROM a TO b INCREMENT n, used inside MODEL cell subscripts.</summary>
public sealed partial class ForIterationSubscript : Expression
{
    /// <summary>The dimension variables, in source order.</summary>
    public List<Identifier> Dimensions { get; set; } = [];
    /// <summary>The values of an <c>IN (...)</c> form, in source order; empty when absent.</summary>
    public List<Expression> InList { get; set; } = [];
    /// <summary>The subquery of an <c>IN (SELECT ...)</c> form, or null when absent.</summary>
    public SelectStatement? InQuery { get; set; }
    /// <summary>The pattern of a <c>LIKE</c> form, or null when absent.</summary>
    public Expression? LikePattern { get; set; }
    /// <summary>The start of a range, or null for the list and subquery forms.</summary>
    public Expression? From { get; set; }
    /// <summary>The end of a range, or null when absent.</summary>
    public Expression? To { get; set; }
    /// <summary>The <c>INCREMENT</c> or <c>DECREMENT</c> step of a range, or null when absent.</summary>
    public Expression? Step { get; set; }
    /// <summary>True for <c>DECREMENT</c>: the range runs downward.</summary>
    public bool Decrement { get; set; }
}

/// <summary>The attribute that anchors a declared type: <c>%TYPE</c> or <c>%ROWTYPE</c>.</summary>
public enum TypeAnchor
{
    /// <summary><c>%TYPE</c>: the type of a column or variable.</summary>
    Type,
    /// <summary><c>%ROWTYPE</c>: the row type of a table, view or cursor.</summary>
    RowType,
}

/// <summary>The unit of a length in a character type.</summary>
public enum CharSemantics
{
    /// <summary><c>CHAR</c>: the length counts characters.</summary>
    Char,
    /// <summary><c>BYTE</c>: the length counts bytes.</summary>
    Byte,
}

/// <summary>A data type, such as <c>NUMBER(10,2)</c> or <c>VARCHAR2(10 CHAR)</c>.</summary>
public sealed partial class DataType : SqlNode
{
    /// <summary>
    /// The type name as text: "VARCHAR2", "NUMBER", "TIMESTAMP WITH TIME ZONE", or the dotted user-defined or anchored name
    /// (e.g. "HR.MY_TYPE", "T.C"). Names are normalized and carry no quotes, so a quoted name such as "ab" reads as AB here.
    /// Use <see cref="NameParts"/> when the quoting or the dot boundaries of a user-defined name matter.
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>Parenthesized length/precision/scale arguments (numbers, or * for NUMBER(*,2)).</summary>
    public List<Expression> Arguments { get; set; } = [];
    /// <summary>The <c>CHAR</c> or <c>BYTE</c> length semantics, or null when absent.</summary>
    public CharSemantics? Semantics { get; set; }
    /// <summary>The anchor of an anchored declaration, such as <c>tbl.col%TYPE</c>, or null when the type is not anchored.</summary>
    public TypeAnchor? Anchor { get; set; }
    /// <summary>True for <c>REF object_type</c>: a reference to a row of <see cref="Name"/>.</summary>
    public bool Ref { get; set; }
    /// <summary>
    /// The parts of a user-defined or anchored name, schema first, each with its quoting (for example <c>HR</c> and
    /// <c>"my type"</c> for <c>HR."my type"</c>, or <c>T</c> and <c>"c"</c> for <c>t."c"%TYPE</c>). Empty for a built-in type
    /// such as <c>VARCHAR2</c>, <c>NUMBER</c> or <c>TIMESTAMP WITH TIME ZONE</c>, for which <see cref="Name"/> is the whole
    /// type. Prefer this over <see cref="Name"/> when the spelling of a user-defined name must be preserved.
    /// </summary>
    public List<Identifier> NameParts { get; set; } = [];
}

/// <summary>An <c>AT TIME ZONE</c> expression, such as <c>ts AT TIME ZONE 'UTC'</c>.</summary>
public sealed partial class AtTimeZoneExpression : Expression
{
    /// <summary>The datetime value.</summary>
    public required Expression Value { get; set; }
    /// <summary>AT LOCAL (Zone is null).</summary>
    public bool Local { get; set; }
    /// <summary>The time zone expression, or null for <c>AT LOCAL</c>.</summary>
    public Expression? Zone { get; set; }
}

/// <summary>A function call, such as <c>COUNT(DISTINCT x)</c>, with its optional null-treatment and window clauses.</summary>
public sealed partial class FunctionCall : Expression
{
    /// <summary>The function name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>NEW type(...) constructor call.</summary>
    public bool New { get; set; }
    /// <summary>
    /// The attribute after <c>%</c> of a call, as spelled, or null when there is none. The common value is <c>BULK_ROWCOUNT</c>
    /// in <c>SQL%BULK_ROWCOUNT(i)</c>. Like <see cref="ColumnReference.Attribute"/>, it is a spelling of the attribute, not a
    /// member of an enum, because the set of cursor attributes is open.
    /// </summary>
    public string? Attribute { get; set; }
    /// <summary>Trailing clauses: RETURNING, ON ERROR, FORMAT JSON, ON OVERFLOW, PASSING, ...</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
    /// <summary>JSON_TRANSFORM operations (SET/INSERT/REMOVE/...).</summary>
    public List<JsonTransformOperation> Operations { get; set; } = [];
    /// <summary>The <c>DISTINCT</c>, <c>ALL</c> or <c>UNIQUE</c> keyword inside the call, or null when absent.</summary>
    public QuantifierKind? Quantifier { get; set; }
    /// <summary>The arguments, in source order; empty for a call without arguments.</summary>
    public List<Argument> Arguments { get; set; } = [];
    /// <summary><c>NTH_VALUE</c>'s <c>FROM FIRST</c> or <c>FROM LAST</c>, or null when absent.</summary>
    public NthValueFrom? NthFrom { get; set; }
    /// <summary>The <c>IGNORE NULLS</c> or <c>RESPECT NULLS</c> option of a window function, or null when absent.</summary>
    public NullTreatment? NullTreatment { get; set; }
    /// <summary>The <c>WITHIN GROUP (ORDER BY ...)</c> clause, as in <c>LISTAGG</c>; null when absent.</summary>
    public WithinGroupClause? WithinGroup { get; set; }
    /// <summary>KEEP (DENSE_RANK FIRST|LAST ORDER BY ...)</summary>
    public KeepClause? Keep { get; set; }
    /// <summary>The <c>OVER</c> window of an analytic call, or null when absent.</summary>
    public WindowSpec? Over { get; set; }
    /// <summary>The <c>ORDER BY</c> written inside the call, such as <c>XMLAGG(x ORDER BY y)</c>; null when absent.</summary>
    public OrderByClause? OrderBy { get; set; }
    /// <summary>The <c>PARTITION BY</c> clause of a hypothetical-rank call, such as <c>APPROX_RANK(PARTITION BY owner ORDER BY x)</c>; null when absent.</summary>
    public PartitionByClause? PartitionBy { get; set; }
}

/// <summary>The keyword that introduces a special argument of an XML or JSON function.</summary>
public enum ArgumentKeyword
{
    /// <summary><c>NAME</c>.</summary>
    Name,
    /// <summary><c>EVALNAME</c>.</summary>
    EvalName,
    /// <summary><c>KEY</c>.</summary>
    Key,
    /// <summary><c>DOCUMENT</c>.</summary>
    Document,
    /// <summary><c>CONTENT</c>.</summary>
    Content,
}

/// <summary>One argument of a function call, which may be named, such as <c>x =&gt; 1</c>.</summary>
public sealed partial class Argument : SqlNode
{
    /// <summary>The argument name in named notation (<c>name =&gt; value</c>), or null for a positional argument.</summary>
    public Identifier? Name { get; set; }  // named notation: name => value
    /// <summary>Leading keyword in XML/JSON functions (XMLELEMENT(NAME x), JSON_OBJECT(KEY k ...)).</summary>
    public ArgumentKeyword? Keyword { get; set; }
    /// <summary>The argument value.</summary>
    public required Expression Value { get; set; }
    /// <summary>The value of a JSON_OBJECT pair written after <c>VALUE</c> or <c>:</c>; <see cref="Value"/> then holds the key. Null otherwise.</summary>
    public Expression? MappedValue { get; set; }
    /// <summary>The <c>AS</c> alias of an <c>XMLATTRIBUTES</c> or <c>XMLFOREST</c> item, or null when absent.</summary>
    public Identifier? Alias { get; set; }
    /// <summary>The <c>FORMAT JSON</c> clause on the argument, or null when absent.</summary>
    public FormatClause? Format { get; set; }
    /// <summary>TO_NUMBER(x DEFAULT d ON CONVERSION ERROR, ...)</summary>
    public OnBehaviorClause? OnConversionError { get; set; }
}

/// <summary>The <c>KEEP (DENSE_RANK FIRST | LAST ORDER BY ...)</c> clause of an aggregate.</summary>
public sealed partial class KeepClause : SqlNode
{
    /// <summary>True for <c>FIRST</c>; false for <c>LAST</c>.</summary>
    public bool First { get; set; }
    /// <summary>The <c>ORDER BY</c> of the <c>KEEP</c> clause.</summary>
    public required OrderByClause OrderBy { get; set; }
}

/// <summary>
/// A <c>PARTITION BY</c> list of a window, an analytic function, a <c>MATCH_RECOGNIZE</c> or a partitioned outer join, such as
/// <c>PARTITION BY a, b</c>. (<see cref="ModelClause.PartitionBy"/> is a list of select items, not this.)
/// </summary>
/// <remarks>
/// Spans the <c>PARTITION</c> keyword through the last expression; a partitioned outer join's list also spans its parentheses.
/// See <see cref="FromClause"/>.
/// </remarks>
public sealed partial class PartitionByClause : SqlNode
{
    /// <summary>The partitioning expressions, in source order.</summary>
    public required List<Expression> Items { get; set; }
}

/// <summary>An <c>OVER</c> window specification, such as <c>(PARTITION BY d ORDER BY s ROWS UNBOUNDED PRECEDING)</c>.</summary>
public sealed partial class WindowSpec : SqlNode
{
    /// <summary>The named window of <c>OVER window_name</c>, or null when the window is written inline.</summary>
    public Identifier? Name { get; set; }
    /// <summary>The <c>PARTITION BY</c> clause, or null when absent.</summary>
    public PartitionByClause? PartitionBy { get; set; }
    /// <summary>The <c>ORDER BY</c> clause, or null when absent.</summary>
    public OrderByClause? OrderBy { get; set; }
    /// <summary>The window frame, or null when absent.</summary>
    public WindowFrame? Frame { get; set; }
}

/// <summary>A window frame, such as <c>ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW</c>.</summary>
public sealed partial class WindowFrame : SqlNode
{
    /// <summary>Whether the frame counts rows, a value range or peer groups.</summary>
    public FrameUnit Unit { get; set; }
    /// <summary>The start of the frame.</summary>
    public required FrameBound StartBound { get; set; }
    /// <summary>The end of the frame, or null when the frame has only a start bound.</summary>
    public FrameBound? EndBound { get; set; }
}

/// <summary>The unit of a window frame.</summary>
public enum FrameUnit
{
    /// <summary><c>ROWS</c>: physical rows.</summary>
    Rows,
    /// <summary><c>RANGE</c>: rows whose ordering value is in range.</summary>
    Range,
    /// <summary><c>GROUPS</c>: peer groups of rows.</summary>
    Groups,
}
/// <summary>The kind of a window frame bound.</summary>
public enum FrameBoundKind
{
    /// <summary><c>UNBOUNDED PRECEDING</c>.</summary>
    UnboundedPreceding,
    /// <summary><c>UNBOUNDED FOLLOWING</c>.</summary>
    UnboundedFollowing,
    /// <summary><c>CURRENT ROW</c>.</summary>
    CurrentRow,
    /// <summary><c>n PRECEDING</c>.</summary>
    Preceding,
    /// <summary><c>n FOLLOWING</c>.</summary>
    Following,
}

/// <summary>One bound of a window frame, such as <c>2 PRECEDING</c>.</summary>
public sealed partial class FrameBound : SqlNode
{
    /// <summary>The kind of bound.</summary>
    public FrameBoundKind Kind { get; set; }
    /// <summary>The row or value offset for <c>PRECEDING</c> and <c>FOLLOWING</c>; null for the unbounded and current-row kinds.</summary>
    public Expression? Offset { get; set; }
}
