using System.Security.Cryptography;
using System.Text;
using AgentDemo.Web.Services;

namespace AgentDemo.Maui;

// Persists keys as files under the app data directory. Writes go to a temp file first so a kill mid-write never corrupts the old value.
public sealed class FileLocalStore : ILocalStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileLocalStore() : this(Path.Combine(FileSystem.AppDataDirectory, "store"))
    {
    }

    public FileLocalStore(string root)
    {
        _root = root;
    }

    public async Task<string?> GetAsync(string key)
    {
        await _gate.WaitAsync();
        try
        {
            var path = PathFor(key);
            return File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(string key, string value)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_root);
            var path = PathFor(key);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, value, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    // Readable prefix of safe characters plus a hash, so distinct keys never collide after sanitising.
    internal string PathFor(string key)
    {
        var safe = new string(key.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        if (safe.Length > 40) safe = safe[..40];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];
        return Path.Combine(_root, $"{safe}-{hash}.json");
    }
}
