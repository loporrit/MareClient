using System.Security.Cryptography;
using System.Text;

namespace MareSynchronos.Utils;

public static class Crypto
{
#pragma warning disable SYSLIB0021 // Type or member is obsolete

    private static readonly SHA256CryptoServiceProvider _sha256CryptoProvider = new();

    public static string GetFileHash(this string filePath)
    {
        using SHA1CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash256(this string stringToHash)
    {
        return GetOrComputeHashSHA256(stringToHash);
    }

    private static string GetOrComputeHashSHA256(string stringToCompute)
    {
        return BitConverter.ToString(_sha256CryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToCompute))).Replace("-", "", StringComparison.Ordinal);
    }



#pragma warning restore SYSLIB0021 // Type or member is obsolete
}