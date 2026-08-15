using System.Text;

namespace QrCatalog.Application.Common;

/// <summary>
/// Ad → URL slug. Azərbaycan hərfləri latın ASCII-yə transliterasiya olunur:
/// "Şezlonq və çətirlər" → "sezlonq-ve-cetirler".
/// </summary>
public static class SlugGenerator
{
    private static readonly Dictionary<char, string> Map = new()
    {
        ['ə'] = "e", ['Ə'] = "e",
        ['ı'] = "i", ['I'] = "i", ['İ'] = "i",
        ['ö'] = "o", ['Ö'] = "o",
        ['ü'] = "u", ['Ü'] = "u",
        ['ş'] = "s", ['Ş'] = "s",
        ['ç'] = "c", ['Ç'] = "c",
        ['ğ'] = "g", ['Ğ'] = "g",
    };

    public static string FromName(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastWasDash = true; // əvvəldəki defisləri udmaq üçün

        foreach (var raw in name.Trim())
        {
            var ch = raw;
            if (Map.TryGetValue(ch, out var mapped))
            {
                sb.Append(mapped);
                lastWasDash = false;
                continue;
            }

            ch = char.ToLowerInvariant(ch);
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        // sondakı defisi at
        while (sb.Length > 0 && sb[^1] == '-')
            sb.Length--;

        return sb.Length == 0 ? "kateqoriya" : sb.ToString();
    }
}
