using System.Text.Encodings.Web;
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

    public static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonDocumentOptions JsonReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string WriteJsonObject(JsonObject node) => node.ToJsonString(JsonWriteOptions);

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

        var options = JsonWriteOptions;
        return (node.ToJsonString(options), secrets);
    }

    public static string SanitizeAppSettingsForBackup(string content)
    {
        var (publicContent, _) = SplitSensitive(content);
        return publicContent;
    }

    /// <summary>
    /// Gộp backup vào appsettings source — giữ secrets từ source nếu backup export đã strip.
    /// ConnectionStrings: merge từng key; không Unicode-escape PublicKey (+ giữ nguyên).
    /// </summary>
    public static string MergeAppSettingsForApply(string? existingContent, string backupContent)
    {
        var backup = ParseJsonObject(backupContent);
        if (backup == null)
            return existingContent ?? backupContent;

        if (string.IsNullOrWhiteSpace(existingContent))
            return WriteJsonObject(backup);

        var existing = ParseJsonObject(existingContent);
        if (existing == null)
            return backupContent;

        foreach (var prop in backup)
        {
            if (IsSensitiveAppSettingsKey(prop.Key) && !HasSensitiveContent(prop.Key, prop.Value))
                continue;

            if (prop.Key.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase) &&
                prop.Value is JsonObject backupConn)
            {
                var targetConn = existing["ConnectionStrings"] as JsonObject ?? new JsonObject();
                foreach (var kv in backupConn)
                    targetConn[kv.Key] = kv.Value?.DeepClone();
                existing["ConnectionStrings"] = targetConn;
                continue;
            }

            existing[prop.Key] = prop.Value?.DeepClone();
        }

        return WriteJsonObject(existing);
    }

    /// <summary>Chỉ cập nhật giá trị trong ConnectionStrings — giữ nguyên các key/name hiện có.</summary>
    public static string PatchConnectionStrings(string existingContent, string connectionString)
    {
        var existing = ParseJsonObject(existingContent)
                       ?? throw new InvalidOperationException("appsettings.json không hợp lệ.");

        JsonObject conn;
        if (existing["ConnectionStrings"] is JsonObject existingConn && existingConn.Count > 0)
        {
            conn = existingConn;
            foreach (var key in conn.Select(p => p.Key).ToList())
                conn[key] = connectionString;
        }
        else
        {
            conn = new JsonObject { ["Default"] = connectionString };
        }

        var patch = new JsonObject { ["ConnectionStrings"] = conn.DeepClone() };
        return MergeAppSettingsForApply(existingContent, WriteJsonObject(patch));
    }

    private static bool IsSensitiveAppSettingsKey(string key) =>
        SensitiveAppSettingsKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static bool HasSensitiveContent(string key, JsonNode? value)
    {
        if (value == null)
            return false;

        if (key.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase) && value is JsonObject conn)
        {
            return conn.Any(p => !string.IsNullOrWhiteSpace(p.Value?.GetValue<string>()));
        }

        if (value is JsonObject obj)
        {
            return obj.Any(p =>
                p.Value != null &&
                !string.IsNullOrWhiteSpace(p.Value.GetValue<string>()));
        }

        return !string.IsNullOrWhiteSpace(value.GetValue<string>());
    }

    public static string SanitizeSecretsFileForExport(string content)
    {
        var node = ParseJsonObject(content);
        if (node == null)
            return content;

        RedactObject(node);
        return WriteJsonObject(node);
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
