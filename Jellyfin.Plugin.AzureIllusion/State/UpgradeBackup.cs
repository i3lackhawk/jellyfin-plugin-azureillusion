using System.Security.Cryptography;
using System.Text.Json;

namespace Jellyfin.Plugin.AzureIllusion.State;

/// <summary>Server-local, verified snapshot before the plugin exposes any services after an upgrade.</summary>
internal static class UpgradeBackup
{
    internal sealed record Entry(string Path, string Sha256);
    internal sealed record Manifest(string Version, DateTimeOffset CreatedAtUtc, Entry[] Files);

    public static string Ensure(string stateDirectory, string configurationPath, string version)
    {
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Revision < 0
            || parsedVersion.ToString(4) != version)
            throw new ArgumentException("Nieprawidłowy numer wersji kopii.", nameof(version));
        var backupRoot = stateDirectory + "-backups";
        var final = Path.Combine(backupRoot, version);
        // Do not use File.Exists/Directory.Exists to turn permission errors into an empty snapshot.
        if (Attributes(final) is { } existing)
        {
            if (!existing.HasFlag(FileAttributes.Directory)) throw new IOException("Ścieżka kopii nie jest katalogiem.");
            Verify(final, version);
            return final;
        }

        Directory.CreateDirectory(backupRoot);
        RejectLink(backupRoot);
        var staging = Path.Combine(backupRoot, version + ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var entries = new List<Entry>();
        void Copy(string source, string relative)
        {
            RejectLink(source);
            var target = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
                input.Position = 0;
                var sourceHash = Convert.ToHexStringLower(SHA256.HashData(input));
                entries.Add(new(relative, sourceHash));
            }
            if (Hash(target) != entries[^1].Sha256) throw new IOException("Weryfikacja kopii wtyczki nie powiodła się.");
        }
        if (Attributes(configurationPath) is not null) Copy(configurationPath, "configuration.xml");
        if (Attributes(stateDirectory) is not null)
        {
            RejectLink(stateDirectory);
            foreach (var file in Directory.GetFiles(stateDirectory)) Copy(file, Path.Combine("state", Path.GetFileName(file)));
            var transactions = Path.Combine(stateDirectory, "transactions");
            if (Attributes(transactions) is not null)
            {
                RejectLink(transactions);
                foreach (var file in Directory.GetFiles(transactions)) Copy(file, Path.Combine("state", "transactions", Path.GetFileName(file)));
            }
        }
        using (var stream = new FileStream(Path.Combine(staging, "manifest.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new Manifest(version, DateTimeOffset.UtcNow, entries.ToArray()));
            stream.Flush(flushToDisk: true);
        }
        Verify(staging, version);
        Directory.Move(staging, final);
        // Incomplete staging folders are deliberately retained for inspection, never accepted as completed backups.
        return final;
    }

    private static void Verify(string directory, string version)
    {
        RejectLink(directory);
        using var stream = File.OpenRead(Path.Combine(directory, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<Manifest>(stream) ?? throw new InvalidDataException("Pusta kopia wtyczki.");
        if (manifest.Version != version || manifest.Files is null) throw new InvalidDataException("Nieprawidłowy manifest kopii.");
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        foreach (var file in manifest.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path) || Path.IsPathRooted(file.Path)) throw new InvalidDataException("Nieprawidłowa ścieżka kopii.");
            var path = Path.GetFullPath(Path.Combine(directory, file.Path));
            if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Ścieżka poza katalogiem kopii.");
            RejectLink(path);
            if (Hash(path) != file.Sha256) throw new InvalidDataException("Kopia wtyczki jest niekompletna lub zmieniona.");
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static void RejectLink(string path)
    {
        if (Attributes(path)?.HasFlag(FileAttributes.ReparsePoint) == true)
            throw new IOException("Kopia nie obsługuje dowiązań symbolicznych: " + path);
    }
}
