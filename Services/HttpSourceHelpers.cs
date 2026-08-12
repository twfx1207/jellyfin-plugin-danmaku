using System.Globalization;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Danmaku.Models;

namespace Jellyfin.Plugin.Danmaku.Services;

internal static class HttpSourceHelpers
{
    public static Uri CombineUrl(string baseUrl, string path)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DanmakuSourceException("弹幕源地址必须是有效的 HTTP 或 HTTPS URL。");
        }

        var normalizedBase = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(normalizedBase, path.TrimStart('/'));
    }

    public static Uri AddQuery(Uri uri, IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        var builder = new UriBuilder(uri);
        var query = new StringBuilder(builder.Query.TrimStart('?'));
        foreach (var pair in parameters)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (query.Length > 0)
            {
                query.Append('&');
            }

            query.Append(Uri.EscapeDataString(pair.Key));
            query.Append('=');
            query.Append(Uri.EscapeDataString(pair.Value));
        }

        builder.Query = query.ToString();
        return builder.Uri;
    }

    public static void EnsureSuccessfulPayload(JsonElement root)
    {
        var success = root.GetBooleanValue("success", true);
        var errorCode = root.GetInt32Value("errorCode");
        if (success && errorCode == 0)
        {
            return;
        }

        var message = root.GetStringValue("errorMessage", "弹幕源返回了错误。");
        throw new DanmakuSourceException(message);
    }

    public static IReadOnlyList<DanmakuCommentDto> ParseComments(JsonElement root)
    {
        if (!root.TryGetPropertyIgnoreCase("comments", out var comments)
            || comments.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DanmakuCommentDto>();
        }

        var result = new List<DanmakuCommentDto>();
        foreach (var comment in comments.EnumerateArray())
        {
            var text = comment.GetStringValue("text", comment.GetStringValue("m"));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (comment.TryGetPropertyIgnoreCase("p", out var p) && p.ValueKind == JsonValueKind.String)
            {
                var parameters = (p.GetString() ?? string.Empty).Split(',');
                if (parameters.Length < 3
                    || !double.TryParse(parameters[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time))
                {
                    continue;
                }

                _ = int.TryParse(parameters[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode);
                _ = int.TryParse(parameters[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var color);
                result.Add(new DanmakuCommentDto
                {
                    Time = Math.Max(0, time),
                    Mode = NormalizeMode(mode),
                    Color = NormalizeColor(color),
                    Text = text
                });
                continue;
            }

            result.Add(new DanmakuCommentDto
            {
                Time = Math.Max(0, comment.GetDoubleValue("time")),
                Mode = NormalizeMode(ParseMode(comment)),
                Color = NormalizeColor(ParseColor(comment)),
                Text = text
            });
        }

        return result.OrderBy(comment => comment.Time).ToArray();
    }

    private static int ParseMode(JsonElement comment)
    {
        if (!comment.TryGetPropertyIgnoreCase("mode", out var value))
        {
            return 1;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericMode))
        {
            return numericMode;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return 1;
        }

        return value.GetString()?.ToLowerInvariant() switch
        {
            "bottom" => 4,
            "top" => 5,
            _ => 1
        };
    }

    private static int ParseColor(JsonElement comment)
    {
        if (!comment.TryGetPropertyIgnoreCase("color", out var value))
        {
            return 0xFFFFFF;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericColor))
        {
            return numericColor;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return 0xFFFFFF;
        }

        var text = value.GetString()?.Trim();
        if (!string.IsNullOrEmpty(text) && text[0] == '#')
        {
            text = text[1..];
        }

        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out numericColor)
            ? numericColor
            : 0xFFFFFF;
    }

    private static int NormalizeMode(int mode) => mode is 4 or 5 ? mode : 1;

    private static int NormalizeColor(int color) => Math.Clamp(color, 0, 0xFFFFFF);
}
