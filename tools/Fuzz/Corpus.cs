using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OracleSqlParser.Fuzzing;

/// <summary>
/// Extracts the SQL statements from the test sources (SmokeCorpusTests InlineData and the ExpertStressTests statements) with
/// Roslyn, so string escapes and raw literals are decoded exactly as the compiler decodes them.
/// </summary>
public static class Corpus
{
    private static readonly Regex SqlKeyword = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|BEGIN|DECLARE|GRANT|REVOKE|WITH|CALL|ANALYZE|AUDIT|NOAUDIT|TRUNCATE|EXPLAIN|LOCK|COMMIT|ROLLBACK|SAVEPOINT|EXEC|EXECUTE|PURGE|FLASHBACK|RENAME|COMMENT|ADMINISTER|SET|VALUES)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>All SQL-looking string literals from the given C# source files.</summary>
    public static List<string> Load(IEnumerable<string> files)
    {
        var result = new List<string>();
        foreach (var file in files)
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
            {
                if (lit.Kind() != SyntaxKind.StringLiteralExpression) continue;
                string text = lit.Token.ValueText;
                if (text.Length >= 6 && SqlKeyword.IsMatch(text)) result.Add(text);
            }
        }
        return result.Distinct().ToList();
    }

    /// <summary>Finds the repository root (the folder holding OracleSqlParser.slnx) by walking up from <paramref name="start"/>.</summary>
    public static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OracleSqlParser.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("OracleSqlParser.slnx not found above " + start);
    }
}
