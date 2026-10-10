using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TodoDock.Core;

namespace TodoDock;

/// <summary>
/// Base for store-backed task commands: captures only the task id (never the item), re-checks the
/// store at Invoke and never lets an exception escape.
/// </summary>
internal abstract partial class StoreCommand : InvokableCommand
{
    protected StoreCommand(TaskStore store, TasksPage page, string id, string name)
    {
        Store = store;
        Page = page;
        Id = id;
        Name = name;
    }

    protected TaskStore Store { get; }

    protected TasksPage Page { get; }

    protected abstract void Execute();

    public override ICommandResult Invoke()
    {
        try
        {
            Execute();
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"{Id} failed: {ex}");
        }

        return CommandResult.KeepOpen();
    }
}

internal abstract partial class TaskIdCommand(TaskStore store, TasksPage page, string taskId, string verb, string name)
    : StoreCommand(store, page, "TodoDock." + verb + "." + taskId, name)
{
    protected abstract bool Apply(string id);

    protected override void Execute()
    {
        if (!Exists(taskId))
        {
            return; // task vanished (deleted/purged/undone) since the list was built
        }

        Apply(taskId);
    }

    protected bool Exists(string id)
    {
        foreach (var i in Store.Items)
        {
            if (i.Id == id)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed partial class CompleteTaskCommand(TaskStore store, TasksPage page, string taskId)
    : TaskIdCommand(store, page, taskId, "complete", "Complete")
{
    protected override bool Apply(string id) => Store.Complete(id);
}

internal sealed partial class ReopenTaskCommand(TaskStore store, TasksPage page, string taskId)
    : TaskIdCommand(store, page, taskId, "reopen", "Reopen")
{
    protected override bool Apply(string id) => Store.Reopen(id);
}

internal sealed partial class PostponeTaskCommand(TaskStore store, TasksPage page, string taskId)
    : TaskIdCommand(store, page, taskId, "postpone", "Postpone to tomorrow")
{
    protected override bool Apply(string id) => Store.Postpone(id);
}

internal sealed partial class DeleteTaskCommand(TaskStore store, TasksPage page, string taskId)
    : TaskIdCommand(store, page, taskId, "delete", "Delete")
{
    protected override bool Apply(string id) => Store.Delete(id);
}

internal sealed partial class UndoCommand(TaskStore store, TasksPage page)
    : StoreCommand(store, page, "TodoDock.undo", "Undo")
{
    protected override void Execute()
    {
        Store.Undo();
    }
}

/// <summary>Add row. Holds no parse: Submit re-reads and re-parses the live search text.</summary>
internal sealed partial class AddTaskCommand : InvokableCommand
{
    private readonly TasksPage _page;

    public AddTaskCommand(TasksPage page, string text)
    {
        _page = page;
        Id = "TodoDock.add." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 4);
        Name = "Add";
    }

    public override ICommandResult Invoke()
    {
        try
        {
            _page.Submit();
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Add failed: {ex}");
        }

        return CommandResult.KeepOpen();
    }
}
