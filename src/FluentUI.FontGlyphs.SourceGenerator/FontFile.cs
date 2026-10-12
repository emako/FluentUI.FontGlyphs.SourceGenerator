using System;
using System.Collections.Generic;
using System.IO;

namespace FluentUI.FontGlyphs.SourceGenerator;

internal readonly struct GlyphMapping(uint codepoint, string name)
{
    public uint Codepoint { get; } = codepoint;

    public string Name { get; } = name;
}

internal static class FontFile
{
    public static List<GlyphMapping> Read(byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length < 12)
        {
            throw new InvalidDataException("The font file is too small to be a TrueType font.");
        }

        Reader reader = new Reader(data);
        reader.U32();
        ushort tableCount = reader.U16();
        reader.Skip(6);

        uint cmapOffset = 0;
        uint cmapLength = 0;
        uint postOffset = 0;
        uint postLength = 0;
        bool foundCmap = false;
        bool foundPost = false;

        for (int i = 0; i < tableCount; i++)
        {
            string tag = reader.ReadTag();
            reader.U32();
            uint offset = reader.U32();
            uint length = reader.U32();
            if (tag == "cmap")
            {
                cmapOffset = offset;
                cmapLength = length;
                foundCmap = true;
            }
            else if (tag == "post")
            {
                postOffset = offset;
                postLength = length;
                foundPost = true;
            }
        }

        if (!foundCmap || !foundPost)
        {
            throw new InvalidDataException("The font is missing a cmap or post table.");
        }

        string[]? names = ReadPostNames(data, postOffset, postLength);
        SortedDictionary<uint, int> mappings = ReadCmap(data, cmapOffset, cmapLength);
        List<GlyphMapping> result = new List<GlyphMapping>(mappings.Count);
        foreach (KeyValuePair<uint, int> pair in mappings)
        {
            string name;
            if (names == null)
            {
                if (pair.Key < 0x21)
                {
                    continue;
                }

                name = UnicodeGlyphName(pair.Key);
            }
            else
            {
                if (pair.Value <= 0 || pair.Value >= names.Length)
                {
                    continue;
                }

                name = names[pair.Value];
                if (string.IsNullOrEmpty(name) || name[0] == '.')
                {
                    continue;
                }
            }

            result.Add(new GlyphMapping(pair.Key, name));
        }

        result.Sort(static (left, right) => left.Codepoint.CompareTo(right.Codepoint));
        return result;
    }

    private static string UnicodeGlyphName(uint codepoint)
    {
        return codepoint <= 0xFFFF
            ? "uni" + codepoint.ToString("X4")
            : "u" + codepoint.ToString("X");
    }

    /// <summary>
    /// Format 2 and 2.5 store PostScript names. Format 3 omits them; callers then name glyphs from Unicode scalars.
    /// </summary>
    private static string[]? ReadPostNames(byte[] data, uint offset, uint length)
    {
        Reader reader = new Reader(data, (int)offset);
        int end = checked((int)offset + (int)length);
        uint format = reader.U32();
        if (format == 0x00030000)
        {
            return null;
        }

        if (format != 0x00020000 && format != 0x00025000)
        {
            throw new InvalidDataException("Only post table format 2 fonts expose glyph names. This font uses format 0x" + format.ToString("X8") + ".");
        }

        reader.Seek((int)offset + 32);
        int glyphCount = reader.U16();
        ushort[] indexes = new ushort[glyphCount];
        int maxIndex = 257;
        for (int i = 0; i < glyphCount; i++)
        {
            indexes[i] = reader.U16();
            if (indexes[i] > maxIndex)
            {
                maxIndex = indexes[i];
            }
        }

        int customCount = maxIndex - 257;
        string[] custom = new string[customCount];
        for (int i = 0; i < customCount; i++)
        {
            if (reader.Position >= end)
            {
                throw new InvalidDataException("The post table ended before all glyph names were read.");
            }

            byte count = reader.U8();
            custom[i] = reader.ReadAscii(count);
        }

        string[] names = new string[glyphCount];
        for (int i = 0; i < glyphCount; i++)
        {
            int index = indexes[i];
            if (index < MacintoshGlyphNames.Names.Length)
            {
                names[i] = MacintoshGlyphNames.Names[index];
            }
            else
            {
                int customIndex = index - 258;
                names[i] = customIndex >= 0 && customIndex < custom.Length ? custom[customIndex] : string.Empty;
            }
        }

        return names;
    }

    private static SortedDictionary<uint, int> ReadCmap(byte[] data, uint offset, uint length)
    {
        Reader reader = new Reader(data, (int)offset);
        int tableEnd = checked((int)offset + (int)length);
        reader.U16();
        int subtableCount = reader.U16();
        List<CmapSubtable> subtables = new List<CmapSubtable>();
        for (int i = 0; i < subtableCount; i++)
        {
            int platform = reader.U16();
            int encoding = reader.U16();
            uint subOffset = reader.U32();
            int absolute = checked((int)offset + (int)subOffset);
            if (absolute + 2 > data.Length)
            {
                continue;
            }

            int format = (data[absolute] << 8) | data[absolute + 1];
            if (format == 4 || format == 12)
            {
                subtables.Add(new CmapSubtable(platform, encoding, format, absolute));
            }
        }

        if (subtables.Count == 0)
        {
            throw new InvalidDataException("The cmap table has no Unicode format 4 or format 12 subtable.");
        }

        subtables.Sort(static (left, right) => Score(right).CompareTo(Score(left)));
        CmapSubtable selected = subtables[0];
        return selected.Format == 12
            ? ReadFormat12(data, selected.Offset, tableEnd)
            : ReadFormat4(data, selected.Offset, tableEnd);
    }

    private static int Score(CmapSubtable subtable)
    {
        int score = subtable.Format == 12 ? 1000 : 0;
        if (subtable.PlatformId == 3 && (subtable.EncodingId == 10 || subtable.EncodingId == 1))
        {
            score += 100;
        }
        else if (subtable.PlatformId == 0)
        {
            score += 50;
        }

        return score;
    }

    private static SortedDictionary<uint, int> ReadFormat12(byte[] data, int offset, int tableEnd)
    {
        Reader reader = new Reader(data, offset);
        reader.U16();
        reader.U16();
        reader.U32();
        reader.U32();
        uint groupCount = reader.U32();
        SortedDictionary<uint, int> map = new SortedDictionary<uint, int>();
        for (uint i = 0; i < groupCount; i++)
        {
            if (reader.Position + 12 > data.Length)
            {
                throw new InvalidDataException("The cmap format 12 subtable is truncated.");
            }

            uint start = reader.U32();
            uint end = reader.U32();
            uint startGlyph = reader.U32();
            if (end < start)
            {
                continue;
            }

            uint count = end - start;
            for (uint delta = 0; delta <= count; delta++)
            {
                map[start + delta] = checked((int)(startGlyph + delta));
            }
        }

        return map;
    }

    private static SortedDictionary<uint, int> ReadFormat4(byte[] data, int offset, int tableEnd)
    {
        Reader reader = new Reader(data, offset);
        reader.U16();
        int subtableLength = reader.U16();
        reader.U16();
        int segCount = reader.U16() / 2;
        reader.Skip(6);
        int[] endCode = new int[segCount];
        for (int i = 0; i < segCount; i++)
        {
            endCode[i] = reader.U16();
        }

        reader.U16();
        int[] startCode = new int[segCount];
        for (int i = 0; i < segCount; i++)
        {
            startCode[i] = reader.U16();
        }

        int[] idDelta = new int[segCount];
        for (int i = 0; i < segCount; i++)
        {
            idDelta[i] = reader.I16();
        }

        int idRangeOffsetPosition = reader.Position;
        int[] idRangeOffset = new int[segCount];
        for (int i = 0; i < segCount; i++)
        {
            idRangeOffset[i] = reader.U16();
        }

        int limit = Math.Min(data.Length, offset + subtableLength);
        if (limit > tableEnd)
        {
            limit = tableEnd;
        }

        int wordCount = Math.Max(0, (limit - idRangeOffsetPosition) / 2);
        int[] words = new int[wordCount];
        reader.Seek(idRangeOffsetPosition);
        for (int i = 0; i < wordCount; i++)
        {
            words[i] = reader.U16();
        }

        SortedDictionary<uint, int> map = new SortedDictionary<uint, int>();
        for (int segment = 0; segment < segCount; segment++)
        {
            if (startCode[segment] == 0xFFFF)
            {
                continue;
            }

            for (int code = startCode[segment]; code <= endCode[segment]; code++)
            {
                int glyphId;
                if (idRangeOffset[segment] == 0)
                {
                    glyphId = (code + idDelta[segment]) & 0xFFFF;
                }
                else
                {
                    int wordIndex = segment + (idRangeOffset[segment] / 2) + (code - startCode[segment]);
                    if (wordIndex < 0 || wordIndex >= words.Length)
                    {
                        continue;
                    }

                    glyphId = words[wordIndex];
                    if (glyphId != 0)
                    {
                        glyphId = (glyphId + idDelta[segment]) & 0xFFFF;
                    }
                }

                if (glyphId != 0)
                {
                    map[(uint)code] = glyphId;
                }
            }
        }

        return map;
    }

    private readonly struct CmapSubtable
    {
        public CmapSubtable(int platformId, int encodingId, int format, int offset)
        {
            PlatformId = platformId;
            EncodingId = encodingId;
            Format = format;
            Offset = offset;
        }

        public int PlatformId { get; }

        public int EncodingId { get; }

        public int Format { get; }

        public int Offset { get; }
    }

    private sealed class Reader
    {
        private readonly byte[] _data;
        private int _position;

        public Reader(byte[] data, int position = 0)
        {
            _data = data;
            _position = position;
        }

        public int Position => _position;

        public void Seek(int position)
        {
            _position = position;
        }

        public void Skip(int count)
        {
            _position += count;
        }

        public byte U8()
        {
            return _data[_position++];
        }

        public ushort U16()
        {
            ushort value = (ushort)((_data[_position] << 8) | _data[_position + 1]);
            _position += 2;
            return value;
        }

        public short I16()
        {
            return (short)U16();
        }

        public uint U32()
        {
            uint value = ((uint)_data[_position] << 24)
                | ((uint)_data[_position + 1] << 16)
                | ((uint)_data[_position + 2] << 8)
                | _data[_position + 3];
            _position += 4;
            return value;
        }

        public string ReadTag()
        {
            string tag = ReadAscii(4);
            return tag;
        }

        public string ReadAscii(int count)
        {
            char[] chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                chars[i] = (char)_data[_position++];
            }

            return new string(chars);
        }
    }
}
