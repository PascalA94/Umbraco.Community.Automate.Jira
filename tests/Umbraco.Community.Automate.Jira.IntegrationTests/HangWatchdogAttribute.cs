using System.Collections.Concurrent;
using System.Diagnostics;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Umbraco.Community.Automate.Jira.IntegrationTests;

/// <summary>
/// Ends a test run that has hung: when one test exceeds its time limit it captures the managed stacks of every
/// thread, then terminates the test host so the run fails instead of waiting 10 to 15 minutes.
/// </summary>
/// <remarks>
/// Why not NUnit's [CancelAfter] or [Timeout]: the intermittent hang (SQLite error 6 on the first content save
/// of the process) blocks a thread inside Microsoft.Data.Sqlite and Umbraco's RetryPolicy, which never observe a
/// CancellationToken. [CancelAfter] is cooperative. [Timeout] was tried against a deliberately hung test on .NET 10:
/// it did not end the run, because the blocked thread cannot be aborted.
/// Why <c>dotnet-stack</c> and not an in-process capture: .NET has no API for another thread's stack, and a ClrMD
/// or dump-writing dependency would add packages to the per-line lock files. The tool attaches over the diagnostics
/// socket, so it works while a thread is blocked, and the watchdog runs on a thread-pool timer that a blocked test
/// thread cannot starve.
/// Why <see cref="Environment.FailFast(string)"/>: it is the only way to stop a thread that cannot be cancelled. The
/// test host crash fails <c>dotnet test</c> (non-zero exit, "Test host process crashed" with the message below), so a
/// timed-out test can never pass or be skipped. The cost is that the remaining tests do not run, which is acceptable
/// because the shared database is still locked and they would hang too.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class HangWatchdogAttribute : Attribute, ITestAction
{
    // Set by the CI workflow after it installs dotnet-stack; unset locally, where a PATH lookup is tried instead.
    public const string ToolEnvironmentVariable = "JIRA_TEST_DOTNET_STACK";

    // NUnit shares one attribute instance across every test in a fixture, so a field would let a second (parallel)
    // test overwrite the first one's timer, leak it, and fire a false FailFast. Keyed by test id instead.
    private static readonly ConcurrentDictionary<string, Timer> Timers = new();

    public HangWatchdogAttribute(int timeoutSeconds) => TimeoutSeconds = timeoutSeconds;

    public int TimeoutSeconds { get; }

    public ActionTargets Targets => ActionTargets.Test;

    /// <summary>The folder the CI workflow uploads. Next to the test binaries, so it is created fresh on every runner.</summary>
    public static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "hang-diagnostics");

    public void BeforeTest(ITest test)
    {
        // A debugger pause at a breakpoint is not a hang; the watchdog would kill the session being debugged.
        if (Debugger.IsAttached)
        {
            return;
        }

        var id = test.Id;
        var name = test.FullName;
        var started = Stopwatch.StartNew();
        var timer = new Timer(_ => Capture(id, name, started.Elapsed), null, TimeSpan.FromSeconds(TimeoutSeconds), Timeout.InfiniteTimeSpan);
        if (Timers.TryAdd(test.Id, timer))
        {
            return;
        }

        // Same test id armed twice: keep the first timer and drop this one rather than leak it.
        timer.Dispose();
    }

    public void AfterTest(ITest test)
    {
        if (Timers.TryRemove(test.Id, out var timer))
        {
            timer.Dispose();
        }
    }

    private static void Capture(string testId, string testName, TimeSpan elapsed)
    {
        // The test can finish between the timer firing and here (AfterTest removes the entry). A finished test is not a
        // hang, so crashing the host would be a false failure.
        if (!Timers.ContainsKey(testId))
        {
            return;
        }

        Directory.CreateDirectory(OutputDirectory);
        var file = Path.Combine(OutputDirectory, $"stacks-{DateTime.UtcNow:yyyyMMddTHHmmss}.txt");
        var header = $"Test '{testName}' still running at {DateTime.UtcNow:O}; capturing managed stacks of process {Environment.ProcessId}.{Environment.NewLine}";
        try
        {
            var tool = Environment.GetEnvironmentVariable(ToolEnvironmentVariable);
            using var process = Process.Start(new ProcessStartInfo(string.IsNullOrWhiteSpace(tool) ? "dotnet-stack" : tool, $"report -p {Environment.ProcessId}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (process.WaitForExit(60_000))
            {
                File.WriteAllText(file, header + stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            }
            else
            {
                process.Kill(entireProcessTree: true);
                File.WriteAllText(file, header + "dotnet-stack did not finish within 60 s.");
            }
        }
        catch (Exception ex)
        {
            // dotnet-stack missing is the expected local case; say so rather than hiding it.
            File.WriteAllText(file, header + "Stack capture failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        // dotnet-stack can take up to 60 s, so the test may have finished meanwhile; check again as late as possible.
        if (!Timers.ContainsKey(testId))
        {
            return;
        }

        var message = $"Integration test '{testName}' exceeded its time limit after {elapsed.TotalSeconds:F0} s. Thread stacks: {file}";

        // Umbraco's harness (UmbracoTestOptions.Logger.Serilog, CreateLoggerFactory in Umbraco.Tests.Integration)
        // assigns the TEMP/logs file sink to the static Serilog.Log.Logger, so flushing that logger flushes the file.
        // FailFast skips finalisers and normal shutdown, so without this the last Debug lines are lost.
        try
        {
            Serilog.Log.CloseAndFlush();
        }
        catch (Exception)
        {
            // Never let a logging failure stop the crash below.
        }

        // stderr first-hand: the reason must reach the CI console even if the file log is cut short.
        try
        {
            Console.Error.WriteLine(message);
            Console.Error.Flush();
        }
        catch (Exception)
        {
            // Closed stderr must not prevent FailFast.
        }

        Environment.FailFast(message);
    }
}
