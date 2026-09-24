using System.IO;
using System.Text.Json;

namespace DoodleDrive.Services;

/// <summary>État d'un fichier au dernier sync (taille + date de modification en secondes Unix).</summary>
public sealed record FileState(long Size, long MtimeUnix);

/// <summary>
/// Mémorise, pour chaque paire de synchronisation, l'état des fichiers au dernier sync
/// (le « manifeste »). C'est ce qui permet de distinguer une suppression/un renommage d'un
/// simple ajout, et donc de faire une vraie synchro bidirectionnelle sans perte.
/// Stocké par machine dans %AppData%\DoodleDrive\sync\&lt;pairId&gt;.json.
/// </summary>
public sealed class SyncStateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly string _dir;

    public SyncStateStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DoodleDrive", "sync");
    }

    private string FileFor(int pairId) => Path.Combine(_dir, $"{pairId}.json");

    public Dictionary<string, FileState> Load(int pairId)
    {
        try
        {
            var path = FileFor(pairId);
            if (!File.Exists(path)) return new(StringComparer.Ordinal);
            var data = JsonSerializer.Deserialize<Dictionary<string, FileState>>(File.ReadAllText(path));
            return data is null ? new(StringComparer.Ordinal) : new(data, StringComparer.Ordinal);
        }
        catch
        {
            return new(StringComparer.Ordinal);
        }
    }

    public void Save(int pairId, Dictionary<string, FileState> manifest)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FileFor(pairId), JsonSerializer.Serialize(manifest, Json));
        }
        catch
        {
            // best-effort : un manifeste non sauvegardé refera juste une fusion complète au prochain sync
        }
    }

    public void Delete(int pairId)
    {
        try { File.Delete(FileFor(pairId)); } catch { /* best-effort */ }
    }
}
