using ControlMenu.Modules.Imaging.Services.Options;

namespace ControlMenu.Modules.Imaging.Services;

public interface IImageService
{
    Task<byte[]> ConvertFormatAsync(byte[] input, string targetFormat, ConvertFormatOptions? options = null, CancellationToken ct = default);
    Task<byte[]> ResizeAsync(byte[] input, ResizeOptions options, CancellationToken ct = default);
    Task<byte[]> ConvertToIcoAsync(byte[] input, int[] sizes, IcoOptions? options = null, CancellationToken ct = default);
    Task<byte[]> RemoveBackgroundAsync(byte[] input, BackgroundRemoveOptions options, CancellationToken ct = default);
    Task<byte[]> RasterizeSvgAsync(byte[] svgBytes, RasterizeOptions options, CancellationToken ct = default);
    Task<ImageInfo> GetInfoAsync(byte[] input, CancellationToken ct = default);
}
