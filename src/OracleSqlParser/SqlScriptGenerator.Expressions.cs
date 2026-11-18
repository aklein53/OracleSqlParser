using System.Text;

namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        private void JsonMemberDef(JsonMember m)
        {
            Expr(m.Key); Add(" : ");
            if (m.Value is JsonArrayConstructor ja) JsonArrayDef(ja); else Expr(m.Value);
            if (m.NullHandling != null) Add(m.NullHandling == JsonNullHandling.AbsentOnNull ? " ABSENT ON NULL" : " NULL ON NULL");
            if (m.Returning != null) { Add(" RETURNING "); Type(m.Returning); }
        }

        /// <summary>The bracketed array; the JSON keyword is added by the caller when the array stands alone.</summary>
        private void JsonArrayDef(JsonArrayConstructor a)
        {
            Add("[");
            if (a.Query != null) Select(a.Query); else Sep(a.Elements, Expr);
            Add("]");
        }

        private void DualityWordDef(DualityAnnotationKind a) => Add(a switch
        {
            DualityAnnotationKind.Insert => "INSERT", DualityAnnotationKind.Update => "UPDATE", DualityAnnotationKind.Delete => "DELETE",
            DualityAnnotationKind.NoInsert => "NOINSERT", DualityAnnotationKind.NoUpdate => "NOUPDATE", DualityAnnotationKind.NoDelete => "NODELETE",
            DualityAnnotationKind.NoCheck => "NOCHECK", DualityAnnotationKind.Etag => "ETAG", _ => "NO ETAG",
        });

        private void Type(DataType dt)
        {
            if (dt.Ref) Add("REF ");
            // A user-defined or anchored name is printed from its parts, so quoting and dot boundaries survive.
            if (dt.NameParts.Count > 0)
            {
                Name(dt.NameParts);
                if (dt.Attribute != null) { Add("%").Add(dt.Attribute); return; }
                if (dt.Arguments.Count > 0) TypeArguments(dt);
                return;
            }
            if (dt.Attribute != null) { Add(dt.Name).Add("%").Add(dt.Attribute); return; }
            // name words: the parenthesized arguments follow the first word group that precedes WITH/TO/etc.
            var words = dt.Name.Split(' ');
            int argAt = words.Length;
            for (int i = 1; i < words.Length; i++)
                if (words[i] is "WITH" or "WITHOUT" or "TO") { argAt = i; break; }
            if (dt.Name.StartsWith("INTERVAL")) argAt = 2; // INTERVAL DAY(2) TO SECOND(6)
            Add(string.Join(" ", words.Take(argAt)));
            if (dt.Arguments.Count > 0)
            {
                if (dt.Name.StartsWith("INTERVAL") && dt.Arguments.Count == 2)
                {
                    Add("(").ExprC(dt.Arguments[0]).Add(")");
                    Add(" " + string.Join(" ", words.Skip(argAt)));
                    Add("(").ExprC(dt.Arguments[1]).Add(")");
                    return;
                }
                TypeArguments(dt);
            }
            if (argAt < words.Length) Add(" " + string.Join(" ", words.Skip(argAt)));
        }

        /// <summary>The parenthesized length, precision or scale arguments, with CHAR or BYTE semantics when given.</summary>
        private void TypeArguments(DataType dt)
        {
            Add("(");
            Sep(dt.Arguments, a => Expr(a));
            if (dt.Semantics != null) Add(dt.Semantics == CharSemantics.Char ? " CHAR" : " BYTE");
            Add(")");
        }

        private Printer ExprC(Expression e) { Expr(e); return this; }

        // ---------- expressions ----------

        /// <summary>The text of a comparison operator: the source spelling when one is recorded for NotEquals, otherwise the canonical text.</summary>
        private static string ComparisonText(BinaryOperator op, string? spelling)
        {
            if (op == BinaryOperator.NotEquals && spelling is "!=" or "^=" or "~=") return spelling;
            return op.ToSql();
        }

        private static int Prec(Expression e) => e switch
        {
            BinaryExpression b => b.Operator switch
            {
                BinaryOperator.Or => 1,
                BinaryOperator.And => 2,
                BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Concatenate => 5,
                BinaryOperator.Multiply or BinaryOperator.Divide => 6,
                BinaryOperator.Power => 8,
                BinaryOperator.MultisetUnion or BinaryOperator.MultisetUnionAll or BinaryOperator.MultisetUnionDistinct
                    or BinaryOperator.MultisetIntersect or BinaryOperator.MultisetIntersectAll or BinaryOperator.MultisetIntersectDistinct
                    or BinaryOperator.MultisetExcept or BinaryOperator.MultisetExceptAll or BinaryOperator.MultisetExceptDistinct => 5,
                _ => 4,
            },
            UnaryExpression u => u.Operator == UnaryOperator.Not ? 3 : 7,
            BetweenExpression or InExpression or LikeExpression or IsExpression or QuantifiedComparison => 4,
            AtTimeZoneExpression or CollateExpression => 9,
            _ => 100,
        };

        private void Child(Expression e, int minPrec)
        {
            bool paren = Prec(e) < minPrec;
            if (paren) Add("(");
            Expr(e);
            if (paren) Add(")");
        }

        private void Expr(Expression e)
        {
            switch (e)
            {
                case Literal l: Lit(l); break;
                case ColumnReference c:
                    Name(c.Parts);
                    if (c.Attribute != null) Add("%" + c.Attribute);
                    if (c.OuterJoin) Add("(+)");
                    break;
                case BindVariable b: Raw(b.Quoted ? ":\"" + b.Name + "\"" : ":" + b.Name); break;
                case InquiryDirective inq: Id(inq.Name); break;
                case SubstitutionVariable sv: Raw(sv.Persistent ? new string(sv.Prefix, 2) : sv.Prefix.ToString()).Raw(sv.Name); break;
                case StarExpression st:
                    if (st.Qualifier.Count > 0) { Name(st.Qualifier); Add("."); }
                    Add("*");
                    break;
                case ParenthesizedExpression p: Add("("); Expr(p.Inner); Add(")"); break;
                case ExpressionList el: Add("("); Sep(el.Items, i => Expr(i)); Add(")"); break;
                case BinaryExpression be: Binary(be); break;
                case UnaryExpression ue:
                {
                    int op = ue.Operator == UnaryOperator.Not ? 3 : 7;
                    if (ue.Operator is UnaryOperator.Not or UnaryOperator.Prior or UnaryOperator.ConnectByRoot) { Add(ue.Operator.ToSql() + " "); Child(ue.Operand, op); }
                    else
                    {
                        Add(ue.Operator.ToSql());
                        int pos = _sb.Length;
                        Child(ue.Operand, op);
                        if (pos < _sb.Length && _sb[pos] is '-' or '+') _sb.Insert(pos, ' ');
                    }
                    break;
                }
                // The left operand of a comparison-level form may itself be one (the parser reads "a IN (1) IN (2)" as a
                // left-associative chain, with no parentheses), so it is printed at comparison level, not wrapped.
                case BetweenExpression bt:
                    Child(bt.Value, 4); Add(bt.Not ? " NOT BETWEEN " : " BETWEEN "); Child(bt.Low, 5); Add(" AND "); Child(bt.High, 5);
                    break;
                case InExpression ie:
                    Child(ie.Value, 4); Add(ie.Not ? " NOT IN " : " IN ");
                    if (ie.Subquery != null) ParenQuery(ie.Subquery); else { Add("("); Sep(ie.List, i => Expr(i)); Add(")"); }
                    break;
                case LikeExpression lk:
                    Child(lk.Value, 4);
                    Add((lk.Not ? " NOT " : " ") + lk.Kind.ToString().ToUpperInvariant() + " ");
                    Child(lk.Pattern, 5);
                    if (lk.Escape != null) { Add(" ESCAPE "); Child(lk.Escape, 5); }
                    break;
                case IsExpression isx: Is(isx); break;
                case QuantifiedComparison qc:
                    Child(qc.Left, 4); Add(" " + ComparisonText(qc.Operator, qc.OperatorSpelling) + " " + qc.Quantifier.ToString().ToUpperInvariant() + " ");
                    Expr(qc.Right);
                    break;
                case ExistsExpression ex: Add("EXISTS "); ParenQuery(ex.Subquery); break;
                case SubqueryExpression sq: ParenQuery(sq.Query); break;
                case CursorExpression cu: Add("CURSOR"); ParenQuery(cu.Query); break;
                case CaseExpression ce:
                    Add("CASE");
                    if (ce.Operand != null) { Add(" "); Expr(ce.Operand); }
                    foreach (var w in ce.Whens) { Add(" WHEN "); Expr(w.When); Add(" THEN "); Expr(w.Then); }
                    if (ce.Else != null) { Add(" ELSE "); Expr(ce.Else); }
                    Add(" END");
                    break;
                case CastExpression ca: Cast(ca); break;
                case ExtractExpression ex2: Add("EXTRACT(" + ex2.Field.ToString().ToUpperInvariant().Replace("TIMEZONE", "TIMEZONE_") + " FROM "); Expr(ex2.Source); Add(")"); break;
                case TrimExpression tr:
                    Add("TRIM(");
                    if (tr.Spec != null) Add(tr.Spec.ToString()!.ToUpperInvariant() + " ");
                    if (tr.Characters != null) { Expr(tr.Characters); Add(" "); }
                    if (tr.Spec != null || tr.Characters != null) Add("FROM ");
                    Expr(tr.Source); Add(")");
                    break;
                case AtTimeZoneExpression az:
                    Child(az.Value, 9);
                    if (az.Local) Add(" AT LOCAL"); else { Add(" AT TIME ZONE "); Child(az.Zone!, 100); }
                    break;
                case CollateExpression co: Child(co.Value, 9); Add(" COLLATE "); Id(co.Collation); break;
                case IntervalLiteral il: Interval(il); break;
                case IntervalExpression ix:
                    Expr(ix.Value); Add(" " + ix.From.ToString().ToUpperInvariant());
                    if (ix.FromPrecision != null) Add($"({ix.FromPrecision})");
                    Add(" TO " + ix.To.ToString().ToUpperInvariant());
                    if (ix.ToPrecision != null) Add($"({ix.ToPrecision})");
                    break;
                case FunctionCall fc: Function(fc); break;
                case MethodCall mc: Expr(mc.Target); Add("."); Id(mc.Name); Add("("); Sep(mc.Arguments, Arg); Add(")"); break;
                case MemberAccess ma: Expr(ma.Target); Add("."); Id(ma.Member); break;
                case ChainedCall cc: Expr(cc.Target); Add("("); Sep(cc.Arguments, Arg); Add(")"); break;
                case JsonObjectConstructor jo:
                    Add("JSON {");
                    if (jo.Wildcard) Add("*"); else Sep(jo.Members, JsonMemberDef);
                    Add("}");
                    break;
                case JsonArrayConstructor ja: Add("JSON "); JsonArrayDef(ja); break;
                case CellReference cr:
                    Expr(cr.Base); Add("[");
                    Sep(cr.Subscripts, s => Expr(s));
                    Add("]");
                    break;
                case ForIterationSubscript fi: ForIteration(fi); break;
                case SubscriptRange sr: Expr(sr.From); Add(" TO "); Expr(sr.To); break;
                case QualifiedExpression qx:
                    if (qx.Others) Add("OTHERS");
                    else if (qx.Iterator is { } it) { Add("FOR "); Id(it.Variable); Add(" IN "); Expr(it.Low); Add(".."); Expr(it.High); }
                    else Expr(qx.Choice!);
                    Add(" => "); Expr(qx.Value);
                    break;
                case CurrentOfExpression co2: Add("CURRENT OF "); Id(co2.Cursor); break;
                default: throw Unsupported(e);
            }
        }

        private void Lit(Literal l)
        {
            switch (l.Kind)
            {
                case LiteralKind.String: StringLit(l); break;
                case LiteralKind.Date: Add("DATE "); Raw(Quote(l.Value ?? "")); break;
                case LiteralKind.Timestamp: Add("TIMESTAMP "); Raw(Quote(l.Value ?? "")); break;
                case LiteralKind.Null: Add("NULL"); break;
                case LiteralKind.Default: Add("DEFAULT"); break;
                case LiteralKind.Any: Add("ANY"); break;
                case LiteralKind.Boolean: Add(l.Value ?? ""); break;
                default: Raw(l.Value ?? ""); break;
            }
        }

        /// <summary>'text', N'text', or q'[text]' / nq'[text]' when the parsed literal used alternative quoting (and the text allows it).</summary>
        private void StringLit(Literal l)
        {
            var v = l.Value ?? "";
            if (l.QuoteDelimiter is { } open)
            {
                char close = open switch { '[' => ']', '(' => ')', '{' => '}', '<' => '>', _ => open };
                if (!v.Contains(close + "'"))
                {
                    Raw((l.National ? "nq" : "q") + "'" + open + v + close + "'");
                    return;
                }
            }
            Raw((l.National ? "N" : "") + Quote(v));
        }

        private void Binary(BinaryExpression b)
        {
            // Iterate down the left spine so a 20,000-term chain (a AND b AND c ...) does not recurse once per link.
            var spine = new List<BinaryExpression> { b };
            Expression cur = b.Left;
            while (cur is BinaryExpression lb && Prec(lb) >= Prec(spine[^1])) { spine.Add(lb); cur = lb.Left; }
            Child(cur, Prec(spine[^1]));
            for (int i = spine.Count - 1; i >= 0; i--)
            {
                var x = spine[i];
                int p = Prec(x);
                bool assoc = x.Operator is BinaryOperator.And or BinaryOperator.Or;
                Add(" " + ComparisonText(x.Operator, x.OperatorSpelling) + " ");
                Child(x.Right, assoc ? p : p + 1);
            }
        }

        private void Is(IsExpression x)
        {
            Child(x.Value, 4);
            Add(x.Not ? " IS NOT " : " IS ");
            switch (x.Test)
            {
                case IsTest.Null: Add("NULL"); break;
                case IsTest.Nan: Add("NAN"); break;
                case IsTest.Infinite: Add("INFINITE"); break;
                case IsTest.Empty: Add("EMPTY"); break;
                case IsTest.Present: Add("PRESENT"); break;
                case IsTest.Dangling: Add("DANGLING"); break;
                case IsTest.ASet: Add("A SET"); break;
                case IsTest.True: Add("TRUE"); break;
                case IsTest.False: Add("FALSE"); break;
                case IsTest.Json: Add("JSON"); foreach (var c in x.JsonClauses) { Add(" "); Clause(c); } break;
                case IsTest.OfType:
                    Add("OF (");
                    Sep(x.OfTypes, o => { if (o.Only) Add("ONLY "); Type(o.Type); });
                    Add(")");
                    break;
            }
        }

        private void Interval(IntervalLiteral il)
        {
            Add("INTERVAL "); Expr(il.Value); Add(" " + il.From.ToString().ToUpperInvariant());
            if (il.FromPrecision != null) Add($"({il.FromPrecision})");
            if (il.To != null)
            {
                Add(" TO " + il.To.ToString()!.ToUpperInvariant());
                if (il.ToPrecision != null) Add($"({il.ToPrecision})");
            }
        }

        private void Cast(CastExpression c)
        {
            if (c.Kind == CastKind.GeneralizedInvocation)
            {
                Add("("); Expr(c.Value); Add(" AS "); Type(c.Type!); Add(")");
                return;
            }
            string fn = c.Kind switch { CastKind.XmlCast => "XMLCAST", CastKind.Treat => "TREAT", CastKind.ValidateConversion => "VALIDATE_CONVERSION", CastKind.XmlSerialize => "XMLSERIALIZE", _ => "CAST" };
            Add(fn + "(");
            if (c.Kind == CastKind.XmlSerialize)
                foreach (var f in c.Clauses.OfType<FlagClause>().Where(f => f.Flag is FunctionFlag.Document or FunctionFlag.Content)) { Clause(f); Add(" "); }
            if (c.Multiset) Add("MULTISET");
            Expr(c.Value);
            if (c.Type != null) { Add(" AS "); if (c.Ref) Add("REF "); Type(c.Type); }
            if (c.OnConversionError != null) { Add(" "); Clause(c.OnConversionError); }
            if (c.Format != null) { Add(", "); Expr(c.Format); if (c.NlsParam != null) { Add(", "); Expr(c.NlsParam); } }
            foreach (var cl in c.Clauses.Where(x => !(x is FlagClause f && f.Flag is FunctionFlag.Document or FunctionFlag.Content)))
            {
                Add(" "); Clause(cl);
            }
            Add(")");
        }

        private void ForIteration(ForIterationSubscript fi)
        {
            Add("FOR ");
            if (fi.Dimensions.Count == 1) Id(fi.Dimensions[0]); else { Add("("); IdList(fi.Dimensions); Add(")"); }
            if (fi.InQuery != null) { Add(" IN ("); Select(fi.InQuery); Add(")"); }
            else if (fi.From == null) { Add(" IN ("); Sep(fi.InList, i => Expr(i)); Add(")"); }
            else
            {
                if (fi.LikePattern != null) { Add(" LIKE "); Expr(fi.LikePattern); }
                Add(" FROM "); Expr(fi.From); Add(" TO "); Expr(fi.To!);
                if (fi.Step != null) { Add(fi.Decrement ? " DECREMENT " : " INCREMENT "); Expr(fi.Step); }
            }
        }

        // ---------- functions ----------

        private void Function(FunctionCall fc)
        {
            if (fc.New) Add("NEW ");
            Name(fc.Name);
            if (fc.Attribute != null) Add("%" + fc.Attribute);
            Add("(");
            if (fc.Quantifier != null) Add(fc.Quantifier.ToString()!.ToUpperInvariant() + " ");
            Sep(fc.Arguments, Arg);
            if (fc.Operations.Count > 0)
            {
                foreach (var op in fc.Operations) { Add(", "); JsonOp(op); }
            }
            if (fc.PartitionBy.Count > 0) { Add("PARTITION BY "); Sep(fc.PartitionBy, e => Expr(e)); }
            if (fc.OrderBy is { } callOrder) { Add(" ORDER BY "); Sep(callOrder.Items, OrderItem); }
            foreach (var c in fc.Clauses) { Add(" "); Clause(c); }
            Add(")");
            if (fc.WithinGroup is { } withinGroup) { Add(" WITHIN GROUP (ORDER BY "); Sep(withinGroup.Items, OrderItem); Add(")"); }
            if (fc.Keep is { } keep) { Add(" KEEP (DENSE_RANK " + (keep.First ? "FIRST" : "LAST") + " ORDER BY "); Sep(keep.OrderBy?.Items ?? [], OrderItem); Add(")"); }
            if (fc.FromLast != null) Add(fc.FromLast.Value ? " FROM LAST" : " FROM FIRST");
            if (fc.IgnoreNulls) Add(" IGNORE NULLS");
            if (fc.RespectNulls) Add(" RESPECT NULLS");
            if (fc.Over != null) { Add(" OVER "); Window(fc.Over); }
        }

        private void Arg(Argument a)
        {
            if (a.Name != null) { Id(a.Name); Add(" => "); }
            if (a.Keyword != null) Add(a.Keyword.ToString()!.ToUpperInvariant() + " ");
            Expr(a.Value);
            if (a.MappedValue != null) { Add(" VALUE "); Expr(a.MappedValue); }
            if (a.Format != null) { Add(" "); Clause(a.Format); }
            if (a.Alias != null) { Add(" AS "); Id(a.Alias); }
            if (a.OnConversionError != null) { Add(" "); Clause(a.OnConversionError); }
        }

        private void JsonOp(JsonTransformOperation op)
        {
            Add(op.Op.ToString().ToUpperInvariant() + " "); Expr(op.Path);
            if (op.Op == JsonTransformOp.Rename) { Add(" WITH "); Expr(op.Value!); }
            else if (op.Op == JsonTransformOp.Sort) { if (op.Descending) Add(" DESC"); }
            else if (op.Value != null) { Add(" = "); Expr(op.Value); if (op.Format != null) { Add(" "); Clause(op.Format); } }
            foreach (var c in op.Clauses) { Add(" "); Clause(c); }
        }

        private static string FlagText(FunctionFlag f) => f switch
        {
            FunctionFlag.WithUniqueKeys => "WITH UNIQUE KEYS", FunctionFlag.WithoutUniqueKeys => "WITHOUT UNIQUE KEYS",
            FunctionFlag.NoIndent => "NO INDENT", FunctionFlag.HideDefaults => "HIDE DEFAULTS", FunctionFlag.ShowDefaults => "SHOW DEFAULTS",
            FunctionFlag.AllowScalars => "ALLOW SCALARS", FunctionFlag.DisallowScalars => "DISALLOW SCALARS",
            FunctionFlag.AllowArray => "ALLOW ARRAY", FunctionFlag.DisallowArray => "DISALLOW ARRAY",
            _ => f.ToString().ToUpperInvariant(),
        };

        private void Clause(FunctionClause c)
        {
            switch (c)
            {
                case ReturningClause r: Add("RETURNING "); if (r.Content) Add("CONTENT"); else Type(r.Type!); break;
                case FormatClause f: Add("FORMAT JSON"); if (f.Encoding != null) { Add(" ENCODING "); Id(f.Encoding); } break;
                case NullOnClause n: Add(n.Absent ? "ABSENT ON NULL" : "NULL ON NULL"); break;
                case OnBehaviorClause b:
                    Add(b.Behavior switch
                    {
                        BehaviorKind.Default => "DEFAULT ", BehaviorKind.EmptyArray => "EMPTY ARRAY", BehaviorKind.EmptyObject => "EMPTY OBJECT",
                        _ => b.Behavior.ToString().ToUpperInvariant(),
                    });
                    if (b.Behavior == BehaviorKind.Default) Expr(b.Value!);
                    Add(" ON " + (b.Event == BehaviorEvent.ConversionError ? "CONVERSION ERROR" : b.Event.ToString().ToUpperInvariant()));
                    break;
                case WrapperClause w:
                    Add(w.With ? "WITH " : "WITHOUT ");
                    if (w.Conditional) Add("CONDITIONAL "); else if (w.Unconditional) Add("UNCONDITIONAL ");
                    if (w.Array) Add("ARRAY ");
                    Add("WRAPPER");
                    break;
                case QuotesClause q: Add((q.Keep ? "KEEP" : "OMIT") + " QUOTES" + (q.OnScalarString ? " ON SCALAR STRING" : "")); break;
                case OverflowClause o:
                    Add("ON OVERFLOW ");
                    if (!o.Truncate) Add("ERROR");
                    else
                    {
                        Add("TRUNCATE");
                        if (o.Indicator != null) { Add(" "); Expr(o.Indicator); }
                        if (o.WithCount != null) Add(o.WithCount == true ? " WITH COUNT" : " WITHOUT COUNT");
                    }
                    break;
                case PassingClause p:
                    Add("PASSING " + (p.ByValue ? "BY VALUE " : ""));
                    Sep(p.Items, Item);
                    break;
                case FlagClause f2: Add(FlagText(f2.Flag)); break;
                case ValueClause v:
                    switch (v.Kind)
                    {
                        case ValueClauseKind.Encoding: Add("ENCODING "); Expr(v.Value!); break;
                        case ValueClauseKind.Version: Add("VERSION "); Expr(v.Value!); break;
                        case ValueClauseKind.IndentSize: Add("SIZE = "); Expr(v.Value!); break;
                        case ValueClauseKind.StandaloneYes: Add("STANDALONE YES"); break;
                        case ValueClauseKind.StandaloneNo: Add("STANDALONE NO"); break;
                        default: Add("STANDALONE NO VALUE"); break;
                    }
                    break;
                case UsingCharsetClause u: Add("USING "); Id(u.Charset); break;
                case UsingListClause ul: Add("USING "); Sep(ul.Items, Item); break;
                default: throw Unsupported(c);
            }
        }

        private void Window(WindowSpec w)
        {
            if (w.Name != null && w.PartitionBy.Count == 0 && w.OrderBy == null && w.Frame == null) { Id(w.Name); return; }
            Add("(");
            bool any = false;
            if (w.Name != null) { Id(w.Name); any = true; }
            if (w.PartitionBy.Count > 0) { if (any) Add(" "); Add("PARTITION BY "); Sep(w.PartitionBy, e => Expr(e)); any = true; }
            if (w.OrderBy is { } windowOrder) { if (any) Add(" "); Add("ORDER BY "); Sep(windowOrder.Items, OrderItem); any = true; }
            if (w.Frame != null)
            {
                if (any) Add(" ");
                Add(w.Frame.Unit.ToString().ToUpperInvariant() + " ");
                if (w.Frame.EndBound != null) { Add("BETWEEN "); Bound(w.Frame.StartBound); Add(" AND "); Bound(w.Frame.EndBound); }
                else Bound(w.Frame.StartBound);
            }
            Add(")");
        }

        private void Bound(FrameBound b)
        {
            switch (b.Kind)
            {
                case FrameBoundKind.UnboundedPreceding: Add("UNBOUNDED PRECEDING"); break;
                case FrameBoundKind.UnboundedFollowing: Add("UNBOUNDED FOLLOWING"); break;
                case FrameBoundKind.CurrentRow: Add("CURRENT ROW"); break;
                case FrameBoundKind.Preceding: Child(b.Offset!, 5); Add(" PRECEDING"); break;
                default: Child(b.Offset!, 5); Add(" FOLLOWING"); break;
            }
        }
    }
}
