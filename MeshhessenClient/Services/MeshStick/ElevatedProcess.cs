using System.ComponentModel;
using System.Diagnostics;

namespace MeshhessenClient.Services.MeshStick;

public enum ElevatedRunResult { Success, Cancelled, Failed }

/// <summary>Runs a program through the UAC prompt (ShellExecute "runas") and waits for it. The user may
/// decline the prompt, which Windows reports as ERROR_CANCELLED (1223); that is a normal outcome here.</summary>
public static class ElevatedProcess
{
    /// <summary>PowerShell single-quoted literal (the only escape is doubling the quote).</summary>
    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    public static async Task<(ElevatedRunResult Result, int ExitCode)> RunAsync(string fileName, string arguments,
        bool hidden, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return (ElevatedRunResult.Failed, -1);
            await proc.WaitForExitAsync(ct);
            return (proc.ExitCode == 0 ? ElevatedRunResult.Success : ElevatedRunResult.Failed, proc.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223 || ex.HResult == unchecked((int)0x800704C7))
        {
            return (ElevatedRunResult.Cancelled, 1223);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] elevated start of {fileName} failed: {ex.Message}");
            return (ElevatedRunResult.Failed, -1);
        }
    }
}
