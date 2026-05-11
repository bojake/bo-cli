using System.Text.Json;

namespace BO.Cli;

internal static class CliJsonWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static void Write(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        Console.WriteLine(json);
    }
}
