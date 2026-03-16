using Xunit;

namespace coverutil.Tests;

public class ParsingTests
{
    [Theory]
    [InlineData("Artist - Title", "Artist", "Title")]
    [InlineData("A - B - C",      "A",      "B - C")]
    [InlineData("  Artist  -  Title  ", "Artist", "Title")]
    public void Parse_ValidInput_ReturnsParsedPair(string input, string expectedArtist, string expectedTitle)
    {
        var result = NowPlayingParser.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(expectedArtist, result.Value.artist);
        Assert.Equal(expectedTitle,  result.Value.title);
    }

    [Theory]
    [InlineData("NoSeparator")]
    [InlineData("")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(NowPlayingParser.Parse(input));
    }
}
