using ControlMenu.Common.Paths;
using ControlMenu.Modules.Imaging.Services.Options;
using ControlMenu.Services;
using Serilog;

namespace ControlMenu.Modules.Imaging.Services;

public class ImageService : IImageService
{
    private const string ModuleId = "imaging";
    private const string MagickName = "magick";

    // Per-call resource caps -- defense in depth alongside policy.xml.
    private const string LimitFlags = "-limit memory 512MB -limit area 16384x16384 -limit map 1GB";

    private readonly ICommandExecutor _executor;
    private readonly IDependencyPathResolver _resolver;
    private readonly IDataPathResolver _paths;

    public ImageService(
        ICommandExecutor executor,
        IDependencyPathResolver resolver,
        IDataPathResolver paths)
    {
        _executor = executor;
        _resolver = resolver;
        _paths = paths;
    }

    public Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException("Phase C");

    public Task<byte[]> ResizeAsync(byte[] input, ResizeOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase C");

    public Task<byte[]> ConvertToIcoAsync(byte[] input, int[] sizes, IcoOptions? options = null, CancellationToken ct = default)
        => throw new NotImplementedException("Phase B");

    public Task<byte[]> RemoveBackgroundAsync(byte[] input, BackgroundRemoveOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase E");

    public Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default)
        => throw new NotImplementedException("Phase D");

    public async Task<ImageInfo> GetInfoAsync(byte[] input, CancellationToken ct = default)
    {
        var workDir = CreateWorkDir();
        try
        {
            var inputPath = Path.Combine(workDir, "input");
            await File.WriteAllBytesAsync(inputPath, input, ct);

            // %w=width %h=height %m=format code (e.g. PNG) %A=alpha/matte state.
            var result = await InvokeMagickAsync($"identify -format \"%w %h %m %A\" \"{inputPath}\"", ct);

            var parts = result.StandardOutput.Trim().Split(' ');
            if (parts.Length < 4)
                throw new ImagingException($"Unexpected identify output: '{result.StandardOutput.Trim()}'");

            // %A reports "True"/"Blend"/"False"/"Undefined"; treat anything that isn't
            // an explicit no-alpha state as having an alpha channel.
            var alpha = parts[3];
            var hasAlpha = !alpha.Equals("False", StringComparison.OrdinalIgnoreCase)
                        && !alpha.Equals("Undefined", StringComparison.OrdinalIgnoreCase);

            return new ImageInfo(
                Width: int.Parse(parts[0]),
                Height: int.Parse(parts[1]),
                Format: parts[2],
                HasAlpha: hasAlpha,
                SizeBytes: input.LongLength);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Allocate a per-call workdir under &lt;dataRoot&gt;/temp/imaging/&lt;guid&gt;/.</summary>
    private string CreateWorkDir()
    {
        var dir = Path.Combine(_paths.GetDataRoot(), "temp", "imaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Invoke the bundled magick.exe (resolved via IDependencyPathResolver per the
    /// Local-Dependencies-Only rule) with the given args. magick reads our hardened
    /// policy.xml from its own directory, so no environment overrides are needed.
    /// Throws <see cref="ImagingException"/> on non-zero exit.
    /// </summary>
    private async Task<CommandResult> InvokeMagickAsync(string args, CancellationToken ct)
    {
        var result = await _executor.ExecuteResolvedAsync(_resolver, ModuleId, MagickName, args, cancellationToken: ct);

        if (result.ExitCode != 0)
        {
            Log.Error("magick exit {ExitCode}: {Stderr}", result.ExitCode, result.StandardError);
            throw new ImagingException($"magick failed (exit {result.ExitCode}): {result.StandardError}");
        }
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Log.Warning("magick: {Stderr}", result.StandardError);
        }
        return result;
    }
}
