using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.Danmaku.Services;

internal static class JsonElementExtensions
{
    public static bool TryGetPropertyIgnoreCase(this JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string GetStringValue(this JsonElement element, string name, string defaultValue = "")
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? defaultValue,
            JsonValueKind.Number => value.GetRawText(),
            _ => defaultValue
        };
    }

    public static bool GetBooleanValue(this JsonElement element, string name, bool defaultValue = false)
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out var result)
            ? result
            : defaultValue;
    }

    public static int GetInt32Value(this JsonElement element, string name, int defaultValue = 0)
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : defaultValue;
    }

    public static double GetDoubleValue(this JsonElement element, string name, double defaultValue = 0)
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : defaultValue;
    }
}
