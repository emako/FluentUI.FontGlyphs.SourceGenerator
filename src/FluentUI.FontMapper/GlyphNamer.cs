using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FluentUI.FontMapper;

internal readonly struct NamedGlyph(string identifier, uint codepoint, string glyphName)
{
    public string Identifier { get; } = identifier;

    public uint Codepoint { get; } = codepoint;

    public string GlyphName { get; } = glyphName;
}

internal static class GlyphNamer
{
    private static readonly string[] Suffixes = ["_regular", "_filled", "_light", "_resizable"];

    private static readonly HashSet<string> Keywords =
    [
        with(StringComparer.Ordinal),
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];

    public static List<NamedGlyph> Name(IReadOnlyList<GlyphMapping> glyphs)
    {
        HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
        List<NamedGlyph> result = new List<NamedGlyph>(glyphs.Count);
        foreach (GlyphMapping glyph in glyphs)
        {
            string identifier = ToIdentifier(glyph.Name);
            if (!used.Add(identifier))
            {
                identifier = identifier + "_" + glyph.Codepoint.ToString("X", CultureInfo.InvariantCulture);
                int suffix = 2;
                while (!used.Add(identifier))
                {
                    identifier = ToIdentifier(glyph.Name) + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                    suffix++;
                }
            }

            result.Add(new NamedGlyph(identifier, glyph.Codepoint, glyph.Name));
        }

        return result;
    }

    public static string ToIdentifier(string glyphName)
    {
        string raw = glyphName ?? string.Empty;
        if (raw.StartsWith("ic_fluent_", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Substring("ic_fluent_".Length);
        }

        foreach (string suffix in Suffixes)
        {
            if (raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(0, raw.Length - suffix.Length);
                break;
            }
        }

        string[] parts = raw.Split(['_', '-', ' ', '.'], StringSplitOptions.RemoveEmptyEntries);
        StringBuilder builder = new StringBuilder();
        foreach (string part in parts)
        {
            StringBuilder cleaned = new StringBuilder();
            foreach (char character in part)
            {
                if (char.IsLetterOrDigit(character))
                {
                    cleaned.Append(character);
                }
            }

            if (cleaned.Length == 0)
            {
                continue;
            }

            cleaned[0] = char.ToUpperInvariant(cleaned[0]);
            builder.Append(cleaned);
        }

        string name = builder.Length == 0 ? "Glyph" : builder.ToString();
        if (char.IsDigit(name[0]))
        {
            name = "_" + name;
        }

        if (Keywords.Contains(name))
        {
            name = "@" + name;
        }

        return name;
    }
}
