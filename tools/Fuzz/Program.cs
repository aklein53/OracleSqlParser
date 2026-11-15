using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OracleSqlParser;

namespace OracleSqlParser.Fuzzing;

/// <summary>
/// Contract fuzzer for SqlParser (contract: Contract.cs). Usage:
///   dotnet run --project tools/Fuzz -c Release -- --minutes 30 --out &lt;dir&gt;     fuzz run (default)
///   dotnet run --project tools/Fuzz -c Release -- --check &lt;repro.json&gt;        re-check one saved input in a worker
///   dotnet run --project tools/Fuzz -c Release -- --scaling                   super-linear time probe
///   dotnet run --project tools/Fuzz -c Release -- --probe-stack               depth reachable on a pool thread
/// Every input runs in a persistent worker process (1 MB stack, 2 s budget); crashes and hangs are recorded as findings.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--worker") return Worker.Run();
        if (args.Length > 0 && args[0] == "--probe-stack") return ProbeStack();
        var o = Options.Parse(args);
        if (o.Stages) return Stages(o.Check);
        if (o.Check is not null) return CheckFile(o.Check);
        if (o.Slowest) return Slowest(o);
        if (o.Scaling) return Scaling(fullContract: o.FullContract);
        return FuzzRun(o);
    }

    // ---------- options ----------

    private sealed class Options
    {
        public double Minutes = 30;
        public long MaxInputs = long.MaxValue;
        public int Seed = 1;
        public int Jobs = Math.Max(2, Environment.ProcessorCount - 2);
        public string? Out;
        public string? Repo;
        public string? Check;
        public bool Scaling;
        public bool FullContract;
        public bool Stages;
        public bool Slowest;
        public int MaxChainTerms = 50_000;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value for " + args[i]);
                switch (args[i])
                {
                    case "--minutes": o.Minutes = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--max-inputs": o.MaxInputs = long.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--seed": o.Seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--jobs": o.Jobs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--out": o.Out = Next(); break;
                    case "--repo": o.Repo = Next(); break;
                    case "--check": o.Check = Next(); break;
                    case "--scaling": o.Scaling = true; break;
                    case "--full-contract": o.FullContract = true; break;
                    case "--stages": o.Stages = true; break;
                    case "--slowest": o.Slowest = true; break;
                    case "--max-chain": o.MaxChainTerms = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException("unknown option " + args[i]);
                }
            }
            return o;
        }
    }

    // ---------- fuzz run ----------

    private sealed class Finding
    {
        public required string Signature;
        public required string Category;
        public long Count;
        public required string File;
        public FuzzCase Repro;
    }

    private static int FuzzRun(Options o)
    {
        string repo = o.Repo ?? Corpus.FindRepoRoot(AppContext.BaseDirectory);
        string outDir = o.Out ?? Path.Combine(Path.GetTempPath(), "oracle-fuzz-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(Path.Combine(outDir, "findings"));
        var seeds = Corpus.Load(
        [
            Path.Combine(repo, "tests", "OracleSqlParser.Tests", "SmokeCorpusTests.cs"),
            Path.Combine(repo, "tests", "OracleSqlParser.IntegrationTests", "ExpertStressTests.cs"),
        ]);
        Console.WriteLine($"seeds: {seeds.Count} statements from SmokeCorpusTests.cs and ExpertStressTests.cs; jobs={o.Jobs} seed={o.Seed} minutes={o.Minutes}");

        var findings = new Dictionary<string, Finding>();
        var findingLock = new object();
        long inputs = 0, failures = 0;
        long micros = 0;
        double slowestMs = 0;
        string slowest = "";
        var stopwatch = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow.AddMinutes(o.Minutes);
        var categories = new ConcurrentDictionary<string, long>();

        void Job(int j)
        {
            var gen = new FuzzInputs(seeds, new Random(unchecked(o.Seed * 1_000_003 + j * 7919))) { MaxChainTerms = o.MaxChainTerms };
            using var client = new Worker.Client();
            while (DateTime.UtcNow < deadline && Interlocked.Read(ref inputs) < o.MaxInputs)
            {
                var c = gen.Next();
                var res = client.Run(c);
                Interlocked.Increment(ref inputs);
                Interlocked.Add(ref micros, (long)(res.Milliseconds * 1000));
                if (res.Milliseconds > slowestMs)
                {
                    lock (findingLock)
                        if (res.Milliseconds > slowestMs) { slowestMs = res.Milliseconds; slowest = c.Sql.Length > 300 ? c.Sql[..300] : c.Sql; }
                }
                if (res.Failure is null) continue;

                Interlocked.Increment(ref failures);
                categories.AddOrUpdate(res.Failure.Category, 1, (_, n) => n + 1);
                Finding f;
                lock (findingLock)
                {
                    if (findings.TryGetValue(res.Failure.Signature, out var known)) { known.Count++; continue; }
                    // Reserve the signature first, so no other job minimises the same bug; the repro is filled in below.
                    f = new Finding
                    {
                        Signature = res.Failure.Signature,
                        Category = res.Failure.Category,
                        Count = 1,
                        File = Path.Combine(outDir, "findings", $"{findings.Count + 1:D3}-{Safe(res.Failure.Category)}.json"),
                        Repro = c,
                    };
                    findings[res.Failure.Signature] = f;
                }
                // Minimise outside the lock, so the other jobs keep running. Minimised to a repro with the same signature.
                var min = Minimise(c, res.Failure.Signature, client);
                lock (findingLock)
                {
                    f.Repro = min;
                    SaveRepro(f, res.Failure);
                    Console.WriteLine($"NEW {f.Category}: {res.Failure.Signature}  (minimised {c.Sql.Length} -> {min.Sql.Length} chars) -> {f.File}");
                }
            }
        }

        var threads = Enumerable.Range(0, o.Jobs).Select(j => new Thread(() => Job(j)) { IsBackground = true }).ToList();
        foreach (var t in threads) t.Start();
        var progress = new Thread(() =>
        {
            while (threads.Any(t => t.IsAlive))
            {
                Thread.Sleep(TimeSpan.FromSeconds(30));
                long n = Interlocked.Read(ref inputs);
                Console.WriteLine($"[{stopwatch.Elapsed:hh\\:mm\\:ss}] inputs={n:N0} rate={n / Math.Max(1, stopwatch.Elapsed.TotalSeconds):N0}/s failures={Interlocked.Read(ref failures):N0} distinct={findings.Count}");
            }
        }) { IsBackground = true };
        progress.Start();
        foreach (var t in threads) t.Join();

        long totalInputs = Interlocked.Read(ref inputs);
        var summary = new StringBuilder();
        summary.AppendLine($"inputs run: {totalInputs:N0}");
        summary.AppendLine($"wall time: {stopwatch.Elapsed}");
        summary.AppendLine($"parse time (sum over workers): {micros / 1e6:F1} s");
        summary.AppendLine($"failing inputs: {Interlocked.Read(ref failures):N0}; distinct signatures: {findings.Count}");
        summary.AppendLine($"slowest input: {slowestMs:F1} ms  {slowest.Replace("\n", "\\n")}");
        foreach (var (cat, n) in categories.OrderBy(k => k.Key)) summary.AppendLine($"  category {cat}: {n:N0}");
        foreach (var f in findings.Values.OrderBy(f => f.File))
            summary.AppendLine($"  {f.Count,6}x {f.Signature}  -> {f.File}");
        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString());
        Console.Write(summary.ToString());
        Console.WriteLine("output: " + outDir);
        return findings.Count == 0 ? 0 : 1;
    }

    /// <summary>Delta-debugging on characters: removes chunks while the same failure signature still reproduces.</summary>
    private static FuzzCase Minimise(FuzzCase c, string signature, Worker.Client client)
    {
        string best = c.Sql;
        int runs = 0;
        var sw = Stopwatch.StartNew();
        bool Still(string s)
        {
            runs++;
            var r = client.Run(new FuzzCase(s, c.Variant));
            return r.Failure is { } f && f.Signature == signature;
        }
        for (int chunk = best.Length / 2; chunk >= 1; )
        {
            bool removed = false;
            for (int i = 0; i + chunk <= best.Length && runs < 1500 && sw.Elapsed < TimeSpan.FromSeconds(60);)
            {
                string cand = best[..i] + best[(i + chunk)..];
                if (Still(cand)) { best = cand; removed = true; }
                else i += chunk;
            }
            if (!removed) chunk /= 2;
            if (runs >= 1500 || sw.Elapsed >= TimeSpan.FromSeconds(60)) break;
        }
        return new FuzzCase(best, c.Variant);
    }

    private static void SaveRepro(Finding f, FuzzFailure failure)
    {
        var json = JsonSerializer.Serialize(new { variant = f.Repro.Variant, sql = f.Repro.Sql, signature = failure.Signature, category = failure.Category },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(f.File, json);
        File.WriteAllText(Path.ChangeExtension(f.File, ".detail.txt"), failure.Detail);
    }

    private static string Safe(string s) => Regex.Replace(s, "[^A-Za-z0-9_.-]+", "-");

    // ---------- re-check one saved repro ----------

    private static int CheckFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var c = new FuzzCase(root.GetProperty("sql").GetString()!, root.GetProperty("variant").GetInt32());
        using var client = new Worker.Client();
        var r = client.Run(c);
        if (r.Failure is null)
        {
            Console.WriteLine($"ok ({r.Milliseconds:F1} ms): the contract holds for this input");
            return 0;
        }
        Console.WriteLine($"{r.Failure.Category}: {r.Failure.Signature}\n{r.Failure.Detail}");
        return 1;
    }

    // ---------- stack probe (sanity check of the 1 MB pool-thread stack) ----------

    private static int ProbeStack()
    {
        static int Depth(int n) => RuntimeHelpers.TryEnsureSufficientExecutionStack() ? Depth(n + 1) + 1 : n;
        int main = Depth(0);
        int pool = Task.Run(() => Depth(0)).Result;
        int oneMb = 0;
        var t = new Thread(() => oneMb = Depth(0), 1024 * 1024);
        t.Start();
        t.Join();
        Console.WriteLine($"frames reachable: main thread {main}; pool thread {pool}; explicit 1 MB thread {oneMb}");
        return 0;
    }

    // ---------- which generated inputs are slow (in-process, same seeds as a run) ----------

    private static int Slowest(Options o)
    {
        string repo = o.Repo ?? Corpus.FindRepoRoot(AppContext.BaseDirectory);
        var seeds = Corpus.Load(
        [
            Path.Combine(repo, "tests", "OracleSqlParser.Tests", "SmokeCorpusTests.cs"),
            Path.Combine(repo, "tests", "OracleSqlParser.IntegrationTests", "ExpertStressTests.cs"),
        ]);
        var gen = new FuzzInputs(seeds, new Random(o.Seed)) { MaxChainTerms = o.MaxChainTerms };
        var timed = new List<(double Ms, FuzzCase Case)>();
        var total = Stopwatch.StartNew();
        for (long i = 0; i < o.MaxInputs; i++)
        {
            var c = gen.Next();
            var sw = Stopwatch.StartNew();
            ContractChecker.Check(c);
            timed.Add((sw.Elapsed.TotalMilliseconds, c));
            if (total.Elapsed > TimeSpan.FromMinutes(o.Minutes)) break;
        }
        Console.WriteLine($"{timed.Count} inputs in {total.Elapsed}; total check time {timed.Sum(t => t.Ms) / 1000:F1} s");
        foreach (var t in timed.OrderByDescending(t => t.Ms).Take(12))
            Console.WriteLine($"{t.Ms,9:F1} ms  len={t.Case.Sql.Length,-7} v={t.Case.Variant}  {(t.Case.Sql.Length > 110 ? t.Case.Sql[..110] : t.Case.Sql).Replace("\n", " ").Replace("\r", " ")}");
        return 0;
    }

    // ---------- per-stage timing of the contract path (for chains that scale worse than linearly) ----------

    private static int Stages(string? file = null)
    {
        if (file is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return StageTimes("file", doc.RootElement.GetProperty("sql").GetString()!);
        }
        var families = new (string Name, Func<int, string> Make)[]
        {
            ("AND chain", n => "SELECT 1 FROM t WHERE " + string.Join(" AND ", Enumerable.Range(0, n).Select(i => "a" + i + " = 1"))),
            ("|| chain", n => "SELECT " + string.Join(" || ", Enumerable.Range(0, n).Select(_ => "'x'")) + " FROM dual"),
            ("binds", n => "SELECT " + string.Join(" + ", Enumerable.Range(0, n).Select(i => ":b" + i)) + " FROM dual"),
            ("JOIN chain", n => "SELECT 1 FROM t0 " + string.Concat(Enumerable.Range(1, n).Select(i => $"JOIN t{i} ON t{i}.id = t0.id "))),
        };
        foreach (var (name, make) in families)
        {
            foreach (int n in new[] { 2000, 4000, 8000, 16000 })
            {
                StageTimes(name + " n=" + n, make(n));
            }
        }
        return 0;
    }

    private static int StageTimes(string label, string sql)
    {
        {
            {
                var r = SqlParser.Parse(sql);
                var script = r.Script;
                int n = sql.Length;
                var stages = new List<(string, double)>();
                void T(string label, Action a)
                {
                    var sw = Stopwatch.StartNew();
                    a();
                    stages.Add((label, sw.Elapsed.TotalMilliseconds));
                }
                string printed = "", gen = "";
                T("Parse", () => SqlParser.Parse(sql));
                T("Print", () => printed = AstPrinter.Print(script));
                if (n <= 2000) T("Json", () => AstJson.Serialize(script, includePositions: true));
                T("Analysis", () => { SqlAnalysis.GetTables(script); SqlAnalysis.GetColumns(script); SqlAnalysis.GetBinds(script); SqlAnalysis.GetFunctions(script); });
                SqlNode? clone = null;
                T("DeepClone", () => clone = script.DeepClone());
                T("StructEq", () => SqlNodeComparer.StructurallyEqual(script, clone!));
                T("Generate", () => gen = new SqlScriptGenerator().Generate(script));
                T("Reparse", () => SqlParser.Parse(gen));
                Console.WriteLine($"{label,-24} len={n,-7}" + string.Concat(stages.Select(x => $"  {x.Item1}={x.Item2:F1}")));
            }
        }
        return 0;
    }

    // ---------- super-linear time probe ----------

    private static int Scaling(bool fullContract)
    {
        var families = new (string Name, Func<int, string> Make)[]
        {
            ("AND chain", n => "SELECT 1 FROM t WHERE " + string.Join(" AND ", Enumerable.Range(0, n).Select(i => "a" + i + " = 1"))),
            ("|| chain", n => "SELECT " + string.Join(" || ", Enumerable.Range(0, n).Select(_ => "'x'")) + " FROM dual"),
            ("UNION ALL chain", n => string.Join(" UNION ALL ", Enumerable.Range(0, n).Select(i => "SELECT " + i + " FROM dual"))),
            ("statements", n => string.Concat(Enumerable.Repeat("SELECT 1 FROM dual;\n", n))),
            ("statements with syntax error", n => string.Concat(Enumerable.Repeat("SELECT FROM dual;\n", n))),
            ("statements with lexical error", n => string.Concat(Enumerable.Repeat("SELECT ` FROM dual;\n", n))),
            ("unterminated string per line", n => string.Concat(Enumerable.Repeat("SELECT 'abc\n", n))),
            ("unterminated q-quote per line", n => string.Concat(Enumerable.Repeat("SELECT q'[abc\n", n))),
            ("unterminated quoted identifier per line", n => string.Concat(Enumerable.Repeat("SELECT \"abc\n", n))),
            ("unterminated comment", n => "SELECT 1 FROM dual /*" + string.Concat(Enumerable.Repeat("x\n", n))),
            ("PL/SQL blocks", n => string.Concat(Enumerable.Repeat("BEGIN NULL; END;\n/\n", n))),
            ("CASE WHENs", n => "SELECT CASE " + string.Concat(Enumerable.Range(0, n).Select(i => $"WHEN a = {i} THEN {i} ")) + "END FROM dual"),
            ("IN list", n => "SELECT 1 FROM dual WHERE x IN (" + string.Join(",", Enumerable.Range(0, n)) + ")"),
            ("select list", n => "SELECT " + string.Join(", ", Enumerable.Range(0, n).Select(i => "c" + i)) + " FROM dual"),
            ("JOIN chain", n => "SELECT 1 FROM t0 " + string.Concat(Enumerable.Range(1, n).Select(i => $"JOIN t{i} ON t{i}.id = t0.id "))),
            ("hints", n => string.Concat(Enumerable.Repeat("SELECT /*+ INDEX(t i) */ 1 FROM dual;\n", n))),
            ("$IF blocks", n => string.Concat(Enumerable.Repeat("$IF TRUE $THEN\nSELECT 1 FROM dual;\n$END\n", n))),
            ("binds", n => "SELECT " + string.Join(" + ", Enumerable.Range(0, n).Select(i => ":b" + i)) + " FROM dual"),
            ("long identifier", n => "SELECT " + new string('a', n) + " FROM dual"),
            ("long string literal", n => "SELECT '" + new string('a', n) + "' FROM dual"),
            ("long multi-line literal", n => "SELECT '" + string.Concat(Enumerable.Repeat("line\n", n)) + "' FROM dual"),
        };
        int[] sizes = [500, 1000, 2000, 4000, 8000, 16000];
        int failures = 0;
        foreach (var (name, make) in families)
        {
            var times = new List<double>();
            foreach (int n in sizes)
            {
                string sql = make(n);
                Action run = fullContract ? () => ContractChecker.Check(sql, null) : () => Parse(sql);
                run(); // warm-up for this size
                var samples = new List<double>();
                for (int k = 0; k < 3; k++)
                {
                    var sw = Stopwatch.StartNew();
                    run();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                samples.Sort();
                times.Add(samples[1]);
            }
            var sb = new StringBuilder($"{name,-42}");
            for (int i = 0; i < sizes.Length; i++) sb.Append($" {times[i],9:F1}");
            var ratios = new List<string>();
            for (int i = 1; i < sizes.Length; i++)
            {
                double ratio = times[i] / Math.Max(times[i - 1], 0.01);
                ratios.Add(ratio.ToString("F2", CultureInfo.InvariantCulture));
            }
            // Doubling the input should at most double the time; flag ratios clearly above that at the largest sizes.
            bool flag = times[^1] > 50 && times[^1] / Math.Max(times[^2], 0.01) > 3.0;
            if (flag) failures++;
            Console.WriteLine(sb + "  ratios " + string.Join(" ", ratios) + (flag ? "   <-- super-linear?" : ""));

        }
        Console.WriteLine("sizes: " + string.Join(" ", sizes) + $" (ms, median of 3; {(fullContract ? "full contract check" : "Parse only")})");
        return failures == 0 ? 0 : 1;
    }

    private static void Parse(string sql) => SqlParser.Parse(sql);
}
