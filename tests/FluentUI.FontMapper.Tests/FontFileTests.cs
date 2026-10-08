namespace FluentUI.FontMapper.Tests;

public class FontFileTests
{
    [Fact]
    public void Reads_format12_glyph_names()
    {
        var font = TestFont.Create(
        [
            (0xE010, "ic_fluent_add_20_regular"),
            (0xE011, "ic_fluent_home_24_filled"),
            (0x000F0BC4, "ic_fluent_zoom_20_light")
        ]);

        var glyphs = FontFile.Read(font);

        Assert.Equal(
            new[]
            {
                (0xE010u, "ic_fluent_add_20_regular"),
                (0xE011u, "ic_fluent_home_24_filled"),
                (0x000F0BC4u, "ic_fluent_zoom_20_light")
            },
            glyphs.Select(static glyph => (glyph.Codepoint, glyph.Name)).ToArray());
    }

    [Fact]
    public void Reads_format4_range_offsets()
    {
        var font = TestFont.Create(
            [
                (0xE100, "ic_fluent_save_20_regular"),
                (0xE250, "ic_fluent_open_20_regular")
            ],
            CmapKind.Format4RangeOffset);

        var glyphs = FontFile.Read(font);

        Assert.Equal("ic_fluent_save_20_regular", Assert.Single(glyphs, glyph => glyph.Codepoint == 0xE100).Name);
        Assert.Equal("ic_fluent_open_20_regular", Assert.Single(glyphs, glyph => glyph.Codepoint == 0xE250).Name);
    }

    [Fact]
    public void Reads_format4_id_delta()
    {
        var font = TestFont.Create(
            [
                (0xE001, "ic_fluent_add_20_regular"),
                (0xE002, "ic_fluent_home_20_regular")
            ],
            CmapKind.Format4IdDelta);

        var glyphs = FontFile.Read(font);

        Assert.Equal(new[] { "ic_fluent_add_20_regular", "ic_fluent_home_20_regular" }, glyphs.Select(static glyph => glyph.Name).ToArray());
        Assert.Equal(new uint[] { 0xE001, 0xE002 }, glyphs.Select(static glyph => glyph.Codepoint).ToArray());
    }

    [Fact]
    public void Skips_notdef_and_unmapped_names()
    {
        var font = TestFont.Create([(0x20, "space")]);
        var withEmpty = TestFont.Create([(0xE001, ".notdef"), (0xE002, "ic_fluent_add_20_regular")]);

        Assert.DoesNotContain(FontFile.Read(font), static glyph => glyph.Name == ".notdef");
        Assert.Equal("ic_fluent_add_20_regular", Assert.Single(FontFile.Read(withEmpty)).Name);
    }

    [Fact]
    public void Reads_fluent_system_icons_regular()
    {
        var path = Environment.GetEnvironmentVariable("FLUENT_SYSTEM_ICONS_TTF")
            ?? @"D:\GitHub\FluentUI.SystemIcons.Assets\fluentui-system-icons\fonts\FluentSystemIcons-Regular.ttf";
        Assert.True(File.Exists(path), "Fluent System Icons Regular was not found at " + path);

        var glyphs = FontFile.Read(File.ReadAllBytes(path));

        Assert.Contains(glyphs, glyph => glyph.Codepoint == 0xE000 && glyph.Name == "ic_fluent_access_time_20_regular");
        Assert.Contains(glyphs, glyph => glyph.Codepoint == 0xE001 && glyph.Name == "ic_fluent_accessibility_32_regular");
        Assert.Contains(glyphs, glyph => glyph.Codepoint == 0xF0BC4);
        Assert.True(glyphs.Count > 9000);
    }
}
