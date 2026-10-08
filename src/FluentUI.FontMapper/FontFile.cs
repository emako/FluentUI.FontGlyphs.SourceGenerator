using System;
using System.Collections.Generic;
using System.IO;

namespace FluentUI.FontMapper;

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

        var reader = new Reader(data);
        reader.U32();
        var tableCount = reader.U16();
        reader.Skip(6);

        uint cmapOffset = 0;
        uint cmapLength = 0;
        uint postOffset = 0;
        uint postLength = 0;
        var foundCmap = false;
        var foundPost = false;

        for (var i = 0; i < tableCount; i++)
        {
            var tag = reader.ReadTag();
            reader.U32();
            var offset = reader.U32();
            var length = reader.U32();
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

        var names = ReadPostNames(data, postOffset, postLength);
        var mappings = ReadCmap(data, cmapOffset, cmapLength);
        var result = new List<GlyphMapping>(mappings.Count);
        foreach (var pair in mappings)
        {
            if (pair.Value <= 0 || pair.Value >= names.Length)
            {
                continue;
            }

            var name = names[pair.Value];
            if (string.IsNullOrEmpty(name) || name[0] == '.')
            {
                continue;
            }

            result.Add(new GlyphMapping(pair.Key, name));
        }

        result.Sort(static (left, right) => left.Codepoint.CompareTo(right.Codepoint));
        return result;
    }

    private static string[] ReadPostNames(byte[] data, uint offset, uint length)
    {
        var reader = new Reader(data, (int)offset);
        var end = checked((int)offset + (int)length);
        var format = reader.U32();
        if (format != 0x00020000 && format != 0x00025000)
        {
            throw new InvalidDataException("Only post table format 2 fonts expose glyph names. This font uses format 0x" + format.ToString("X8") + ".");
        }

        reader.Seek((int)offset + 32);
        var glyphCount = reader.U16();
        var indexes = new ushort[glyphCount];
        var maxIndex = 257;
        for (var i = 0; i < glyphCount; i++)
        {
            indexes[i] = reader.U16();
            if (indexes[i] > maxIndex)
            {
                maxIndex = indexes[i];
            }
        }

        var customCount = maxIndex - 257;
        var custom = new string[customCount];
        for (var i = 0; i < customCount; i++)
        {
            if (reader.Position >= end)
            {
                throw new InvalidDataException("The post table ended before all glyph names were read.");
            }

            var count = reader.U8();
            custom[i] = reader.ReadAscii(count);
        }

        var names = new string[glyphCount];
        for (var i = 0; i < glyphCount; i++)
        {
            var index = indexes[i];
            if (index < MacintoshGlyphNames.Names.Length)
            {
                names[i] = MacintoshGlyphNames.Names[index];
            }
            else
            {
                var customIndex = index - 258;
                names[i] = customIndex >= 0 && customIndex < custom.Length ? custom[customIndex] : string.Empty;
            }
        }

        return names;
    }

    private static SortedDictionary<uint, int> ReadCmap(byte[] data, uint offset, uint length)
    {
        var reader = new Reader(data, (int)offset);
        var tableEnd = checked((int)offset + (int)length);
        reader.U16();
        var subtableCount = reader.U16();
        var subtables = new List<CmapSubtable>();
        for (var i = 0; i < subtableCount; i++)
        {
            var platform = reader.U16();
            var encoding = reader.U16();
            var subOffset = reader.U32();
            var absolute = checked((int)offset + (int)subOffset);
            if (absolute + 2 > data.Length)
            {
                continue;
            }

            var format = (data[absolute] << 8) | data[absolute + 1];
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
        var selected = subtables[0];
        return selected.Format == 12
            ? ReadFormat12(data, selected.Offset, tableEnd)
            : ReadFormat4(data, selected.Offset, tableEnd);
    }

    private static int Score(CmapSubtable subtable)
    {
        var score = subtable.Format == 12 ? 1000 : 0;
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
        var reader = new Reader(data, offset);
        reader.U16();
        reader.U16();
        reader.U32();
        reader.U32();
        var groupCount = reader.U32();
        var map = new SortedDictionary<uint, int>();
        for (uint i = 0; i < groupCount; i++)
        {
            if (reader.Position + 12 > data.Length)
            {
                throw new InvalidDataException("The cmap format 12 subtable is truncated.");
            }

            var start = reader.U32();
            var end = reader.U32();
            var startGlyph = reader.U32();
            if (end < start)
            {
                continue;
            }

            var count = end - start;
            for (uint delta = 0; delta <= count; delta++)
            {
                map[start + delta] = checked((int)(startGlyph + delta));
            }
        }

        return map;
    }

    private static SortedDictionary<uint, int> ReadFormat4(byte[] data, int offset, int tableEnd)
    {
        var reader = new Reader(data, offset);
        reader.U16();
        var subtableLength = reader.U16();
        reader.U16();
        var segCount = reader.U16() / 2;
        reader.Skip(6);
        var endCode = new int[segCount];
        for (var i = 0; i < segCount; i++)
        {
            endCode[i] = reader.U16();
        }

        reader.U16();
        var startCode = new int[segCount];
        for (var i = 0; i < segCount; i++)
        {
            startCode[i] = reader.U16();
        }

        var idDelta = new int[segCount];
        for (var i = 0; i < segCount; i++)
        {
            idDelta[i] = reader.I16();
        }

        var idRangeOffsetPosition = reader.Position;
        var idRangeOffset = new int[segCount];
        for (var i = 0; i < segCount; i++)
        {
            idRangeOffset[i] = reader.U16();
        }

        var limit = Math.Min(data.Length, offset + subtableLength);
        if (limit > tableEnd)
        {
            limit = tableEnd;
        }

        var wordCount = Math.Max(0, (limit - idRangeOffsetPosition) / 2);
        var words = new int[wordCount];
        reader.Seek(idRangeOffsetPosition);
        for (var i = 0; i < wordCount; i++)
        {
            words[i] = reader.U16();
        }

        var map = new SortedDictionary<uint, int>();
        for (var segment = 0; segment < segCount; segment++)
        {
            if (startCode[segment] == 0xFFFF)
            {
                continue;
            }

            for (var code = startCode[segment]; code <= endCode[segment]; code++)
            {
                int glyphId;
                if (idRangeOffset[segment] == 0)
                {
                    glyphId = (code + idDelta[segment]) & 0xFFFF;
                }
                else
                {
                    var wordIndex = segment + (idRangeOffset[segment] / 2) + (code - startCode[segment]);
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
            var value = (ushort)((_data[_position] << 8) | _data[_position + 1]);
            _position += 2;
            return value;
        }

        public short I16()
        {
            return (short)U16();
        }

        public uint U32()
        {
            var value = ((uint)_data[_position] << 24)
                | ((uint)_data[_position + 1] << 16)
                | ((uint)_data[_position + 2] << 8)
                | _data[_position + 3];
            _position += 4;
            return value;
        }

        public string ReadTag()
        {
            var tag = ReadAscii(4);
            return tag;
        }

        public string ReadAscii(int count)
        {
            var chars = new char[count];
            for (var i = 0; i < count; i++)
            {
                chars[i] = (char)_data[_position++];
            }

            return new string(chars);
        }
    }
}
