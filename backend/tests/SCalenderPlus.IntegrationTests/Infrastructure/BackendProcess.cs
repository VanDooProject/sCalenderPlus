using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>Runs a backend host (Api/Worker) as a real child process, as the container would.</summary>
internal static class BackendProcess
{
    public const string Api = "SCalenderPlus.Api";
    public const string Worker = "SCalenderPlus.Worker";

    private static readonly string[] _inheritedPrefixesToDrop = ["App__", "ConnectionStrings__", "Database__", "Otel__", "Smtp__", "Jobs__", "ReverseProxy__", "ASPNETCORE_", "DOTNET_ENVIRONMENT"];

    public static async Task<ProcessResult> RunAsync(
        string project,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout)
    {
        using var process = Start(project, args, environment, out var output);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{project} {string.Join(' ', args)} did not exit within {timeout}. Output:\n{output}");
        }

        process.WaitForExit(); // flush async output handlers
        return new ProcessResult(process.ExitCode, output.ToString());
    }

    public static Process Start(
        string project,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?> environment,
        out StringBuilder output)
    {
        var assemblyPath = AssemblyPath(project);
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(assemblyPath);
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (var key in psi.Environment.Keys.Where(k => _inheritedPrefixesToDrop.Any(p => k.StartsWith(p, StringComparison.Ordinal))).ToList())
        {
            psi.Environment.Remove(key);
        }

        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }

        var buffer = new StringBuilder();
        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => Append(buffer, e.Data);
        process.ErrorDataReceived += (_, e) => Append(buffer, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        output = buffer;
        return process;
    }

    private static void Append(StringBuilder buffer, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (buffer)
        {
            buffer.AppendLine(line);
        }
    }

    private static string AssemblyPath(string project)
    {
        var configuration = typeof(BackendProcess).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var path = Path.Combine(BackendRoot(), "src", project, "bin", configuration, "net10.0", project + ".dll");
        return File.Exists(path) ? path : throw new FileNotFoundException($"Build {project} first.", path);
    }

    private static string BackendRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SCalenderPlus.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate backend/SCalenderPlus.slnx.");
    }
}

internal sealed record ProcessResult(int ExitCode, string Output);
