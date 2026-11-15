using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace OracleSqlParser.Fuzzing;

/// <summary>
/// The result of checking one input in a worker process: Failure is null when the contract held.
/// Hangs and crashes are failures too (category "hang" / "crash").
/// </summary>
public sealed record Outcome(FuzzFailure? Failure, double Milliseconds);

/// <summary>
/// The child side of the fuzzer. Reads one input per line ("variant TAB base64(UTF-16 SQL)"), checks it on a 1 MB-stack
/// thread with a 2 s budget, and answers "OK TAB ms" or "FAIL TAB ms TAB category TAB base64(sig) TAB base64(detail)".
/// A hang answers "HANG" and exits; a stack overflow or other crash just ends the process, which the parent detects.
/// </summary>
public static class Worker
{
    /// <summary>The per-input budget inside the worker.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The stack of the thread that runs each input: an explicit 1 MB. (DOTNET_DefaultStackSize does not reach thread-pool
    /// threads on this platform, so pool threads would run with a larger, unpinned stack.)
    /// </summary>
    public const int StackBytes = 1024 * 1024;

    public static int Run()
    {
        var input = Console.In;
        var output = Console.Out;
        string? line;
        while ((line = input.ReadLine()) != null)
        {
            int tab = line.IndexOf('\t');
            int variant = int.Parse(line.AsSpan(0, tab));
            string sql = Encoding.Unicode.GetString(Convert.FromBase64String(line[(tab + 1)..]));
            var c = new FuzzCase(sql, variant);
            var sw = Stopwatch.StartNew();
            FuzzFailure? f = null;
            var thread = new Thread(() => f = ContractChecker.Check(c), StackBytes) { IsBackground = true };
            thread.Start();
            if (!thread.Join(Budget))
            {
                output.WriteLine("HANG");
                return 3;
            }
            double ms = sw.Elapsed.TotalMilliseconds;
            if (f is null)
                output.WriteLine("OK\t" + ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            else
                output.WriteLine(string.Join('\t', "FAIL", ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), f.Category,
                    B64(f.Signature), B64(f.Detail)));
        }
        return 0;
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    private static string Unb64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

    /// <summary>The parent side: owns one worker process at a time and restarts it after a hang or crash.</summary>
    public sealed class Client : IDisposable
    {
        private Process? _p;
        private Task<string>? _stderr;

        /// <summary>Checks one input. Never throws for a worker problem; those come back as "hang" or "crash" outcomes.</summary>
        public Outcome Run(FuzzCase c)
        {
            if (_p is null || _p.HasExited) Start();
            var sw = Stopwatch.StartNew();
            string request = c.Variant + "\t" + Convert.ToBase64String(Encoding.Unicode.GetBytes(c.Sql));
            Task<string?> reply;
            try
            {
                _p!.StandardInput.WriteLine(request);
                _p.StandardInput.Flush();
                reply = _p.StandardOutput.ReadLineAsync();
            }
            catch (IOException)
            {
                return Crashed(c, sw);
            }
            if (!reply.Wait(Budget + TimeSpan.FromSeconds(5)))
            {
                Kill();
                return new Outcome(new FuzzFailure("hang", "hang", "no answer from the worker within 7 s"), sw.Elapsed.TotalMilliseconds);
            }
            string? line = reply.Result;
            if (line is null) return Crashed(c, sw);
            if (line == "HANG")
            {
                Kill();
                return new Outcome(new FuzzFailure("hang", "hang", "parse exceeded " + Budget.TotalSeconds + " s"), sw.Elapsed.TotalMilliseconds);
            }
            var parts = line.Split('\t');
            double ms = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            if (parts[0] == "OK") return new Outcome(null, ms);
            var failure = new FuzzFailure(parts[2], Unb64(parts[3]), Unb64(parts[4]));
            return new Outcome(failure, ms);
        }

        private Outcome Crashed(FuzzCase c, Stopwatch sw)
        {
            _p!.WaitForExit(5000);
            string err = _stderr is { } t && t.Wait(TimeSpan.FromSeconds(2)) ? t.Result : "";
            int code = _p.HasExited ? _p.ExitCode : -1;
            Kill();
            // .NET reports a stack overflow on stderr: "Stack overflow." followed by the repeated frame.
            var m = Regex.Match(err, @"Stack overflow\.[\s\S]*?\n\s*at ([^\(\r\n]+)\(");
            string sig = m.Success ? "crash:stack-overflow:" + m.Groups[1].Value.Trim() : "crash:exit" + code;
            string detail = $"worker exited with code {code}\n{err}";
            return new Outcome(new FuzzFailure("crash", sig, detail), sw.Elapsed.TotalMilliseconds);
        }

        private void Start()
        {
            Kill();
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // Under "dotnet <dll>" the host is the process; an apphost build runs the executable directly.
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                psi.ArgumentList.Add(typeof(Worker).Assembly.Location);
            psi.ArgumentList.Add("--worker");
            _p = Process.Start(psi) ?? throw new InvalidOperationException("could not start worker");
            _stderr = _p.StandardError.ReadToEndAsync();
        }

        private void Kill()
        {
            if (_p is null) return;
            try
            {
                if (!_p.HasExited) _p.Kill(entireProcessTree: true);
                _p.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
                // already gone
            }
            _p.Dispose();
            _p = null;
            _stderr = null;
        }

        public void Dispose() => Kill();
    }
}
