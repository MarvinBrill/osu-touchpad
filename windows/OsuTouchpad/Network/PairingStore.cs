using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OsuTouchpad.Mapping;

namespace OsuTouchpad.Network;

internal sealed class PairingStore : IDisposable
{
    private readonly Profile profile;
    private readonly Dictionary<string, byte[]> devices;
    public X509Certificate2 Certificate { get; }
    public byte[] Fingerprint { get; }
    public string Pin => Convert.ToHexString(Fingerprint).ToLowerInvariant();

    public PairingStore(Profile profile)
    {
        this.profile = profile;
        devices = profile.Read<Dictionary<string, byte[]>>("devices.json") ?? [];
        string path = Path.Combine(profile.DirectoryPath, "receiver.pfx");
        if (!File.Exists(path))
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=osu-touchpad", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            profile.Write("receiver.pfx", cert.Export(X509ContentType.Pfx));
        }
        // Schannel needs a persisted user key container for server-side TLS.
        Certificate = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.UserKeySet);
        Fingerprint = SHA256.HashData(Certificate.RawData);
    }

    public bool Known(Hello hello) => hello.Credential.Length == Wire.KeySize &&
        devices.TryGetValue(hello.Id, out var stored) && CryptographicOperations.FixedTimeEquals(hello.Credential, stored);

    public byte[] Trust(string id)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(Wire.KeySize);
        var next = new Dictionary<string, byte[]>(devices) { [id] = secret };
        profile.Save("devices.json", next);
        devices[id] = secret;
        return secret;
    }

    public string Code(byte[] nonce, byte[] challenge)
    {
        byte[] digest = SHA256.HashData([.. Fingerprint, .. nonce, .. challenge]);
        return (BinaryPrimitives.ReadUInt32BigEndian(digest) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public void Dispose() => Certificate.Dispose();
}
