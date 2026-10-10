using System;
using System.Text;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TodoDock.Core;

namespace TodoDock;

public partial class TodoDockCommandsProvider : CommandProvider
{
    private readonly TaskStore _store;
    private readonly SettingsManager _settingsManager = new();
    private readonly TasksPage _page;
    private readonly TodoBand _band;
    private readonly CommandItem _topItem;
    private readonly ICommandItem[] _commands;
    private readonly WrappedDockItem _dockBand;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private readonly Timer _timer;
    private readonly object _gate = new();
    private string _signature = "";
    private string _topSubtitle = "";
    private long _seq;
    private DateOnly _lastDate;
    private bool _disposed;

    public TodoDockCommandsProvider()
    {
        DisplayName = "Todo Dock";
        Id = "TodoDock";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _store = new TaskStore(TaskStore.DefaultPath(), TimeProvider.System);
        _lastDate = _store.Today;
        _page = new TasksPage(_store, _settingsManager);
        _band = new TodoBand(_page);

        _topItem = new CommandItem(_page)
        {
            Title = DisplayName,
            MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
        };
        _commands = [_topItem];

        // Command.Id must be non-empty or the host silently drops the band.
        _dockBand = new WrappedDockItem([_band], "TodoDock.dock.tasks", "Todo Dock");
        Settings = _settingsManager.Settings;

        _store.Changed += OnChanged;
        _store.SaveFailed += OnSaveFailed;
        _settingsManager.Settings.SettingsChanged += OnChanged;
        Refresh(force: true);
        _timer = new Timer(OnTick, null, TickInterval, Timeout.InfiniteTimeSpan);
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_dockBand];

    private void OnChanged(object? sender, object? e) => Refresh(force: false);

    private void OnSaveFailed(object? sender, string message) => DiagLog.WriteLine($"tasks save failed: {message}");

    private void OnTick(object? state)
    {
        try
        {
            var today = _store.Today;
            bool dayChanged;
            lock (_gate)
            {
                dayChanged = today != _lastDate;
            }

            if (dayChanged)
            {
                _store.Purge(); // raises Changed when something was removed
                lock (_gate)
                {
                    _lastDate = today; // only after Purge succeeded, so a failure retries next tick
                }
            }

            Refresh(force: dayChanged);
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Timer tick failed: {ex}");
        }
        finally
        {
            // Non-reentrant: re-arm only after this tick finished.
            try
            {
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        _timer.Change(TickInterval, Timeout.InfiniteTimeSpan);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void Refresh(bool force)
    {
        try
        {
            TaskSnapshot snapshot;
            string format;
            string? saveError;
            bool refreshPage;
            long seq;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                snapshot = TaskClassifier.Classify(_store.Items, _store.LocalNow, _settingsManager.ProgressMode);
                format = _settingsManager.LabelFormat;
                saveError = _store.LastSaveError;
                var signature = Signature(snapshot, format, _store);
                refreshPage = force || signature != _signature;
                _signature = signature;
                seq = ++_seq;
            }

            // Host property sets happen with no lock held: the host may answer PropChanged by calling back
            // into the extension on another thread, so holding a lock here can deadlock. A refresh that was
            // overtaken by a newer one skips applying its stale values.
            if (seq == Interlocked.Read(ref _seq))
            {
                _band.Render(snapshot, format, saveError);

                var counts = snapshot.CountsLine;
                if (counts != _topSubtitle)
                {
                    try
                    {
                        _topItem.Subtitle = counts;
                        _topSubtitle = counts;
                    }
                    catch (Exception ex)
                    {
                        DiagLog.WriteLine($"Top item subtitle failed: {ex}");
                    }
                }
            }

            if (refreshPage)
            {
                _page.Refresh();
            }
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Refresh failed: {ex}");
        }
    }

    /// <summary>Everything TasksPage renders: row titles, due/time/recurrence/done state, sections, undo row.</summary>
    private static string Signature(TaskSnapshot s, string format, TaskStore store)
    {
        var sb = new StringBuilder();
        sb.Append(format).Append('|').Append(s.Label(format)).Append('|').Append(s.Tooltip).Append('|').Append((int)s.Light);
        sb.Append('|').Append(store.Today.DayNumber).Append('|').Append(store.UndoDescription);
        foreach (var list in new[] { s.Overdue, s.Today, s.Upcoming, s.Done })
        {
            sb.Append('#');
            foreach (var i in list)
            {
                sb.Append(i.Id).Append(';').Append(i.Title).Append(';').Append(i.Due.DayNumber).Append(';')
                    .Append(i.Time).Append(';').Append((int)i.Recurrence).Append(';')
                    .Append(i.CompletedAt?.UtcTicks).Append(',');
            }
        }

        return sb.ToString();
    }

    public override void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _timer.Dispose();
        _store.Changed -= OnChanged;
        _store.SaveFailed -= OnSaveFailed;
        _settingsManager.Settings.SettingsChanged -= OnChanged;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
