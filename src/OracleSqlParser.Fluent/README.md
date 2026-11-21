# OracleSqlParser.Fluent

Fluent builders that create and edit [OracleSqlParser](https://github.com/aklein53/OracleSqlParser) syntax trees. They are for
two jobs: generating and modifying DML in an application, and writing expected trees in test fixtures.

The builders return ordinary parser nodes (`SelectStatement`, `InsertStatement`, `ColumnReference`, ...), so the generator,
`SqlNodeComparer`, the visitor and the rewriter all work on them unchanged.

The guarantee: every tree the builders produce is the tree the parser returns for the SQL they print. Parentheses that the
printed text needs are inserted as `ParenthesizedExpression` nodes, and names are read by the parser's own identifier rules.
`ToSql` checks this at run time, so a tree that would print as different SQL is refused.

## Installing

```
dotnet add package OracleSqlParser.Fluent --prerelease
```

The package depends on `OracleSqlParser`. It targets net8.0 and net10.0, is marked `IsAotCompatible`, and uses no reflection.

## Building from source

```
dotnet build OracleSqlParser.slnx -c Release -warnaserror
dotnet test tests/OracleSqlParser.Fluent.Tests -c Release
```

Add `-p:CI=true` to the test run to test net8.0 as well, when the .NET 8 runtime is installed.

## Quick start

```csharp
using OracleSqlParser;
using OracleSqlParser.Fluent;

var q = Sql.Select("e.name", "e.sal")
    .From("emp", "e")
    .LeftJoin("dept", "d", Sql.Col("e.dept_id").Eq(Sql.Col("d.id")))
    .Where(Sql.Col("e.sal").Gt(Sql.Bind("min")))
    .OrderByDesc("e.sal")
    .FetchFirst(10);
string sql = q.ToSql(target: OracleVersion.Oracle19);
// SELECT e.name, e.sal FROM emp e LEFT OUTER JOIN dept d ON e.dept_id = d.id WHERE e.sal > :min ORDER BY e.sal DESC FETCH FIRST 10 ROWS ONLY

var parsed = (SelectStatement)SqlParser.ParseStatement("SELECT * FROM orders o WHERE o.status = 'OPEN'");
parsed.AndWhere(Sql.Col("o.tenant_id").Eq(Sql.Bind("tenant")));
parsed.ToSql(); // SELECT * FROM orders o WHERE o.status = 'OPEN' AND o.tenant_id = :tenant

var update = Sql.Update("emp").Set("sal", Sql.Col("sal").Times(Sql.Lit(1.1m))).Where(Sql.Col("id").Eq(Sql.Bind("id")));
update.ToSql();
```

Test fixtures compare a built tree with a parsed one:

```csharp
Assert.True(SqlNodeComparer.StructurallyEqual(built, SqlParser.ParseStatement(expectedSql)));
```

## Names and literals

- A `string` parameter is a name, an alias, a bind name or a literal value. It is never SQL text. Only `Sql.Expr` parses SQL
  text. `Sql.Col("count(*)")` and `Select("a; drop table t")` throw `ArgumentException`.
- Names follow the parser's rules. Unquoted parts are upper-cased (`"e.sal"` is `E.SAL`). Quoted parts keep their case:
  `Sql.Col("\"Order\".\"Id\"")`. Invalid names (`"a b"`, `""`, `"1abc"`) throw `ArgumentException`.
- `Sql.Lit("O'Brien")` stores the text unescaped, and the generator quotes it. `Sql.Lit((string)null!)` throws; use `Sql.Null`.
- `Sql.Lit(decimal)` keeps its scale: `Sql.Lit(1.10m)` prints `1.10`. Numbers are formatted with the invariant culture.
- Negative numbers are a unary minus over a literal, as the parser reads `-1`.
- `Sql.Func` refuses the names the parser reads as special forms (`CAST`, `TRIM`, `EXTRACT`, ...). Use the dedicated factory
  or `Sql.Expr`.

## Editing parsed SQL

Edits change the node you call them on, and return it.

- **In place.** A parsed statement is mutated. Spans, line and column numbers, and token indexes of the edited parts are stale
  afterwards. Do not use them with `SourceEditor` on an edited tree. Print the edited tree with `ToSql` (or
  `SqlScriptGenerator`), which regenerates the SQL and drops comments and original layout.
- **Keep the original.** If you still need the parsed tree, call `DeepClone<T>()` first and edit the copy:
  `var copy = parsed.DeepClone<SelectStatement>().AndWhere(...)`.
- **Do not reuse a node.** The builders insert nodes as they are passed in. Passing one expression to two places makes the
  tree share it, and an edit to one place changes both. Build a new node for each place, or clone it with `DeepClone<T>()`.
- **Shape mismatch throws.** Clause methods on a `SELECT` act on its query block. On a `UNION`, a `VALUES` list or a
  parenthesized query, `Where`, `From`, `AddColumn` and the other query-block methods throw `InvalidOperationException` that
  names the body type. `OrderBy`, `Offset`, `FetchFirst` and `With` work on any body except a `VALUES` list. `ForUpdate` needs a
  query block.
- **`Where` replaces, `AndWhere` and `OrWhere` combine.** An existing `OR` condition is grouped:
  `a = 1 OR b = 2` with `AndWhere(c = 3)` becomes `(a = 1 OR b = 2) AND c = 3`.

## Printing and the target Oracle version

- `node.ToSql(options, target)` checks the node, prints it with `SqlScriptGenerator`, and checks the printed text. `Validate`
  does the same checks and returns the node.
- The check reparses the printed SQL with `ParseOptions { OracleVersion = target }`. Any diagnostic, including a version gate, is
  an `InvalidOperationException` that lists the messages. The reparsed tree must equal the node, or the call throws. The default
  target is Oracle 23. Pass `OracleVersion.Oracle19` for SQL that must run on 19c, for example:
  `Sql.InsertInto("t").Values(Sql.Lit(1)).Values(Sql.Lit(2)).ToSql(target: OracleVersion.Oracle19)` throws, because multi-row
  `VALUES` needs 23ai, and `Sql.Lit(true)` throws on 19c.
- `Validate` and `ToSql` check completeness first: an `UPDATE` with no `Set`, a `MERGE` with no source, `ON` or clause, an
  `INSERT` with neither `VALUES` nor a query, an empty select list, and an `INSERT` column list whose count differs from its
  query's select list. A `MERGE` built with `Sql.MergeInto("t")` but never given a source throws.
- `Validate` and `ToSql` on a `SqlScript`, `Statement` or `Expression` also check the reparse. Other nodes (a `WHERE` clause, a
  table reference) are checked for completeness only.
- `SqlScriptGenerator.Generate` does not run these checks. On a half-built tree it prints `SET ` or `ON ` with nothing after
  them. Call `ToSql` for built SQL.
- The reparse checks what the parser checks for the target version, and nothing more. Fluent does not add its own checks on
  the content of expressions you pass in. For example, `Sql.Expr("t.x(+) = u.x")` in an ANSI `LEFT JOIN ... ON` is printed,
  and `ToSql` accepts it, although Oracle rejects an outer-join marker in an ANSI join.

## Not covered in v1

- DDL, PL/SQL and SQL*Plus commands.
- Database links in table names (`hr.emp@remote`): `Sql.Table` throws.
- `MERGE ... RETURNING`: the parser rejects it, so there is no builder.
- Clauses the parser models but the builders do not: MODEL, MATCH_RECOGNIZE, PIVOT, flashback, sampling, hierarchical queries,
  error logging, hints. Build those nodes with object initializers and mix them with the fluent calls, since each is an ordinary
  node. The printed text is still checked by `ToSql`.
- Comments and layout in edited trees. Edits regenerate through `SqlScriptGenerator`, which normalises them.

## Versioning

`OracleSqlParser.Fluent` is versioned on its own, in 0.x. Any release may change its API. It has no `[Experimental]` attribute.
Its public surface is listed in `PublicAPI.Unshipped.txt` until it ships.
