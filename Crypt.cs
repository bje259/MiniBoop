using System;
using System.Security.Cryptography;
using System.Text;

public static class CryptoHelper
{
    public static string Encrypt(string plainText)
    {
        byte[] input = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectedData.Protect(input, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string cipherBase64)
    {
        byte[] encrypted = Convert.FromBase64String(cipherBase64);
        byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}