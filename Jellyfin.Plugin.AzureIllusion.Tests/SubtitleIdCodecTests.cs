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

    [Fact]
    public void Decode_AcceptsJellyfinProviderPrefixedIdentifier()
    {
        var source = new SubtitleIdPayload("release-2", "series/s01e01.mkv", "pl", "srt", "def456");
        var jellyfinId = $"0123456789abcdef0123456789abcdef_{SubtitleIdCodec.Encode(source)}";

        var decoded = SubtitleIdCodec.Decode(jellyfinId);

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
