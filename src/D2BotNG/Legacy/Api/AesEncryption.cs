using System.Security.Cryptography;
using System.Text;

namespace D2BotNG.Legacy.Api;

public static class AesEncryption
{
    private const int Iterations = 1000;
    private const int SaltSize = 32;
    private const int IvSize = 16;
    private const int KeySize = 32;

    public static string Encrypt(string input, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Padding = PaddingMode.PKCS7;
        aes.Mode = CipherMode.CBC;
        aes.GenerateIV();

        var iv = aes.IV;
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(input);
        }

        var ciphertext = ms.ToArray();
        var result = new byte[SaltSize + IvSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, result, SaltSize, IvSize);
        Buffer.BlockCopy(ciphertext, 0, result, SaltSize + IvSize, ciphertext.Length);
        return Convert.ToBase64String(result);
    }

    public static string? Decrypt(string input, string password)
    {
        try
        {
            var data = Convert.FromBase64String(input);
            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            var ciphertext = new byte[data.Length - SaltSize - IvSize];

            Buffer.BlockCopy(data, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(data, SaltSize, iv, 0, IvSize);
            Buffer.BlockCopy(data, SaltSize + IvSize, ciphertext, 0, ciphertext.Length);

            var key = DeriveKey(password, salt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream(ciphertext);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    public static string GenerateKey(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes)
        {
            sb.Append(chars[b % chars.Length]);
        }
        return sb.ToString();
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA1, KeySize);
    }
}
