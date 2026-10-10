using System.Text;

namespace TodoDock.Core;

/// <summary>
/// Minimal diagnostics: appends errors to %LOCALAPPDATA%\TodoDock\diag.log.
/// The file is rolled to diag.log.old once it reaches 1 MB.
/// </summary>
public static class DiagLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly int Pid = Environment.ProcessId;
    /// <summary>Tests point this at a temp folder so they don't write into the real log.</summary>
    internal static string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodoDock", "diag.log");

    public static void WriteLine(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length >= MaxBytes)
                {
                    File.Copy(LogPath, LogPath + ".old", true);
                    File.Delete(LogPath);
                }

                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{Pid}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must never break the extension.
        }
    }
}
