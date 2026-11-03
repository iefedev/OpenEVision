using System.Text.Json;

namespace OpenEVision.Device;

public record KeyCap(string Code, string Label, double X, double Y, double Width);

/// <summary>On-screen layout and the custom/flash LED order of the active device profile.</summary>
public static class KeyLayout
{
    static DeviceProfile Profile => DeviceProfiles.Active;
    public static int Columns => Profile.Columns;

    /// <summary>Key code -> custom (flash) LED index, from the profile's LED matrix.</summary>
    public static readonly IReadOnlyDictionary<string, int> CustomLed = BuildCustomLed();

    public static readonly IReadOnlyList<KeyCap> Keys = LoadKeys();
    public static double TotalWidth => Keys.Max(k => k.X + k.Width);

    static Dictionary<string, int> BuildCustomLed()
    {
        var rows = Profile.CustomLedRows;
        var map = new Dictionary<string, int>();
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                if (rows[r][c] is { } code) map[code] = r * Profile.Columns + c;
        return map;
    }

    static List<KeyCap> LoadKeys()
    {
        using var s = typeof(KeyLayout).Assembly.GetManifestResourceStream(Profile.LayoutResource)!;
        using var doc = JsonDocument.Parse(s);
        var keys = new List<KeyCap>();
        int y = 0;
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            double x = 0;
            foreach (var item in row.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number) { x += item.GetDouble(); continue; }
                double w = item.GetArrayLength() > 2 ? item[2].GetDouble() : 1;
                keys.Add(new KeyCap(item[0].GetString()!, item[1].GetString()!, x, y, w));
                x += w;
            }
            y++;
        }
        return keys;
    }
}
