# OracleSqlParser.Fluent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new package, `OracleSqlParser.Fluent`, with extension-method builders that create and edit SELECT/INSERT/UPDATE/DELETE/MERGE trees.

**Architecture:** A static `Sql` factory returns real core nodes. Extension methods mutate a node in place and return it, so the same chain works on built trees and parsed trees. Every builder produces exactly the tree the parser returns for the printed SQL. Parentheses are inserted as `ParenthesizedExpression` nodes, and names are read by the core parser.

**Tech Stack:** C# (LangVersion latest), net8.0 + net10.0, xunit 2.9.3, Microsoft.CodeAnalysis.PublicApiAnalyzers 4.14.0.

**Spec:** `docs/superpowers/specs/2026-11-21-oraclesqlparser-fluent-design.md` (read it; it is the source of truth for behaviour).

## Global Constraints

- No edits to anything under `src/OracleSqlParser/` (core public API frozen at 1.0). If a task needs a core change, stop and report.
- New project `src/OracleSqlParser.Fluent`, namespace `OracleSqlParser.Fluent`, `<Version>0.1.0-preview.1</Version>`, modelled on `src/OracleSqlParser.Guard/OracleSqlParser.Guard.csproj` plus `<IsAotCompatible>true</IsAotCompatible>`. No `[Experimental]` attribute.
- No reflection. No NuGet runtime dependencies beyond the core project reference.
- Every public member listed in `src/OracleSqlParser.Fluent/PublicAPI.Unshipped.txt` (populate from `RS0016` build output; `PublicAPI.Shipped.txt` holds only `#nullable enable`).
- Build gate: `dotnet build OracleSqlParser.slnx -c Release -warnaserror` clean. Existing tests unchanged and passing.
- Round-trip guarantee: for every built node `n`, `SqlNodeComparer.StructurallyEqual(n, SqlParser.ParseStatement(n.ToSql()))` (or `ParseExpression` for expressions).
- Names given as strings go through the core parser; invalid names throw `ArgumentException`.
- Nodes passed into fluent methods are inserted as-is (not cloned). The Fluent README says to not reuse one node in two places, and to call `DeepClone<T>()` before editing when the original is still needed.
- Code style: match `src/OracleSqlParser.Guard/QueryGuard.cs` (file-scoped namespace, XML docs on every public member, short `<summary>`s).

## Review Focus

1. Culture: number literals must print `1.5` under `de-DE` (`CultureInfo.CurrentCulture` set in a test), not `1,5`. Owned by Task 2.
2. Strings with quotes or non-ASCII: `Sql.Lit("O'Brien")` and `Sql.Lit("naïve €")` round-trip. `Sql.Lit((string)null!)` throws `ArgumentNullException` (use `Sql.Null`). Owned by Task 2.
3. Quoted and keyword-like names: `Sql.Col("\"Order\".\"Id\"")` keeps case and `Quoted`. `Sql.Col("e.\"date\"")` round-trips. Owned by Task 1.
4. Edits on parsed trees of the wrong shape: `.Where` on a parsed `UNION` throws `InvalidOperationException` whose message names `SetOperation`. `AndWhere` on a parsed `a = 1 OR b = 2` prints `(a = 1 OR b = 2) AND c = 3`. Owned by Task 3.
5. Version gate on DML: `Sql.InsertInto("t").Values(Sql.Lit(1)).Values(Sql.Lit(2)).ToSql(target: OracleVersion.Oracle19)` throws, and the same call with Oracle23 succeeds. Owned by Task 5.

---

### Task 1: Project scaffolding and names

**Files:**
- Create: `src/OracleSqlParser.Fluent/OracleSqlParser.Fluent.csproj`, `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`, `README.md` (stub, one paragraph; Task 6 fills it), `Sql.cs`, `Names.cs` (internal)
- Create: `tests/OracleSqlParser.Fluent.Tests/OracleSqlParser.Fluent.Tests.csproj` (copy of `OracleSqlParser.Guard.Tests.csproj` with references swapped), `RoundTrip.cs`, `NameTests.cs`
- Modify: `OracleSqlParser.slnx` (add both projects)

**Interfaces:**
- Produces: `public static partial class Sql` in `Sql.cs` (later tasks add partial files `Sql.Expressions.cs`, `Sql.Statements.cs`).
- Produces: `public static ColumnReference Sql.Col(string name)` and `Sql.Col(params Identifier[] parts)`.
- Produces: `public static NamedTable Sql.Table(string name, string? alias = null)`.
- Produces: `public static Identifier Sql.Id(string name)` (one part; `"\"x\""` gives a quoted identifier).
- Produces (internal): `Names.Parse(string text) -> List<Identifier>`, which parses with `SqlParser.ParseExpression(text)`, requires a `ColumnReference` with `Attribute is null` and `!OuterJoin`, and otherwise throws `ArgumentException(paramName: "name")` that includes the parse message.
- Produces (test helper): `RoundTrip.AssertStatement(Statement built, string expectedSql)` asserts `new SqlScriptGenerator().Generate(built) == expectedSql` and `StructurallyEqual(built, SqlParser.ParseStatement(expectedSql))`. Also `RoundTrip.AssertExpression(Expression built, string expectedSql)`, the same with `ParseExpression`.

- [ ] **Step 1: Scaffold both projects**, add them to the slnx, and confirm with `dotnet build OracleSqlParser.slnx -c Release -warnaserror` (expected: success).
- [ ] **Step 2: Write failing `NameTests`:**
  - `Col("e.sal")` has parts `["E","SAL"]`, not quoted.
  - `Col("\"Order\".\"Id\"")` has parts `["Order","Id"]`, both `Quoted`.
  - `Table("hr.emp","e")` has `Name` `["HR","EMP"]` and `Alias.Value == "E"`.
  - `Col("a b")`, `Col("")`, `Col("1abc")`, `Col("x(+)")` and `Col("c%FOUND")` all throw `ArgumentException`.
  - `RoundTrip.AssertExpression(Sql.Col("e.\"date\""), "e.\"date\"")` passes. Use the generator's actual quoting in the expected text.
- [ ] **Step 3: Run** `dotnet test tests/OracleSqlParser.Fluent.Tests`. Expected: compile failure or FAIL.
- [ ] **Step 4: Implement** `Names.Parse`, `Sql.Col`, `Sql.Table` and `Sql.Id`. `Table` and `Id` reuse `Names.Parse`: an alias must be one part, and the table name parts move into `NamedTable.Name`.
- [ ] **Step 5: Run the tests** (expected: PASS) and the build gate. Populate `PublicAPI.Unshipped.txt` from the RS0016 output.
- [ ] **Step 6: Commit** `Fluent: project scaffolding and name parsing`.

### Task 2: Expressions

**Files:**
- Create: `src/OracleSqlParser.Fluent/Sql.Expressions.cs`, `ExpressionExtensions.cs`, `Parens.cs` (internal), `CaseBuilder` (inside `Sql.Expressions.cs` or its own file)
- Test: `tests/OracleSqlParser.Fluent.Tests/ExpressionTests.cs`

**Interfaces:**
- Consumes: `Names.Parse`, `Sql.Col`, `RoundTrip`.
- Produces on `Sql`:
  - `Bind(string name) -> BindVariable`
  - `Lit(int|long|decimal|double|string|bool) -> Expression`. Negative numbers become `UnaryExpression(Minus, Literal)`, and numbers are formatted with `CultureInfo.InvariantCulture`. `Lit(double)` throws `ArgumentOutOfRangeException` for NaN and Infinity.
  - `Null -> Literal` (static property)
  - `Func(string name, params Expression[] args) -> FunctionCall`. It throws `ArgumentException` for names the parser treats as special forms. Build the reject list by reading `Parser.Expressions.cs:~400-420`: at least CAST, TRIM, EXTRACT, TREAT and any others routed there.
  - `Star() -> StarExpression`, `Star(string qualifier) -> StarExpression`
  - `Exists(SelectStatement) -> ExistsExpression`, `Subquery(SelectStatement) -> SubqueryExpression`
  - `Case() -> CaseBuilder` (searched) and `Case(Expression operand) -> CaseBuilder` (simple). `CaseBuilder.When(Expression when, Expression then) -> CaseBuilder`, `.Else(Expression) -> CaseBuilder`, `.End() -> CaseExpression`. `End()` with no `When` throws `InvalidOperationException`.
  - `Expr(string sql) -> Expression`, which wraps `SqlParser.ParseExpression`. A `ParseException` becomes `ArgumentException`.
- Produces on `ExpressionExtensions` (extension methods on `Expression`, each returning `Expression`):
  - comparisons: `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge`
  - logic: `And`, `Or`, `Not`
  - arithmetic: `Plus`, `Minus`, `Times`, `DividedBy`, `Concat`
  - `In(params Expression[])`, `In(SelectStatement)`, `NotIn(params Expression[])`, `NotIn(SelectStatement)`
  - `Between(Expression low, Expression high)`, `Like(Expression pattern)`, `NotLike(Expression pattern)`
  - `IsNull()`, `IsNotNull()`
  - `As(string alias) -> ExpressionSelectItem`
- Produces (internal): `Parens.Wrap(Expression operand, int parentPrecedence, bool rightSide) -> Expression` and `Parens.Precedence(Expression) -> int`. They mirror `SqlScriptGenerator.Expressions.cs:85-111` exactly. An operand is wrapped in `ParenthesizedExpression` when its precedence is below the parent's, or when it is the right operand with equal precedence. Tasks 3 and 4 use `Conditions.And(Expression? existing, Expression added) -> Expression` and `Conditions.Or(...)` (internal, in `Parens.cs`) for `AndWhere`/`OrWhere`: a null `existing` returns `added`.
- For each operator method, the exact `BinaryOperator`/`UnaryOperator`/`LikeKind`/`IsTest` value is the one the parser produces for the same SQL. Check with `oraclesqlparser --shape -e "..."` or by parsing in a test.

- [ ] **Step 1: Write failing `ExpressionTests`**, each using `RoundTrip.AssertExpression`:
  - `Col("a").Eq(Bind("x"))` gives `a = :x`.
  - `Col("a").Eq(Lit(1)).Or(Col("b").Eq(Lit(2))).And(Col("c").Eq(Lit(3)))` gives `(a = 1 OR b = 2) AND c = 3`.
  - `Col("a").Minus(Col("b").Minus(Col("c")))` gives `a - (b - c)`.
  - `Col("a").And(Col("b").And(Col("c")))` gives `a AND (b AND c)`.
  - `Lit(-1)` gives `-1`.
  - `Lit(1.5m)` under `CultureInfo.CurrentCulture = new("de-DE")` gives `1.5`.
  - `Lit("O'Brien")` gives `'O''Brien'`, and `Lit("naïve €")` round-trips.
  - `Lit(true)` gives `TRUE`, and `Null` gives `NULL`.
  - `Func("NVL", Col("a"), Lit(0))` gives `NVL(a, 0)`.
  - `Col("a").In(Lit(1), Lit(2))` gives `a IN (1, 2)`. `Col("a").Between(Lit(1), Lit(2))` gives `a BETWEEN 1 AND 2`. `Col("a").Like(Lit("x%"))` gives `a LIKE 'x%'`. `Col("a").IsNotNull()` gives `a IS NOT NULL`. `Col("a").Not()`, `NotIn` and `NotLike` each round-trip.
  - `Case().When(Col("a").Eq(Lit(1)), Lit("one")).Else(Lit("other")).End()` gives the matching `CASE ... END`.
  - `Expr("a > :b")` equals `ParseExpression("a > :b")`.
  - Throws: `Func("CAST", Col("a"))` throws `ArgumentException`, `Lit((string)null!)` throws `ArgumentNullException`, `Lit(double.NaN)` throws `ArgumentOutOfRangeException`, `Expr("a >")` throws `ArgumentException`, and `Case().End()` throws `InvalidOperationException`.
  - Expected strings are what `SqlScriptGenerator` prints by default. Where it differs in spacing or casing, use its output, but never weaken the `StructurallyEqual` half.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** the factories, extensions and `Parens`/`Conditions`.
- [ ] **Step 4: Run the tests and the build gate**, expect PASS. Update `PublicAPI.Unshipped.txt`.
- [ ] **Step 5: Commit** `Fluent: expression factories and operators`.

### Task 3: SELECT building and editing

**Files:**
- Create: `src/OracleSqlParser.Fluent/Sql.Select.cs`, `SelectExtensions.cs`
- Test: `tests/OracleSqlParser.Fluent.Tests/SelectTests.cs`, `SelectEditTests.cs`

**Interfaces:**
- Consumes: Task 1 and 2 APIs, `Conditions.And/Or`, `Parens`.
- Produces on `Sql`:
  - `Select(params string[] columns)`. Each string is read by `Sql.Expr` and becomes an `ExpressionSelectItem`, so `"e.name"`, `"count(*)"` and `"*"` all work. `"*"` and `"e.*"` become `StarSelectItem`.
  - `Select(params SelectItem[] items)` and `Select(params Expression[] expressions)`. A `StarExpression` becomes a `StarSelectItem`.
  - All three return a `SelectStatement` whose `Body` is a `QuerySpecification`.
- Produces on `SelectExtensions` (each `this SelectStatement s`, returns `SelectStatement`):
  - `Distinct()`
  - `From(string table, string? alias = null)`, `From(TableReference table)`, `From(SelectStatement subquery, string alias)` (a `SubqueryTable`)
  - `Join`, `LeftJoin`, `RightJoin` and `FullJoin`, each `(string table, string? alias, Expression on)` with a `TableReference` overload. Plus `CrossJoin(string table, string? alias = null)`. A join replaces the last `FromClause.Tables` entry with a `JoinTable`. With no `FROM` it throws `InvalidOperationException`.
  - `Where(Expression)` (replaces), `AndWhere(Expression)`, `OrWhere(Expression)`, `RemoveWhere()`
  - `GroupBy(params Expression[])`, `GroupBy(params string[])`, `Having(Expression)`
  - `OrderBy(Expression, NullsOrdering? nulls = null)`, `OrderByDesc(Expression, NullsOrdering? nulls = null)`, plus `string` overloads
  - `Offset(long)`, `FetchFirst(long)`
  - `ForUpdate()`
  - `With(string name, SelectStatement query)` (appends a CTE)
  - `Union`, `UnionAll`, `Intersect` and `Minus`, each `(SelectStatement other)`
  - `AddColumn(Expression, string? alias = null)`, `AddColumn(string)`, `AddColumns(params string[])`
- Body-shape rule: clause methods that act on the query block (`Distinct`, `From*`, `*Join`, `*Where`, `GroupBy`, `Having`, `AddColumn*`) throw `InvalidOperationException($"... requires a query block; the statement body is a {s.Body.GetType().Name}")` unless `Body is QuerySpecification`. `OrderBy*`, `Offset`, `FetchFirst`, `ForUpdate` and `With` work on any body.
- Set operations follow the spec's "Set operations" section exactly. The receiver throws if it has `OrderBy`, `Offset`, `Fetch` or `ForUpdate`. An operand with any of those or `With` is wrapped as `ParenthesizedQuery { Query = other }`; otherwise `other.Body` is used. The receiver's body becomes `SetOperation { Left = s.Body, Operator, All, Right }`. Match the parser's left-deep shape for chained unions.

- [ ] **Step 1: Write failing `SelectTests`**, each using `RoundTrip.AssertStatement`:
  - The spec's example query: `Select("e.name","e.sal").From("emp","e").LeftJoin("dept","d",Col("e.dept_id").Eq(Col("d.id"))).Where(Col("e.sal").Gt(Bind("min"))).OrderByDesc("e.sal").FetchFirst(10)`.
  - `Select("*").From("dual")`.
  - `Distinct`, `GroupBy`+`Having`, a two-table `From` (comma join), a `CrossJoin`, a subquery in `FROM`, and `With("x", ...)`.
  - `Union` of two plain queries, then `.OrderBy("1")`.
  - `Union` where the operand has `FetchFirst(1)`: the operand is wrapped in a paren query.
  - `Offset(5).FetchFirst(10)`.
  - Throws: `Select("a").Join("b", null, ...)` with no `FROM`, and `Select("a").From("t").OrderBy("a").Union(other)`.
- [ ] **Step 2: Write failing `SelectEditTests`**, on `(SelectStatement)SqlParser.ParseStatement(...)`:
  - `AndWhere` on `SELECT * FROM t` gives `... WHERE x = 1`.
  - `AndWhere` on `... WHERE a = 1 OR b = 2` gives `... WHERE (a = 1 OR b = 2) AND c = 3`.
  - `OrWhere`, `RemoveWhere`, and `AddColumn` with an alias.
  - `Where` on a parsed `SELECT a FROM t UNION SELECT b FROM u` throws `InvalidOperationException`, and the message contains `SetOperation`.
  - `OrderBy` on that same union works.
  - Each edit test also checks `StructurallyEqual(edited, ParseStatement(edited.ToSql-or-Generate))`. `ToSql` arrives in Task 5, so use `new SqlScriptGenerator().Generate` here.
- [ ] **Step 3: Run, expect FAIL. Implement. Run the tests and the build gate, expect PASS.** Update `PublicAPI.Unshipped.txt`.
- [ ] **Step 4: Commit** in two parts, `Fluent: SELECT builders` and `Fluent: SELECT editing`.

### Task 4: INSERT, UPDATE, DELETE, MERGE

**Files:**
- Create: `src/OracleSqlParser.Fluent/Sql.Dml.cs`, `DmlExtensions.cs`
- Test: `tests/OracleSqlParser.Fluent.Tests/DmlTests.cs`, `DmlEditTests.cs`

**Interfaces:**
- Consumes: Task 1 and 2 APIs, `Conditions.And/Or`.
- Produces on `Sql`: `InsertInto(string table, string? alias = null) -> InsertStatement`, `Update(string table, string? alias = null) -> UpdateStatement`, `DeleteFrom(string table, string? alias = null) -> DeleteStatement`, `MergeInto(string table, string? alias = null) -> MergeStatement`. Each also has a `TableReference` overload.
- Required properties start as placeholders. `UpdateStatement.Set` is `new SetClause { Items = [] }`. `MergeStatement.Source` is an empty `NamedTable`, and `On` is a placeholder. Use an internal sentinel `Literal` instance (`Placeholders.Missing`) that Task 5's `Validate` detects by reference.
- Produces on `DmlExtensions`:
  - `InsertStatement`: `Columns(params string[])`, `Values(params Expression[])` (appends a `RowValue`; repeatable), `Query(SelectStatement)`. Mixing `Values` and `Query` throws `InvalidOperationException`.
  - `UpdateStatement`: `Set(string column, Expression value)` (repeatable).
  - `UpdateStatement` and `DeleteStatement`: `Where`, `AndWhere`, `OrWhere`, `RemoveWhere`.
  - `MergeStatement`: `Using(string table, string? alias = null)`, `Using(SelectStatement query, string alias)`, `On(Expression)`, `WhenMatchedUpdate(params (string Column, Expression Value)[] set)`, `WhenNotMatchedInsert(string[] columns, Expression[] values)`.
  - `MergeMatchedClause` and `MergeNotMatchedClause` have their own extensions: `WhenMatchedUpdateWhere(Expression)`, `WhenMatchedDeleteWhere(Expression)` and `WhenNotMatchedWhere(Expression)` on `MergeStatement`. Each applies to the last clause of that kind and throws if there is none.
  - All four statement types: `Returning(string[] columns, string[] into)`. The `into` entries are bind or variable names, read with `Sql.Expr`.
  - All methods return the statement they were called on.
- The MERGE `On` condition: print it the way the parser expects (`ON (cond)`). Check how the parser stores the condition (paren node or not) and match it.

- [ ] **Step 1: Write failing `DmlTests`** (`RoundTrip.AssertStatement`):
  - `InsertInto("emp").Columns("id","name").Values(Bind("id"), Bind("name"))`
  - two `Values` rows
  - `InsertInto("t").Query(Select("a").From("u"))`
  - `Update("emp","e").Set("sal", Col("e.sal").Times(Lit(1.1m))).Where(Col("e.id").Eq(Bind("id")))`
  - `DeleteFrom("emp").Where(...)`
  - a MERGE with both `WhenMatchedUpdate` and `WhenNotMatchedInsert`
  - `Returning(["id"], [":out_id"])` on INSERT
  - throws: `Values` after `Query`
- [ ] **Step 2: Write failing `DmlEditTests`:**
  - `AndWhere` on a parsed `UPDATE t SET a = 1 WHERE b = 2 OR c = 3`
  - `RemoveWhere` on a parsed DELETE
  - `Set` that appends to a parsed UPDATE
- [ ] **Step 3: Run, expect FAIL. Implement. Run the tests and the build gate, expect PASS.** Update `PublicAPI.Unshipped.txt`.
- [ ] **Step 4: Commit** `Fluent: DML builders and editing`.

### Task 5: Validate, ToSql, target version, scripts

**Files:**
- Create: `src/OracleSqlParser.Fluent/Printing.cs` (`public static class SqlNodeFluentExtensions`), `Validation.cs` (internal)
- Modify: `src/OracleSqlParser.Fluent/Sql.cs` (add `Script`)
- Test: `tests/OracleSqlParser.Fluent.Tests/ValidationTests.cs`

**Interfaces:**
- Consumes: everything above, including `Placeholders.Missing`.
- Produces:
  - `Sql.Script(params Statement[] statements) -> SqlScript`
  - `public static T Validate<T>(this T node, OracleVersion target = OracleVersion.Oracle23) where T : SqlNode` (returns `node`)
  - `public static string ToSql(this SqlNode node, SqlScriptGeneratorOptions? options = null, OracleVersion target = OracleVersion.Oracle23)`
- Validation runs in this order:
  1. **Completeness.** Walk `node.Descendants()` (include `node`). An `UpdateStatement` with no `Set.Items`, a `MergeStatement` whose `On` or `Source` is a placeholder or that has no clauses, an `InsertStatement` with neither `Values` nor `Query`, and a `QuerySpecification` with an empty `SelectList` all throw `InvalidOperationException`. The message names the statement and the missing part.
  2. **Version.** Print with default generator options, then reparse with `SqlParser.Parse(text, new ParseOptions { OracleVersion = target })`. A `node` that is an `Expression` is parsed with `ParseExpression`; check that it accepts `ParseOptions`, and wrap in `SELECT <expr> FROM dual` if not. On any errors, throw `InvalidOperationException` listing each `ParseError.Message`, joined by `"; "`.
- `ToSql` validates, then returns `new SqlScriptGenerator(options ?? new()).Generate(node)`.

- [ ] **Step 1: Write failing `ValidationTests`:**
  - `Sql.Update("t").ToSql()` throws, and the message contains `SET`.
  - `Sql.MergeInto("t").ToSql()` throws.
  - `Sql.InsertInto("t").ToSql()` throws.
  - `InsertInto("t").Values(Lit(1)).Values(Lit(2)).ToSql(target: OracleVersion.Oracle19)` throws, and the message mentions `23`. The same call with Oracle23 returns the SQL.
  - `Select(Lit(true).As("b")).From("dual").ToSql(target: OracleVersion.Oracle19)` throws.
  - A plain 19c-safe query passes on Oracle19.
  - `Sql.Script(stmtA, stmtB).ToSql()` round-trips through `SqlParser.Parse`.
  - Generator options are honoured: `MultilineSelectElementsList = true` changes the output.
- [ ] **Step 2: Run, expect FAIL. Implement. Run the tests and the build gate, expect PASS.** Update `PublicAPI.Unshipped.txt`.
- [ ] **Step 3: Commit** `Fluent: Validate, ToSql and target-version check`.

### Task 6: Packaging, CI and docs

**Files:**
- Modify: `.github/workflows/build.yml`. Next to every Guard build, test and pack line, add the same for `OracleSqlParser.Fluent`, so the Fluent package lands in `./artifacts` and is pushed by the existing `publish` job with `--skip-duplicate`. Update the comment near line 84 so it covers Fluent too.
- Modify: `README.md`. Add a "Fluent builders (separate package)" section after the "Query guard" section, with the spec's example and a link to `src/OracleSqlParser.Fluent/README.md`. Add one line to "Releasing", step 1, about bumping the Fluent `<Version>`.
- Modify: `CHANGELOG.md`. Add an entry under `## [Unreleased]`: `OracleSqlParser.Fluent 0.1.0-preview.1: new package ...`.
- Write: `src/OracleSqlParser.Fluent/README.md`. Cover install, building, editing parsed SQL (in-place mutation, stale spans, `DeepClone`, don't reuse nodes), target version, and what isn't covered (DDL, PL/SQL, the clauses listed in the spec's non-goals; build those with object initializers).

- [ ] **Step 1: Make the edits.**
- [ ] **Step 2: Run** `dotnet build OracleSqlParser.slnx -c Release -warnaserror && dotnet test OracleSqlParser.slnx -c Release --no-build --filter "FullyQualifiedName!~IntegrationTests"` and `dotnet pack src/OracleSqlParser.Fluent -c Release -o ./artifacts-check`. Expected: everything passes, and `OracleSqlParser.Fluent.0.1.0-preview.1.nupkg` exists. Delete `./artifacts-check` afterwards.
- [ ] **Step 3: Commit** `Fluent: CI packaging and docs`.
