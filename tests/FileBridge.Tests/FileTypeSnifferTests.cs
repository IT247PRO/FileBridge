using System.Text;
using FileBridge.Core.Rules;
using Xunit;

namespace FileBridge.Tests;

public class FileTypeSnifferTests
{
    [Fact]
    public void DetectsPdf() => Assert.Equal("pdf", FileTypeSniffer.Detect("%PDF-1.4"u8));

    [Fact]
    public void DetectsZip() => Assert.Equal("zip", FileTypeSniffer.Detect(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0 }));

    [Fact]
    public void DetectsExecutableAsBinary() => Assert.Equal("executable", FileTypeSniffer.Detect("MZ\x90\x00"u8));

    [Fact]
    public void DetectsCsvAsText() => Assert.Equal("csv", FileTypeSniffer.Detect(Encoding.UTF8.GetBytes("a,b,c\n1,2,3")));

    [Fact]
    public void DetectsJson() => Assert.Equal("json", FileTypeSniffer.Detect(Encoding.UTF8.GetBytes("{\"a\":1}")));

    [Theory]
    [InlineData("csv", "text", true)]
    [InlineData("json", "text", true)]
    [InlineData("executable", "text", false)]
    [InlineData("pdf", "pdf,zip", true)]
    [InlineData("zip", "pdf", false)]
    public void IsAllowedRespectsTextUmbrella(string detected, string allowed, bool expected) =>
        Assert.Equal(expected, FileTypeSniffer.IsAllowed(detected, allowed));
}
