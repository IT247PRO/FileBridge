using System.Security.Cryptography;
using FileBridge.Core;
using FileBridge.Core.Entities;
using PgpCore;

namespace FileBridge.Infrastructure.Crypto;

/// <summary>
/// PGP (partner-standard) via PgpCore/BouncyCastle, plus an authenticated AES-256 format for internal hops:
///   "FBA1" | IV(16) | AES-256-CBC ciphertext | HMAC-SHA256(32) over everything before it.
/// AES key material is 64 random bytes (32 encryption + 32 MAC), stored base64 and protected.
/// </summary>
public sealed class CryptoService(ISecretProtector secrets) : ICryptoService
{
    private static readonly byte[] Magic = "FBA1"u8.ToArray();
    private static readonly string[] EncryptedExtensions = [".pgp", ".gpg", ".asc", ".aes"];

    public static bool IsDecrypt(EncryptionOperation op) =>
        op is EncryptionOperation.PgpDecrypt or EncryptionOperation.PgpDecryptAndVerify or EncryptionOperation.AesDecrypt;

    public static bool IsEncrypt(EncryptionOperation op) =>
        op is EncryptionOperation.PgpEncrypt or EncryptionOperation.PgpEncryptAndSign or EncryptionOperation.AesEncrypt;

    public static string OutputName(EncryptionProfile p, string fileName)
    {
        if (IsEncrypt(p.EncryptionOperationId))
            return fileName + (string.IsNullOrWhiteSpace(p.OutputExtension)
                ? (p.EncryptionOperationId == EncryptionOperation.AesEncrypt ? ".aes" : ".pgp")
                : p.OutputExtension);
        if (IsDecrypt(p.EncryptionOperationId) && p.StripExtensionOnDecrypt
            && EncryptedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(fileName);
        return fileName;
    }

    public async Task TransformAsync(EncryptionProfile p, string input, string output, CancellationToken ct)
    {
        string Req(string? v, string what) => secrets.Unprotect(v) ?? throw new NonRetryableException($"Encryption profile '{p.Name}' is missing its {what}.");

        switch (p.EncryptionOperationId)
        {
            case EncryptionOperation.PgpEncrypt:
                await PgpAsync(new PGP(new EncryptionKeys(Req(p.ProtectedPublicKey, "public key"))),
                    (pgp, i, o) => pgp.EncryptAsync(i, o, p.ArmorOutput, true), input, output);
                break;
            case EncryptionOperation.PgpEncryptAndSign:
                await PgpAsync(new PGP(new EncryptionKeys(Req(p.ProtectedPublicKey, "public key"), Req(p.ProtectedPrivateKey, "private key"), secrets.Unprotect(p.ProtectedPassphrase) ?? "")),
                    (pgp, i, o) => pgp.EncryptAndSignAsync(i, o, p.ArmorOutput, true), input, output);
                break;
            case EncryptionOperation.PgpDecrypt:
                await PgpAsync(new PGP(new EncryptionKeys(Req(p.ProtectedPrivateKey, "private key"), secrets.Unprotect(p.ProtectedPassphrase) ?? "")),
                    (pgp, i, o) => pgp.DecryptAsync(i, o), input, output);
                break;
            case EncryptionOperation.PgpDecryptAndVerify:
                await PgpAsync(new PGP(new EncryptionKeys(Req(p.ProtectedPublicKey, "partner public key"), Req(p.ProtectedPrivateKey, "private key"), secrets.Unprotect(p.ProtectedPassphrase) ?? "")),
                    (pgp, i, o) => pgp.DecryptAndVerifyAsync(i, o), input, output);
                break;
            case EncryptionOperation.AesEncrypt:
                await AesEncryptAsync(Req(p.ProtectedAesKey, "AES key"), input, output, ct);
                break;
            case EncryptionOperation.AesDecrypt:
                await AesDecryptAsync(Req(p.ProtectedAesKey, "AES key"), input, output, ct);
                break;
            default:
                File.Copy(input, output, overwrite: true);
                break;
        }
    }

    private static async Task PgpAsync(PGP pgp, Func<PGP, Stream, Stream, Task> op, string input, string output)
    {
        try
        {
            await using var i = File.OpenRead(input);
            await using var o = File.Create(output);
            await op(pgp, i, o);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bad key, tampered file or failed signature: the content is suspect, so hold it instead of retrying.
            throw new QuarantineException($"PGP operation failed: {ex.Message}");
        }
    }

    public static string GenerateAesKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static (byte[] Enc, byte[] Mac) SplitKey(string base64)
    {
        var k = Convert.FromBase64String(base64);
        if (k.Length != 64) throw new NonRetryableException("AES key must be 64 bytes (base64).");
        return (k[..32], k[32..]);
    }

    internal static async Task AesEncryptAsync(string key, string input, string output, CancellationToken ct)
    {
        var (ek, mk) = SplitKey(key);
        using var aes = Aes.Create();
        aes.Key = ek;
        aes.GenerateIV();
        await using (var o = File.Create(output))
        {
            await o.WriteAsync(Magic, ct);
            await o.WriteAsync(aes.IV, ct);
            await using var cs = new CryptoStream(o, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
            await using (var i = File.OpenRead(input)) await i.CopyToAsync(cs, ct);
            await cs.FlushFinalBlockAsync(ct);
        }
        byte[] tag;
        await using (var r = File.OpenRead(output))
        using (var h = new HMACSHA256(mk)) tag = await h.ComputeHashAsync(r, ct);
        await using (var a = new FileStream(output, FileMode.Append, FileAccess.Write)) await a.WriteAsync(tag, ct);
    }

    internal static async Task AesDecryptAsync(string key, string input, string output, CancellationToken ct)
    {
        var (ek, mk) = SplitKey(key);
        var len = new FileInfo(input).Length;
        if (len < Magic.Length + 16 + 16 + 32) throw new QuarantineException("AES payload is too short or not in FileBridge format.");

        await using var f = File.OpenRead(input);
        var header = new byte[Magic.Length + 16];
        await f.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new QuarantineException("Not a FileBridge AES payload.");

        f.Position = 0;
        byte[] computed;
        using (var h = new HMACSHA256(mk)) computed = await h.ComputeHashAsync(new BoundedStream(f, len - 32), ct);
        var tag = new byte[32];
        f.Position = len - 32;
        await f.ReadExactlyAsync(tag, ct);
        if (!CryptographicOperations.FixedTimeEquals(computed, tag))
            throw new QuarantineException("AES integrity check failed; file was altered or the key is wrong.");

        f.Position = header.Length;
        using var aes = Aes.Create();
        await using var cs = new CryptoStream(new BoundedStream(f, len - 32 - header.Length), aes.CreateDecryptor(ek, header[Magic.Length..]), CryptoStreamMode.Read);
        await using var o = File.Create(output);
        await cs.CopyToAsync(o, ct);
    }
}

/// <summary>Read-only window over the next N bytes of a stream. Does not dispose the inner stream.</summary>
internal sealed class BoundedStream(Stream inner, long length) : Stream
{
    private long _remaining = length;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => length - _remaining; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;
        var n = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
        _remaining -= n;
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_remaining <= 0) return 0;
        var n = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], ct);
        _remaining -= n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
