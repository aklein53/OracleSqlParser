using System.Diagnostics;
using System.Reflection;
using OracleSqlParser;
using OracleSqlParser.Fuzzing;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Fast, deterministic slice of tools/Fuzz: a fixed seed, a few thousand generated inputs (mutations of the smoke corpus,
/// structured SQL, keyword soup, deep nesting, long chains, quoting and unicode), each checked against the full contract
/// on a 1 MB-stack thread with a 2 s budget. The long campaign runs in tools/Fuzz; this keeps the contract in CI.
/// </summary>
public class FuzzSmokeTests
{
    private const int Inputs = 2000;

    private static List<string> SmokeSeeds() => typeof(SmokeCorpusTests).GetMethods()
        .SelectMany(m => m.CustomAttributes)
        .Where(a => a.AttributeType.Name == "InlineDataAttribute")
        .Select(a => ((IEnumerable<CustomAttributeTypedArgument>)a.ConstructorArguments[0].Value!).FirstOrDefault().Value as string)
        .Where(s => s != null)
        .Select(s => s!)
        .ToList();

    [Fact]
    [Trait("Category", "Fuzz")]
    public void Deterministic_Fuzz_HoldsTheContract()
    {
        var seeds = SmokeSeeds();
        Assert.True(seeds.Count > 100, "the smoke corpus should provide seeds");
        // Chains up to 300 terms keep the run short; the fuzzer tool covers 50,000.
        var gen = new FuzzInputs(seeds, new Random(20260101)) { MaxChainTerms = 300 };
        var clock = Stopwatch.StartNew();
        var problems = new List<string>();
        for (int i = 0; i < Inputs && problems.Count < 5; i++)
        {
            var c = gen.Next();
            FuzzFailure? failure = null;
            var worker = new Thread(() => failure = ContractChecker.Check(c), 1024 * 1024) { IsBackground = true };
            worker.Start();
            if (!worker.Join(TimeSpan.FromSeconds(2)))
                problems.Add("hang (no answer in 2 s): " + Show(c));
            else if (failure is not null)
                problems.Add($"{failure.Category} [{failure.Signature}]: {Show(c)}\n{failure.Detail}");
        }
        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"smoke fuzz took {clock.Elapsed}");
    }

    [Fact]
    [Trait("Category", "Fuzz")]
    public void Deterministic_Fuzz_IsReproducible()
    {
        var seeds = SmokeSeeds();
        var a = new FuzzInputs(seeds, new Random(7)) { MaxChainTerms = 1000 };
        var b = new FuzzInputs(seeds, new Random(7)) { MaxChainTerms = 1000 };
        for (int i = 0; i < 200; i++) Assert.Equal(a.Next(), b.Next());
    }

    private static string Show(FuzzCase c) => $"[variant {c.Variant}] " + (c.Sql.Length > 300 ? c.Sql[..300] + "..." : c.Sql).Replace("\n", "\\n");
}
