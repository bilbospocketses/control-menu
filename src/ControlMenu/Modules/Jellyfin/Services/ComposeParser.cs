namespace ControlMenu.Modules.Jellyfin.Services;

public record ComposeParseResult(
    string? ContainerName,
    string? ConfigHostPath,
    string? DbPath,
    string? ErrorMessage);

public static class ComposeParser
{
    // A docker-compose.yml is a small text file; cap the read so a pathological or hostile
    // file can't be slurped whole into memory.
    private const long MaxComposeBytes = 1_048_576; // 1 MB

    public static ComposeParseResult Parse(string composePath)
    {
        if (!File.Exists(composePath))
            return new(null, null, null, $"File not found: {composePath}");

        long fileLength;
        try
        {
            fileLength = new FileInfo(composePath).Length;
        }
        catch (Exception ex)
        {
            return new(null, null, null, $"Cannot read file: {ex.Message}");
        }
        if (fileLength > MaxComposeBytes)
            return new(null, null, null, $"Compose file is too large (> 1 MB): {composePath}");

        string[] lines;
        try
        {
            lines = File.ReadAllLines(composePath);
        }
        catch (Exception ex)
        {
            return new(null, null, null, $"Cannot read file: {ex.Message}");
        }

        // Track per-service state to avoid matching wrong service's container_name/volumes
        string? containerName = null;
        string? configHostPath = null;
        string? currentServiceContainer = null;
        string? currentServiceConfig = null;
        bool inVolumes = false;
        bool inServices = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // Top-level "services:" key
            if (rawLine == "services:" || rawLine.StartsWith("services:"))
            {
                inServices = true;
                inVolumes = false;
                continue;
            }

            // Exit services block on another top-level key (no indentation)
            if (inServices && !rawLine.StartsWith(" ") && !rawLine.StartsWith("\t") && line.EndsWith(':'))
            {
                inServices = false;
                inVolumes = false;
            }

            // Detect service-level keys (2-space or 1-tab indent, e.g. "  jellyfin:")
            if (inServices && (rawLine.StartsWith("  ") || rawLine.StartsWith("\t")) &&
                !rawLine.StartsWith("    ") && !rawLine.StartsWith("\t\t") &&
                line.EndsWith(':') && !line.StartsWith('-'))
            {
                // New service block — save any previous service's data if it had /config
                if (currentServiceConfig is not null)
                {
                    containerName = currentServiceContainer;
                    configHostPath = currentServiceConfig;
                }
                currentServiceContainer = null;
                currentServiceConfig = null;
                inVolumes = false;
                continue;
            }

            if (line.StartsWith("container_name:"))
            {
                currentServiceContainer = line["container_name:".Length..].Trim().Trim('"', '\'');
            }

            if (line == "volumes:")
            {
                inVolumes = true;
                continue;
            }

            if (inVolumes && !rawLine.StartsWith(" ") && !rawLine.StartsWith("\t") && line.Length > 0 && !line.StartsWith("-"))
            {
                inVolumes = false;
            }

            if (inVolumes && line.StartsWith("-"))
            {
                var mount = line[1..].Trim().Trim('"', '\'');
                var colonIdx = FindMountSeparator(mount);
                if (colonIdx > 0)
                {
                    var hostSide = mount[..colonIdx];
                    // Split on ':' to strip options like :ro, :rw
                    var containerSide = mount[(colonIdx + 1)..].Split(':')[0];
                    if (containerSide == "/config")
                    {
                        currentServiceConfig = hostSide;
                    }
                }
            }
        }

        // Save last service
        if (currentServiceConfig is not null)
        {
            containerName = currentServiceContainer;
            configHostPath = currentServiceConfig;
        }

        if (configHostPath is null)
            return new(containerName, null, null, "No volume mount to /config found in compose file");

        if (!IsValidHostPath(configHostPath))
            return new(containerName, null, null,
                $"Unsupported /config host path (must be a fully-qualified path with no quotes or control characters): {configHostPath}");

        var dbPath = Path.Combine(configHostPath, "data", "jellyfin.db");
        return new(containerName, configHostPath, dbPath, null);
    }

    /// <summary>
    /// The /config host path flows to <see cref="Path.Combine(string,string,string)"/> and the
    /// sqlite3 update sink, so reject anything that isn't a plain fully-qualified path: no quotes
    /// or control characters, and rooted enough to resolve to a real local jellyfin.db (a relative
    /// or Unix-style path can't on this host).
    /// <para>
    /// A <c>..</c> segment in an otherwise fully-qualified path is intentionally allowed: it just
    /// normalises to another real local path the user already controls (it's their own compose
    /// file), and the sqlite3 sink it feeds is structured-arg (no shell, no injection), so there is
    /// nothing to escalate. Don't "tighten" this to ban <c>..</c> without that context.
    /// </para>
    /// </summary>
    private static bool IsValidHostPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains('"') || path.Contains('\'')) return false;
        foreach (var c in path)
            if (char.IsControl(c)) return false;
        return Path.IsPathFullyQualified(path);
    }

    private static int FindMountSeparator(string mount)
    {
        // Find the colon that separates host:container (not the Windows drive letter colon)
        for (int i = 1; i < mount.Length - 1; i++)
        {
            if (mount[i] == ':' && mount[i + 1] == '/')
            {
                // Skip Windows drive letter (e.g., C:/)
                if (i == 1 && char.IsLetter(mount[0]))
                    continue;
                return i;
            }
        }
        // No ":<path>" found — don't use LastIndexOf as it matches option colons (:ro, :rw)
        return -1;
    }
}
