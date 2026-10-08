using System.Text;

namespace FluentUI.FontMapper.Tests;

internal enum CmapKind
{
    Format12,
    Format4RangeOffset,
    Format4IdDelta
}

internal static class TestFont
{
    public static byte[] Create(IReadOnlyList<(uint Codepoint, string Name)> glyphs, CmapKind kind = CmapKind.Format12)
    {
        var cmap = kind switch
        {
            CmapKind.Format12 => Format12(glyphs),
            CmapKind.Format4RangeOffset => Format4RangeOffset(glyphs),
            CmapKind.Format4IdDelta => Format4IdDelta(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var post = Post(glyphs.Select(static glyph => glyph.Name).ToArray());
        return Package(cmap, post);
    }

    private static byte[] Package(byte[] cmap, byte[] post)
    {
        cmap = Align(cmap);
        post = Align(post);
        var header = 12 + (16 * 2);
        var cmapOffset = header;
        var postOffset = header + cmap.Length;
        using var stream = new MemoryStream();
        WriteU32(stream, 0x00010000);
        WriteU16(stream, 2);
        WriteU16(stream, 32);
        WriteU16(stream, 1);
        WriteU16(stream, 0);
        WriteTable(stream, "cmap", cmapOffset, cmap.Length);
        WriteTable(stream, "post", postOffset, post.Length);
        stream.Write(cmap);
        stream.Write(post);
        return stream.ToArray();
    }

    private static void WriteTable(Stream stream, string tag, int offset, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(tag);
        stream.Write(bytes);
        WriteU32(stream, 0);
        WriteU32(stream, (uint)offset);
        WriteU32(stream, (uint)length);
    }

    private static byte[] Post(string[] names)
    {
        using var stream = new MemoryStream();
        WriteU32(stream, 0x00020000);
        WriteU32(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU32(stream, 0);
        WriteU32(stream, 0);
        WriteU32(stream, 0);
        WriteU32(stream, 0);
        WriteU32(stream, 0);
        WriteU16(stream, (ushort)(names.Length + 1));
        WriteU16(stream, 0);
        for (var i = 0; i < names.Length; i++)
        {
            WriteU16(stream, (ushort)(258 + i));
        }

        foreach (var name in names)
        {
            var bytes = Encoding.ASCII.GetBytes(name);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        return stream.ToArray();
    }

    private static byte[] Format12(IReadOnlyList<(uint Codepoint, string Name)> glyphs)
    {
        using var stream = new MemoryStream();
        var groups = glyphs.Count;
        var subtableLength = 16 + (groups * 12);
        WriteU16(stream, 12);
        WriteU16(stream, 0);
        WriteU32(stream, (uint)subtableLength);
        WriteU32(stream, 0);
        WriteU32(stream, (uint)groups);
        for (var i = 0; i < glyphs.Count; i++)
        {
            WriteU32(stream, glyphs[i].Codepoint);
            WriteU32(stream, glyphs[i].Codepoint);
            WriteU32(stream, (uint)(i + 1));
        }

        var subtable = stream.ToArray();
        using var cmap = new MemoryStream();
        WriteU16(cmap, 0);
        WriteU16(cmap, 1);
        WriteU16(cmap, 3);
        WriteU16(cmap, 10);
        WriteU32(cmap, 12);
        cmap.Write(subtable);
        return cmap.ToArray();
    }

    private static byte[] Format4RangeOffset(IReadOnlyList<(uint Codepoint, string Name)> glyphs)
    {
        if (glyphs.Any(static glyph => glyph.Codepoint > 0xFFFF))
        {
            throw new InvalidOperationException("Format 4 only covers the basic multilingual plane.");
        }

        var count = glyphs.Count;
        var segCount = count + 1;
        using var sub = new MemoryStream();
        var glyphArrayBytes = count * 2;
        var length = 14 + (segCount * 2) + 2 + (segCount * 2) + (segCount * 2) + (segCount * 2) + glyphArrayBytes;
        WriteU16(sub, 4);
        WriteU16(sub, (ushort)length);
        WriteU16(sub, 0);
        WriteU16(sub, (ushort)(segCount * 2));
        WriteU16(sub, 2);
        WriteU16(sub, 0);
        WriteU16(sub, (ushort)((segCount * 2) - 2));
        for (var i = 0; i < count; i++)
        {
            WriteU16(sub, (ushort)glyphs[i].Codepoint);
        }

        WriteU16(sub, 0xFFFF);
        WriteU16(sub, 0);
        for (var i = 0; i < count; i++)
        {
            WriteU16(sub, (ushort)glyphs[i].Codepoint);
        }

        WriteU16(sub, 0xFFFF);
        for (var i = 0; i < segCount; i++)
        {
            WriteU16(sub, 0);
        }

        var rangeOffset = (ushort)(segCount * 2);
        for (var i = 0; i < count; i++)
        {
            WriteU16(sub, rangeOffset);
        }

        WriteU16(sub, 0);
        for (var i = 0; i < count; i++)
        {
            WriteU16(sub, (ushort)(i + 1));
        }

        return WrapFormat4(sub.ToArray());
    }

    private static byte[] Format4IdDelta()
    {
        using var sub = new MemoryStream();
        const int segCount = 2;
        const int length = 32;
        WriteU16(sub, 4);
        WriteU16(sub, length);
        WriteU16(sub, 0);
        WriteU16(sub, segCount * 2);
        WriteU16(sub, 4);
        WriteU16(sub, 1);
        WriteU16(sub, 0);
        WriteU16(sub, 0xE002);
        WriteU16(sub, 0xFFFF);
        WriteU16(sub, 0);
        WriteU16(sub, 0xE001);
        WriteU16(sub, 0xFFFF);
        WriteU16(sub, 0x2000);
        WriteU16(sub, 1);
        WriteU16(sub, 0);
        WriteU16(sub, 0);
        return WrapFormat4(sub.ToArray());
    }

    private static byte[] WrapFormat4(byte[] subtable)
    {
        using var cmap = new MemoryStream();
        WriteU16(cmap, 0);
        WriteU16(cmap, 2);
        WriteU16(cmap, 3);
        WriteU16(cmap, 1);
        WriteU32(cmap, 20);
        WriteU16(cmap, 0);
        WriteU16(cmap, 3);
        WriteU32(cmap, 20);
        cmap.Write(subtable);
        return cmap.ToArray();
    }

    private static byte[] Align(byte[] data)
    {
        var padding = (4 - (data.Length % 4)) % 4;
        if (padding == 0)
        {
            return data;
        }

        var aligned = new byte[data.Length + padding];
        data.CopyTo(aligned, 0);
        return aligned;
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteU16(Stream stream, int value) => WriteU16(stream, (ushort)value);

    private static void WriteU32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
