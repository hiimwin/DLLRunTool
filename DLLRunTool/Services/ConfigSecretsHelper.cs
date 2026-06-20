using System.Text.Json;
using System.Text.Json.Nodes;

namespace DLLRunTool.Services;

/// <summary>
/// Tách / làm sạch dữ liệu nhạy cảm — không ghi secrets vào appsettings.json trong source.
/// </summary>
public static class ConfigSecretsHelper
{
    public static readonly string[] SensitiveAppSettingsKeys =
        ["ConnectionStrings", "StringEncryption", "AbpLicenseCode"];

    private static readonly JsonDocumentOptions JsonReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static JsonObject? ParseJsonObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonNode.Parse(json, documentOptions: JsonReadOptions) as JsonObject;
    }

    public static (string PublicContent, JsonObject? SecretsNode) SplitSensitive(string content)
    {
        var node = ParseJsonObject(content);
        if (node == null)
            return (content, null);

        JsonObject? secrets = null;
        foreach (var key in SensitiveAppSettingsKeys)
        {
            if (node[key] == null)
                continue;

            secrets ??= new JsonObject();
            secrets[key] = node[key]!.DeepClone();
            node.Remove(key);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        return (node.ToJsonString(options), secrets);
    }

    public static string SanitizeAppSettingsForBackup(string content)
    {
        var (publicContent, _) = SplitSensitive(content);
        return publicContent;
    }

    public static string SanitizeSecretsFileForExport(string content)
    {
        var node = ParseJsonObject(content);
        if (node == null)
            return content;

        RedactObject(node);
        var options = new JsonSerializerOptions { WriteIndented = true };
        return node.ToJsonString(options);
    }

    public static void RedactServiceUiConfigJson(JsonObject node)
    {
        if (node.TryGetPropertyValue("connectionString", out var conn) && conn != null)
            node["connectionString"] = "";

        if (node.TryGetPropertyValue("ConnectionString", out var conn2) && conn2 != null)
            node["ConnectionString"] = "";
    }

    private static void RedactObject(JsonObject node)
    {
        foreach (var prop in node.ToList())
        {
            switch (prop.Value)
            {
                case JsonObject child:
                    RedactObject(child);
                    break;
                case JsonArray arr:
                    foreach (var item in arr)
                    {
                        if (item is JsonObject itemObj)
                            RedactObject(itemObj);
                    }
                    break;
                case JsonValue when LooksSensitiveKey(prop.Key):
                    node[prop.Key] = "***";
                    break;
            }
        }
    }

    private static bool LooksSensitiveKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("passphrase", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("license", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("StringEncryption", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("AbpLicenseCode", StringComparison.OrdinalIgnoreCase);
}
