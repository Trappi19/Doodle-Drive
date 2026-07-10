using System.IO;
using System.Security.Cryptography;
using System.Text;
using DoodleDrive.Models;

namespace DoodleDrive.Services;

/// <summary>
/// Export/import d'un fichier de configuration serveur portable (<c>.ddconfig</c>).
/// Le fichier est chiffré (AES-GCM) avec une clé embarquée dans l'app : il est illisible
/// à l'ouverture, ce qui permet de l'envoyer à un utilisateur sans lui livrer les
/// identifiants en clair. ⚠️ Il s'agit d'obfuscation, pas d'un secret fort : la clé
/// voyage dans l'application. À l'import, un fichier <c>.env</c> en clair (mêmes clés
/// <c>DOODLE_*</c>) est également accepté.
/// </summary>
public static class PortableConfig
{
    // Marqueur des fichiers chiffrés (« DDC1 »).
    private static readonly byte[] Magic = "DDC1"u8.ToArray();

    // Clé AES-256 embarquée. Obfuscation volontaire — ne pas considérer comme secrète.
    private static readonly byte[] Key =
    {
        0x7A, 0x1C, 0x93, 0xE4, 0x2B, 0xD6, 0x08, 0x5F,
        0xA9, 0x34, 0x71, 0xC0, 0x6E, 0x8D, 0x12, 0xB7,
        0x4F, 0xE2, 0x59, 0x83, 0x0A, 0xD1, 0x66, 0x9C,
        0x27, 0xB4, 0xF8, 0x3E, 0x50, 0xAB, 0x1D, 0xC9
    };

    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Construit le texte <c>KEY=VALUE</c> (clés <c>DOODLE_*</c>) à partir d'une config.</summary>
    public static string BuildEnvText(AppConfig c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Doodle Drive — configuration serveur (généré)");
        sb.AppendLine($"DOODLE_DB_HOST={c.DbHost}");
        sb.AppendLine($"DOODLE_DB_PORT={c.DbPort}");
        sb.AppendLine($"DOODLE_DB_NAME={c.DbName}");
        sb.AppendLine($"DOODLE_DB_USER={c.DbUser}");
        sb.AppendLine($"DOODLE_DB_PASSWORD={c.DbPassword}");
        sb.AppendLine($"DOODLE_FTP_HOST={c.FtpHost}");
        sb.AppendLine($"DOODLE_FTP_PORT={c.FtpPort}");
        sb.AppendLine($"DOODLE_FTP_USER={c.FtpUser}");
        sb.AppendLine($"DOODLE_FTP_PASSWORD={c.FtpPassword}");
        sb.AppendLine($"DOODLE_FTP_ROOT={c.FtpRootPath}");
        sb.AppendLine($"DOODLE_FTP_TLS={(c.FtpUseTls ? "true" : "false")}");
        return sb.ToString();
    }

    /// <summary>Chiffre le texte en un blob <c>.ddconfig</c> : magic + nonce + tag + données.</summary>
    public static byte[] Encrypt(string text)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(Key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipher);
        return ms.ToArray();
    }

    /// <summary>
    /// Lit un fichier de config et renvoie ses clés <c>DOODLE_*</c>. Accepte le format
    /// chiffré (<c>.ddconfig</c>) et le format <c>.env</c> en clair. Dictionnaire vide si illisible.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = TryDecrypt(bytes) ?? SafeUtf8(bytes);
        return text is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : EnvFile.Parse(text.Replace("\r\n", "\n").Split('\n'));
    }

    private static string? TryDecrypt(byte[] bytes)
    {
        if (bytes.Length < Magic.Length + NonceSize + TagSize) return null;
        for (var i = 0; i < Magic.Length; i++)
            if (bytes[i] != Magic[i]) return null;

        try
        {
            var offset = Magic.Length;
            var nonce = bytes.AsSpan(offset, NonceSize);
            offset += NonceSize;
            var tag = bytes.AsSpan(offset, TagSize);
            offset += TagSize;
            var cipher = bytes.AsSpan(offset);

            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(Key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeUtf8(byte[] bytes)
    {
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return null; }
    }
}
