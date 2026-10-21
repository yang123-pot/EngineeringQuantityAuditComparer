using System.Globalization;
using System.Text;

namespace EngineeringQuantityAuditComparer.Core;

public static class TextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (!char.IsWhiteSpace(character) && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)
            {
                builder.Append(character switch
                {
                    '，' => ',',
                    '。' => '.',
                    '：' => ':',
                    '；' => ';',
                    '（' => '(',
                    '）' => ')',
                    '【' => '[',
                    '】' => ']',
                    _ => character
                });
            }
        }

        return builder.ToString().Trim().ToUpperInvariant();
    }

    public static string CompositeKey(BillItem item) => string.Join('|', Normalize(item.Name), Normalize(item.Feature), Normalize(item.Unit));
}
