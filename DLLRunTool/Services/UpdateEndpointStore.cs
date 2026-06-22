using System.Security.Cryptography;
using System.Text;

namespace DLLRunTool.Services;

/// <summary>
/// Manifest URL nhÃƒÂºng trong exe (obfuscate) Ã¢â‚¬â€ khÃƒÂ´ng ship plain text trong zip.
/// Regenerate payload khi publish: scripts/embed-update-endpoint.ps1
/// </summary>
internal static class UpdateEndpointStore
{
    // Replaced by publish.ps1 Ã¢â‚¬â€ do not edit payload by hand.
    private const string Payload = "Uxx0C/WgU5A0v8YNIQXv/U4KdQjj6B/QKKrUTTJC+PpWR2gS7/cL1ijx9W8KPu77bwdvF6n3HdYo8cRTIg3v8BYFYRXv/BnMMvDbUCkC";

    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes("AKC.Products.MCP.UpdateEndpoint.v1"))[..16];

    public static string? GetManifestUrl()
    {
        if (string.IsNullOrWhiteSpace(Payload) ||
            Payload.Equals("PLACEHOLDER", StringComparison.Ordinal))
        {
            return TryDevConfigOverride();
        }

        try
        {
            return Decode(Payload);
        }
        catch
        {
            return TryDevConfigOverride();
        }
    }

    /// <summary>ChÃ¡Â»â€° dÃƒÂ¹ng khi chÃ¡ÂºÂ¡y tÃ¡Â»Â« source/debug Ã¢â‚¬â€ file khÃƒÂ´ng Ã„â€˜ÃƒÂ³ng gÃƒÂ³i trong zip release.</summary>
    private static string? TryDevConfigOverride()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "update-check.config.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("manifestUrl", out var node))
            {
                var url = node.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(url) ? null : url;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    internal static string EncodeForPublish(string plainUrl)
    {
        var data = Encoding.UTF8.GetBytes(plainUrl);
        for (var i = 0; i < data.Length; i++)
            data[i] ^= Key[i % Key.Length];
        return Convert.ToBase64String(data);
    }

    private static string Decode(string payload)
    {
        var data = Convert.FromBase64String(payload);
        for (var i = 0; i < data.Length; i++)
            data[i] ^= Key[i % Key.Length];
        return Encoding.UTF8.GetString(data);
    }
}
