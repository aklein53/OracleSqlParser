using OracleSqlParser;

namespace OracleSqlParser.Fuzzing;

/// <summary>One fuzz input: the SQL text and the parse-option variant it is checked with.</summary>
public readonly record struct FuzzCase(string Sql, int Variant)
{
    /// <summary>The options for this variant; null means the defaults.</summary>
    public ParseOptions? Options => Variant switch
    {
        1 => new ParseOptions { SubstitutionEnabled = false },
        2 => new ParseOptions { OracleVersion = OracleVersion.Oracle19 },
        3 => new ParseOptions { MaxNestingDepth = 7 },
        4 => new ParseOptions { MaxNestingDepth = 40, SubstitutionEnabled = false },
        _ => null,
    };
}

/// <summary>
/// A contract violation. <see cref="Category"/> names the kind of defect; <see cref="Signature"/> identifies the
/// underlying bug (same signature = same bug; used for de-duplication and for minimisation).
/// </summary>
public sealed record FuzzFailure(string Category, string Signature, string Detail);

/// <summary>
/// The SqlParser contract, checked for one input. Shared by tools/Fuzz and the unit smoke test (linked, not referenced).
/// Contract: Parse never throws and never reports InternalError; TryParse/ParseSelect/ParseStatement/ParseExpression throw only
/// ParseException; error positions are sane; on success the printer, JSON, analysis and DeepClone work, and the generated
/// SQL re-parses without errors to a StructurallyEqual tree (and regenerates to the same text).
/// </summary>
public static class ContractChecker
{
    /// <summary>Inputs longer than this (in characters) skip the entry-point checks, which parse the input again each time.</summary>
    public const int EntryPointCheckLimit = 50_000;

    /// <summary>Returns null when the input satisfies the contract, otherwise the first violation found.</summary>
    public static FuzzFailure? Check(FuzzCase c) => Check(c.Sql, c.Options);

    /// <summary>Checks one input with explicit options.</summary>
    public static FuzzFailure? Check(string sql, ParseOptions? options)
    {
        try
        {
            return CheckCore(sql, options);
        }
        catch (Exception e)
        {
            // Only reached when the harness itself misbehaves (every API call below is wrapped separately).
            return new FuzzFailure("harness", "harness:" + e.GetType().Name, e.ToString());
        }
    }

    private static FuzzFailure? CheckCore(string sql, ParseOptions? options)
    {
        ParseResult r;
        try
        {
            r = SqlParser.Parse(sql, options);
        }
        catch (Exception e)
        {
            return Thrown("Parse", e);
        }

        foreach (var err in r.Errors)
        {
            if (err.Code == ParseErrorCodes.InternalError)
                return new FuzzFailure("internal", "internal:" + err.Message, err.ToString());
            if (err.Offset < 0 || err.Offset > sql.Length || err.Length < 0 || err.Line < 1 || err.Column < 1)
                return new FuzzFailure("position", "position", $"{err} offset={err.Offset} length={err.Length} input length={sql.Length}");
        }

        // Every entry point must throw only ParseException. Each one parses again, so the (cheap) checks are skipped for
        // very large inputs to keep the fuzzing rate up; the parse above has already covered those inputs.
        if (sql.Length <= EntryPointCheckLimit)
        {
            if (ExpectParseExceptionOnly("ParseStatement", () => SqlParser.ParseStatement(sql, options)) is { } f1) return f1;
            if (ExpectParseExceptionOnly("ParseExpression", () => SqlParser.ParseExpression(sql, options)) is { } f2) return f2;
            if (ExpectParseExceptionOnly("ParseSelect", () => SqlParser.ParseSelect(sql, options)) is { } f3) return f3;
            if (ExpectParseExceptionOnly("TryParse", () => SqlParser.TryParse(sql, out _, out _, options)) is { } f4) return f4;
            if (ExpectParseExceptionOnly("TryParseSelect", () => SqlParser.TryParseSelect(sql, out _, out _, options)) is { } f5) return f5;
        }

        if (!r.Success) return null;
        var script = r.Script;

        if (Attempt("Print/Json", () =>
            {
                AstPrinter.Print(script);
                AstJson.Serialize(script, includePositions: true);
                AstJson.ToJson(script);
            }) is { } fp) return fp;

        if (Attempt("SqlAnalysis", () =>
            {
                SqlAnalysis.GetTables(script);
                SqlAnalysis.GetColumns(script);
                SqlAnalysis.GetBinds(script);
                SqlAnalysis.GetFunctions(script);
            }) is { } fa) return fa;

        SqlNode? clone = null;
        if (Attempt("DeepClone", () => clone = script.DeepClone()) is { } fc) return fc;
        if (clone is null || !SqlNodeComparer.StructurallyEqual(script, clone))
            return new FuzzFailure("clone", "clone-diff", "DeepClone is not StructurallyEqual to the original");

        string? gen = null;
        if (Attempt("SqlScriptGenerator", () => gen = new SqlScriptGenerator().Generate(script)) is { } fg) return fg;
        if (gen is null) return new FuzzFailure("generate", "generate:null", "Generate returned null");

        ParseResult r2;
        try
        {
            r2 = SqlParser.Parse(gen, options);
        }
        catch (Exception e)
        {
            return Thrown("Parse(generated)", e);
        }
        if (!r2.Success)
        {
            var first = r2.Errors[0];
            return new FuzzFailure("reparse", "reparse:" + first.Code, $"generated SQL does not re-parse: {first}\n--- generated:\n{gen}");
        }
        if (!SqlNodeComparer.StructurallyEqual(script, r2.Script))
            return RoundTripFailure(script, r2.Script, gen);

        string? gen2 = null;
        if (Attempt("SqlScriptGenerator(2)", () => gen2 = new SqlScriptGenerator().Generate(r2.Script)) is { } fg2) return fg2;
        if (gen2 != gen)
            return new FuzzFailure("gen-unstable", "gen-unstable", $"regenerating the re-parsed tree gives different SQL\n--- first:\n{gen}\n--- second:\n{gen2}");
        return null;
    }

    /// <summary>Finds the first printed difference between two trees and names the node it is in.</summary>
    private static FuzzFailure RoundTripFailure(SqlScript a, SqlScript b, string gen)
    {
        string pa = AstPrinter.Print(a), pb = AstPrinter.Print(b);
        int i = 0;
        while (i < pa.Length && i < pb.Length && pa[i] == pb[i]) i++;
        string node = NodeNameAt(pa, i);
        string context = pa[Math.Max(0, i - 80)..Math.Min(pa.Length, i + 40)] + "\n  vs\n" + pb[Math.Max(0, i - 80)..Math.Min(pb.Length, i + 40)];
        return new FuzzFailure("roundtrip", "roundtrip:" + node, $"tree differs after regeneration at node {node}\n{context}\n--- generated:\n{gen}");
    }

    private static string NodeNameAt(string printed, int index)
    {
        if (printed.Length == 0) return "?";
        int p = Math.Min(index, printed.Length - 1);
        while (p > 0 && printed[p] != '(') p--;
        int e = p + 1;
        while (e < printed.Length && char.IsLetterOrDigit(printed[e])) e++;
        return e > p + 1 ? printed[(p + 1)..e] : "?";
    }

    private static FuzzFailure? ExpectParseExceptionOnly(string api, Action call)
    {
        try
        {
            call();
            return null;
        }
        catch (ParseException)
        {
            return null;
        }
        catch (Exception e)
        {
            return Thrown(api, e);
        }
    }

    private static FuzzFailure? Attempt(string api, Action call)
    {
        try
        {
            call();
            return null;
        }
        catch (Exception e)
        {
            return Thrown(api, e);
        }
    }

    private static FuzzFailure Thrown(string api, Exception e)
    {
        string top = TopMethod(e);
        return new FuzzFailure("throw", $"throw:{api}:{e.GetType().Name}:{top}", e.ToString());
    }

    /// <summary>The method name of the innermost frame of an exception's stack trace, or "?".</summary>
    internal static string TopMethod(Exception e)
    {
        var lines = (e.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return "?";
        string line = lines[0].Trim();
        if (line.StartsWith("at ", StringComparison.Ordinal)) line = line[3..];
        int paren = line.IndexOf('(');
        return paren > 0 ? line[..paren] : line;
    }
}
