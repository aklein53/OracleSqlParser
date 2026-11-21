namespace OracleSqlParser.Fluent;

public static partial class Sql
{
    /// <summary>An <c>INSERT INTO</c> statement for a table, with an optional alias. Add columns and rows with the chain.</summary>
    /// <param name="table">The dotted table name, such as <c>hr.emp</c>.</param>
    /// <param name="alias">The optional single-name alias.</param>
    /// <returns>A new <see cref="InsertStatement"/> with no rows yet.</returns>
    public static InsertStatement InsertInto(string table, string? alias = null) => InsertInto(Table(table, alias));

    /// <summary>An <c>INSERT INTO</c> statement for a table reference.</summary>
    /// <param name="table">The target table reference. Used as-is.</param>
    /// <returns>A new <see cref="InsertStatement"/> with no rows yet.</returns>
    public static InsertStatement InsertInto(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new InsertStatement { Target = table };
    }

    /// <summary>An <c>UPDATE</c> statement for a table, with an optional alias. Add assignments with <c>Set</c>.</summary>
    /// <param name="table">The dotted table name.</param>
    /// <param name="alias">The optional single-name alias.</param>
    /// <returns>A new <see cref="UpdateStatement"/> with no assignments yet.</returns>
    public static UpdateStatement Update(string table, string? alias = null) => Update(Table(table, alias));

    /// <summary>An <c>UPDATE</c> statement for a table reference.</summary>
    /// <param name="table">The target table reference. Used as-is.</param>
    /// <returns>A new <see cref="UpdateStatement"/> with no assignments yet.</returns>
    public static UpdateStatement Update(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new UpdateStatement { Target = table, Set = new SetClause { Items = [] } };
    }

    /// <summary>A <c>DELETE FROM</c> statement for a table, with an optional alias.</summary>
    /// <param name="table">The dotted table name.</param>
    /// <param name="alias">The optional single-name alias.</param>
    /// <returns>A new <see cref="DeleteStatement"/> with no condition.</returns>
    public static DeleteStatement DeleteFrom(string table, string? alias = null) => DeleteFrom(Table(table, alias));

    /// <summary>A <c>DELETE FROM</c> statement for a table reference.</summary>
    /// <param name="table">The target table reference. Used as-is.</param>
    /// <returns>A new <see cref="DeleteStatement"/> with no condition.</returns>
    public static DeleteStatement DeleteFrom(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new DeleteStatement { Target = table };
    }

    /// <summary>A <c>MERGE INTO</c> statement for a table, with an optional alias. Set the source with <c>Using</c> and the join with <c>On</c>.</summary>
    /// <param name="table">The dotted table name.</param>
    /// <param name="alias">The optional single-name alias.</param>
    /// <returns>A new <see cref="MergeStatement"/> with no source, condition or clauses yet.</returns>
    public static MergeStatement MergeInto(string table, string? alias = null) => MergeInto(Table(table, alias));

    /// <summary>A <c>MERGE INTO</c> statement for a table reference.</summary>
    /// <param name="table">The target table reference. Used as-is.</param>
    /// <returns>A new <see cref="MergeStatement"/> with no source, condition or clauses yet.</returns>
    public static MergeStatement MergeInto(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new MergeStatement { Target = table, Source = Placeholders.NewSource(), On = Placeholders.NewCondition() };
    }
}
