using System.Diagnostics;
using System.Text;

namespace Membran.MultiTool.Core.Windows;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);

public sealed class SystemCommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        int timeoutMs = 15_000,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            return new CommandResult(-1, string.Empty, "Process failed to start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var waitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeoutMs, cancellationToken);
        var completedTask = await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false);

        if (completedTask != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            return new CommandResult(-1, stdout.ToString(), "Timed out.");
        }

        await waitTask.ConfigureAwait(false);

        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
