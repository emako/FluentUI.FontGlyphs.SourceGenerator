namespace FluentUI.FontGlyphs.SourceGenerator.Tests;

public class FontFileTests
{
    [Fact]
    public void Reads_fontawesome_glyph_names()
    {
        List<GlyphMapping> glyphs = FontFile.Read(File.ReadAllBytes(TestFonts.FontAwesome));

        Assert.Equal("heart", Assert.Single(glyphs, glyph => glyph.Codepoint == 0xF004).Name);
        Assert.Equal("search", Assert.Single(glyphs, glyph => glyph.Codepoint == 0xF002).Name);
        Assert.Equal("star_empty", Assert.Single(glyphs, glyph => glyph.Codepoint == 0xF006).Name);
        Assert.Equal("uniF2EE", Assert.Single(glyphs, glyph => glyph.Codepoint == 0xF2EE).Name);
        Assert.DoesNotContain(glyphs, static glyph => glyph.Name.StartsWith(".", StringComparison.Ordinal));
        Assert.True(glyphs.Count > 600);
    }

    [Fact]
    public void Segoe_fluent_icons_falls_back_to_unicode_names()
    {
        List<GlyphMapping> glyphs = FontFile.Read(File.ReadAllBytes(TestFonts.SegoeFluentIcons));

        Assert.Contains(glyphs, glyph => glyph.Name == "uniE700");
        Assert.DoesNotContain(glyphs, glyph => glyph.Codepoint < 0x21);
        Assert.True(glyphs.Count > 1000);
    }
}
