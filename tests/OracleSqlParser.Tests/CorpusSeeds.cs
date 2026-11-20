using System.Reflection;

namespace OracleSqlParser.Tests;

/// <summary>
/// The SQL texts of the smoke and real-world gap corpora, read from their InlineData attributes (the same way
/// FuzzSmokeTests reads its seeds).
/// </summary>
internal static class CorpusSeeds
{
    public static IEnumerable<string> All() =>
        new[] { typeof(SmokeCorpusTests), typeof(RealWorldGapCorpusTests) }
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.CustomAttributes)
            .Where(a => a.AttributeType.Name == "InlineDataAttribute")
            .Select(a => ((IEnumerable<CustomAttributeTypedArgument>)a.ConstructorArguments[0].Value!).FirstOrDefault().Value as string)
            .Where(s => s is not null)
            .Select(s => s!)
            .Distinct();
}
