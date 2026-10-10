using System.Runtime.CompilerServices;
using TodoDock.Core;

namespace TodoDock.Tests;

internal static class TestLogSetup
{
    [ModuleInitializer]
    internal static void RedirectDiagLog() =>
        DiagLog.LogPath = Path.Combine(Path.GetTempPath(), "TodoDock.Tests", "diag.log");
}
