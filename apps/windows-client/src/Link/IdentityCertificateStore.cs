using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security;
using System.Text;
using System.IO;

namespace LyricRelay.Windows;

public sealed class IdentityCertificateStore
{
    public X509Certificate2 GetOrCreate(string deviceId)
    {
        const string storeSubjectPrefix = "CN=LyricRelay-";
        var subject = storeSubjectPrefix + deviceId;
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var existing = store.Certificates
                .Find(X509FindType.FindBySubjectDistinguishedName, subject, validOnly: false)
                .OfType<X509Certificate2>()
                .FirstOrDefault(certificate => certificate.HasPrivateKey && certificate.NotAfter > DateTime.UtcNow);
            if (existing is not null)
            {
                return existing;
            }

            var persisted = CreateCertificate(subject, ephemeral: false, out _);
            store.Add(persisted);
            return persisted;
        }
        catch (UnauthorizedAccessException)
        {
            return GetOrCreateFileFallback(deviceId, subject);
        }
        catch (SecurityException)
        {
            return GetOrCreateFileFallback(deviceId, subject);
        }
        catch (CryptographicException)
        {
            return GetOrCreateFileFallback(deviceId, subject);
        }
    }

    private static X509Certificate2 GetOrCreateFileFallback(string deviceId, string subject)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)));
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyricRelay",
            $"identity-{digest}.bin");
        try
        {
            if (File.Exists(path))
            {
                var protectedPfx = File.ReadAllBytes(path);
                var pfx = ProtectedData.Unprotect(protectedPfx, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
            }
        }
        catch (CryptographicException)
        {
            // Recreate an unreadable or stale fallback certificate below.
        }
        catch (IOException)
        {
            // Continue with an in-memory certificate if the fallback path is unavailable.
        }

        var certificate = CreateCertificate(subject, ephemeral: true, out var exportedPfx);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var protectedPfx = ProtectedData.Protect(exportedPfx, optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, protectedPfx);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return certificate;
    }

    private static X509Certificate2 CreateCertificate(string subject, bool ephemeral, out byte[] exportedPfx)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(5));
        exportedPfx = generated.Export(X509ContentType.Pfx);
        var persisted = new X509Certificate2(
            exportedPfx,
            (string?)null,
            ephemeral ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
        return persisted;
    }
}
