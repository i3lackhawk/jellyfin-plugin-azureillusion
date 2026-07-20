using Jellyfin.Plugin.AzureIllusion.Subtitles;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class SubtitleIdCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTripsAllDownloadData()
    {
        var source = new SubtitleIdPayload("release-1", "series/s01e00.mkv", "pl2", "ass", "abc123");

        var decoded = SubtitleIdCodec.Decode(SubtitleIdCodec.Encode(source));

        Assert.Equal(source, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("niepoprawny-identyfikator")]
    public void Decode_RejectsInvalidPayload(string value)
    {
        Assert.Throws<FormatException>(() => SubtitleIdCodec.Decode(value));
    }
}
