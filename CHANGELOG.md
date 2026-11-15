# Changelog

All notable changes to the `OracleSqlParser` packages. Versions follow [Semantic Versioning](https://semver.org/); before
1.0 every minor version may contain breaking changes. `OracleSqlParser.Guard` is experimental and versioned separately.

Each release is a git tag `vX.Y.Z[-preview.N]`; the release workflow publishes the section for that version as the
GitHub release notes, so keep the headings in the form `## [X.Y.Z...]`.

## [Unreleased]

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
- `CREATE TYPE t;` (`TypeKind.Incomplete`), `TABLE OF REF t` (`DataType.Ref`), `APPROX_RANK(PARTITION BY ...)`
  (`FunctionCall.PartitionBy`), `(SELF AS t).m()` (`CastKind.GeneralizedInvocation`), `SET(nt)`, member access after
  `TREAT(...)` and method calls.
- `tools/RealWorldCorpus` (parse and round-trip whole repositories) and `tools/Fuzz` (contract fuzzer); a deterministic
  fuzz run is part of the unit suite.

### Fixed
- Found by parsing 915 files from open-source PL/SQL projects (utPLSQL, Logger, Oracle sample schemas and examples, and
  others; 97% of files now parse cleanly and all of them round-trip): a `/` line after `CREATE TYPE`, `DROP` and other
  statements without `;` now ends them instead of being read as division or swallowing the next statement; `/*` in an
  `@` path is no longer a comment; `FILE` is a valid PL/SQL variable name.
- Found by fuzzing (530,000 inputs): no stack overflow when printing, generating, cloning or analysing very deep trees or
  10,000-join chains; indented JSON stays linear for deep trees; `a IN (1) IN (2)` and similar chained comparisons,
  AUDIT options, column constraints after unrecognised words, and quoted statement labels round-trip.

### Changed
- `OracleSqlParser.Guard` is versioned independently of the core library and stays 0.x while experimental.
- Unit and Guard tests run on both net8.0 and net10.0 in CI.
- README code samples are compiled by a unit test.
- Tagged releases create a GitHub release with the CHANGELOG section as notes.

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
