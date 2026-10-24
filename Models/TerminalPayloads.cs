namespace AgentsHub.Models;

public record TerminalInitPayload(
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("isRunning")] bool IsRunning,
    [property: System.Text.Json.Serialization.JsonPropertyName("statusText")] string StatusText,
    [property: System.Text.Json.Serialization.JsonPropertyName("history")] string History);

public record TerminalOutputPayload(
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("data")] string Data);

public record TerminalStatusPayload(
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("isRunning")] bool IsRunning,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message);
