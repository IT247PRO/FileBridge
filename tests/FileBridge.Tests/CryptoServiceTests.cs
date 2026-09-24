using FileBridge.Infrastructure.Crypto;
using Xunit;

namespace FileBridge.Tests;

public class CryptoServiceTests
{
    [Fact]
    public async Task AesRoundTripsAndDetectsTamper()
    {
        var key = CryptoService.GenerateAesKey();
        var dir = Directory.CreateTempSubdirectory("fbtest");
        try
        {
            var input = Path.Combine(dir.FullName, "in.txt");
            var enc = Path.Combine(dir.FullName, "enc.aes");
            var dec = Path.Combine(dir.FullName, "dec.txt");
            var original = "The quick brown fox jumps over the lazy dog. 0123456789.";
            await File.WriteAllTextAsync(input, original);

            await CryptoService.AesEncryptAsync(key, input, enc, CancellationToken.None);
            await CryptoService.AesDecryptAsync(key, enc, dec, CancellationToken.None);
            Assert.Equal(original, await File.ReadAllTextAsync(dec));

            // Flip a byte in the ciphertext body (well past the header) and confirm tampering is detected.
            var bytes = await File.ReadAllBytesAsync(enc);
            bytes[bytes.Length - 40] ^= 0xFF;
            await File.WriteAllBytesAsync(enc, bytes);
            await Assert.ThrowsAsync<FileBridge.Core.QuarantineException>(() =>
                CryptoService.AesDecryptAsync(key, enc, dec, CancellationToken.None));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void GeneratedKeyIs64Bytes()
    {
        var key = Convert.FromBase64String(CryptoService.GenerateAesKey());
        Assert.Equal(64, key.Length);
    }
}
