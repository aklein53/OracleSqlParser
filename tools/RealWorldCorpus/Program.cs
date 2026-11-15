// Parses every Oracle source file under the given directories and reports how many parse cleanly and how many
// survive a generate-then-reparse round trip. Reads third-party files as data only; nothing from them is executed.
//
// Usage: RealWorldCorpus --out <report-dir> [--exclude <file>] <dir>...
//   --exclude  lines of "<display-path-prefix><TAB><reason>" (blank lines and '#' comments are ignored). A display path
//              is relative to the parent of the directory argument, so "Logger/examples" covers that subtree.
//   --out      receives summary.txt, errors.tsv (every diagnostic) and groups.tsv (every cause group).
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using OracleSqlParser;

const string Usage = "usage: RealWorldCorpus --out <report-dir> [--exclude <file>] <dir>...";
string? outDir = null, excludeFile = null;
var roots = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
        case "--exclude" when i + 1 < args.Length: excludeFile = args[++i]; break;
        case "-h" or "--help": Console.WriteLine(Usage); return 0;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal)) { Console.Error.WriteLine(Usage); return 2; }
            roots.Add(args[i]);
            break;
    }
}
if (outDir == null || roots.Count == 0) { Console.Error.WriteLine(Usage); return 2; }

var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{ ".sql", ".pks", ".pkb", ".pls", ".plb", ".trg", ".fnc", ".prc", ".tps", ".tpb", ".vw" };

var excludes = new List<(string Prefix, string Reason)>();
if (excludeFile != null)
{
    foreach (var raw in File.ReadAllLines(excludeFile))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var tab = line.IndexOf('\t');
        excludes.Add(tab < 0 ? (line, "") : (line[..tab].Trim(), line[(tab + 1)..].Trim()));
    }
}

// Each root contributes its files with display paths relative to its parent directory.
var files = new List<(string Path, string Display)>();
foreach (var root in roots)
{
    var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var baseDir = Path.GetDirectoryName(full) ?? full;
    foreach (var f in EnumerateFiles(full))
        files.Add((f, Path.GetRelativePath(baseDir, f).Replace('\\', '/')));
}
files.Sort((a, b) => string.CompareOrdinal(a.Display, b.Display));

// The parser is recursive; a deep input must not overflow the default 1 MB stack and kill the whole run.
var holder = new Report[1];
var worker = new Thread(() => holder[0] = Run(files, excludes, extensions), 512 * 1024 * 1024);
worker.Start();
worker.Join();
var report = holder[0];

Directory.CreateDirectory(outDir);
string summary = report.Summary();
File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary);
File.WriteAllText(Path.Combine(outDir, "errors.tsv"), report.ErrorsTsv());
File.WriteAllText(Path.Combine(outDir, "groups.tsv"), report.GroupsTsv());
Console.Write(summary);
return 0;

static IEnumerable<string> EnumerateFiles(string dir)
{
    var pending = new Stack<string>();
    pending.Push(dir);
    while (pending.Count > 0)
    {
        var d = pending.Pop();
        foreach (var sub in Directory.EnumerateDirectories(d).OrderBy(s => s, StringComparer.Ordinal))
        {
            if (Path.GetFileName(sub) == ".git") continue; // version-control metadata, not source
            var info = new DirectoryInfo(sub);
            if (info.LinkTarget != null) continue; // symlinked directories could loop or escape the root
            pending.Push(sub);
        }
        foreach (var f in Directory.EnumerateFiles(d).OrderBy(s => s, StringComparer.Ordinal))
        {
            var info = new FileInfo(f);
            if (info.LinkTarget != null) continue;
            yield return f;
        }
    }
}

static Report Run(List<(string Path, string Display)> files, List<(string Prefix, string Reason)> excludes,
    HashSet<string> extensions)
{
    var report = new Report();
    foreach (var (path, display) in files)
    {
        var ext = Path.GetExtension(path);
        if (!extensions.Contains(ext)) continue;
        var excluded = excludes.FirstOrDefault(e => display.StartsWith(e.Prefix, StringComparison.Ordinal));
        if (excluded.Prefix != null)
        {
            report.Excluded.Add((display, excluded.Reason));
            continue;
        }
        Console.Error.WriteLine($"[file] {display}");
        var sw = Stopwatch.StartNew();
        try
        {
            report.Add(path, display);
        }
        catch (Exception ex)
        {
            // Parse and Generate must never throw; anything reaching here is a tool or library defect.
            report.ToolFailure(display, ex);
        }
        sw.Stop();
        report.Timing(display, sw.Elapsed);
    }
    return report;
}

sealed class Report
{
    // The inputs are SQL*Plus scripts, so the parser reads them with the client's line rules (see ParseOptions.SqlPlusScript).
    private static readonly ParseOptions ScriptOptions = new() { SqlPlusScript = true };

    public readonly List<(string File, string Reason)> Excluded = [];
    private readonly List<string> _readFailures = [];
    private int _files, _cleanFiles, _fallbackDecodes, _toolFailures;
    private int _statements, _attemptedStatements, _statementsRoundTripped, _statementsInCleanFiles;
    private int _roundTripFiles, _roundTripPresent;
    private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);
    private readonly List<(string File, TimeSpan Time)> _slowest = [];
    private readonly List<(string File, int Line, string Code, string Message, string Source)> _errors = [];

    private sealed class Group
    {
        public int Count;
        public readonly HashSet<string> Files = new(StringComparer.Ordinal);
        public string Raw = "";
        public readonly List<string> Examples = [];
    }

    private static readonly Regex QuotedSingle = new("'(?:[^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex QuotedDouble = new("\"[^\"]*\"", RegexOptions.Compiled);
    private static readonly Regex Digits = new(@"\b\d+\b", RegexOptions.Compiled);

    public void Timing(string display, TimeSpan time)
    {
        _slowest.Add((display, time));
        _slowest.Sort((a, b) => b.Time.CompareTo(a.Time));
        if (_slowest.Count > 5) _slowest.RemoveAt(_slowest.Count - 1);
    }

    public void ToolFailure(string display, Exception ex)
    {
        _toolFailures++;
        AddToGroup("TOOL: " + ex.GetType().Name, display, 0, ex.Message, "");
    }

    public void Add(string path, string display)
    {
        _files++;
        string text;
        try
        {
            text = ReadText(path, out bool fallback);
            if (fallback) _fallbackDecodes++;
        }
        catch (IOException ex)
        {
            _readFailures.Add($"{display}: {ex.Message}");
            return;
        }

        var lines = text.Split('\n');
        var result = SqlParser.Parse(text, ScriptOptions);
        int statements = result.Script.Statements.Count;
        _statements += statements;
        _attemptedStatements += statements + result.Errors.Count;

        foreach (var e in result.Errors)
        {
            var source = e.Line >= 1 && e.Line <= lines.Length ? lines[e.Line - 1].TrimEnd('\r') : "<end of input>";
            if (source.Length > 200) source = source[..200] + "...";
            _errors.Add((display, e.Line, e.Code, e.Message, source));
            AddToGroup($"PARSE {e.Code}", display, e.Line, e.Message, source);
        }
        if (result.Errors.Count > 0) return;

        _cleanFiles++;
        _statementsInCleanFiles += statements;
        RoundTrip(result.Script, display);
    }

    private void RoundTrip(SqlScript script, string display)
    {
        _roundTripPresent++;
        string generated;
        try
        {
            generated = new SqlScriptGenerator(new SqlScriptGeneratorOptions { SqlPlusSlashAfterPlsql = true }).Generate(script);
        }
        catch (Exception ex)
        {
            AddToGroup($"RT generator {ex.GetType().Name}", display, 0, ex.Message, "");
            return;
        }

        var reparsed = SqlParser.Parse(generated, ScriptOptions);
        if (reparsed.Errors.Count > 0)
        {
            var first = reparsed.Errors[0];
            var genLines = generated.Split('\n');
            var src = first.Line >= 1 && first.Line <= genLines.Length ? genLines[first.Line - 1].TrimEnd('\r') : "<end of input>";
            AddToGroup($"RT reparse {first.Code}", display, first.Line, first.Message, src);
            return;
        }

        var before = script.Statements;
        var after = reparsed.Script.Statements;
        if (before.Count != after.Count)
        {
            AddToGroup("RT statement count", display, 0, $"{before.Count} statements became {after.Count}", "");
            // Still count the statements that do match by position so the statement-level figure stays honest.
        }
        bool fileOk = before.Count == after.Count;
        int common = Math.Min(before.Count, after.Count);
        for (int i = 0; i < common; i++)
        {
            if (SqlNodeComparer.StructurallyEqual(before[i], after[i])) { _statementsRoundTripped++; continue; }
            fileOk = false;
            AddToGroup($"RT structure {before[i].GetType().Name}", display, 0,
                $"statement {i + 1} ({before[i].GetType().Name}) differs after generate+reparse", "");
        }
        if (fileOk) _roundTripFiles++;
    }

    // Cause groups key on the code and a normalized message: quoted text and numbers become placeholders.
    private void AddToGroup(string key, string display, int line, string message, string source)
    {
        var norm = QuotedSingle.Replace(message, "'?'");
        norm = QuotedDouble.Replace(norm, "\"?\"");
        norm = Digits.Replace(norm, "N");
        var groupKey = key + " | " + norm;
        if (!_groups.TryGetValue(groupKey, out var g)) _groups[groupKey] = g = new Group { Raw = message };
        g.Count++;
        g.Files.Add(display);
        if (g.Examples.Count < 3)
            g.Examples.Add((line > 0 ? $"{display}:{line}: " : $"{display}: ") + source.Trim());
    }

    private static string ReadText(string path, out bool fallback)
    {
        var bytes = File.ReadAllBytes(path);
        fallback = false;
        int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: Windows-1252 and Latin-1 agree on every byte that matters for the lexer, so read as Latin-1.
            fallback = true;
            return Encoding.Latin1.GetString(bytes, offset, bytes.Length - offset);
        }
    }

    private double Pct(int part, int whole) => whole == 0 ? 0 : 100.0 * part / whole;

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Real-world corpus summary ===");
        sb.AppendLine($"files considered:           {_files}");
        sb.AppendLine($"files parsed cleanly:       {_cleanFiles} ({Pct(_cleanFiles, _files):F1}%)");
        sb.AppendLine($"statements parsed:          {_statements}");
        sb.AppendLine($"statements attempted:       {_attemptedStatements} (parsed + one per diagnostic; approximate denominator)");
        sb.AppendLine($"statements parsing:         {Pct(_statements, _attemptedStatements):F1}%");
        sb.AppendLine($"clean files round-tripped:  {_roundTripFiles} of {_roundTripPresent} ({Pct(_roundTripFiles, _roundTripPresent):F1}%)");
        sb.AppendLine($"statements round-tripped:   {_statementsRoundTripped} of {_statementsInCleanFiles} ({Pct(_statementsRoundTripped, _statementsInCleanFiles):F1}%)");
        sb.AppendLine($"files excluded (category c): {Excluded.Count}");
        sb.AppendLine($"read failures / tool failures: {_readFailures.Count} / {_toolFailures}");
        sb.AppendLine($"files decoded as Latin-1 (not UTF-8): {_fallbackDecodes}");
        sb.AppendLine();
        sb.AppendLine("Slowest files:");
        foreach (var s in _slowest) sb.AppendLine($"  {s.Time.TotalMilliseconds,8:F0} ms  {s.File}");
        sb.AppendLine();
        sb.AppendLine("Top causes (count | files | example):");
        foreach (var (key, g) in _groups.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(60))
        {
            sb.AppendLine($"{g.Count,6} | {g.Files.Count,4} | {key}");
            foreach (var ex in g.Examples) sb.AppendLine($"           e.g. {ex}");
        }
        if (_readFailures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Read failures:");
            foreach (var r in _readFailures.Take(50)) sb.AppendLine("  " + r);
        }
        if (Excluded.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Excluded:");
            foreach (var (file, reason) in Excluded.Take(200)) sb.AppendLine($"  {file}  -- {reason}");
        }
        return sb.ToString();
    }

    public string ErrorsTsv()
    {
        var sb = new StringBuilder("file\tline\tcode\tmessage\tsource\n");
        foreach (var e in _errors) sb.Append($"{e.File}\t{e.Line}\t{e.Code}\t{e.Message}\t{e.Source.Replace('\t', ' ')}\n");
        return sb.ToString();
    }

    public string GroupsTsv()
    {
        var sb = new StringBuilder("count\tfiles\tcause\n");
        foreach (var (key, g) in _groups.OrderByDescending(kv => kv.Value.Count))
            sb.Append($"{g.Count}\t{g.Files.Count}\t{key.Replace('\t', ' ')}\n");
        return sb.ToString();
    }
}
