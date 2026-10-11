using System.Net.Sockets;
using System.Text;
using System.Text.Json;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
var initialized = false;
var ready = false;
var catalog = Catalog.Create();
while (await Console.In.ReadLineAsync() is { } line)
{
    object? id = null;
    var hasId = false;
    try
    {
        if (line.Length > 65536) throw new RpcException(-32600, "Request too large");
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new RpcException(-32600, "Expected an object");
        hasId = root.TryGetProperty("id", out var requestId);
        if (hasId)
        {
            if (requestId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                throw new RpcException(-32600, "Invalid request id");
            id = requestId.Clone();
        }
        if (!root.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0" ||
            !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            throw new RpcException(-32600, "Invalid JSON-RPC request");
        var name = method.GetString();
        if (!hasId)
        {
            if (name == "notifications/initialized" && initialized) ready = true;
            continue;
        }
        var parameters = root.TryGetProperty("params", out var value) ? value : default;
        object result;
        switch (name)
        {
            case "initialize":
                if (initialized) throw new RpcException(-32600, "Already initialized");
                if (parameters.ValueKind != JsonValueKind.Object ||
                    !parameters.TryGetProperty("protocolVersion", out var requested) ||
                    requested.ValueKind != JsonValueKind.String)
                    throw new RpcException(-32602, "Missing protocolVersion");
                var protocol = requested.GetString();
                if (protocol is not ("2025-11-25" or "2025-06-18" or "2025-03-26" or "2024-11-05"))
                    protocol = "2025-11-25";
                initialized = true;
                result = new { protocolVersion = protocol, capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "jacquard", version = "1.0.0" },
                    instructions = "Use jacquard_state before edits. Coordinates are current grid cells. Mutations affect the running desktop app; save explicitly." };
                break;
            case "ping": result = new { }; break;
            case "tools/list":
                RequireReady();
                result = new { tools = catalog.Values.Select(tool => new
                    { name = "jacquard_" + tool.Operation, description = tool.Description,
                      inputSchema = tool.Schema,
                      annotations = new { readOnlyHint = tool.ReadOnly, openWorldHint = false } }) };
                break;
            case "tools/call":
                RequireReady();
                if (parameters.ValueKind != JsonValueKind.Object ||
                    !parameters.TryGetProperty("name", out var toolName) || toolName.ValueKind != JsonValueKind.String ||
                    !catalog.TryGetValue(toolName.GetString() ?? "", out var tool))
                    throw new RpcException(-32602, "Unknown tool name");
                var toolArguments = parameters.TryGetProperty("arguments", out var a) ? a : default;
                var command = tool.Command(toolArguments);
                try
                {
                    var response = await Bridge.Call(command);
                    using var reply = JsonDocument.Parse(response);
                    var ok = reply.RootElement.TryGetProperty("ok", out var success) && success.ValueKind == JsonValueKind.True;
                    result = new { content = new[] { new { type = "text", text = response } }, isError = !ok };
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or InvalidOperationException or JsonException)
                {
                    result = new { content = new[] { new { type = "text", text = "Jacquard control failed: " + ex.Message +
                        ". Inspect state before retrying a mutation." } }, isError = true };
                }
                break;
            default: throw new RpcException(-32601, "Method not found");
        }
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
    }
    catch (JsonException)
    {
        await Error(-32700, "Parse error");
    }
    catch (RpcException ex) { await Error(ex.Code, ex.Message); }
    catch (Exception) { await Error(-32603, "Internal error"); }

    void RequireReady()
    {
        if (!ready) throw new RpcException(-32600, "Initialize the session first");
    }
    async Task Error(int code, string message)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
            { jsonrpc = "2.0", id, error = new { code, message } }));
    }
}

sealed class RpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

sealed record Field(string Name, string Type, double? Min = null, double? Max = null);
sealed record Tool(string Operation, string Description, bool ReadOnly, Field[] Fields)
{
    public object Schema => new
    {
        type = "object",
        properties = Fields.ToDictionary(f => f.Name, f =>
        {
            var schema = new Dictionary<string, object> { ["type"] = f.Type };
            if (f.Min.HasValue) schema["minimum"] = f.Min.Value;
            if (f.Max.HasValue) schema["maximum"] = f.Max.Value;
            return schema;
        }),
        required = Fields.Select(f => f.Name).ToArray(), additionalProperties = false
    };

    public Dictionary<string, object> Command(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Undefined && Fields.Length == 0)
            return new() { ["operation"] = Operation };
        if (args.ValueKind != JsonValueKind.Object) throw new RpcException(-32602, "Arguments must be an object");
        var command = new Dictionary<string, object> { ["operation"] = Operation };
        foreach (var property in args.EnumerateObject())
            if (!Fields.Any(f => f.Name == property.Name)) throw new RpcException(-32602, "Unknown argument: " + property.Name);
        foreach (var field in Fields)
        {
            if (!args.TryGetProperty(field.Name, out var value)) throw new RpcException(-32602, "Missing argument: " + field.Name);
            if (field.Type == "string")
            {
                if (value.ValueKind != JsonValueKind.String) throw new RpcException(-32602, "Expected string: " + field.Name);
                command[field.Name] = value.GetString()!;
            }
            else
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n) ||
                    (field.Type == "integer" && !value.TryGetInt32(out _)) ||
                    (field.Min.HasValue && n < field.Min) || (field.Max.HasValue && n > field.Max))
                    throw new RpcException(-32602, "Invalid number: " + field.Name);
                command[field.Name] = value.Clone();
            }
        }
        return command;
    }
}

static class Catalog
{
    public static Dictionary<string, Tool> Create()
    {
        Field X(string name) => new(name, "integer", 0, 4096);
        var tools = new[]
        {
            new Tool("state", "Read the current score text, playing state, cursor and selection count.", true, []),
            new Tool("presets", "List available FM timbre presets.", true, []),
            new Tool("play", "Start playback if stopped.", false, []),
            new Tool("stop", "Stop playback if playing.", false, []),
            new Tool("tempo", "Set tempo in beats per minute.", false, [new("value", "number", 20, 300)]),
            new Tool("preset", "Replace a channel's entire FM patch with a named preset.", false,
                [new("channel", "integer", 1, 8), new("preset", "string")]),
            new Tool("cursor", "Move the cursor within the visible score plane.", false, [X("x"), X("y")]),
            new Tool("select", "Select tiles in an inclusive grid rectangle; replaces the selection.", false,
                [X("x"), X("y"), X("x2"), X("y2")]),
            new Tool("select_all", "Select all tiles in the score.", false, []),
            new Tool("clear_selection", "Clear tile selection.", false, []),
            new Tool("copy", "Copy selected tiles; with no selection copy the cursor's sub-stack. Flow tiles are excluded.", false, []),
            new Tool("paste", "Paste at the given cell preserving group offsets. Requires empty cells or stack bottoms; refuses the whole group if it cannot fit.", false, [X("x"), X("y")]),
            new Tool("delete", "Delete selected tiles, or the cursor tile/lane if there is no selection. A jump removes its branch.", false, []),
            new Tool("fold", "Toggle folding of the cursor stack in the view; playback stays unchanged.", false, []),
            new Tool("note", "Append a MIDI note to an empty cell or stack bottom, extending at a terminator if possible. Length is in lane steps.", false,
                [X("x"), X("y"), new("note", "integer", 0, 127), new("length", "number", 0.001, 64)]),
            new Tool("save", "Save the current project to the currently selected score slot.", false, [])
        };
        return tools.ToDictionary(t => "jacquard_" + t.Operation);
    }
}

static class Bridge
{
    public static async Task<string> Call(Dictionary<string, object> command)
    {
        var token = Environment.GetEnvironmentVariable("JACQUARD_CONTROL_TOKEN");
        if (string.IsNullOrEmpty(token) || token.Length < 32)
            throw new InvalidOperationException("Set JACQUARD_CONTROL_TOKEN to the same 32+ character token as the app");
        if (!int.TryParse(Environment.GetEnvironmentVariable("JACQUARD_CONTROL_PORT"), out var port) || port < 1024 || port > 65535)
            throw new InvalidOperationException("Set JACQUARD_CONTROL_PORT to the app's port in 1024..65535");
        command["token"] = token;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port, timeout.Token);
        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        await writer.WriteLineAsync(JsonSerializer.Serialize(command).AsMemory(), timeout.Token);
        var response = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), timeout.Token) != 0)
        {
            if (buffer[0] == '\n') return response.ToString();
            if (response.Length >= 4 * 1024 * 1024) throw new IOException("Response too large");
            response.Append(buffer[0]);
        }
        throw new IOException("The app closed the control connection");
    }
}
