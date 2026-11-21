namespace OracleSqlParser.Fluent;

/// <summary>
/// Clauses of <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c> and <c>MERGE</c>. Each method changes the statement in place and returns
/// it, so the chain continues. Column names are names, never SQL text; only <see cref="Sql.Expr"/> takes SQL.
/// </summary>
public static class DmlExtensions
{
    /// <summary>Sets the column list of an <c>INSERT</c>, replacing any earlier list. Each name is read with the name rules.</summary>
    /// <param name="statement">The insert.</param>
    /// <param name="columns">The column names, such as <c>id</c> or <c>e.id</c>. Empty clears the list.</param>
    /// <returns>The same statement.</returns>
    public static InsertStatement Columns(this InsertStatement statement, params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(columns);
        statement.Columns = columns.Select(c => Sql.Col(c)).ToList();
        return statement;
    }

    /// <summary>Adds one row to the <c>VALUES</c> list of an <c>INSERT</c>. Call it again for more rows (Oracle 23 and later).</summary>
    /// <param name="statement">The insert.</param>
    /// <param name="values">The values of the row, in column order.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The insert already has a <c>Query</c>; an insert takes rows or a query, not both.</exception>
    public static InsertStatement Values(this InsertStatement statement, params Expression[] values)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0) throw new ArgumentException("A VALUES row needs at least one value.", nameof(values));
        if (values.Any(v => v is null)) throw new ArgumentNullException(nameof(values), "A value is null.");
        if (statement.Query is not null)
            throw new InvalidOperationException("INSERT already has a query; Values cannot be added. An INSERT takes either VALUES rows or a query, not both.");
        if (statement.Values?.Record is not null)
            throw new InvalidOperationException("INSERT has a PL/SQL record in VALUES (VALUES rec); rows cannot be added beside it.");
        statement.Values ??= new ValuesClause();
        statement.Values.Rows.Add(new RowValue { Values = [.. values] });
        return statement;
    }

    /// <summary>Sets the rows of an <c>INSERT ... SELECT</c> from a query, replacing any earlier query.</summary>
    /// <param name="statement">The insert.</param>
    /// <param name="query">The query that supplies the rows. Used as-is.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The insert already has <c>VALUES</c> rows; an insert takes rows or a query, not both.</exception>
    public static InsertStatement Query(this InsertStatement statement, SelectStatement query)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(query);
        if (statement.Values is not null)
            throw new InvalidOperationException("INSERT already has VALUES rows; a query cannot be added. An INSERT takes either VALUES rows or a query, not both.");
        statement.Query = query;
        return statement;
    }

    /// <summary>Adds an assignment to the <c>SET</c> clause of an <c>UPDATE</c>. Call it again for more columns.</summary>
    /// <param name="statement">The update.</param>
    /// <param name="column">The column name, such as <c>sal</c> or <c>e.sal</c>.</param>
    /// <param name="value">The new value. Used as-is.</param>
    /// <returns>The same statement.</returns>
    public static UpdateStatement Set(this UpdateStatement statement, string column, Expression value)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(value);
        statement.Set.Items.Add(new SetItem { Columns = [Sql.Col(column)], Value = value });
        return statement;
    }

    /// <summary>Sets the <c>WHERE</c> condition of an <c>UPDATE</c>, replacing any earlier one.</summary>
    /// <param name="statement">The update.</param>
    /// <param name="condition">The search condition. Used as-is.</param>
    /// <returns>The same statement.</returns>
    public static UpdateStatement Where(this UpdateStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = NewWhere(condition);
        return statement;
    }

    /// <summary>Adds <paramref name="condition"/> to the <c>WHERE</c> of an <c>UPDATE</c> with AND. Without a <c>WHERE</c> it acts as <see cref="Where(UpdateStatement, Expression)"/>.</summary>
    /// <param name="statement">The update.</param>
    /// <param name="condition">The condition to add.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The existing condition is <c>WHERE CURRENT OF</c>, which cannot be combined.</exception>
    public static UpdateStatement AndWhere(this UpdateStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = NewWhere(Conditions.And(ExistingCondition(statement.Where), RequiredCondition(condition)));
        return statement;
    }

    /// <summary>Adds <paramref name="condition"/> to the <c>WHERE</c> of an <c>UPDATE</c> with OR. Without a <c>WHERE</c> it acts as <see cref="Where(UpdateStatement, Expression)"/>.</summary>
    /// <param name="statement">The update.</param>
    /// <param name="condition">The condition to add.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The existing condition is <c>WHERE CURRENT OF</c>, which cannot be combined.</exception>
    public static UpdateStatement OrWhere(this UpdateStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = NewWhere(Conditions.Or(ExistingCondition(statement.Where), RequiredCondition(condition)));
        return statement;
    }

    /// <summary>Removes the <c>WHERE</c> clause of an <c>UPDATE</c>.</summary>
    /// <param name="statement">The update.</param>
    /// <returns>The same statement.</returns>
    public static UpdateStatement RemoveWhere(this UpdateStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = null;
        return statement;
    }

    /// <summary>Sets the <c>WHERE</c> condition of a <c>DELETE</c>, replacing any earlier one.</summary>
    /// <param name="statement">The delete.</param>
    /// <param name="condition">The search condition. Used as-is.</param>
    /// <returns>The same statement.</returns>
    public static DeleteStatement Where(this DeleteStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = NewWhere(condition);
        return statement;
    }

    /// <summary>Adds <paramref name="condition"/> to the <c>WHERE</c> of a <c>DELETE</c> with AND. Without a <c>WHERE</c> it acts as <see cref="Where(DeleteStatement, Expression)"/>.</summary>
    /// <param name="statement">The delete.</param>
    /// <param name="condition">The condition to add.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The existing condition is <c>WHERE CURRENT OF</c>, which cannot be combined.</exception>
    public static DeleteStatement AndWhere(this DeleteStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = NewWhere(Conditions.And(ExistingCondition(statement.Where), RequiredCondition(condition)));
        return statement;
    }

    /// <summary>Adds <paramref name="condition"/> to the <c>WHERE</c> of a <c>DELETE</c> with OR. Without a <c>WHERE</c> it acts as <see cref="Where(DeleteStatement, Expression)"/>.</summary>
    /// <param name="statement">The delete.</param>
    /// <param name="condition">The condition to add.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The existing condition is <c>WHERE CURRENT OF</c>, which cannot be combined.</exception>
    public static DeleteStatement OrWhere(this DeleteStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = NewWhere(Conditions.Or(ExistingCondition(statement.Where), RequiredCondition(condition)));
        return statement;
    }

    /// <summary>Removes the <c>WHERE</c> clause of a <c>DELETE</c>.</summary>
    /// <param name="statement">The delete.</param>
    /// <returns>The same statement.</returns>
    public static DeleteStatement RemoveWhere(this DeleteStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Where = null;
        return statement;
    }

    /// <summary>
    /// Sets the <c>RETURNING ... INTO</c> clause of an <c>INSERT</c>, replacing any earlier one. The parser reads RETURNING on
    /// <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c>; it does not read it on <c>MERGE</c>, so there is no overload for <c>MERGE</c>.
    /// </summary>
    /// <param name="statement">The insert.</param>
    /// <param name="columns">The returned column names, such as <c>id</c>.</param>
    /// <param name="into">The receiving targets, one per column: a bind such as <c>:out_id</c>, or a plain variable name.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="ArgumentException">The lists are empty or differ in length, a column is not a name, or a target is neither a bind nor a plain name.</exception>
    public static InsertStatement Returning(this InsertStatement statement, string[] columns, string[] into)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Returning = NewReturning(columns, into);
        return statement;
    }

    /// <summary>Sets the <c>RETURNING ... INTO</c> clause of an <c>UPDATE</c>, replacing any earlier one.</summary>
    /// <param name="statement">The update.</param>
    /// <param name="columns">The returned column names, such as <c>sal</c>.</param>
    /// <param name="into">The receiving targets, one per column: a bind such as <c>:new_sal</c>, or a plain variable name.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="ArgumentException">The lists are empty or differ in length, a column is not a name, or a target is neither a bind nor a plain name.</exception>
    public static UpdateStatement Returning(this UpdateStatement statement, string[] columns, string[] into)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Returning = NewReturning(columns, into);
        return statement;
    }

    /// <summary>Sets the <c>RETURNING ... INTO</c> clause of a <c>DELETE</c>, replacing any earlier one.</summary>
    /// <param name="statement">The delete.</param>
    /// <param name="columns">The returned column names, such as <c>name</c>.</param>
    /// <param name="into">The receiving targets, one per column: a bind such as <c>:old_name</c>, or a plain variable name.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="ArgumentException">The lists are empty or differ in length, a column is not a name, or a target is neither a bind nor a plain name.</exception>
    public static DeleteStatement Returning(this DeleteStatement statement, string[] columns, string[] into)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Returning = NewReturning(columns, into);
        return statement;
    }

    /// <summary>The <c>RETURNING</c> clause for the given columns and targets, with the counts checked.</summary>
    private static ReturningClause NewReturning(string[] columns, string[] into)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(into);
        if (columns.Length == 0) throw new ArgumentException("RETURNING needs at least one column.", nameof(columns));
        if (columns.Length != into.Length)
            throw new ArgumentException($"RETURNING lists {columns.Length} column(s) but INTO has {into.Length} target(s); they must match.", nameof(into));
        return new ReturningClause
        {
            Items = columns.Select(c => new ReturningItem { Expression = Sql.Col(c) }).ToList(),
            Into = new IntoClause { Targets = into.Select(IntoTarget).ToList() },
        };
    }

    /// <summary>Adds a <c>WHEN MATCHED THEN UPDATE SET</c> clause to a <c>MERGE</c>. Set the clause's <c>WHERE</c> with <see cref="WhenMatchedUpdateWhere"/>.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="set">The assignments as (column name, new value) pairs. At least one.</param>
    /// <returns>The same statement.</returns>
    public static MergeStatement WhenMatchedUpdate(this MergeStatement statement, params (string Column, Expression Value)[] set)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(set);
        if (set.Length == 0) throw new ArgumentException("WHEN MATCHED UPDATE needs at least one assignment.", nameof(set));
        var items = new List<SetItem>(set.Length);
        foreach (var (column, value) in set)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(set));
            items.Add(new SetItem { Columns = [Sql.Col(column)], Value = value });
        }
        statement.Clauses.Add(new MergeMatchedClause { Set = new SetClause { Items = items } });
        return statement;
    }

    /// <summary>Adds a <c>WHEN NOT MATCHED THEN INSERT</c> clause to a <c>MERGE</c>. Set its <c>WHERE</c> with <see cref="WhenNotMatchedWhere"/>.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="columns">The column names to insert into. Empty means the insert has no column list.</param>
    /// <param name="values">The values of the one inserted row. At least one.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="ArgumentException">There are no values, or columns are given and their count differs from the values.</exception>
    public static MergeStatement WhenNotMatchedInsert(this MergeStatement statement, string[] columns, Expression[] values)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0) throw new ArgumentException("WHEN NOT MATCHED INSERT needs at least one value.", nameof(values));
        if (columns.Length > 0 && columns.Length != values.Length)
            throw new ArgumentException($"WHEN NOT MATCHED INSERT lists {columns.Length} column(s) but {values.Length} value(s); they must match.", nameof(values));
        if (values.Any(v => v is null)) throw new ArgumentNullException(nameof(values), "A value is null.");
        statement.Clauses.Add(new MergeNotMatchedClause
        {
            Columns = columns.Select(c => Sql.Col(c)).ToList(),
            Values = new ValuesClause { Rows = [new RowValue { Values = [.. values] }] },
        });
        return statement;
    }

    /// <summary>Sets the source of a <c>MERGE</c> to a table, replacing any earlier source.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="table">The dotted table name.</param>
    /// <returns>The same statement.</returns>
    public static MergeStatement Using(this MergeStatement statement, string table)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Source = Sql.Table(table);
        return statement;
    }

    /// <summary>Sets the source of a <c>MERGE</c> to a table with an alias, replacing any earlier source.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="table">The dotted table name.</param>
    /// <param name="alias">The single-name alias.</param>
    /// <returns>The same statement.</returns>
    public static MergeStatement Using(this MergeStatement statement, string table, string alias)
    {
        ArgumentNullException.ThrowIfNull(statement);
        statement.Source = Sql.Table(table, alias);
        return statement;
    }

    /// <summary>Sets the source of a <c>MERGE</c> to a subquery with its alias, replacing any earlier source.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="query">The source query. Used as-is.</param>
    /// <param name="alias">The alias of the subquery. Must be one name.</param>
    /// <returns>The same statement.</returns>
    public static MergeStatement Using(this MergeStatement statement, SelectStatement query, string alias)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(alias);
        statement.Source = new SubqueryTable { Query = query, Alias = Sql.SingleName(alias, nameof(alias)) };
        return statement;
    }

    /// <summary>Sets the <c>ON</c> condition of a <c>MERGE</c>. It is stored in parentheses, as the parser stores <c>ON (cond)</c>.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="condition">The join condition. Used as-is; wrapped in parentheses unless it already is.</param>
    /// <returns>The same statement.</returns>
    public static MergeStatement On(this MergeStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(condition);
        statement.On = condition is ParenthesizedExpression ? condition : new ParenthesizedExpression { Inner = condition };
        return statement;
    }

    /// <summary>Sets the <c>WHERE</c> of the last <c>WHEN MATCHED ... UPDATE</c> clause, replacing any earlier one.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="condition">The condition. Used as-is.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The merge has no <c>WHEN MATCHED</c> clause.</exception>
    public static MergeStatement WhenMatchedUpdateWhere(this MergeStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        LastMatched(statement, "WhenMatchedUpdateWhere").UpdateWhere = NewMergeWhere(condition);
        return statement;
    }

    /// <summary>Sets the <c>DELETE WHERE</c> of the last <c>WHEN MATCHED ... UPDATE</c> clause, replacing any earlier one.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="condition">The condition. Used as-is.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The merge has no <c>WHEN MATCHED</c> clause.</exception>
    public static MergeStatement WhenMatchedDeleteWhere(this MergeStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        LastMatched(statement, "WhenMatchedDeleteWhere").DeleteWhere = NewMergeWhere(condition);
        return statement;
    }

    /// <summary>Sets the <c>WHERE</c> of the last <c>WHEN NOT MATCHED</c> clause, replacing any earlier one.</summary>
    /// <param name="statement">The merge.</param>
    /// <param name="condition">The condition. Used as-is.</param>
    /// <returns>The same statement.</returns>
    /// <exception cref="InvalidOperationException">The merge has no <c>WHEN NOT MATCHED</c> clause.</exception>
    public static MergeStatement WhenNotMatchedWhere(this MergeStatement statement, Expression condition)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var last = statement.Clauses.OfType<MergeNotMatchedClause>().LastOrDefault()
            ?? throw new InvalidOperationException("WhenNotMatchedWhere needs a WHEN NOT MATCHED clause; add one with WhenNotMatchedInsert first.");
        last.Where = NewMergeWhere(condition);
        return statement;
    }

    /// <summary>The last <c>WHEN MATCHED</c> clause, or throws naming the method.</summary>
    private static MergeMatchedClause LastMatched(MergeStatement statement, string method) =>
        statement.Clauses.OfType<MergeMatchedClause>().LastOrDefault()
        ?? throw new InvalidOperationException($"{method} needs a WHEN MATCHED clause; add one with WhenMatchedUpdate first.");

    /// <summary>A <c>WHERE</c> clause of an <c>UPDATE</c> or <c>DELETE</c>. Such a clause may be <c>WHERE CURRENT OF</c>.</summary>
    private static WhereClause NewWhere(Expression condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new WhereClause { Condition = condition };
    }

    /// <summary>A <c>WHERE</c> clause of a MERGE action. The parser reads it as a plain condition, so <c>WHERE CURRENT OF</c> is refused.</summary>
    private static WhereClause NewMergeWhere(Expression condition) => new() { Condition = RequiredCondition(condition) };

    /// <summary>The existing condition of a <c>WHERE</c>, or null when there is none. <c>WHERE CURRENT OF</c> is not a condition and cannot be combined.</summary>
    private static Expression? ExistingCondition(WhereClause? where)
    {
        if (where?.Condition is CurrentOfExpression)
            throw new InvalidOperationException("WHERE CURRENT OF cannot be combined with AND or OR; replace it with Where instead.");
        return where?.Condition;
    }

    private static Expression RequiredCondition(Expression condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (condition is CurrentOfExpression)
            throw new InvalidOperationException("WHERE CURRENT OF cannot be combined with AND or OR; use it alone.");
        return condition;
    }

    /// <summary>
    /// A <c>RETURNING ... INTO</c> target: a bind such as <c>:out_id</c>, or a plain name. Anything else is rejected, because
    /// <see cref="Sql.Expr"/> is not used for targets.
    /// </summary>
    private static Expression IntoTarget(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var trimmed = target.Trim();
        if (trimmed.StartsWith(':')) return Sql.Bind(trimmed);
        return Sql.Col(trimmed);
    }
}
