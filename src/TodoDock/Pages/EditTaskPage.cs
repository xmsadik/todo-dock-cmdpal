using System;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using TodoDock.Core;

namespace TodoDock;

/// <summary>
/// Edit form for one task: a single quick-add text box prefilled with the task's current text.
/// A form is used because the host does not mirror an extension-set SearchText into its search box.
/// </summary>
internal sealed partial class EditTaskPage : ContentPage
{
    private readonly TaskStore _store;
    private readonly string _taskId;

    public EditTaskPage(TaskStore store, string taskId)
    {
        _store = store;
        _taskId = taskId;
        Id = "TodoDock.edit." + taskId;
        Name = "Edit";
        Title = "Edit task";
        Icon = new IconInfo("✏️");
    }

    // Built on every open so the form shows the task as it is now, not as it was when the list was built.
    public override IContent[] GetContent()
    {
        try
        {
            foreach (var item in _store.Items)
            {
                if (item.Id == _taskId)
                {
                    return [new EditTaskForm(_store, _taskId, QuickAddFormatter.ToText(item, _store.Today))];
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Edit form failed: {ex}");
        }

        return [new MarkdownContent("This task no longer exists.")];
    }
}

internal sealed partial class EditTaskForm : FormContent
{
    private readonly TaskStore _store;
    private readonly string _taskId;

    public EditTaskForm(TaskStore store, string taskId, string text)
    {
        _store = store;
        _taskId = taskId;
        TemplateJson = """
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "type": "AdaptiveCard",
            "version": "1.6",
            "body": [
                {
                    "type": "Input.Text",
                    "id": "text",
                    "label": "Task",
                    "value": "${text}",
                    "isRequired": true,
                    "errorMessage": "Type a task title"
                },
                {
                    "type": "TextBlock",
                    "text": "Same words as quick add: today, tomorrow, monday, oct 12, 17:00, every day, weekdays, every week.",
                    "wrap": true,
                    "isSubtle": true,
                    "size": "Small"
                }
            ],
            "actions": [
                { "type": "Action.Submit", "title": "Save" }
            ]
        }
        """;

        // JsonObject escapes the value, so quotes or backslashes in a title can't break the card.
        DataJson = new JsonObject { ["text"] = text }.ToJsonString();
    }

    public override ICommandResult SubmitForm(string payload)
    {
        try
        {
            var text = JsonNode.Parse(payload)?["text"]?.GetValue<string>();
            var parsed = QuickAddParser.Parse(text, _store.Today);
            if (parsed is null)
            {
                return CommandResult.ShowToast("Type a task title");
            }

            if (!_store.Update(_taskId, parsed))
            {
                _store.Add(parsed); // the task vanished meanwhile: never lose the text
            }
        }
        catch (Exception ex)
        {
            DiagLog.WriteLine($"Edit submit failed: {ex}");
        }

        return CommandResult.GoBack();
    }
}
