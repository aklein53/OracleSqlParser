using System.Text;
using OracleSqlParser;

namespace OracleSqlParser.Fuzzing;

/// <summary>
/// Random input generator shared by tools/Fuzz and the unit smoke test (linked, not referenced). Deterministic for a given
/// seed. Families: mutations of seed statements, structured random SQL and PL/SQL, keyword soup, deep nesting around and far
/// beyond the depth limit, long left-deep chains, long identifiers and literals, quoting edge cases and unicode.
/// </summary>
public sealed class FuzzInputs
{
    private static readonly string[] Keywords =
    [
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "OUTER", "INNER", "CROSS", "NATURAL", "FULL", "ON", "USING", "GROUP", "BY",
        "ORDER", "HAVING", "CONNECT", "PRIOR", "START", "WITH", "AS", "CASE", "WHEN", "THEN", "ELSE", "END", "IF", "ELSIF",
        "LOOP", "WHILE", "FOR", "IN", "OUT", "IS", "NOT", "NULL", "AND", "OR", "BETWEEN", "LIKE", "ESCAPE", "EXISTS", "ANY",
        "ALL", "SOME", "UNION", "INTERSECT", "MINUS", "MULTISET", "MERGE", "INSERT", "UPDATE", "DELETE", "INTO", "VALUES",
        "SET", "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "PACKAGE", "BODY", "PROCEDURE", "FUNCTION", "TRIGGER",
        "TYPE", "DECLARE", "BEGIN", "EXCEPTION", "RAISE", "RETURN", "CURSOR", "OPEN", "FETCH", "CLOSE", "PIVOT", "UNPIVOT",
        "MODEL", "DIMENSION", "MEASURES", "MATCH_RECOGNIZE", "PATTERN", "DEFINE", "OVER", "PARTITION", "ROWS", "RANGE",
        "PRECEDING", "FOLLOWING", "UNBOUNDED", "CURRENT", "ROW", "FILTER", "WITHIN", "IGNORE", "RESPECT", "NULLS", "JSON_TABLE",
        "JSON_OBJECT", "XMLTABLE", "XMLELEMENT", "TABLESAMPLE", "SAMPLE", "FLASHBACK", "VERSIONS", "SCN", "TIMESTAMP", "DATE",
        "INTERVAL", "DAY", "YEAR", "TO", "SECOND", "PIPE", "PARALLEL_ENABLE", "DETERMINISTIC", "PRAGMA", "AUTONOMOUS_TRANSACTION",
        "FORALL", "BULK", "COLLECT", "EXECUTE", "IMMEDIATE", "GRANT", "REVOKE", "ROLE", "USER", "IDENTIFIED", "BY", "CONSTRAINT",
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CHECK", "UNIQUE", "DEFAULT", "ENABLE", "DISABLE", "VALIDATE", "COMMIT",
        "ROLLBACK", "SAVEPOINT", "LOCK", "EXPLAIN", "PLAN", "ANALYZE", "AUDIT", "CALL", "LATERAL", "CUBE", "ROLLUP", "GROUPING",
        "SETS", "LEVEL", "SIBLINGS", "NOCYCLE", "DUAL", "ROWNUM", "ROWID", "SYSDATE", "$IF", "$THEN", "$ELSE", "$END", "$ERROR",
        "DIRECTORY", "CONTEXT", "JSON_EXISTS", "JSON_VALUE", "JSON_QUERY", "GRAPH", "MATCH", "VERTEX", "EDGE", "PROPERTY",
        "SEARCH", "CYCLE", "RECURSIVE", "RETURNING", "LOG", "ERRORS", "REJECT", "LIMIT", "FETCH", "FIRST", "NEXT", "ONLY",
        "PURGE", "TRUNCATE", "RENAME", "COMMENT", "NOAUDIT", "ASSOCIATE", "STATISTICS", "OF", "PURGE", "SESSION", "SYSTEM",
        "CONTAINERS", "EDITIONABLE", "NONEDITIONABLE", "FORCE", "REPLACE", "OR", "TEMPORARY", "GLOBAL", "PARALLEL", "NOLOGGING",
    ];

    private static readonly string[] Operators =
    [
        "(", ")", ",", ";", ".", "*", "/", "+", "-", "=", "!=", "<>", "<", ">", "<=", ">=", "||", ":=", "=>", "..", "**", "%",
        "@", ":", "?", "&", "&&", "^", "~", "'", "\"", "[", "]", "{", "}", "#", "$", "|", "--", "/*", "*/", "/*+", "::",
        "->", "!", "\\", "`", "&x", ":b", "1e", "1.", ".5", "0x", "q'[", "q'{", "Q'<", "N'", "n'", "''", "$$x", "$IF", "\r\n",
    ];

    private static readonly string[] Identifiers = ["a", "b", "t", "x", "T1", "\"Q\"", "\"a b\"", "emp", "dual", "s.t", "t@link", "c$1", "_x", "A#"];
    private static readonly string[] Literals = ["1", "0", "1.5", "1e10", "'s'", "'it''s'", "q'[x]'", "N'nat'", ":bind", "&sub", "&&d.", "DATE '2020-01-01'", "NULL", "TIMESTAMP '2020-01-01 00:00:00'"];
    private static readonly string[] Starters = ["SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "BEGIN", "DECLARE", "WITH", "GRANT", "CALL", "EXPLAIN", "$IF", "ANALYZE", "AUDIT", "LOCK", "TRUNCATE", "("];
    private static readonly string[] QuoteFragments = ["'", "''", "\"", "q'[", "q'{x", "Q'<", "q'!", "N'", "n'", "'\n", "\n", "'a\nb\nc'", "--", "/*", "*/", "/*+", "$IF TRUE $THEN", "$END", "\r", "\r\n", "&x", "&&y.", ":b", "1e", "1.", ".5", "0x", "\t"];
    private static readonly int[] DepthChoices = [1, 2, 5, 50, 197, 198, 199, 200, 201, 202, 203, 300, 1000, 5000];

    /// <summary>Unicode scalars to insert: ASCII controls, BOM, NBSP, line/paragraph separators, astral, lone surrogates, noncharacters.</summary>
    private static readonly string[] UnicodeFragments =
    [
        "\0", "\u0001", "\u001a", "\u007f", "\u00a0", "\u00e9", "\u0100", "\ufeff", "\u2028", "\u2029", "\u200b", "\u3000",
        "\U0001F600", "\U00010000", "\ud800", "\udbff", "\udc00", "\udfff", "\ufffe", "\uffff", "\u0660", "\u2212", "\u201c", "\u2019",
    ];

    private readonly (string Text, (int Start, int End)[] Spans)[] _seeds;
    private readonly Random _rng;

    /// <summary>The longest chain (in terms) that LongChain may generate. The unit smoke test lowers it to keep runs fast.</summary>
    public int MaxChainTerms { get; init; } = 50_000;

    /// <summary>Creates a generator. <paramref name="seeds"/> are SQL texts to mutate (their tokens are computed with the parser).</summary>
    public FuzzInputs(IEnumerable<string> seeds, Random rng)
    {
        _rng = rng;
        _seeds = seeds.Distinct()
            .Select(s => (s, Spans(s)))
            .Where(x => x.Item2.Length > 0)
            .ToArray();
    }

    /// <summary>The number of seed statements that have at least one token.</summary>
    public int SeedCount => _seeds.Length;

    private static (int Start, int End)[] Spans(string sql) =>
        SqlParser.Parse(sql).Tokens.Where(t => t.Type != TokenType.EOF).Select(t => (t.Start, t.End)).ToArray();

    /// <summary>The next input: mostly default options, a few option variants that change the parse.</summary>
    public FuzzCase Next()
    {
        int variant = _rng.Next(100) switch { < 80 => 0, < 86 => 1, < 91 => 2, < 96 => 3, _ => 4 };
        return new FuzzCase(NextSql(), variant);
    }

    /// <summary>The next SQL text, from one of the generation families.</summary>
    public string NextSql()
    {
        int pick = _rng.Next(100);
        return pick switch
        {
            < 28 => MutateSeed(),
            < 40 => Script(),
            < 48 => KeywordSoup(),
            < 60 => DeepNesting(),
            < 68 => LongChain(),
            < 73 => LongNames(),
            < 80 => QuoteSoup(),
            < 87 => UnicodeSoup(),
            < 93 => MutateString(DeepNesting()),
            _ => MutateString(Script()),
        };
    }

    // ---------- (a) mutations of seed statements ----------

    /// <summary>Applies one to three token-level or character-level mutations to a random seed statement.</summary>
    public string MutateSeed()
    {
        if (_seeds.Length == 0) return KeywordSoup();
        var (text, spans) = _seeds[_rng.Next(_seeds.Length)];
        var toks = spans.Select(s => text[s.Start..s.End]).ToList();
        string? mutated = null;
        int ops = 1 + _rng.Next(3);
        for (int k = 0; k < ops; k++)
        {
            if (mutated is null && _rng.Next(4) == 0)
            {
                // Some mutations are character-level: they may split a token.
                mutated = MutateString(string.Join(" ", toks));
                continue;
            }
            MutateTokens(toks);
        }
        if (mutated is not null) return mutated;
        return Join(toks);
    }

    private string Join(List<string> toks) => string.Join(_rng.Next(10) switch { < 8 => " ", 8 => "\n", _ => "" }, toks);

    private void MutateTokens(List<string> toks)
    {
        if (toks.Count == 0)
        {
            toks.Add(Keyword());
            return;
        }
        int k = _rng.Next(toks.Count);
        switch (_rng.Next(14))
        {
            case 0: toks.RemoveAt(k); break;
            case 1: toks.Insert(k, toks[k]); break;
            case 2: if (k + 1 < toks.Count) (toks[k], toks[k + 1]) = (toks[k + 1], toks[k]); break;
            case 3: toks.Insert(k, Keyword()); break;
            case 4: toks.Insert(k, Operators[_rng.Next(Operators.Length)]); break;
            case 5: toks.RemoveRange(k, toks.Count - k); break; // truncate at a token boundary
            case 6: toks.Insert(k, _rng.Next(2) == 0 ? "(" : ")"); break; // unbalanced parenthesis
            case 7: toks.Insert(k, QuoteFragments[_rng.Next(QuoteFragments.Length)]); break;
            case 8: toks[k] = new string('L', _rng.Choose(100, 300, 1000, 5000)); break; // very long identifier
            case 9:
                {
                    // wrap a run of tokens in parentheses of a depth around the limit
                    int depth = DepthChoices[_rng.Next(DepthChoices.Length)];
                    int end = Math.Min(toks.Count, k + 1 + _rng.Next(4));
                    var inner = toks.GetRange(k, end - k);
                    toks.RemoveRange(k, end - k);
                    toks.Insert(k, Repeat("(", depth) + string.Join(" ", inner) + Repeat(")", depth));
                    break;
                }
            case 10: toks.Insert(k, Literals[_rng.Next(Literals.Length)]); break;
            case 11: toks.Insert(k, "/*" + new string('c', _rng.Next(20)) + (_rng.Next(2) == 0 ? "*/" : "")); break;
            case 12: toks.Insert(k, "'" + Repeat("\n", _rng.Next(4)) + "x"); break; // multi-line literal, maybe unterminated
            default: toks[k] = Identifiers[_rng.Next(Identifiers.Length)]; break;
        }
    }

    /// <summary>Character-level mutation: truncate mid-token, insert junk, unicode, a newline, or duplicate a region.</summary>
    public string MutateString(string sql)
    {
        if (sql.Length == 0) return sql;
        int i = _rng.Next(sql.Length + 1);
        switch (_rng.Next(7))
        {
            case 0: return sql[..i];
            case 1: return sql[..i] + UnicodeFragments[_rng.Next(UnicodeFragments.Length)] + sql[i..];
            case 2: return sql[..i] + Operators[_rng.Next(Operators.Length)] + sql[i..];
            case 3: return sql[..i] + "\n" + sql[i..];
            case 4:
                {
                    int len = Math.Min(sql.Length - i, 1 + _rng.Next(200));
                    return sql[..(i + len)] + sql[i..(i + len)] + sql[(i + len)..];
                }
            case 5: return sql + Operators[_rng.Next(Operators.Length)];
            default:
                {
                    int j = _rng.Next(sql.Length + 1);
                    return i <= j ? sql[..i] + sql[j..] : sql;
                }
        }
    }

    // ---------- (b) grammar-ish and keyword-soup generation ----------

    /// <summary>A random script of one to four structured statements, separated by ';' or a lone '/'.</summary>
    public string Script()
    {
        int n = 1 + _rng.Next(4);
        var parts = new List<string>();
        for (int i = 0; i < n; i++) parts.Add(Statement());
        return string.Join(_rng.Next(2) == 0 ? ";\n" : "\n/\n", parts);
    }

    private string Statement()
    {
        return _rng.Next(20) switch
        {
            < 11 => Query(0),
            11 => $"INSERT INTO t ({Column()}, {Column()}) VALUES ({Expr(0)}, {Expr(0)})",
            12 => $"INSERT ALL INTO t VALUES ({Expr(0)}) INTO u VALUES ({Expr(0)}) SELECT 1 FROM dual",
            13 => $"UPDATE t SET {Column()} = {Expr(0)} WHERE {Expr(0)}",
            14 => $"DELETE FROM t WHERE {Expr(0)}",
            15 => $"MERGE INTO t USING s ON ({Expr(0)}) WHEN MATCHED THEN UPDATE SET {Column()} = {Expr(0)} WHEN NOT MATCHED THEN INSERT ({Column()}) VALUES ({Expr(0)})",
            16 => $"CREATE TABLE t ({Column()} NUMBER(10,2) NOT NULL, {Column()} VARCHAR2(20) DEFAULT 'x' PRIMARY KEY)",
            17 => $"CREATE OR REPLACE VIEW v AS {Query(0)}",
            18 => PlsqlBlock(0),
            _ => $"GRANT SELECT ON t TO {Column()}",
        };
    }

    private string PlsqlBlock(int depth)
    {
        if (depth > 2) return "NULL";
        return _rng.Next(4) switch
        {
            0 => $"DECLARE v NUMBER := {Expr(0)}; BEGIN IF {Expr(0)} THEN NULL; ELSIF {Expr(0)} THEN v := 1; ELSE NULL; END IF; END",
            1 => $"BEGIN FOR i IN 1..10 LOOP {PlsqlBlock(depth + 1)}; END LOOP; WHILE {Expr(0)} LOOP EXIT; END LOOP; END",
            2 => $"BEGIN SELECT {Expr(0)} INTO v FROM dual; EXCEPTION WHEN OTHERS THEN RAISE; END",
            _ => $"BEGIN BEGIN NULL; END; CASE WHEN {Expr(0)} THEN NULL; END CASE; END",
        };
    }

    private string Query(int depth)
    {
        var sb = new StringBuilder();
        if (depth < 3 && _rng.Next(4) == 0) sb.Append($"WITH c{depth} AS ({Query(depth + 1)}) ");
        sb.Append("SELECT ");
        if (_rng.Next(4) == 0) sb.Append("DISTINCT ");
        int n = 1 + _rng.Next(4);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(_rng.Next(6) == 0 ? "*" : Expr(depth));
            if (_rng.Next(3) == 0) sb.Append(" AS a" + i);
        }
        sb.Append(" FROM ").Append(TableRef(depth));
        if (_rng.Next(3) == 0) sb.Append($" JOIN {TableRef(depth)} ON {Expr(depth)}");
        if (_rng.Next(3) == 0) sb.Append($" LEFT OUTER JOIN {TableRef(depth)} USING (id)");
        if (_rng.Next(2) == 0) sb.Append(" WHERE ").Append(Expr(depth));
        if (_rng.Next(4) == 0) sb.Append($" GROUP BY {Expr(depth)} HAVING COUNT(*) > 1");
        if (_rng.Next(4) == 0) sb.Append($" ORDER BY {Expr(depth)} {(_rng.Next(2) == 0 ? "ASC" : "DESC")} NULLS LAST");
        if (_rng.Next(6) == 0) sb.Append(" FETCH FIRST 5 ROWS ONLY");
        if (depth < 3 && _rng.Next(6) == 0) sb.Append((_rng.Next(2) == 0 ? " UNION ALL " : " MINUS ") + Query(depth + 1));
        return sb.ToString();
    }

    private string TableRef(int depth)
    {
        return _rng.Next(8) switch
        {
            0 => $"({Query(depth + 1)}) q{depth}",
            1 => "t PARTITION (p1)",
            2 => "s.t SAMPLE (10)",
            3 => "TABLE(f(x))",
            4 => "t AS OF SCN 10",
            5 => $"JSON_TABLE(j, '$[*]' COLUMNS (v NUMBER PATH '$.v')) jt",
            _ => "t" + _rng.Next(5),
        };
    }

    private string Column() => _rng.Next(3) == 0 ? "c" + _rng.Next(5) : Identifiers[_rng.Next(Identifiers.Length)];

    private string Expr(int depth)
    {
        if (depth > 3) return Atom();
        depth++;
        return _rng.Next(20) switch
        {
            0 => Atom(),
            1 => $"{Expr(depth)} {Binary()} {Expr(depth)}",
            2 => $"({Expr(depth)})",
            3 => $"ABS({Expr(depth)}, {Expr(depth)})",
            4 => "COUNT(*)",
            5 => $"SUM(DISTINCT {Expr(depth)}) OVER (PARTITION BY {Expr(depth)} ORDER BY {Expr(depth)} ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)",
            6 => $"CASE WHEN {Expr(depth)} THEN {Expr(depth)} ELSE {Expr(depth)} END",
            7 => $"CASE {Expr(depth)} WHEN {Expr(depth)} THEN {Expr(depth)} END",
            8 => $"{Expr(depth)} IS NOT NULL",
            9 => $"{Expr(depth)} NOT BETWEEN {Expr(depth)} AND {Expr(depth)}",
            10 => $"{Expr(depth)} IN ({Expr(depth)}, {Expr(depth)})",
            11 => $"{Expr(depth)} IN (SELECT {Expr(depth)} FROM dual)",
            12 => $"EXISTS (SELECT 1 FROM dual WHERE {Expr(depth)})",
            13 => $"NOT {Expr(depth)}",
            14 => $"-{Expr(depth)}",
            15 => $"CAST({Expr(depth)} AS VARCHAR2(10))",
            16 => $"JSON_OBJECT('k' VALUE {Expr(depth)} NULL ON NULL)",
            17 => $"{Expr(depth)} LIKE 'a%' ESCAPE '\\'",
            18 => $"LISTAGG({Expr(depth)}, ',') WITHIN GROUP (ORDER BY {Expr(depth)})",
            _ => $"seq.NEXTVAL + {Expr(depth)}",
        };
    }

    private static readonly string[] BinaryOps = ["+", "-", "*", "/", "||", "=", "<>", "<", ">=", "AND", "OR"];

    private string Binary() => BinaryOps[_rng.Next(BinaryOps.Length)];

    private string Atom() => _rng.Next(3) switch
    {
        0 => Literals[_rng.Next(Literals.Length)],
        1 => Identifiers[_rng.Next(Identifiers.Length)],
        _ => "t." + Identifiers[_rng.Next(Identifiers.Length)],
    };

    /// <summary>Random keywords, operators, identifiers and literals in a random order, starting with a statement keyword.</summary>
    public string KeywordSoup()
    {
        var toks = new List<string> { Starters[_rng.Next(Starters.Length)] };
        int n = 1 + _rng.Next(40);
        for (int i = 0; i < n; i++) toks.Add(_rng.Next(3) switch { 0 => Keyword(), 1 => Operators[_rng.Next(Operators.Length)], _ => Atom() });
        return Join(toks);
    }

    private string Keyword() => Keywords[_rng.Next(Keywords.Length)];

    // ---------- (c) deep nesting and long chains ----------

    /// <summary>A construct nested to a depth around, at, or far beyond the default limit of 200.</summary>
    public string DeepNesting()
    {
        int d = _rng.Next(10) switch
        {
            < 3 => 1 + _rng.Next(20),
            < 8 => 195 + _rng.Next(12),
            < 9 => 300 + _rng.Next(200),
            _ => _rng.Choose(1000, 5000, 20000),
        };
        return _rng.Next(26) switch
        {
            0 => "SELECT " + Repeat("(", d) + "1" + Repeat(")", d) + " FROM dual",
            1 => "SELECT " + Repeat("ABS(", d) + "1" + Repeat(")", d) + " FROM dual",
            2 => "SELECT " + Repeat("(SELECT ", d) + "1 FROM dual" + Repeat(")", d) + " FROM dual",
            3 => "SELECT * FROM " + Repeat("(SELECT * FROM ", d) + "dual" + Repeat(")", d),
            4 => "SELECT " + Repeat("CASE WHEN 1=1 THEN ", d) + "1" + Repeat(" END", d) + " FROM dual",
            5 => "SELECT CASE " + Repeat("WHEN ", d) + "1" + Repeat(" THEN 1 END", d) + " FROM dual",
            6 => "SELECT 1 FROM dual WHERE " + Repeat("NOT ", d) + "1=1",
            7 => "SELECT " + Repeat("- ", d) + "1 FROM dual",
            8 => "SELECT " + Repeat("1 || (", d) + "1" + Repeat(")", d) + " FROM dual",
            9 => "DECLARE x NUMBER; BEGIN " + Repeat("BEGIN ", d) + "NULL; " + Repeat("END; ", d) + "END;",
            10 => "BEGIN " + Repeat("IF 1=1 THEN ", d) + "NULL; " + Repeat("END IF; ", d) + "END;",
            11 => "BEGIN " + Repeat("LOOP ", d) + "EXIT; " + Repeat("END LOOP; ", d) + "END;",
            12 => Repeat("$IF TRUE $THEN ", d) + "SELECT 1 FROM dual; " + Repeat("$END ", d),
            13 => "SELECT " + Repeat("JSON_OBJECT('k' VALUE ", d) + "1" + Repeat(")", d) + " FROM dual",
            14 => "SELECT 1 FROM dual WHERE " + Repeat("EXISTS (SELECT 1 FROM dual WHERE ", d) + "1=1" + Repeat(")", d),
            15 => "SELECT * FROM " + Repeat("(t JOIN ", d) + "t2 ON 1=1" + Repeat(" ON 1=1)", d),
            16 => "ALTER SESSION SET x = " + Repeat("(", d) + "1" + Repeat(")", d),
            17 => "SELECT 1 FROM dual WHERE " + Repeat("1=1 AND (", d) + "1=1" + Repeat(")", d),
            18 => "SELECT a" + Repeat("(1)", d) + " FROM dual",
            19 => "SELECT a" + Repeat(".b", d) + " FROM t",
            20 => "SELECT " + Repeat("(1, ", d) + "1" + Repeat(")", d) + " FROM dual",
            21 => "SELECT 1 FROM dual WHERE " + Repeat("(", d) + "1=1" + Repeat(")", d),
            22 => "SELECT " + Repeat("MULTISET(SELECT 1 FROM dual) ", 1) + Repeat("(SELECT 1 FROM dual UNION ", d) + "SELECT 2 FROM dual" + Repeat(")", d),
            23 => "SELECT x FROM t MODEL DIMENSION BY (a) MEASURES (" + Repeat("(", d) + "1" + Repeat(")", d) + ")",
            24 => "SELECT " + Repeat("ROW_NUMBER() OVER (ORDER BY ", d) + "1" + Repeat(")", d) + " FROM dual",
            _ => "SELECT " + Repeat("x[", d) + "1" + Repeat("]", d) + " FROM dual",
        };
    }

    /// <summary>A left-deep (or right-deep) chain of 1k to 50k terms.</summary>
    public string LongChain()
    {
        int[] sizes = [1000, 5000, 10000, 20000, 50000];
        var allowed = sizes.Where(t => t <= MaxChainTerms).ToArray();
        int n = allowed.Length == 0 ? MaxChainTerms : allowed[_rng.Next(allowed.Length)];
        string Many(Func<int, string> term, string sep) => string.Join(sep, Enumerable.Range(0, n).Select(term));
        return _rng.Next(20) switch
        {
            0 => "SELECT 1 FROM t WHERE " + Many(i => "a" + i + " = 1", " AND "),
            1 => "SELECT 1 FROM t WHERE " + Many(i => "a" + i + " = 1", " OR "),
            2 => "SELECT " + Many(i => "'x'", " || ") + " FROM dual",
            3 => "SELECT " + Many(i => "1", " + ") + " FROM dual",
            4 => "SELECT " + Many(i => "1", " - ") + " FROM dual",
            5 => "SELECT " + Many(i => "2", " * ") + " FROM dual",
            6 => "SELECT " + Many(i => "1", " / ") + " FROM dual",
            7 => string.Join(" UNION ALL ", Enumerable.Range(0, n / 5).Select(i => "SELECT " + i + " FROM dual")),
            8 => string.Join(" INTERSECT ", Enumerable.Range(0, n / 5).Select(i => "SELECT " + i + " FROM dual")),
            9 => string.Join(" MINUS ", Enumerable.Range(0, n / 5).Select(i => "SELECT " + i + " FROM dual")),
            10 => "SELECT CASE " + Many(i => "WHEN a = " + i + " THEN " + i, " ") + " END FROM dual",
            11 => "SELECT 1 FROM dual WHERE x IN (" + Many(i => i.ToString(), ",") + ")",
            12 => "SELECT " + Many(i => "c" + i, ", ") + " FROM dual",
            13 => "SELECT 1 FROM t0 " + string.Concat(Enumerable.Range(1, n / 5).Select(i => $"JOIN t{i} ON t{i}.id = t0.id ")),
            14 => "SELECT 1 FROM " + Many(i => "t" + i, ", "),
            15 => "BEGIN " + string.Concat(Enumerable.Range(0, n / 2).Select(i => "x := " + i + "; ")) + "END;",
            16 => "BEGIN IF a = 0 THEN NULL; " + string.Concat(Enumerable.Range(1, n / 5).Select(i => "ELSIF a = " + i + " THEN NULL; ")) + "END IF; END;",
            17 => "SELECT 1 FROM dual WHERE " + string.Concat(Enumerable.Range(0, n / 5).Select(i => "a = " + i + " AND (")) + "1=1" + new string(')', n / 5),
            18 => "SELECT 1 FROM dual WHERE " + Many(i => "NOT", " ") + " 1=1",
            _ => string.Join(";\n", Enumerable.Range(0, n / 5).Select(i => "SELECT " + i + " FROM dual")),
        };
    }

    // ---------- long identifiers, quoting, unicode ----------

    /// <summary>Very long identifiers, bind names, quoted identifiers and literals, and long comments.</summary>
    public string LongNames()
    {
        int n = _rng.Choose(100, 128, 300, 1000, 4000, 30000, 100000);
        string id = new string(_rng.Next(2) == 0 ? 'A' : 'z', n);
        return _rng.Next(7) switch
        {
            0 => $"SELECT {id} FROM {id}",
            1 => $"SELECT 1 AS {id} FROM dual",
            2 => $"SELECT \"{id}\" FROM dual",
            3 => $"SELECT :{id} FROM dual",
            4 => $"SELECT 'q' || '{new string('x', n)}' FROM dual",
            5 => $"SELECT q'[{new string('y', n)}]' FROM dual /* {new string('c', n)}",
            _ => $"SELECT {new string('9', n)}.5 FROM dual",
        };
    }

    /// <summary>Random concatenations of quoting fragments: unterminated, multi-line and q-quoted literals and comments.</summary>
    public string QuoteSoup()
    {
        var sb = new StringBuilder(_rng.Next(2) == 0 ? "SELECT " : "BEGIN ");
        int n = 1 + _rng.Next(30);
        for (int i = 0; i < n; i++)
        {
            sb.Append(_rng.Next(3) == 0 ? QuoteFragments[_rng.Next(QuoteFragments.Length)] : Atom());
            sb.Append(_rng.Next(2) == 0 ? " " : "");
        }
        return sb.ToString();
    }

    /// <summary>Random unicode (including lone surrogates and noncharacters) inserted into a statement or into keyword soup.</summary>
    public string UnicodeSoup()
    {
        string sql = _rng.Next(2) == 0 ? Script() : KeywordSoup();
        int n = 1 + _rng.Next(4);
        for (int i = 0; i < n; i++)
        {
            int at = _rng.Next(sql.Length + 1);
            string frag = _rng.Next(2) == 0 ? UnicodeFragments[_rng.Next(UnicodeFragments.Length)] : ((char)_rng.Next(0x20, 0x3000)).ToString();
            sql = sql[..at] + frag + sql[at..];
        }
        return sql;
    }

    internal static string Repeat(string s, int n)
    {
        var sb = new StringBuilder(s.Length * Math.Max(n, 0));
        for (int i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }
}

internal static class RandomExtensions
{
    /// <summary>A uniformly chosen element of the given values.</summary>
    public static T Choose<T>(this Random rng, params T[] values) => values[rng.Next(values.Length)];
}
