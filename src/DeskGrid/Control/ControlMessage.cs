using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskGrid.Control;

/// <summary>
/// A command for the running DeskGrid, as <c>DeskGrid.exe --ctl</c> (or any other program) sends it over the
/// control pipe: one line of JSON, <c>{"command":"list","args":["Work"]}</c>.
/// </summary>
internal sealed record ControlRequest(string Command, IReadOnlyList<string> Args)
{
    public string Format() => new JsonObject
    {
        ["command"] = Command,
        ["args"] = new JsonArray([.. Args.Select(a => JsonValue.Create(a))]),
    }.ToJsonString();

    /// <summary>The request in <paramref name="line"/>, or <see langword="null"/> if it isn't one.</summary>
    public static ControlRequest? TryParse(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject request
                || request["command"] is not JsonValue command
                || !command.TryGetValue(out string? name)
                || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var args = new List<string>();
            if (request["args"] is JsonArray array)
            {
                foreach (var arg in array)
                {
                    if (arg is not JsonValue value || !value.TryGetValue(out string? text))
                    {
                        return null;
                    }

                    args.Add(text);
                }
            }
            else if (request["args"] is not null)
            {
                return null;
            }

            return new ControlRequest(name.Trim().ToLowerInvariant(), args);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The answer to a <see cref="ControlRequest"/>: one line of JSON, <c>{"ok":true,"result":…}</c> or
/// <c>{"ok":false,"error":"…"}</c>. Non-ASCII characters are escaped (<c>\uXXXX</c>), so the line reads the same
/// whatever code page the console that shows it uses.
/// </summary>
internal sealed record ControlResponse(bool Ok, JsonNode? Result, string? Error)
{
    public static ControlResponse Success(JsonNode? result = null) => new(true, result ?? new JsonObject(), null);

    public static ControlResponse Failure(string error) => new(false, null, error);

    public string Format()
    {
        var response = new JsonObject { ["ok"] = Ok };
        if (Ok)
        {
            response["result"] = Result?.DeepClone();
        }
        else
        {
            response["error"] = Error;
        }

        return response.ToJsonString();
    }

    public static ControlResponse? TryParse(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject response || response["ok"] is not JsonValue ok || !ok.TryGetValue(out bool success))
            {
                return null;
            }

            return success
                ? new ControlResponse(true, response["result"]?.DeepClone(), null)
                : new ControlResponse(false, null, response["error"]?.GetValue<string>() ?? "");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
