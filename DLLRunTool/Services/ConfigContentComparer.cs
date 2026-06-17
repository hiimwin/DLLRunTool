using System.Text.Json;
using System.Text.Json.Nodes;

namespace DLLRunTool.Services;

internal static class ConfigContentComparer
{
    private static readonly JsonDocumentOptions JsonReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool AreEqual(string? existing, string incoming, string key)
    {
        if (existing == null)
            return false;

        var left = NormalizeNewlines(existing);
        var right = NormalizeNewlines(incoming);
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;

        if (!IsJsonFile(key))
            return false;

        try
        {
            var leftNode = JsonNode.Parse(left, documentOptions: JsonReadOptions);
            var rightNode = JsonNode.Parse(right, documentOptions: JsonReadOptions);
            if (leftNode == null || rightNode == null)
                return false;

            return JsonSerializer.Serialize(leftNode) == JsonSerializer.Serialize(rightNode);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsJsonFile(string key) =>
        key.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
}
