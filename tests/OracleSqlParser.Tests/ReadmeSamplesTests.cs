using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OracleSqlParser.Tests;

/// <summary>
/// Compiles every <c>```csharp</c> block in README.md as its own program, so the samples cannot drift from the API.
/// A block may be preceded by <c>&lt;!-- sample-prelude: code --&gt;</c> for setup the README leaves implicit.
/// </summary>
public partial class ReadmeSamplesTests
{
    [GeneratedRegex(@"(?:<!-- sample-prelude: (?<prelude>.*?) -->\r?\n)?```csharp\r?\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    public static TheoryData<int, int> Blocks()
    {
        var data = new TheoryData<int, int>();
        var readme = ReadReadme();
        foreach (Match m in CodeBlock().Matches(readme))
            data.Add(data.Count + 1, readme[..m.Index].Count(c => c == '\n') + 1);
        return data;
    }

    [Fact]
    public void Readme_HasSamples() => Assert.True(Blocks().Count >= 5);

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Sample_Compiles(int block, int line)
    {
        var m = CodeBlock().Matches(ReadReadme())[block - 1];
        var code = m.Groups["prelude"].Value + "\n" + m.Groups["code"].Value;

        // Samples show a class next to the code that uses it; C# wants top-level statements before type declarations.
        var members = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot().Members;
        var reordered = members.OfType<GlobalStatementSyntax>().Cast<MemberDeclarationSyntax>()
            .Concat(members.Where(x => x is not GlobalStatementSyntax));
        var tree = CSharpSyntaxTree.ParseText(
            "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing OracleSqlParser;\n" +
            string.Concat(reordered.Select(x => x.ToFullString())));

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(SqlParser).Assembly.Location)
            .Distinct()
            .Select(p => MetadataReference.CreateFromFile(p));
        var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable);
        var compilation = CSharpCompilation.Create("ReadmeSample", [tree], references, options);

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"README.md block {block} (line {line}) does not compile:\n" + string.Join("\n", errors));
    }

    private static string ReadReadme() => File.ReadAllText(Path.Combine(GeneratedCodeTests.RepoRoot(), "README.md"));
}
