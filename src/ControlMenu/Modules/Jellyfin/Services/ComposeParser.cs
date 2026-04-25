namespace ControlMenu.Modules.Jellyfin.Services;

public record ComposeParseResult(
    string? ContainerName,
    string? ConfigHostPath,
    string? DbPath,
    string? ErrorMessage);

public static class ComposeParser
{
    public static ComposeParseResult Parse(string composePath)
    {
        if (!File.Exists(composePath))
            return new(null, null, null, $"File not found: {composePath}");

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

        var dbPath = Path.Combine(configHostPath, "data", "jellyfin.db");
        return new(containerName, configHostPath, dbPath, null);
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
