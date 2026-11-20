# Changelog

All notable changes to the `OracleSqlParser` packages. Versions follow [Semantic Versioning](https://semver.org/); before
1.0 every minor version may contain breaking changes. `OracleSqlParser.Guard` is experimental and versioned separately.

Each release is a git tag `vX.Y.Z[-preview.N]`; the release workflow publishes the section for that version as the
GitHub release notes, so keep the headings in the form `## [X.Y.Z...]`.

## [Unreleased]

### Added
- README: a "Versioning and compatibility" section stating what counts as a breaking change from 1.0.0 on.
- README: a "Guarantees" section (spans, traversal, SourceEditor preservation, the canonical printer, identifier
  spelling and folding, and the keyword pseudo-columns). Each guarantee is checked by a test: `SpanInvariantTests`,
  `TraversalCompletenessTests` and `SourceEditorPreservationTests`, over the corpora and a deterministic fuzz run.

### Changed (breaking)
- Two names now denote different nodes. Code that still compiles, such as `Visit(ReturningClause)` or `OfType<SetClause>()`,
  changes meaning without an error: `ReturningClause` was the JSON/XML `RETURNING type` clause and is now the DML `RETURNING`
  clause (the JSON one is `ReturningTypeClause`); `SetClause` was one `col = expr` assignment and is now the whole `SET` clause
  (the assignment is `SetItem`). Search for both names when you upgrade.
- The DML `RETURNING` clause is a node that spans its keyword through its `INTO` target, as the other clauses do. The
  JSON and XML `RETURNING type` clause, which was also called `ReturningClause`, is renamed.

  | Property | Was | Is |
  |---|---|---|
  | `ReturningClause` (JSON/XML `RETURNING type`) | class `ReturningClause` | class `ReturningTypeClause` (same members) |
  | `DmlStatement.Returning` | `List<ReturningItem>` | `ReturningClause?` (`.Items`, and a required `.Into`, since Oracle needs the INTO after DML RETURNING) |
  | `DmlStatement.ReturningInto` | `IntoClause?` | removed; use `Returning.Into` |
  | `ExecuteImmediateStatement.ReturningInto` | `IntoClause?` | unchanged; documented as the `RETURNING INTO` of the dynamic SQL |
  | `FetchClause.Next` | `bool`, compared structurally | `bool`, marked spelling only: `FETCH NEXT n` and `FETCH FIRST n` compare equal |
  | `ExistsExpression.Subquery` | `SelectStatement` | `SelectStatement` as `.Query` |
  | `InExpression.Subquery` | `SelectStatement?` | `SelectStatement?` as `.Query` |
  | `InsertStatement.ValueRows` | `List<List<Expression>>` | removed; use `InsertStatement.Values.Rows[i].Values` |
  | `InsertStatement.RecordValue` | `Expression?` | removed; use `InsertStatement.Values.Record` |
  | `InsertStatement.Values` (new) | n/a | `ValuesClause?`; exactly one of `Values` and `Query` is set |
  | `InsertIntoClause.Values` | `List<Expression>` | `ValuesClause?` (one row) |
  | `ValuesQuery.Rows`, `ValuesTable.Rows` | `List<List<Expression>>` | `List<RowValue>` (`.Values`) |
  | `UpdateStatement.Set` | `List<SetClause>` | `SetClause` (required, spans `SET`; `.Items` holds the assignments) |
  | `SetClause` (one assignment, `col = expr`) | class `SetClause` | class `SetItem` (same members: `Columns`, `Value`, `Row`). The new `SetClause.Items` is required |
  | `WindowSpec.PartitionBy`, `FunctionCall.PartitionBy`, `MatchRecognizeTable.PartitionBy` | `List<Expression>` | `PartitionByClause?` (`.Items`, spanning `PARTITION BY`) |
  | `JoinTable.LeftPartitionBy`, `JoinTable.RightPartitionBy` | `List<Expression>` | `PartitionByClause?` (`.Items`, spanning `PARTITION BY (...)`) |

  | `InsertStatement.Columns`, `InsertIntoClause.Columns` | `List<Identifier>` | `List<ColumnReference>`, so `INSERT INTO t x (x.a)` keeps its qualifier (Oracle 23 accepts it; the parser does not reject it) |

  New nodes: `ValuesClause` (spans `VALUES` through its last row or record) and `RowValue` (spans one row's parentheses).

  `ModelClause.PartitionBy` stays a list of select items, since a model's partitioning takes aliased expressions.

- `FORALL` has one `Bounds` property, whose type says which form is written.

  | Property | Was | Is |
  |---|---|---|
  | `ForallStatement.Lower`, `ForallStatement.Upper` | `Expression?` | removed; `ForallRange` (`.Lower`, `.Upper`, both required) |
  | `ForallStatement.IndicesOf`, `BetweenLow`, `BetweenHigh` | `Expression?` | removed; `ForallIndicesOf` (`.Collection`, `.Low`, `.High`) |
  | `ForallStatement.ValuesOf` | `Expression?` | removed; `ForallValuesOf` (`.Collection`) |
  | `ForallStatement.Bounds` (new) | n/a | `required ForallBounds`, the base of the three forms |

- `CALL ... INTO` is an `IntoClause` with its one target, as the other INTO clauses are.

  | Property | Was | Is |
  |---|---|---|
  | `CallStatement.Into` | `Expression?` | `IntoClause?` (`.Targets`, one target; `BulkCollect` is always false) |

  `CALL f() BULK COLLECT INTO :a` and `CALL f() INTO :a, :b` are rejected, as Oracle rejects them (ORA-03049, ORA-03048).

- Renamed for the `...Clause` suffix that the other clause nodes use:

  | Property | Was | Is |
  |---|---|---|
  | `ErrorLogging` (class; `DmlStatement.ErrorLogging` keeps its name) | `ErrorLogging` | `ErrorLoggingClause` |
  | `PivotTable.InSubquery` | `SelectStatement?` | `SelectStatement?` as `.InQuery`, as `InExpression.Query` and `ExistsExpression.Query` are named |

- Pairs of flags that are one choice are enums:

  | Property | Was | Is |
  |---|---|---|
  | `FunctionCall.IgnoreNulls`, `FunctionCall.RespectNulls` | `bool`, `bool` | `NullTreatment?` (`enum NullTreatment { Ignore, Respect }`) |
  | `FunctionCall.FromLast` | `bool?` | `FirstOrLast?` `NthFrom` (`enum FirstOrLast { First, Last }`, shared with KEEP) |
  | `KeepClause.First` | `bool` | `FirstOrLast` `Position` (required: KEEP always writes FIRST or LAST) |
  | `ForUpdateClause.NoWait`, `ForUpdateClause.SkipLocked` | `bool`, `bool` | removed; `ForUpdateClause.Wait` is `LockWait?` (`enum LockWait { NoWait, Wait, SkipLocked }`) |
  | `ForUpdateClause.Wait` (seconds) | `Expression?` | `Expression?` as `WaitSeconds`, set with `LockWait.Wait` |
  | `LockTableStatement.NoWait` | `bool` | removed; `LockTableStatement.Wait` is `LockWait?` |
  | `LockTableStatement.Wait` (seconds) | `Expression?` | `Expression?` as `WaitSeconds`, set with `LockWait.Wait` |

  `CommitWrite.Wait` keeps its `WaitMode` (`Wait`, `NoWait`), which is unchanged.

- `ForUpdateClause.Wait` and `LockTableStatement.Wait` keep their names, but their type changed from `Expression?` to `LockWait?`.
  So `Wait != null` now also matches `NOWAIT` and, for `FOR UPDATE`, `SKIP LOCKED`. Code that tested for a wait must test for
  `LockWait.Wait`, and reads the seconds from `WaitSeconds`, which is set exactly when `Wait` is `LockWait.Wait`.

- Labels are names, and are typed as names:

  | Property | Was | Is |
  |---|---|---|
  | `Statement.Labels` | `List<string>` (upper-cased value) | `List<Identifier>`, so a label keeps its spelling and quoting (`Value`, `Text`, `Quoted`) |

  `BlockStatement.EndName` (the label after `END`, or the subprogram name of a subprogram body) and the package `EndName`s keep their names.

- Statement labels are `Identifier` nodes, so the visitor and rewriter visit them, and `SqlNodeComparer` compares them with
  their quoting: `<<a>>` and `<<"a">>` are different labels. A labelled statement's span starts at its first `<<`, as a labelled
  block's does, so `SourceEditor.Replace` on a labelled statement replaces the label with it; replacing with text that
  repeats the label duplicates it.
- Visitor and rewriter overloads follow the type changes: `MergeClause` is abstract, with `MergeMatchedClause` and
  `MergeNotMatchedClause` as its subclasses, and `ErrorLogging` is `ErrorLoggingClause`.
- `FunctionCall`'s children come out in source order: `PartitionBy` (of `APPROX_RANK`) comes before `OrderBy`, and `Over` last.
- `IGNORE NULLS` and `RESPECT NULLS` together on one call are rejected, as Oracle rejects them (ORA-30484):
  `LAG(x) IGNORE NULLS RESPECT NULLS OVER (...)` and `LAG(x RESPECT NULLS) IGNORE NULLS OVER (...)`. The parser used to accept
  them and the generator dropped one of the two.
- `SqlScriptGenerator` throws `InvalidOperationException` for an `InsertStatement` with neither or both of `Values` and `Query`
  set, instead of writing an empty or ambiguous INSERT.
- `KEEP (DENSE_RANK ...)` and `NTH_VALUE ... FROM FIRST|LAST` share one enum, `FirstOrLast`. `NullsOrdering` (`NULLS FIRST|LAST`)
  and `WaitMode` are unchanged.

- Anchored types take an enum:

  | Property | Was | Is |
  |---|---|---|
  | `DataType.Attribute` (`"TYPE"` or `"ROWTYPE"`) | `string?` | `TypeAnchor?` `Anchor` (`enum TypeAnchor { Type, RowType }`) |

  `ColumnReference.Attribute` and `FunctionCall.Attribute` stay strings, because cursor attributes (`%FOUND`, `%ROWCOUNT`,
  `%BULK_EXCEPTIONS`) are an open set. Their XML docs list the common values and say that `SQL%FOUND` parses as a
  `ColumnReference` with `Attribute` set. `SqlAnalysis.GetColumns` already leaves those out, so its output is unchanged.

- `MERGE` clauses are two node types, so each has only the members it uses. Each spans `WHEN` through the end of its action.

  | Property | Was | Is |
  |---|---|---|
  | `MergeClause` | one class for both kinds | abstract base with no members (it spans `WHEN` through the action); `MergeMatchedClause` and `MergeNotMatchedClause` derive from it |
  | `MergeClause.AndCondition` | `Expression?` | removed. Oracle has no condition after `MATCHED`: `WHEN MATCHED AND c` and `WHEN NOT MATCHED AND c` are rejected (ORA-02000); write the condition as the action's `WHERE` |
  | `MergeClause.Matched` | `bool` | removed; the type says it (`WHEN MATCHED` is `MergeMatchedClause`) |
  | `MergeClause.Set`, `UpdateWhere`, `DeleteWhere` | on `MergeClause` | `MergeMatchedClause.Set`, `.UpdateWhere`, `.DeleteWhere` |
  | `MergeClause.InsertColumns`, `InsertValues`, `InsertWhere` | on `MergeClause` | `MergeNotMatchedClause.Columns`, `.Values` (`ValuesClause`, one row), `.Where` |
  | `MergeMatchedClause.Set` | `List<SetClause>` | `SetClause` (required, `.Items`) |

- The parser rejects `WHEN MATCHED THEN INSERT`, `WHEN NOT MATCHED THEN UPDATE` and the `AND` condition form, which used to parse. Oracle rejects all three
  (ORA-02000). A `DELETE` action without `UPDATE` is still rejected.

### Fixed
- Span bug: a node that consumed no tokens had `End` before `Start` on the parse (for example the empty `DatafileSpec` of
  `CREATE TEMPORARY TABLESPACE tmp TEMPFILE DROP 't.dbf' SIZE 5M REUSE`, which parsed, and the empty `GrantPrivilege` of
  `GRANT TO PROCEDURE ...`, which failed). Such a node is now zero-width at the next token. The table under `PIVOT`,
  `UNPIVOT` or `MATCH_RECOGNIZE` had a `[0,0)` span, and hint arguments had offsets relative to the hint text. Both now
  have source offsets.
- `DBTIMEZONE` and `SESSIONTIMEZONE` are reported as `ColumnUseKind.PseudoColumn` by `SqlAnalysis.GetColumns`, as the other
  keyword pseudo-columns are. They were reported as `Column`.

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
