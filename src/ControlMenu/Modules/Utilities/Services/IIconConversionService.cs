namespace ControlMenu.Modules.Utilities.Services;

public interface IIconConversionService
{
    Task ConvertToIcoAsync(string sourcePath, string targetPath, int[]? sizes = null);
    Task<byte[]> ConvertToIcoBytesAsync(byte[] sourceImageBytes, int[]? sizes = null);
}
