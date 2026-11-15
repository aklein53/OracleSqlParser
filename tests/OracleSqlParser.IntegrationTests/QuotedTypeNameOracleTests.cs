using Oracle.ManagedDataAccess.Client;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// Forms that name a type with quotes (a type named "ab", "my type", "a.b", or a column type through %TYPE). Each statement
/// is run as typed and as the generator prints it; both must run on Oracle without an error and leave no compile errors.
/// The generated text of a quoted name must keep its quotes, or Oracle reads it as another type (AB) or as a syntax error.
/// </summary>
[Collection(OracleCollection.Name)]
public class QuotedTypeNameOracleTests(OracleFixture db)
{
    private const string Pw = "scratch_pw_1";

    // Setup runs as typed in the scratch user. OWNER stands for that user, so the qualified forms name a real schema.
    private static readonly string[] Setup =
    [
        "CREATE TYPE \"ab\" AS OBJECT (n NUMBER)",
        "CREATE TYPE \"my type\" AS OBJECT (n NUMBER)",
        "CREATE TYPE \"a.b\" AS OBJECT (n NUMBER)",
        "CREATE TYPE \"tab t\" AS TABLE OF \"ab\"",
        "CREATE TABLE tq (\"c\" NUMBER)",
    ];

    // Each statement names a quoted type in one position of the grammar.
    private static readonly string[] Statements =
    [
        "DECLARE x \"ab\" := \"ab\"(1); BEGIN NULL; END;",
        "DECLARE x \"my type\" := \"my type\"(2); BEGIN NULL; END;",
        "DECLARE x \"a.b\" := \"a.b\"(3); BEGIN NULL; END;",
        "DECLARE x OWNER.\"my type\" := OWNER.\"my type\"(4); BEGIN NULL; END;",
        "DECLARE x tq.\"c\"%TYPE := 5; BEGIN NULL; END;",
        "DECLARE y \"ab\" := CAST(NULL AS \"ab\"); BEGIN NULL; END;",
        "DECLARE z \"tab t\" := \"tab t\"(\"ab\"(6)); BEGIN NULL; END;",
        "CREATE PROCEDURE pq (a \"ab\", b IN \"my type\") AS BEGIN NULL; END;",
        "CREATE TABLE tc (c \"ab\", d \"my type\")",
    ];

    public static IEnumerable<object[]> Cases() => Statements.Select(s => new object[] { s });

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public async Task QuotedTypeNameRunsOnOracleAsWrittenAndGenerated(string sql)
    {
        db.SkipIfUnavailable();
        await RunInScratchUser(sql, generated: false);
        await RunInScratchUser(sql, generated: true);
    }

    private async Task RunInScratchUser(string sql, bool generated)
    {
        var user = "QTN" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using (var sys = await db.OpenSystemAsync())
        {
            await OracleFixture.ExecAsync(sys, $"CREATE USER {user} IDENTIFIED BY {Pw} QUOTA UNLIMITED ON USERS");
            await OracleFixture.ExecAsync(sys, $"GRANT CREATE SESSION, CREATE TABLE, CREATE PROCEDURE, CREATE TYPE TO {user}");
        }
        try
        {
            await using var c = await db.OpenAsync(user, Pw);
            foreach (var s in Setup) await OracleFixture.ExecAsync(c, s);
            var text = generated ? new SqlScriptGenerator().Generate(SqlParser.Parse(sql).Script) : sql;
            text = text.Replace("OWNER", user);
            try
            {
                await OracleFixture.ExecAsync(c, OracleHarness.Prepare(text));
            }
            catch (OracleException e)
            {
                throw new Xunit.Sdk.XunitException($"{(generated ? "generated" : "as written")} failed: {e.Message}\n{text}");
            }
            var errors = await OracleFixture.QueryAsync(c, "SELECT name || ': ' || text AS e FROM user_errors ORDER BY name, sequence");
            Assert.True(errors.Rows.Count == 0, $"{(generated ? "generated" : "as written")} compiled with errors:\n{errors.Render()}\n{text}");
        }
        finally
        {
            await using var sys = await db.OpenSystemAsync();
            try { await OracleFixture.ExecAsync(sys, $"DROP USER {user} CASCADE"); } catch (OracleException) { }
        }
    }
}
