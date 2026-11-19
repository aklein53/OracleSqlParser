namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- statement dispatch ----------

        private void Statement(Statement st)
        {
            // A statement label is kept as its value, not as typed: a quoted label such as "a b" must stay quoted.
            foreach (var label in st.Labels) { Add("<<"); Raw(Word(label, NeedsQuotes(label), null)); Add(">>"); PlSp(); }
            switch (st)
            {
                case SelectStatement s: Select(s); break;
                case InsertStatement i: Insert(i); break;
                case MultiTableInsertStatement mi: MultiInsert(mi); break;
                case UpdateStatement u: Update(u); break;
                case DeleteStatement d: Delete(d); break;
                case MergeStatement m: Merge(m); break;
                // transaction / session / admin
                case AnalyzeStatement an: Analyze(an); break;
                case CommitStatement or RollbackStatement or SavepointStatement or SetTransactionStatement or SetConstraintsStatement
                    or SetRoleStatement or AlterSessionStatement or CallStatement or ExplainPlanStatement or LockTableStatement
                    or TruncateStatement or CommentStatement or GrantStatement or RenameStatement or PurgeStatement
                    or FlashbackTableStatement or ExecStatement or SqlPlusCommandStatement or AdminStatement:
                    AdminLike(st); break;
                // generic and typed DDL
                case AlterIndexStatement ai: AlterIndex(ai); break;
                case AlterSequenceStatement asq: AlterSequence(asq); break;
                case AlterCompileStatement acs: AlterCompile(acs); break;
                case AlterTriggerStatement ats: AlterTrigger(ats); break;
                case AlterViewStatement avs: AlterView(avs); break;
                case AlterMaterializedViewStatement amv: AlterMaterializedView(amv); break;
                case CreateObjectStatement or AlterObjectStatement or DropStatement or CreateTableStatement or CreateIndexStatement
                    or CreateViewStatement or CreateSequenceStatement or CreateSynonymStatement or AlterTableStatement or CreatePropertyGraphStatement
                    or CreateDomainStatement or AlterDomainStatement or CreateDualityViewStatement or AlterAnnotationsStatement
                    or CreateJavaStatement or CreateLibraryStatement or CreateUserStatement or AlterUserStatement
                    or CreateRoleStatement or AlterRoleStatement
                    or CreateProfileStatement or AlterProfileStatement
                    or CreateDirectoryStatement or CreateDatabaseLinkStatement or CreateContextStatement
                    or CreateTablespaceStatement or AlterTablespaceStatement
                    or CreateAuditPolicyStatement or AlterAuditPolicyStatement or AuditStatement:
                    Ddl(st); break;
                // PL/SQL
                case BlockStatement or CreateProgramStatement or CreatePackageStatement or CreatePackageBodyStatement or CreateTriggerStatement
                    or CreateTypeStatement or CreateTypeBodyStatement or NullStatement or AssignmentStatement or ProcedureCallStatement
                    or IfStatement or PlsqlCaseStatement or LoopStatement or ExitStatement or ContinueStatement or GotoStatement
                    or ReturnStatement or RaiseStatement or OpenStatement or FetchStatement or CloseStatement
                    or ExecuteImmediateStatement or ForallStatement or PipeRowStatement or InlinePragmaStatement or ConditionalStatement or CompilationErrorStatement:
                    Plsql(st); break;
                default: throw Unsupported(st);
            }
        }

        /// <summary>Space after a label (a new line in multi-line PL/SQL mode).</summary>
        private void PlSp() { if (_o.MultilinePlsql) NewLine(); else Sp(); }

        // ---------- INSERT / UPDATE / DELETE / MERGE ----------

        private void DmlHint(DmlStatement d) { if (d.Hint != null) { Sp(); Hint(d.Hint); } }

        private void DmlTail(DmlStatement d)
        {
            if (d.Returning is { } returning)
            {
                ClauseStart(_o.NewLineBeforeReturningClause);
                Add("RETURNING "); Sep(returning.Items, ReturningItemText);
                if (returning.Into is { } returningInto) { Add(returningInto.BulkCollect ? " BULK COLLECT INTO " : " INTO "); Sep(returningInto.Targets, e => Expr(e)); }
            }
            if (d.ErrorLogging != null) { ClauseStart(_o.NewLineBeforeReturningClause); ErrorLoggingText(d.ErrorLogging); }
        }

        private void ReturningItemText(ReturningItem r)
        {
            if (r.Image is { } image) Add(image == ReturningImage.Old ? "OLD " : "NEW ");
            Expr(r.Expression);
        }

        private void ErrorLoggingText(ErrorLoggingClause el)
        {
            Add("LOG ERRORS");
            if (el.Table.Count > 0) { Add(" INTO "); Name(el.Table); }
            if (el.Tag != null) { Add(" ("); Expr(el.Tag); Add(")"); }
            if (el.RejectUnlimited) Add(" REJECT LIMIT UNLIMITED");
            else if (el.RejectLimit != null) { Add(" REJECT LIMIT "); Expr(el.RejectLimit); }
        }

        private void Insert(InsertStatement i)
        {
            Add("INSERT"); DmlHint(i);
            Add(" INTO "); Table(i.Target);
            if (i.Columns.Count > 0) { Add(" ("); Sep(i.Columns, col => Expr(col)); Add(")"); }
            if (i.Values is { } values) { ClauseStart(_o.NewLineBeforeValuesClause); ValuesText(values); }
            else if (i.Query != null) { ClauseStart(_o.NewLineBeforeValuesClause); Select(i.Query); }
            DmlTail(i);
        }

        /// <summary>The <c>VALUES</c> clause: its rows, or the PL/SQL record.</summary>
        private void ValuesText(ValuesClause v)
        {
            Add("VALUES ");
            if (v.Record != null) Expr(v.Record);
            else Sep(v.Rows, ValueRowText);
        }

        /// <summary>One parenthesized row of a VALUES list.</summary>
        private void ValueRowText(RowValue r) { Add("("); Sep(r.Values, e => Expr(e)); Add(")"); }

        private void InsertInto(InsertIntoClause c)
        {
            Add("INTO "); Table(c.Table);
            if (c.Columns.Count > 0) { Add(" ("); Sep(c.Columns, col => Expr(col)); Add(")"); }
            if (c.Values is { } values) { Add(" "); ValuesText(values); }
        }

        private void MultiInsert(MultiTableInsertStatement m)
        {
            Add("INSERT"); DmlHint(m);
            Add(m.First ? " FIRST" : " ALL");
            if (m.Conditional)
            {
                foreach (var b in m.Branches)
                {
                    ClauseStart(_o.NewLineBeforeValuesClause); Add("WHEN "); Expr(b.When!); Add(" THEN");
                    foreach (var t in b.Targets) { ClauseStart(_o.NewLineBeforeValuesClause); InsertInto(t); }
                }
                if (m.Else != null)
                {
                    ClauseStart(_o.NewLineBeforeValuesClause); Add("ELSE");
                    foreach (var t in m.Else.Targets) { ClauseStart(_o.NewLineBeforeValuesClause); InsertInto(t); }
                }
            }
            else
                foreach (var b in m.Branches)
                    foreach (var t in b.Targets) { ClauseStart(_o.NewLineBeforeValuesClause); InsertInto(t); }
            ClauseStart(_o.NewLineBeforeValuesClause);
            Select(m.Query);
            DmlTail(m);
        }

        /// <summary>The SET clause: the keyword and its assignments.</summary>
        private void SetText(SetClause s) { Add("SET "); Sep(s.Items, SetItemText); }

        private void SetItemText(SetItem sc)
        {
            if (sc.Row) { Add("ROW = "); Expr(sc.Value); return; }
            if (sc.Columns.Count == 1) Expr(sc.Columns[0]);
            else { Add("("); Sep(sc.Columns, c => Expr(c)); Add(")"); }
            Add(" = ");
            Expr(sc.Value);
        }

        private void Update(UpdateStatement u)
        {
            Add("UPDATE"); DmlHint(u);
            Sp(); Table(u.Target);
            ClauseStart(_o.NewLineBeforeSetClause); SetText(u.Set);
            if (u.From is { } uFrom) { ClauseStart(_o.NewLineBeforeFromClause); Add("FROM "); Sep(uFrom.Tables, Table); }
            if (u.Where is { } uWhere) { ClauseStart(_o.NewLineBeforeWhereClause); Add("WHERE "); Expr(uWhere.Condition); }
            DmlTail(u);
        }

        private void Delete(DeleteStatement d)
        {
            Add("DELETE"); DmlHint(d);
            Add(" FROM "); Table(d.Target);
            if (d.From is { } dFrom) { ClauseStart(_o.NewLineBeforeFromClause); Add("FROM "); Sep(dFrom.Tables, Table); }
            if (d.Where is { } dWhere) { ClauseStart(_o.NewLineBeforeWhereClause); Add("WHERE "); Expr(dWhere.Condition); }
            DmlTail(d);
        }

        private void Merge(MergeStatement m)
        {
            Add("MERGE"); DmlHint(m);
            Add(" INTO "); Table(m.Target);
            ClauseStart(_o.NewLineBeforeFromClause); Add("USING "); Table(m.Source);
            ClauseStart(_o.NewLineBeforeFromClause); Add("ON "); Expr(m.On);
            foreach (var c in m.Clauses)
            {
                ClauseStart(_o.NewLineBeforeSetClause);
                Add(c is MergeMatchedClause ? "WHEN MATCHED" : "WHEN NOT MATCHED");
                if (c.AndCondition != null) { Add(" AND "); Expr(c.AndCondition); }
                Add(" THEN ");
                switch (c)
                {
                    case MergeMatchedClause matched:
                        Add("UPDATE "); SetText(matched.Set);
                        if (matched.UpdateWhere is { } updateWhere) { Add(" WHERE "); Expr(updateWhere.Condition); }
                        if (matched.DeleteWhere is { } deleteWhere) { Add(" DELETE WHERE "); Expr(deleteWhere.Condition); }
                        break;
                    case MergeNotMatchedClause notMatched:
                        Add("INSERT");
                        if (notMatched.Columns.Count > 0) { Add(" ("); Sep(notMatched.Columns, col => Expr(col)); Add(")"); }
                        Add(" "); ValuesText(notMatched.Values);
                        if (notMatched.Where is { } insertWhere) { Add(" WHERE "); Expr(insertWhere.Condition); }
                        break;
                    default: throw Unsupported(c);
                }
            }
            DmlTail(m);
        }
    }
}
