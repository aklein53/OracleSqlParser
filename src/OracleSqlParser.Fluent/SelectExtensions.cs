namespace OracleSqlParser.Fluent;

/// <summary>
/// Clause builders for <see cref="SelectStatement"/>. Each method changes the node in place and returns it, so calls chain
/// on built and parsed trees alike. Clauses of the query block (<c>DISTINCT</c>, <c>FROM</c>, joins, <c>WHERE</c>,
/// <c>GROUP BY</c>, <c>HAVING</c>, select items) need a <see cref="QuerySpecification"/> body and throw
/// <see cref="InvalidOperationException"/> otherwise. Ordering, row limiting, locking and <c>WITH</c> apply to any body.
/// </summary>
public static class SelectExtensions
{
    /// <summary><c>SELECT DISTINCT</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement Distinct(this SelectStatement statement)
    {
        QueryBlock(statement, "Distinct").Quantifier = QuantifierKind.Distinct;
        return statement;
    }

    /// <summary>Adds a table to <c>FROM</c>, as a comma-separated item, such as <c>FROM emp</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table name, such as <c>hr.emp</c>.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement From(this SelectStatement statement, string table) =>
        From(statement, Sql.Table(table));

    /// <summary>Adds an aliased table to <c>FROM</c>, such as <c>FROM emp e</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table name, such as <c>hr.emp</c>.</param>
    /// <param name="alias">The alias.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement From(this SelectStatement statement, string table, string alias) =>
        From(statement, Sql.Table(table, alias));

    /// <summary>Adds a table reference to <c>FROM</c>. A join reference is parenthesized, so it keeps its shape.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table reference.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement From(this SelectStatement statement, TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var query = QueryBlock(statement, "From");
        query.From ??= new FromClause();
        query.From.Tables.Add(table is JoinTable ? new ParenthesizedTable { Inner = table } : table);
        return statement;
    }

    /// <summary>Adds a subquery to <c>FROM</c>, such as <c>FROM (SELECT ...) s</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="subquery">The subquery.</param>
    /// <param name="alias">The required alias.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement From(this SelectStatement statement, SelectStatement subquery, string alias)
    {
        ArgumentNullException.ThrowIfNull(subquery);
        return From(statement, new SubqueryTable { Query = subquery, Alias = Sql.SingleName(alias, nameof(alias)) });
    }

    /// <summary>An inner join to the last <c>FROM</c> item: <c>FROM a JOIN b alias ON ...</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table name.</param>
    /// <param name="alias">The alias, or null for none.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement Join(this SelectStatement statement, string table, string? alias, Expression on) =>
        Join(statement, Sql.Table(table, alias), on);

    /// <summary>An inner join to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table reference to join.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement Join(this SelectStatement statement, TableReference table, Expression on) =>
        AddJoin(statement, "Join", JoinKind.Inner, table, on);

    /// <summary>A <c>LEFT OUTER JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table name.</param>
    /// <param name="alias">The alias, or null for none.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement LeftJoin(this SelectStatement statement, string table, string? alias, Expression on) =>
        LeftJoin(statement, Sql.Table(table, alias), on);

    /// <summary>A <c>LEFT OUTER JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table reference to join.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement LeftJoin(this SelectStatement statement, TableReference table, Expression on) =>
        AddJoin(statement, "LeftJoin", JoinKind.Left, table, on);

    /// <summary>A <c>RIGHT OUTER JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table name.</param>
    /// <param name="alias">The alias, or null for none.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement RightJoin(this SelectStatement statement, string table, string? alias, Expression on) =>
        RightJoin(statement, Sql.Table(table, alias), on);

    /// <summary>A <c>RIGHT OUTER JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table reference to join.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement RightJoin(this SelectStatement statement, TableReference table, Expression on) =>
        AddJoin(statement, "RightJoin", JoinKind.Right, table, on);

    /// <summary>A <c>FULL OUTER JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table name.</param>
    /// <param name="alias">The alias, or null for none.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement FullJoin(this SelectStatement statement, string table, string? alias, Expression on) =>
        FullJoin(statement, Sql.Table(table, alias), on);

    /// <summary>A <c>FULL OUTER JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table reference to join.</param>
    /// <param name="on">The join condition.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement FullJoin(this SelectStatement statement, TableReference table, Expression on) =>
        AddJoin(statement, "FullJoin", JoinKind.Full, table, on);

    /// <summary>A <c>CROSS JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table name.</param>
    /// <param name="alias">The alias, or null for none.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement CrossJoin(this SelectStatement statement, string table, string? alias = null) =>
        CrossJoin(statement, Sql.Table(table, alias));

    /// <summary>A <c>CROSS JOIN</c> to the last <c>FROM</c> item.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="table">The table reference to join.</param>
    /// <exception cref="InvalidOperationException">There is no <c>FROM</c> item to join to, or the body is not a query block.</exception>
    public static SelectStatement CrossJoin(this SelectStatement statement, TableReference table) =>
        AddJoin(statement, "CrossJoin", JoinKind.Cross, table, on: null);

    /// <summary>Sets the <c>WHERE</c> condition, replacing any existing one.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="condition">The search condition.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement Where(this SelectStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        QueryBlock(statement, "Where").Where = new WhereClause { Condition = condition };
        return statement;
    }

    /// <summary>Adds an AND condition to <c>WHERE</c>. With no existing condition it acts as <see cref="Where"/>.</summary>
    /// <remarks>An existing <c>OR</c> condition is grouped, so <c>a = 1 OR b = 2</c> becomes <c>(a = 1 OR b = 2) AND c = 3</c>.</remarks>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="condition">The condition to add.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement AndWhere(this SelectStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var query = QueryBlock(statement, "AndWhere");
        query.Where = new WhereClause { Condition = Conditions.And(query.Where?.Condition, condition) };
        return statement;
    }

    /// <summary>Adds an OR condition to <c>WHERE</c>. With no existing condition it acts as <see cref="Where"/>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="condition">The condition to add.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement OrWhere(this SelectStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var query = QueryBlock(statement, "OrWhere");
        query.Where = new WhereClause { Condition = Conditions.Or(query.Where?.Condition, condition) };
        return statement;
    }

    /// <summary>Removes the <c>WHERE</c> clause, if any.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement RemoveWhere(this SelectStatement statement)
    {
        QueryBlock(statement, "RemoveWhere").Where = null;
        return statement;
    }

    /// <summary>Appends grouping expressions to <c>GROUP BY</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="expressions">The grouping expressions, in order.</param>
    /// <exception cref="ArgumentException">No expression is given.</exception>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement GroupBy(this SelectStatement statement, params Expression[] expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        if (expressions.Length == 0) throw new ArgumentException("GROUP BY needs at least one expression.", nameof(expressions));
        if (expressions.Any(e => e is null)) throw new ArgumentNullException(nameof(expressions), "A grouping expression is null.");
        var query = QueryBlock(statement, "GroupBy");
        query.GroupBy ??= new GroupByClause();
        foreach (var expression in expressions) query.GroupBy.Items.Add(new ExpressionGrouping { Expression = expression });
        return statement;
    }

    /// <summary>Appends grouping columns to <c>GROUP BY</c>, given as names.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="columns">Column names, not SQL text.</param>
    /// <exception cref="ArgumentException">No name is given, or a name is not a plain name.</exception>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement GroupBy(this SelectStatement statement, params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return GroupBy(statement, columns.Select(c => (Expression)Sql.Col(c)).ToArray());
    }

    /// <summary>Sets the <c>HAVING</c> condition, replacing any existing one.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="condition">The search condition.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement Having(this SelectStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        QueryBlock(statement, "Having").Having = new HavingClause { Condition = condition };
        return statement;
    }

    /// <summary>Appends an ascending sort key to <c>ORDER BY</c>. Works on any body; on a set operation it orders the whole result.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="expression">The sort key.</param>
    public static SelectStatement OrderBy(this SelectStatement statement, Expression expression) =>
        AddOrderItem(statement, expression, descending: false, nulls: null);

    /// <summary>Appends an ascending sort key to <c>ORDER BY</c>, with an explicit nulls ordering.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="expression">The sort key.</param>
    /// <param name="nulls">Where nulls sort.</param>
    public static SelectStatement OrderBy(this SelectStatement statement, Expression expression, NullsOrdering nulls) =>
        AddOrderItem(statement, expression, descending: false, nulls);

    /// <summary>Appends a descending sort key to <c>ORDER BY</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="expression">The sort key.</param>
    public static SelectStatement OrderByDesc(this SelectStatement statement, Expression expression) =>
        AddOrderItem(statement, expression, descending: true, nulls: null);

    /// <summary>Appends a descending sort key to <c>ORDER BY</c>, with an explicit nulls ordering.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="expression">The sort key.</param>
    /// <param name="nulls">Where nulls sort.</param>
    public static SelectStatement OrderByDesc(this SelectStatement statement, Expression expression, NullsOrdering nulls) =>
        AddOrderItem(statement, expression, descending: true, nulls);

    /// <summary>Appends an ascending sort column, given as a name.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="column">A column name, not SQL text. Use the <c>Expression</c> overload for an ordinal or expression.</param>
    /// <exception cref="ArgumentException">The name is not a plain name.</exception>
    public static SelectStatement OrderBy(this SelectStatement statement, string column) =>
        AddOrderItem(statement, Sql.Col(column), descending: false, nulls: null);

    /// <summary>Appends an ascending sort column, given as a name, with an explicit nulls ordering.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="column">A column name, not SQL text.</param>
    /// <param name="nulls">Where nulls sort.</param>
    /// <exception cref="ArgumentException">The name is not a plain name.</exception>
    public static SelectStatement OrderBy(this SelectStatement statement, string column, NullsOrdering nulls) =>
        AddOrderItem(statement, Sql.Col(column), descending: false, nulls);

    /// <summary>Appends a descending sort column, given as a name.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="column">A column name, not SQL text.</param>
    /// <exception cref="ArgumentException">The name is not a plain name.</exception>
    public static SelectStatement OrderByDesc(this SelectStatement statement, string column) =>
        AddOrderItem(statement, Sql.Col(column), descending: true, nulls: null);

    /// <summary>Appends a descending sort column, given as a name, with an explicit nulls ordering.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="column">A column name, not SQL text.</param>
    /// <param name="nulls">Where nulls sort.</param>
    /// <exception cref="ArgumentException">The name is not a plain name.</exception>
    public static SelectStatement OrderByDesc(this SelectStatement statement, string column, NullsOrdering nulls) =>
        AddOrderItem(statement, Sql.Col(column), descending: true, nulls);

    /// <summary><c>OFFSET n ROWS</c>, replacing any existing offset.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="rows">The number of rows to skip. Must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> is negative.</exception>
    public static SelectStatement Offset(this SelectStatement statement, long rows)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows), "OFFSET cannot be negative.");
        statement.Offset = new OffsetClause { Offset = Sql.Lit(rows) };
        return statement;
    }

    /// <summary><c>FETCH FIRST n ROWS ONLY</c>, replacing any existing fetch.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="rows">The number of rows to return. Must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> is negative.</exception>
    public static SelectStatement FetchFirst(this SelectStatement statement, long rows)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows), "FETCH cannot be negative.");
        statement.Fetch = new FetchClause { Count = Sql.Lit(rows) };
        return statement;
    }

    /// <summary>Adds <c>FOR UPDATE</c> with no column list and the default wait.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    public static SelectStatement ForUpdate(this SelectStatement statement)
    {
        statement.ForUpdate = new ForUpdateClause();
        return statement;
    }

    /// <summary>Appends a common table expression to <c>WITH</c>, such as <c>WITH x AS (SELECT ...)</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="name">The name of the common table expression.</param>
    /// <param name="query">The query that defines it.</param>
    public static SelectStatement With(this SelectStatement statement, string name, SelectStatement query)
    {
        ArgumentNullException.ThrowIfNull(query);
        statement.With ??= new WithClause();
        statement.With.Ctes.Add(new CommonTableExpression { Name = Sql.Id(name), Query = query });
        return statement;
    }

    /// <summary><c>this UNION other</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="other">The query on the right. Its nodes are used as-is.</param>
    /// <exception cref="InvalidOperationException">This query has ORDER BY, OFFSET, FETCH or FOR UPDATE.</exception>
    public static SelectStatement Union(this SelectStatement statement, SelectStatement other) =>
        AddSetOperation(statement, other, SetOperator.Union, all: false, "Union");

    /// <summary><c>this UNION ALL other</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="other">The query on the right. Its nodes are used as-is.</param>
    /// <exception cref="InvalidOperationException">This query has ORDER BY, OFFSET, FETCH or FOR UPDATE.</exception>
    public static SelectStatement UnionAll(this SelectStatement statement, SelectStatement other) =>
        AddSetOperation(statement, other, SetOperator.Union, all: true, "UnionAll");

    /// <summary><c>this INTERSECT other</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="other">The query on the right. Its nodes are used as-is.</param>
    /// <exception cref="InvalidOperationException">This query has ORDER BY, OFFSET, FETCH or FOR UPDATE.</exception>
    public static SelectStatement Intersect(this SelectStatement statement, SelectStatement other) =>
        AddSetOperation(statement, other, SetOperator.Intersect, all: false, "Intersect");

    /// <summary><c>this MINUS other</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="other">The query on the right. Its nodes are used as-is.</param>
    /// <exception cref="InvalidOperationException">This query has ORDER BY, OFFSET, FETCH or FOR UPDATE.</exception>
    public static SelectStatement Minus(this SelectStatement statement, SelectStatement other) =>
        AddSetOperation(statement, other, SetOperator.Minus, all: false, "Minus");

    /// <summary>Appends a select item to the select list, such as <c>b AS bb</c>.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="expression">The expression. A bare star adds a star item and takes no alias.</param>
    /// <param name="alias">The alias, or null for none.</param>
    /// <exception cref="InvalidOperationException">The body is not a query block, or a bare star is given an alias.</exception>
    public static SelectStatement AddColumn(this SelectStatement statement, Expression expression, string? alias = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var query = QueryBlock(statement, "AddColumn");
        query.SelectList.Add(alias is null ? SelectItems.FromExpression(expression) : expression.As(alias));
        return statement;
    }

    /// <summary>Appends a column, given as a name, or <c>*</c> / <c>q.*</c>, to the select list.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="column">A name, not SQL text.</param>
    /// <exception cref="ArgumentException">The name is not a plain name.</exception>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement AddColumn(this SelectStatement statement, string column)
    {
        ArgumentNullException.ThrowIfNull(column);
        QueryBlock(statement, "AddColumn").SelectList.Add(SelectItems.FromName(column, nameof(column)));
        return statement;
    }

    /// <summary>Appends several columns, each given as a name, to the select list.</summary>
    /// <param name="statement">The statement to change, which is returned.</param>
    /// <param name="columns">Names, not SQL text.</param>
    /// <exception cref="ArgumentException">A name is not a plain name.</exception>
    /// <exception cref="InvalidOperationException">The body is not a query block.</exception>
    public static SelectStatement AddColumns(this SelectStatement statement, params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        foreach (var column in columns) AddColumn(statement, column);
        return statement;
    }

    /// <summary>
    /// The body as a query block. Throws with the body's type name when it is a set operation, a parenthesized query or a
    /// <c>VALUES</c> list, so the message says what the statement is.
    /// </summary>
    private static QuerySpecification QueryBlock(SelectStatement statement, string member) =>
        statement.Body as QuerySpecification
        ?? throw new InvalidOperationException(
            $"{member} requires a query block; the statement body is a {statement.Body.GetType().Name}.");

    private static SelectStatement AddJoin(SelectStatement statement, string member, JoinKind kind, TableReference table, Expression? on)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (kind != JoinKind.Cross) ArgumentNullException.ThrowIfNull(on);
        var query = QueryBlock(statement, member);
        if (query.From is not { Tables.Count: > 0 } from)
            throw new InvalidOperationException($"{member} needs a FROM item to join to; call From first.");
        // A join on the right must stay parenthesized, or the printed text would read back with a different shape.
        var right = table is JoinTable ? new ParenthesizedTable { Inner = table } : table;
        from.Tables[^1] = new JoinTable { Left = from.Tables[^1], Kind = kind, Right = right, On = on };
        return statement;
    }

    private static SelectStatement AddOrderItem(SelectStatement statement, Expression expression, bool descending, NullsOrdering? nulls)
    {
        ArgumentNullException.ThrowIfNull(expression);
        statement.OrderBy ??= new OrderByClause();
        statement.OrderBy.Items.Add(new OrderByItem { Expression = expression, Descending = descending, Nulls = nulls });
        return statement;
    }

    private static SelectStatement AddSetOperation(SelectStatement statement, SelectStatement other, SetOperator op, bool all, string member)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(statement, other))
            throw new ArgumentException("A query cannot be combined with itself; pass a second query built or parsed separately.", nameof(other));
        if (statement.OrderBy is not null || statement.Offset is not null || statement.Fetch is not null || statement.ForUpdate is not null)
            throw new InvalidOperationException(
                $"{member} cannot follow a left query with ORDER BY, OFFSET, FETCH or FOR UPDATE: that clause would apply to the " +
                "left query alone, or would move to the whole set operation. Add the ORDER BY after the set operation instead.");

        // The operand keeps its own row limiting, ordering and WITH, and a nested set operation keeps its grouping, so each
        // of those is printed inside parentheses. A plain query body joins the set operation directly.
        bool needsParentheses = other.OrderBy is not null || other.Offset is not null || other.Fetch is not null
            || other.ForUpdate is not null || other.With is not null || other.Body is SetOperation;
        QueryExpression right = needsParentheses ? new ParenthesizedQuery { Query = other } : other.Body;
        statement.Body = new SetOperation { Left = statement.Body, Operator = op, All = all, Right = right };
        return statement;
    }
}
