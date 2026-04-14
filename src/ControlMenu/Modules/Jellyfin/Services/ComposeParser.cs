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

        string? containerName = null;
        string? configHostPath = null;
        bool inVolumes = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("container_name:"))
            {
                containerName = line["container_name:".Length..].Trim().Trim('"', '\'');
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
                    var containerSide = mount[(colonIdx + 1)..].Split(':')[0];
                    if (containerSide == "/config")
                    {
                        configHostPath = hostSide;
                    }
                }
            }
        }

        if (configHostPath is null)
            return new(containerName, null, null, "No volume mount to /config found in compose file");

        var dbPath = Path.Combine(configHostPath, "data", "jellyfin.db");
        return new(containerName, configHostPath, dbPath, null);
    }

    private static int FindMountSeparator(string mount)
    {
        for (int i = 1; i < mount.Length - 1; i++)
        {
            if (mount[i] == ':' && mount[i + 1] == '/')
            {
                if (i == 1 && char.IsLetter(mount[0]))
                    continue;
                return i;
            }
        }
        return mount.LastIndexOf(':');
    }
}
