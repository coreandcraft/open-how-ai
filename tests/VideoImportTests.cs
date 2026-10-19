using System.Text;
using Journey.Service.Features.Imports;

namespace Journey.Tests;

public class VideoImportTests
{
    [Theory]
    [InlineData("mp4", "000000186674797069736F6D0000000069736F326D703431")]
    [InlineData("m4v", "00000010667479704D34562000000000")]
    [InlineData("mov", "00000010667479707174202000000000")]
    [InlineData("webm", "1A45DFA3874282847765626D")]
    [InlineData("mkv", "1A45DFA38B4282886D6174726F736B61")]
    [InlineData("avi", "524946460400000041564920")]
    // An unknown major brand with a supported compatible brand is also recognized.
    [InlineData("mp4", "000000146674797078787878000000006D703432")]
    public async Task VideosAreAttachmentsEvenWithoutFilenameExtensions(string extension, string hex)
    {
        var bytes = Convert.FromHexString(hex);
        var registry = new ImportRegistry();
        foreach (var name in new[] { "folder/preview." + extension, "folder/unnamed" })
        {
            var input = new SourceInput(name, bytes);
            var adapter = registry.Detect(input);
            Assert.IsType<PrivateAssetImporter>(adapter);
            Assert.Contains("." + extension, adapter.Capability.Extensions);
            Assert.Empty((await adapter.PreviewAsync(input, default)).Conversations);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0000000866747970")]
    [InlineData("000001006674797069736F6D00000000")]
    [InlineData("00000010667479706176696600000000")]
    [InlineData("1A45DFA3")]
    [InlineData("1A45DFA387428284776562")]
    [InlineData("1A45DFA38742828478787878")]
    [InlineData("1A45DFA38700000000000000")]
    [InlineData("524946460400000078787878")]
    public void InvalidOrUnrelatedHeadersAreNotVideos(string hex) =>
        Assert.False(PrivateAssetImporter.Recognized(Convert.FromHexString(hex)));

    [Fact]
    public void VideoExtensionAloneDoesNotIdentifyAnAttachment()
    {
        Assert.False(new PrivateAssetImporter().CanImport(
            new SourceInput("preview.mp4", Encoding.UTF8.GetBytes("not a video"))));
    }
}
