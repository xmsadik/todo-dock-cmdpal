using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoDock.Core;

[JsonConverter(typeof(JsonStringEnumConverter<Recurrence>))]
public enum Recurrence
{
    None,
    Daily,
    Weekdays,
    Weekly,
}

/// <summary>A single task. Local dates/times are stored as local values and never converted to UTC (DST safe).</summary>
public sealed record TodoItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; init; } = "";

    /// <summary>Local due day.</summary>
    public DateOnly Due { get; init; }

    /// <summary>Optional local due time of day. A malformed value in the file loads as null.</summary>
    [JsonConverter(typeof(TolerantTimeConverter))]
    public TimeOnly? Time { get; init; }

    public Recurrence Recurrence { get; init; }

    /// <summary>Weekday a Weekly task is anchored to; survives postponing. Null falls back to Due's weekday.</summary>
    public DayOfWeek? RecurrenceDay { get; init; }

    /// <summary>Id of the completed task whose completion spawned this instance (removed again on Reopen).</summary>
    public string? SpawnedFromId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Local day the task was completed on.</summary>
    public DateOnly? CompletedOn { get; init; }

    [JsonIgnore]
    public bool IsDone => CompletedAt is not null;
}

/// <summary>Parse/format TimeOnly? as "HH:mm" (also accepts "HH:mm:ss"); any bad value reads as null.</summary>
internal sealed class TolerantTimeConverter : JsonConverter<TimeOnly?>
{
    public override bool HandleNull => true;

    public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && TimeOnly.TryParse(reader.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var t))
        {
            return t;
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options)
    {
        if (value is { } t)
        {
            writer.WriteStringValue(t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>File shape: { "version": 1, "tasks": [...] }.</summary>
internal sealed class TodoFile
{
    public int Version { get; set; } = 1;

    public List<TodoItem?>? Tasks { get; set; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(TodoFile))]
internal sealed partial class TodoJsonContext : JsonSerializerContext
{
}
