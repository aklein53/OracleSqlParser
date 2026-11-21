# OracleSqlParser.Fluent — design

Date: 2026-11-21. Status: draft for approval.

## Intent

A new package, `OracleSqlParser.Fluent`, that builds and edits OracleSqlParser syntax trees with chained calls instead
of hand-written object initializers. Today a `SELECT` in code means nesting `SelectStatement`, `QuerySpecification`,
`ExpressionSelectItem`, `ColumnReference` and `List<Identifier>` by hand, and setting `Identifier.Value`/`Quoted`
correctly yourself.

Who uses it and for what (from the user):

1. **Generating and modifying DML in an application.** Build statements from metadata, or parse existing SQL and change
   it (add a condition, add a column), then print it with `SqlScriptGenerator`.
2. **Test fixtures**, for OracleSqlParser's own tests or for the consuming application: write an expected tree
   concisely and compare it with `SqlNodeComparer.StructurallyEqual`.

## Goals

- Queries and DML: `SELECT` (with joins, grouping, ordering, set operations, CTEs, row limiting), `INSERT`, `UPDATE`,
  `DELETE`, `MERGE`, plus the expressions they need.
- One API for building new trees and editing parsed ones (the user chose "modify parsed SQL, then regenerate").
- **Core guarantee:** for any tree the builders produce, `SqlParser.Parse(generator.Generate(tree))` yields a tree that
  is `SqlNodeComparer.StructurallyEqual` to it. This is what makes the builders safe for fixtures: a built tree is
  exactly the tree the parser would produce for the printed SQL.
- Safe by construction: values go in as literals or bind variables, never as spliced SQL text (except through the
  explicit `Sql.Expr` escape hatch, which parses its input).

## Non-goals (v1)

- DDL, PL/SQL, SQL*Plus commands. They can come later on the same pattern.
- Preserving comments and layout when editing parsed SQL. Edits regenerate through `SqlScriptGenerator`, which
  normalises layout and drops comments (the user picked this over `SourceEditor` splicing).
- Covering every clause the parser models (MODEL, MATCH_RECOGNIZE, PIVOT, flashback, sampling, hierarchical queries,
  error logging, hints...). Those trees can still be built with plain object initializers and mixed with fluent calls,
  because everything is an ordinary node.
- Any change to the core `OracleSqlParser` public API (frozen at 1.0).

## Anti-goals (a failure even if it works)

- A built tree that prints SQL which parses back to a *different* tree. That would silently break fixtures.
- A second object model (builder classes with `.Build()`) that users have to convert to and from.
- An API that encourages building SQL by concatenating user input.

## Constraints

- Separate project `src/OracleSqlParser.Fluent`, modelled on `OracleSqlParser.Guard`: targets `net8.0;net10.0`,
  `ProjectReference` to the core, `PublicApiAnalyzers` with `PublicAPI.Shipped.txt` (empty) / `PublicAPI.Unshipped.txt`,
  XML docs, SourceLink, its own `README.md` packed into the package, versioned independently on 0.x.
- No reflection; must stay trimming/AOT-safe like the core.
- Build with `-warnaserror` stays clean; all existing tests unchanged and passing.
- No edits to core sources. If something can't be done without a core change, stop and report it.

## Approaches considered

- **A. Extension methods on the existing nodes (chosen by the user).** A static `Sql` factory returns real nodes
  (`SelectStatement`, `InsertStatement`, ...). Clause methods are extension methods that mutate the node in place and
  return it. Building and editing are the same calls; the result works with the generator, comparer, visitor and
  rewriter unchanged. Cost: in-place mutation (spans of a parsed tree go stale after an edit) and some calls only fit
  some shapes (e.g. `.Where` on a `UNION`), which must throw clearly.
- **B. Separate builder classes with `.Build()`** and `Builder.From(parsedNode)` for editing. Cleaner staging and never
  touches the original, but two models for one thing; editing parsed SQL means wrap, copy, rebuild. Rejected because it
  fights the main use (editing parsed DML).
- **C. String templates** (build SQL text, parse it). Rejected: not really fluent, and invites escaping/injection bugs.

## Design

Namespace `OracleSqlParser.Fluent`. Two public static classes do almost all the work:

- `Sql`: factories that create nodes (statements, expressions, table references).
- `FluentExtensions` (one or more `static class`es split by area, e.g. `SelectExtensions`, `DmlExtensions`,
  `ExpressionExtensions`): extension methods on node types.

### Names from strings

Every API that takes a name as a string (`"e.sal"`, `"hr.emp"`, `"\"MixedCase\""`) reads it with the parser's own
identifier rules: dotted parts, unquoted parts upper-cased in `Value`, quoted parts kept exactly with `Quoted = true`.
The simplest correct way is to run the text through the core lexer/parser (for example `SqlParser.ParseExpression` and
require a plain `ColumnReference`), so folding can never drift from the parser. Invalid names (`"a b"`, `""`,
`"1abc"`) throw `ArgumentException` with the parse error. Overloads taking `Identifier`/`params Identifier[]` exist for
callers who already have nodes.

### Expressions

Factories on `Sql`:

| Call | Produces |
|---|---|
| `Sql.Col("e.sal")` | `ColumnReference` |
| `Sql.Bind("min")` | `BindVariable` (`:min`) |
| `Sql.Lit(42)`, `Sql.Lit(1.5m)`, `Sql.Lit("x")`, `Sql.Lit(true)` | `Literal` (numbers formatted invariant-culture; strings stored unescaped, the generator quotes them) |
| `Sql.Null` | `NULL` literal |
| `Sql.Func("NVL", a, b)` | `FunctionCall` (name read with the name rules) |
| `Sql.Star()`, `Sql.Star("e")` | `*`, `e.*` |
| `Sql.Exists(query)`, `Sql.Subquery(query)` | `ExistsExpression`, `SubqueryExpression` |
| `Sql.Case()...` | simple and searched `CaseExpression` (`.When(c, v).Else(v)`) |
| `Sql.Expr("e.sal > :min")` | whatever `SqlParser.ParseExpression` returns (escape hatch) |

Extension methods on `Expression`: comparisons `Eq Ne Lt Le Gt Ge`; logic `And Or Not`; arithmetic `Plus Minus Times
DividedBy Concat`; `In(params Expression[])`, `In(SelectStatement)`, `NotIn`, `Between(lo, hi)`, `Like(pattern)`,
`IsNull()`, `IsNotNull()`; `As("alias")` where an alias is allowed (select items). Exact method names are the
builder's call, but they must read naturally in a chain and not clash with `object` members (`Equals`).

Each produces the node type and operator enum the parser uses for the same SQL.

**Parentheses are inserted as nodes.** The parser keeps every written pair of parentheses as a
`ParenthesizedExpression` (`Parser.Expressions.cs:386`), and `SqlNodeComparer` compares it as a node. The generator
only adds parentheses as text when it prints (`SqlScriptGenerator.Expressions.cs:85-111`). So whenever an operand would
be printed in parentheses, or would re-associate on reparse, the fluent call wraps it in a `ParenthesizedExpression`
itself. That makes the built tree the one the parser returns for the printed text. Examples:
- `a.Or(b).And(c)` builds `And(Paren(Or(a, b)), c)`.
- `a.Minus(b.Minus(c))` builds `Minus(a, Paren(Minus(b, c)))`.
- `a.And(b.And(c))` builds `And(a, Paren(And(b, c)))`. It prints `a AND (b AND c)`, not the left-deep `a AND b AND c`.

The rule lives in one internal helper that mirrors the generator's precedence table, and the round-trip tests are the
gate. This also covers `AndWhere`: an existing `a OR b` condition becomes `Paren(a OR b) AND c`.

Other parser shapes to match:
- `Sql.Lit` with a negative number builds `UnaryExpression(Minus, Literal)`, as the parser does
  (`Parser.Expressions.cs:253-258`).
- `Sql.Star()` in a select list becomes a `StarSelectItem`, not an `ExpressionSelectItem` holding a `StarExpression`
  (`Parser.cs:823-825`).
- `Sql.Func` rejects names the parser routes to special forms, such as `CAST`, `TRIM` and `EXTRACT`
  (`Parser.Expressions.cs:404-406`). It throws `ArgumentException` and points to the dedicated factory or `Sql.Expr`.

### Statements and clauses

Factories: `Sql.Select(params ...)` (items as strings, expressions or `SelectItem`s), `Sql.InsertInto(table)`,
`Sql.Update(table)`, `Sql.DeleteFrom(table)`, `Sql.MergeInto(table)`, `Sql.Table("hr.emp", alias?)`,
`Sql.Script(params Statement[])`.

Extension methods (all return the node they were called on, so chains continue):

- `SelectStatement`: `Distinct`, `From(table[, alias])` (repeatable; adds to `FromClause.Tables`), `Join`/`LeftJoin`/
  `RightJoin`/`FullJoin(table, alias, on)`, `CrossJoin`, `Where`, `AndWhere`, `OrWhere`, `GroupBy`, `Having`,
  `OrderBy`/`OrderByDesc` (with an optional nulls ordering), `Offset(n)`, `FetchFirst(n)`, `With(name, query)` for CTEs,
  `Union`/`UnionAll`/`Intersect`/`Minus(other)`, `AddColumn`/`AddColumns`, `ForUpdate`.
- `InsertStatement`: `Columns(...)`, `Values(...)` (one call per row; multiple rows allowed where Oracle 23 allows
  them), `Query(select)` for `INSERT ... SELECT`, `Returning(...).Into(...)`.
- `UpdateStatement`: `Set(column, value)` (repeatable), `Where`, `AndWhere`, `OrWhere`, `Returning`.
- `DeleteStatement`: `Where`, `AndWhere`, `OrWhere`, `Returning`.
- `MergeStatement`: `Using(table or query, alias)`, `On(condition)`, `WhenMatchedUpdate(set...)` with optional
  `Where`/`Delete Where`, `WhenNotMatchedInsert(columns, values)` with optional `Where`.

Required properties (`SelectStatement.Body`, `UpdateStatement.Set`, `MergeStatement.On`, ...) are filled with valid
placeholders or built up by the chain. Printing a statement before it's complete (for example `UPDATE` with no
`Set`, or `MERGE` with no `On`) must not produce invalid SQL. The core generator does not check this: it prints
`SET ` and `ON ` with nothing after them (`SqlScriptGenerator.Dml.cs:148-200`). So the fluent `ToSql()` validates
first and throws `InvalidOperationException` naming what's missing. A public `node.Validate()` exposes the same
check. The Fluent README states that calling `SqlScriptGenerator.Generate` directly on a half-built tree is not
checked.

**Joins** attach to the preceding `FROM` item. `From("a").Join("b", ...)` replaces the last entry of
`FromClause.Tables` with a `JoinTable(Left: a, Right: b)`. That is the shape the parser builds (`Parser.cs:902`).
Calling `Join` with no `FROM` item throws.

**Set operations** (`Union`, `UnionAll`, `Intersect`, `Minus`):
- If the receiver already has `ORDER BY`, `OFFSET`, `FETCH` or `FOR UPDATE`, the call throws. Hoisting those clauses
  silently would change what the query returns.
- If the operand has any of those clauses, or a `WITH`, it is wrapped in a `ParenthesizedQuery`. That is valid Oracle
  and matches the parser's shape.
- Otherwise the operand's body becomes the right side of a `SetOperation`. An `ORDER BY` added afterwards applies to
  the whole union.

### Editing parsed trees

The same extension methods work on parsed nodes. Rules:

- **Shape mismatch throws.** Clause methods on `SelectStatement` act on its `QuerySpecification` body. If the body is a
  `SetOperation`, `ParenthesizedQuery` or `VALUES` list, `Where`/`From`/`AddColumn`/... throw
  `InvalidOperationException` naming the shape. `OrderBy`/`Fetch`/`Offset`/`ForUpdate` apply to the statement and work
  for every body.
- **`Where(c)` replaces, `AndWhere(c)` combines.** `AndWhere` on a statement with no `WHERE` behaves like `Where`.
  Otherwise it becomes `existing AND c` (with the same parenthesization rule as above, so `a OR b` stays grouped).
  `OrWhere` likewise. `RemoveWhere()` clears it.
- **Mutation is in place.** Documented: spans and token indexes of the edited parts are stale afterwards, so use
  `SqlScriptGenerator`, not `SourceEditor`, on an edited tree. Callers who need the original call the existing
  `DeepClone<T>()` first.
- **Nodes are not shared.** Passing the same expression node into two places would alias it. The builder decides
  whether to clone on insert or document "don't reuse a node"; either way it must be tested.

### Printing

`node.ToSql(SqlScriptGeneratorOptions? options = null, OracleVersion target = OracleVersion.Oracle23)`: it validates
completeness, prints with `SqlScriptGenerator`, and then checks the printed SQL against `target`.

### Target Oracle version

The user's app runs on both 19c and 23ai, so builders must not quietly produce SQL that 19c rejects.
`Validate(OracleVersion target = Oracle23)` and `ToSql(..., target)` check the version by reparsing the printed text
with `new ParseOptions { OracleVersion = target }`. Any version-gate `ParseError` becomes an `InvalidOperationException`
that lists each construct and the version it needs, for example `'IS TRUE/FALSE' requires Oracle 23 or later
(targeting Oracle 19)`.

This reuses the core's own version gates (listed in COVERAGE.md), so Fluent never keeps a second copy of which
construct needs which version. The default is `Oracle23`, matching `ParseOptions`. The cost is one extra parse per
`ToSql` call, which is acceptable for DML generation; plain `SqlScriptGenerator.Generate` skips it. Tests cover
`Sql.Lit(true)` and multi-row `Values` against both targets.

### Example

```csharp
using OracleSqlParser.Fluent;

var q = Sql.Select("e.name", "e.sal")
    .From("emp", "e")
    .LeftJoin("dept", "d", Sql.Col("e.dept_id").Eq(Sql.Col("d.id")))
    .Where(Sql.Col("e.sal").Gt(Sql.Bind("min")))
    .OrderByDesc("e.sal")
    .FetchFirst(10);
q.ToSql();

var parsed = (SelectStatement)SqlParser.ParseStatement("SELECT * FROM orders o WHERE o.status = 'OPEN'");
parsed.AndWhere(Sql.Col("o.tenant_id").Eq(Sql.Bind("tenant")));
parsed.ToSql(); // SELECT * FROM orders o WHERE o.status = 'OPEN' AND o.tenant_id = :tenant

var u = Sql.Update("emp").Set("sal", Sql.Col("sal").Times(Sql.Lit(1.1m))).Where(Sql.Col("id").Eq(Sql.Bind("id")));
```

## Packaging and release

- `src/OracleSqlParser.Fluent` (package `OracleSqlParser.Fluent`, first version `0.1.0-preview.1`), added to
  `OracleSqlParser.slnx`.
- `tests/OracleSqlParser.Fluent.Tests` (xunit, same package versions as `OracleSqlParser.Guard.Tests`).
- `.github/workflows/build.yml`: build, test and pack the new projects alongside Guard, and push the package in the
  existing `publish` job with `--skip-duplicate` (same rule as Guard: an unchanged version is skipped).
- README: a short "Fluent builders (separate package)" section linking to `src/OracleSqlParser.Fluent/README.md`;
  a `CHANGELOG.md` entry under `[Unreleased]`; release steps mention bumping the Fluent version.

## Verification

- **Round-trip test for every builder:** assert the printed SQL text, and assert
  `SqlNodeComparer.StructurallyEqual(built, SqlParser.ParseStatement(printed))`. A helper makes this one line per test.
- **Fixture-equivalence tests:** for a set of SQL strings, a fluent-built tree is structurally equal to the parsed tree
  (covers name folding, quoted names, operator precedence, parenthesization).
- **Editing tests:** each edit method on parsed input, including the shape-mismatch throws and `AndWhere` on an `OR`
  condition.
- **Name parsing tests:** quoted, mixed-case, schema-qualified, db-link-free, invalid names throw.
- `dotnet build -warnaserror` clean; full existing test suite passes unchanged; `PublicAPI.Unshipped.txt` lists exactly
  the intended public surface.

## Decided by me (open to correction)

- **Versioning:** 0.x preview, independent of the core, like Guard. **No `[Experimental]` attribute**, unlike Guard:
  the attribute forces every consumer (including fixture code) to suppress a diagnostic, and 0.x already signals that
  the API may change. Guard needed it because it is security-adjacent; this isn't.
- **Names from strings go through the core parser** so folding always matches.
- **Parentheses become `ParenthesizedExpression` nodes** wherever the printed text would have them. This follows from
  the fixture goal: it is the only option that keeps built trees structurally equal to parsed ones. (The reviewer
  noted this reverses my first draft.)
- **`ToSql()` validates completeness**, and `Validate()` is public. The core generator is left unchanged.
- **Set operations throw** when the receiver already has a row-limiting or ordering clause, and wrap such an operand
  in `ParenthesizedQuery`.
- **The version check works by reparsing the printed SQL** with the target `OracleVersion`, rather than through a
  separate Fluent-side table of which construct needs which version. The default target is 23.
- **`IsAotCompatible`** is set in the csproj, as in the core. Guard's csproj lacks it, so don't copy that part as-is.

## Left to the builder

- Exact file split, method names within the families above, and overload sets (`string` vs `Expression` vs
  `Identifier`).
- Whether incompleteness is checked in `ToSql()` or left to the generator, as long as invalid SQL is never printed.
- Clone-on-insert versus documented no-reuse, as long as it is tested.
- Which extra clauses (hints, `RETURNING` details, `FOR UPDATE OF ... SKIP LOCKED`) make v1, beyond the list above.
