using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AmlaDeveloperAssistant.Services
{
    /// <summary>
    /// Encrypts and decrypts tokens using AES-256-GCM.
    /// The encryption key is derived from a machine-scoped secret stored in
    /// a dedicated key file, so tokens can only be decrypted on the same machine.
    /// </summary>
    public static class TokenEncryptionService
    {
        private const int KeyBytes = 32;   // AES-256
        private const int NonceBytes = 12;  // AES-GCM standard nonce
        private const int TagBytes = 16;   // AES-GCM authentication tag

        // ------------------------------------------------------------------
        // Key management
        // ------------------------------------------------------------------

        /// <summary>Returns (creating if missing) the path to the per-machine key file.</summary>
        public static string GetKeyFilePath()
        {
            var folder = Path.Combine(Environment.CurrentDirectory, "Keys");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "machine.key");
        }

        /// <summary>Loads or generates the 256-bit AES key for this machine.</summary>
        public static byte[] GetOrCreateKey()
        {
            var path = GetKeyFilePath();

            if (File.Exists(path))
            {
                var stored = File.ReadAllBytes(path);
                if (stored.Length == KeyBytes)
                    return stored;
            }

            var key = RandomNumberGenerator.GetBytes(KeyBytes);
            File.WriteAllBytes(path, key);
            return key;
        }

        // ------------------------------------------------------------------
        // Encrypt
        // ------------------------------------------------------------------

        /// <summary>
        /// Encrypts <paramref name="plainText"/> using AES-256-GCM.
        /// Returns a Base64 string containing  nonce | ciphertext | tag.
        /// </summary>
        public static string Encrypt(string plainText)
        {
            var key = GetOrCreateKey();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);

            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[TagBytes];

            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

            // Layout: [nonce (12)] + [ciphertext (n)] + [tag (16)]
            var combined = new byte[NonceBytes + cipherBytes.Length + TagBytes];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceBytes);
            Buffer.BlockCopy(cipherBytes, 0, combined, NonceBytes, cipherBytes.Length);
            Buffer.BlockCopy(tag, 0, combined, NonceBytes + cipherBytes.Length, TagBytes);

            return Convert.ToBase64String(combined);
        }

        // ------------------------------------------------------------------
        // Decrypt
        // ------------------------------------------------------------------

        /// <summary>
        /// Decrypts a value previously produced by <see cref="Encrypt"/>.
        /// Throws <see cref="CryptographicException"/> if the data is tampered or the key is wrong.
        /// </summary>
        public static string Decrypt(string encryptedBase64)
        {
            var key = GetOrCreateKey();
            var combined = Convert.FromBase64String(encryptedBase64);

            if (combined.Length < NonceBytes + TagBytes)
                throw new CryptographicException("Invalid encrypted token: payload too short.");

            var nonce = combined[..NonceBytes];
            var tag = combined[^TagBytes..];
            var cipherBytes = combined[NonceBytes..^TagBytes];
            var plainBytes = new byte[cipherBytes.Length];

            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }

        // ------------------------------------------------------------------
        // Token file helpers
        // ------------------------------------------------------------------

        public record TokenFile(string JiraToken, string GitToken, string AuthToken);

        public static string GetTokenFilePath(string computerName, string username)
        {
            var folder = Path.Combine(Environment.CurrentDirectory, "Secrets");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"{computerName}_{username}.json");
        }

        public static async Task SaveTokensAsync(string computerName, string username, string jiraToken, string gitToken, string authToken)
        {
            var path = GetTokenFilePath(computerName, username);
            var data = new TokenFile(Encrypt(jiraToken), Encrypt(gitToken), Encrypt(authToken));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data));
        }

        public static async Task<(string JiraToken, string GitToken, string AuthToken)> LoadTokensAsync(string computerName, string username)
        {
            var path = GetTokenFilePath(computerName, username);

            if (!File.Exists(path))
                return (string.Empty, string.Empty, string.Empty);

            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<TokenFile>(json)
                       ?? throw new InvalidOperationException("Token file is corrupt.");

            return (Decrypt(data.JiraToken), Decrypt(data.GitToken), Decrypt(data.AuthToken));
        }
    }
}
