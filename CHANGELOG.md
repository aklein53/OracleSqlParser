# Changelog

All notable changes to the `OracleSqlParser` packages. Versions follow [Semantic Versioning](https://semver.org/); before
1.0 every minor version may contain breaking changes. `OracleSqlParser.Guard` is experimental and versioned separately.

Each release is a git tag `vX.Y.Z[-preview.N]`; the release workflow publishes the section for that version as the
GitHub release notes, so keep the headings in the form `## [X.Y.Z...]`.

## [Unreleased]

### Added
- README: a "Versioning and compatibility" section stating what counts as a breaking change from 1.0.0 on.

### Changed (breaking)
- The DML `RETURNING` clause is a node that spans its keyword through its `INTO` target, as the other clauses do. The
  JSON and XML `RETURNING type` clause, which was also called `ReturningClause`, is renamed.

  | Property | Was | Is |
  |---|---|---|
  | `ReturningClause` (JSON/XML `RETURNING type`) | class `ReturningClause` | class `ReturningTypeClause` (same members) |
  | `DmlStatement.Returning` | `List<ReturningItem>` | `ReturningClause?` (`.Items`, `.Into`) |
  | `DmlStatement.ReturningInto` | `IntoClause?` | removed; use `Returning.Into` |
  | `ExecuteImmediateStatement.ReturningInto` | `IntoClause?` | unchanged; documented as the `RETURNING INTO` of the dynamic SQL |
  | `FetchClause.Next` | `bool`, compared structurally | `bool`, marked spelling only: `FETCH NEXT n` and `FETCH FIRST n` compare equal |
  | `ExistsExpression.Subquery` | `SelectStatement` | `SelectStatement` as `.Query` |
  | `InExpression.Subquery` | `SelectStatement?` | `SelectStatement?` as `.Query` |

## [1.0.0-rc.3] - 2026-11-18

Third release candidate. `OracleSqlParser.Guard` 0.10.0-preview.1 is released alongside it: Guard 0.9 reads the clause
properties changed below and does not work with this version of the core library.

### Changed (breaking)
- The remaining query and DML clauses are nodes that span their keyword, as ScriptDom's are: `OFFSET`, `WITHIN GROUP`,
  `WITH`, `INTO` and the grouping elements of `GROUP BY`. Each one is taken as written, or replaced, without a search for
  the keyword. Grouping is typed, so `ROLLUP`, `CUBE` and `GROUPING SETS` are no longer function calls.

  | Property | Was | Is |
  |---|---|---|
  | `SelectStatement.Offset` | `Expression?` | `OffsetClause?` (`.Offset`), spanning `OFFSET n ROW\|ROWS` |
  | `FunctionCall.WithinGroup` | `OrderByClause?` | `WithinGroupClause?` (`.OrderBy`), spanning `WITHIN GROUP (...)` |
  | `KeepClause.OrderBy` | `OrderByClause?` | `OrderByClause` (required: `KEEP` always has its `ORDER BY`) |
  | `MergeClause.UpdateWhere`, `MergeClause.DeleteWhere`, `MergeClause.InsertWhere` | `Expression?` | `WhereClause?` (`.Condition`) |
  | `GroupByClause.Items` | `List<Expression>` | `List<GroupingSpec>` (see the grouping kinds below) |
  | `SelectStatement.With` | `List<CommonTableExpression>` | `WithClause?` (`.Ctes`, `.Declarations`) |
  | `SelectStatement.WithDeclarations` | `List<Declaration>` | removed; use `With.Declarations` |
  | `QuerySpecification.Into`, `DmlStatement.ReturningInto`, `ExecuteImmediateStatement.Into`, `ExecuteImmediateStatement.ReturningInto` | `List<Expression>` | `IntoClause?` (`.Targets`, `.BulkCollect`) |
  | `QuerySpecification.IntoBulkCollect`, `DmlStatement.ReturningBulkCollect`, `ExecuteImmediateStatement.BulkCollect`, `ExecuteImmediateStatement.ReturningBulkCollect` | `bool` | removed; use `IntoClause.BulkCollect` |
  | `FetchStatement.Into` | `List<Expression>` | `IntoClause` (required: `FETCH` always has its `INTO`, and `.Targets` holds the variables) |
  | `FetchStatement.BulkCollect` | `bool` | removed; use `IntoClause.BulkCollect` |

- New nodes: `OffsetClause`, `WithinGroupClause`, `WithClause` and `IntoClause`, and the abstract `GroupingSpec` with its
  kinds. `GROUP BY` elements are written as:

  | Written | Was | Is |
  |---|---|---|
  | `a` | `Expression` | `ExpressionGrouping` (`.Expression`) |
  | `(a, b)` | `ExpressionList` | `CompositeGrouping` (`.Items`) |
  | `()` | `ExpressionList` | `GrandTotalGrouping` |
  | `ROLLUP(...)` | `FunctionCall` named `ROLLUP` | `RollupGrouping` (`.Items`) |
  | `CUBE(...)` | `FunctionCall` named `CUBE` | `CubeGrouping` (`.Items`) |
  | `GROUPING SETS(...)` | `FunctionCall` named `GROUPING SETS` | `GroupingSetsGrouping` (`.Sets`) |

- A single parenthesized expression, `(a)`, is an `ExpressionGrouping` holding its `ParenthesizedExpression`, as it is
  everywhere else in the tree.
- `SqlAnalysis.GetFunctions` no longer reports `ROLLUP`, `CUBE` or `GROUPING SETS`, and `OracleSqlParser.Guard`'s function
  allowlist no longer exempts them by name. `GROUPING()`, `GROUPING_ID()` and `GROUP_ID()` are still functions. `ROLLUP(ROLLUP(a))`
  still reports `ROLLUP` as a function, because Oracle reads it as a call to an unknown function.
- The parser rejects the grouping forms Oracle rejects at parse time: a composite holding a list, a composite holding a
  `ROLLUP`, `CUBE` or `GROUPING SETS` call, `()` inside a list, an empty `ROLLUP()`, `CUBE()` or `GROUPING SETS()`, a nested
  `GROUPING SETS`, and `ROLLUP(a) + 1`. A bare `WITH` followed by `SELECT` or `(` is also rejected, as Oracle rejects it.
  `EXECUTE IMMEDIATE ... INTO a INTO b` (a second `INTO`) is rejected too.
- Deliberate new rejections, each one Oracle also rejects at parse or compile time:
  - `OFFSET n` must be followed by `ROW` or `ROWS` (ORA-03047). `SELECT a FROM t OFFSET 5` used to parse.
  - `MERGE ... WHEN [NOT] MATCHED THEN DELETE [WHERE ...]` is rejected, since Oracle has no delete-only MERGE action (ORA-02000).
    `DeleteWhere` now means only the `DELETE WHERE` of an `UPDATE SET ... DELETE WHERE` action.
  - `EXECUTE IMMEDIATE` clauses must come in Oracle's order: `[BULK COLLECT] INTO`, then `USING`, then `RETURNING [BULK COLLECT] INTO`
    (PLS-00103 otherwise). `INTO` and `RETURNING INTO` cannot be used together (PLS-00429).
- The generated SQL is unchanged for the 915-file real-world corpus and for the test suite, with one exception: `ROLLUP`,
  `CUBE` and `GROUPING SETS` are printed in upper case, as Oracle keywords. A lower-case `GROUP BY rollup(a)` used to print
  as it was written, and now prints `GROUP BY ROLLUP(a)`. The JSON and shape output gain the clause level
  (`offset=(OffsetClause offset=5)`, `groupBy=(GroupByClause items=[(ExpressionGrouping expression=A)])`).
- `OracleSqlParser.Guard` reads these properties, so it is updated in this release and needs this version of the core library
  (as rc.2 noted for its own changes).

## [1.0.0-rc.2] - 2026-11-18

Second release candidate. `OracleSqlParser.Guard` 0.9.0-preview.1 is released alongside it: Guard 0.8 reads the clause
properties changed below and does not work with this version of the core library.

### Changed (breaking)
- Query and DML clauses are nodes, as in ScriptDom, so a clause spans its keyword instead of only its body. This is
  what lets a caller take a clause as written, or replace it, without scanning the token stream back for the keyword.

  | Property | Was | Is |
  |---|---|---|
  | `QuerySpecification.From`, `UpdateStatement.From`, `DeleteStatement.From` | `List<TableReference>` | `FromClause?` (`.Tables`) |
  | `QuerySpecification.Where`, `UpdateStatement.Where`, `DeleteStatement.Where` | `Expression?` | `WhereClause?` (`.Condition`) |
  | `QuerySpecification.StartWith` | `Expression?` | `StartWithClause?` (`.Condition`) |
  | `QuerySpecification.ConnectBy` | `Expression?` | `ConnectByClause?` (`.Condition`) |
  | `QuerySpecification.GroupBy` | `List<Expression>` | `GroupByClause?` (`.Items`) |
  | `QuerySpecification.Having` | `Expression?` | `HavingClause?` (`.Condition`) |
  | `SelectStatement.OrderBy` | `List<OrderByItem>` | `OrderByClause?` (`.Items`) |
  | `FunctionCall.OrderBy`, `FunctionCall.WithinGroup`, `KeepClause.OrderBy`, `WindowSpec.OrderBy`, `MatchRecognizeTable.OrderBy`, `ModelRule.OrderBy` | `List<OrderByItem>` | `OrderByClause?` (`.Items`) |

  An absent clause is null, so `.Count == 0` becomes `is null` and `SELECT 1` has a null `From`. The JSON and shape
  output gain the clause level (`where=(WhereClause condition=...)`).
- `QuerySpecification.NoCycle` moved to `ConnectByClause.NoCycle`, and `SelectStatement.OrderSiblings` to
  `OrderByClause.Siblings`: a modifier belongs to the clause it modifies, as `GroupByClause.All` does in ScriptDom.
- The condition inside a clause is required, so a rewriter that returns null for the condition of a `WHERE` now throws
  instead of silently dropping the clause. Return null for the `WhereClause` to remove the clause.
- The graph pattern `WHERE` (`GraphTable`, `GraphVertexPattern`, `GraphEdgePattern`) and `RefreshClause.StartWith` are
  unchanged: ScriptDom has no nested `WhereClause`, and a materialized view's `START WITH <date>` is not a clause of a
  query.

## [1.0.0-rc.1] - 2026-11-15

Release candidate for 1.0: the public API is frozen and will ship as 1.0.0 unless problems are found.
`OracleSqlParser.Guard` 0.8.0-preview.1 is released alongside it (it no longer uses the removed `SqlWriter`; Guard 0.7
does not work with this version of the core library).

### Changed (breaking)
- Public API reviewed and frozen with PublicApiAnalyzers (`PublicAPI.*.txt`); any public surface change now fails the build
  until the baseline is updated.
- Removed `SqlWriter` (use `SqlScriptGenerator`), the untyped `SqlVisitor` (use `SqlNodeVisitor`) and
  `SqlNode.Children()` (use `GetChildren()`).
- `Lexer` and `AstPrinter` are internal; `ParseResult` can no longer be constructed or mutated by callers.
- Renamed `Block` → `BlockStatement`, `SqlPlusCommand` → `SqlPlusCommandStatement`, `TablespaceType` →
  `TablespaceKind`, `DualityAnnotation` → `DualityAnnotationKind` (also changes the JSON `type` values).
- The `Operator` of `BinaryExpression`, `UnaryExpression` and `QuantifiedComparison` is an enum instead of a string:
  `BinaryOperator` (for example `Equals`, `NotEquals`, `And`, `MultisetUnionAll`) and `UnaryOperator` (`Plus`, `Minus`,
  `Not`, `Prior`, `ConnectByRoot`). `!=`, `^=` and `~=` are all `NotEquals`; the spelling written is kept in the new
  `OperatorSpelling` property, and `OperatorExtensions.ToSql()` gives the canonical SQL text. Shapes and JSON show the
  member name (for example `(Equals a 1)` and `"operator": "Equals"`).
- `MergeClause.InsertColumns` is a list of `ColumnReference` (was `Identifier`); `CastExpression.Type` is nullable
  (`XMLSERIALIZE(... NO INDENT)` has no type).
- `@file` / `@@file` take the rest of the line as one raw string argument.

### Added
- `ParseOptions.SqlPlusScript` (default off) for SQL*Plus script line rules: a line-end `-` continues a command or SQL
  statement, and a blank line ends an unterminated SQL statement (unless `SET SQLBLANKLINES ON`).
- SQL*Plus host commands `$ cmd` / `! cmd`; `&name`/`&&name` substitution variables as names (`CREATE USER &u`,
  `&&owner..pkg.f`).
- Under `ParseOptions.SqlPlusScript`, a `HOST` command continued with a line-end `-` is one `SqlPlusCommandStatement`
  whose arguments cover both lines (SQL*Plus drops the hyphen and joins the lines; so does the parser).
- Code-based access control: `GRANT role TO PACKAGE|FUNCTION|PROCEDURE [schema.]name` and the matching `REVOKE` (Oracle 12c+),
  with substitution names. The grantee carries `ProgramKind` and `ProgramSchema` (`Name` stays the unit name).
- Statement-level `PRAGMA INLINE (subprogram, 'YES'|'NO')` in an executable section (`InlinePragmaStatement`); the
  declaration form (`PragmaDeclaration`) is unchanged.
- A table function named in a query's FROM clause without `TABLE(...)` (Oracle 18c+): `FROM dbms_xplan.display()`,
  `FROM hr.pkg.f(1, 2) t`, `FROM (pkg.f(a)) m`. It is a `TableFunction` with the new `WithoutTableKeyword` flag set;
  `TABLE(...)` trees are unchanged.
- Conditional compilation around the clauses of a subprogram heading, between the RETURN type and IS/AS or `;` (for
  example `$IF $$rac_lt_11_2 $THEN $IF ... $THEN RESULT_CACHE RELIES_ON (...) $END $END`): `ProgramOptions.Conditionals`
  of `ConditionalProgramOptions`, with `$ELSIF`/`$ELSE` branches. Empty branches are accepted, as Oracle accepts them.
- `CREATE TYPE t;` (`TypeKind.Incomplete`), `TABLE OF REF t` (`DataType.Ref`), `APPROX_RANK(PARTITION BY ...)`
  (`FunctionCall.PartitionBy`), `(SELF AS t).m()` (`CastKind.GeneralizedInvocation`), `SET(nt)`, member access after
  `TREAT(...)` and method calls.
- `tools/RealWorldCorpus` (parse and round-trip whole repositories) and `tools/Fuzz` (contract fuzzer); a deterministic
  fuzz run is part of the unit suite.

### Fixed
- The SQL generator printed `CHARACTER VARYING(10)` (and the `NATIONAL CHARACTER`, `NCHAR` and `CHAR` forms) as
  `CHARACTER(10) VARYING`, which Oracle rejects.
- Found by parsing 915 files from open-source PL/SQL projects (utPLSQL, Logger, Oracle sample schemas and examples, and
  others; 97% of files now parse cleanly and all of them round-trip): a `/` line after `CREATE TYPE`, `DROP` and other
  statements without `;` now ends them instead of being read as division or swallowing the next statement; `/*` in an
  `@` path is no longer a comment; `FILE` is a valid PL/SQL variable name.
- Found by fuzzing (530,000 inputs): no stack overflow when printing, generating, cloning or analysing very deep trees or
  10,000-join chains; indented JSON stays linear for deep trees; `a IN (1) IN (2)` and similar chained comparisons,
  AUDIT options, column constraints after unrecognised words, and quoted statement labels round-trip.
- A quoted or dotted user-defined type name keeps its quoting in the generated SQL: `"ab"` no longer prints as `ab` (the
  type AB), and `hr."my type"` and `"a.b".t` no longer lose their quotes. The parts are in the new `DataType.NameParts`;
  `DataType.Name` is unchanged. A quoted type name is also accepted in a column definition and an external parameter.

### Changed
- `OracleSqlParser.Guard` is versioned independently of the core library and stays 0.x while experimental.
- Unit and Guard tests run on both net8.0 and net10.0 in CI.
- README code samples are compiled by a unit test.
- Tagged releases create a GitHub release with the CHANGELOG section as notes.
- The generator prints a user-defined type name as typed under `IdentifierCasing.Preserve`, like other identifiers (it was
  upper-cased). `IdentifierCasing.Uppercase` and `Lowercase` apply as they do to other identifiers.

## [0.7.0-preview.1] - 2026-11-14

### Added
- The library is marked `IsAotCompatible`: trim/Native AOT analyzers run on every build, and CI publishes and runs a
  Native AOT smoke app.
- The Oracle 23 Free integration suite runs in CI on every push; releases publish only when it passes.
- 59 expert-written stress tests (reporting, ETL and DBA-style SQL and PL/SQL), each checked for tree shape, generator
  round trip, and identical results from Oracle for the original and generated SQL.

### Fixed
- `NTH_VALUE(x, n) FROM FIRST|LAST` was read as a table named `FIRST`/`LAST`; it is now `FunctionCall.FromLast`.
- String literals may span lines (an unterminated literal still recovers on the next line).
- `SQL%BULK_EXCEPTIONS.COUNT` (a member after a `%` attribute) parses, e.g. as a FOR range bound.

### Changed
- `AstPrinter` and `AstJson` read node properties through generated code instead of reflection.

## [0.6.0-preview.1] - 2026-11-14

### Changed (breaking)
- Mandatory child properties of AST nodes are `required` (179 properties): building a node without them no longer
  compiles. `CreateDualityViewStatement.Query` is nullable (null for the GraphQL form).

### Added
- XML documentation on every public type and member, enforced by the build; reviewed against Oracle syntax.

## [0.5.0-preview.1] - 2026-11-14

### Changed (breaking)
- Packages renamed to match the namespace: `PlsqlParser` → `OracleSqlParser`, `PlsqlParser.Guard` →
  `OracleSqlParser.Guard`. Namespaces are unchanged; update the package reference only.

### Added
- `ParseError.Code` (PSP0001–PSP0099), `Length`, `Expected`, `FormatWithCaret`; `ParseException.Errors`.
- `SqlParser.TryParse`, `ParseSelect`, `TryParseSelect`.
- `SqlAnalysis`: `GetTables`, `GetColumns`, `GetBinds`, `GetFunctions`.
- `OracleSqlParser.Cli`, a `dotnet tool` (`oraclesqlparser`).

## [0.4.0-preview.1] - 2026-11-14

### Added
- Typed nodes replacing token trees for application DDL: CREATE/ALTER USER, ROLE, PROFILE, TABLESPACE; DIRECTORY,
  DATABASE LINK, CONTEXT; AUDIT/NOAUDIT and AUDIT POLICY; ALTER INDEX/SEQUENCE/MATERIALIZED VIEW; COMPILE on program
  units; ALTER TRIGGER/VIEW; ALTER TABLE partition maintenance; ANALYZE; GRANT/REVOKE ON SCHEMA.
- Oracle 23ai: GRAPH_TABLE and CREATE PROPERTY GRAPH, CREATE/ALTER DOMAIN, JSON relational duality views, `JSON {}` /
  `JSON []` constructors, ANNOTATIONS — accepted and rejected exactly as Oracle 23 Free does.
- Syntax: `IS [NOT] TRUE/FALSE`, interval expressions, JSON dot-notation after array steps, RETURNING OLD/NEW, LOG
  ERRORS on MERGE, UPDATE/DELETE … FROM, partitioned outer join, VALUES in WITH, FETCH APPROX, PL/SQL 21c iteration
  controls, conditional compilation, qualified expressions, chained calls, `EXEC :x := 1`.
- `ParseOptions.OracleVersion` (19/21/23): newer syntax produces a diagnostic while the tree is still built.

### Changed (breaking)
- `ExecStatement.Body`, `DmlStatement.Returning` as `ReturningItem`s, FOR loops as `Iterands`/`Controls`, parameter
  lists as `ParameterItem`s, `CreateViewStatement.Columns` as `ViewColumn`s; `**` is rejected in SQL expressions.

### Fixed
- SQL generator printed constraint states in an order Oracle rejects (`DISABLE NOVALIDATE RELY`).

## [0.3.0-preview.1] - 2026-11-13

### Added
- Token stream with trivia (`ParseResult.Tokens`, `Token.LeadingTrivia`), `FirstTokenIndex`/`LastTokenIndex` on nodes.
- `SourceEditor` / `SourceEdits` for format-preserving edits.
- SQL*Plus `SET DEFINE` handling for `&` substitution variables.

## [0.2.0-preview.1] - 2026-11-13

### Added
- Typed visitor (`SqlNodeVisitor` with `Visit`/`ExplicitVisit`), `SqlNodeRewriter`, `DeepClone`,
  `SqlNodeComparer.StructurallyEqual`, all generated by `tools/AstGen`.

## [0.1.0-preview.2] - 2026-11-13

### Added
- `SqlScriptGenerator` for every statement type, with ScriptDom-style options.

### Fixed
- Robustness: no hangs, no stack overflows (nesting limit), `Parse` never throws; error recovery across `/`.

### Changed
- QueryGuard moved to the experimental `PlsqlParser.Guard` package; its README no longer makes security claims.

## [0.1.0-preview.1] - 2026-11-13

- First preview: Oracle SQL and PL/SQL parser producing a typed AST.
