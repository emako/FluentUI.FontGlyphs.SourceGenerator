namespace FluentUI.FontMapper.Tests;

public class GlyphNamerTests
{
    [Theory]
    [InlineData("ic_fluent_access_time_20_regular", "AccessTime20")]
    [InlineData("ic_fluent_add_20_filled", "Add20")]
    [InlineData("ic_fluent_zoom_fit_24_light", "ZoomFit24")]
    [InlineData("ic_fluent_arrow_upload_20_resizable", "ArrowUpload20")]
    [InlineData("custom-icon.name", "CustomIconName")]
    [InlineData("20_regular", "_20")]
    public void Formats_fluent_names(string raw, string expected)
    {
        Assert.Equal(expected, GlyphNamer.ToIdentifier(raw));
    }

    [Fact]
    public void Disambiguates_duplicate_identifiers()
    {
        var named = GlyphNamer.Name(
        [
            new GlyphMapping(0xE010, "ic_fluent_add_20_regular"),
            new GlyphMapping(0xE011, "ic_fluent_add_20_regular")
        ]);

        Assert.Equal("Add20", named[0].Identifier);
        Assert.Equal("Add20_E011", named[1].Identifier);
    }
}
