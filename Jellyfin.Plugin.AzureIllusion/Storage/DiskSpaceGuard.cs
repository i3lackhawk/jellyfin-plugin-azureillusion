namespace Jellyfin.Plugin.AzureIllusion.Storage;

/// <summary>Result of a non-destructive free-space check.</summary>
public sealed record DiskSpaceCheck(bool WasChecked, bool HasSpace, long AvailableBytes, long RequiredBytes, string? DriveRoot);

/// <summary>Protects subtitle downloads from exhausting the target filesystem.</summary>
public static class DiskSpaceGuard
{
    private const long Megabyte = 1024L * 1024L;

    public static DiskSpaceCheck Check(string targetPath, long? downloadSizeBytes, int minimumFreeSpaceMegabytes)
    {
        var required = CalculateRequiredBytes(downloadSizeBytes, minimumFreeSpaceMegabytes);
        try
        {
            var fullPath = Path.GetFullPath(targetPath);
            var drive = DriveInfo.GetDrives()
                .Where(candidate => candidate.IsReady)
                .Where(candidate => fullPath.StartsWith(candidate.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
                .FirstOrDefault();
            if (drive is null)
            {
                return new DiskSpaceCheck(false, true, 0, required, null);
            }

            var available = drive.AvailableFreeSpace;
            return new DiskSpaceCheck(true, available >= required, available, required, drive.RootDirectory.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DiskSpaceCheck(false, true, 0, required, null);
        }
    }

    internal static long CalculateRequiredBytes(long? downloadSizeBytes, int minimumFreeSpaceMegabytes)
    {
        var reserve = Math.Max(0, minimumFreeSpaceMegabytes) * Megabyte;
        var payload = Math.Max(0, downloadSizeBytes ?? 0);
        var workingSpace = Math.Max(16 * Megabyte, payload > long.MaxValue / 2 ? long.MaxValue : payload * 2);
        return reserve > long.MaxValue - workingSpace ? long.MaxValue : reserve + workingSpace;
    }
}
