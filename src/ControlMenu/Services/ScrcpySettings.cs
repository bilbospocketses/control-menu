namespace ControlMenu.Services;

public record ScrcpySettings(
    string? Codec,
    string? Encoder,
    int? Bitrate,
    int? MaxFps,
    int? MaxSize,
    bool? Audio,
    string? AudioSource,
    string? AudioCodec)
{
    private const string Module = "android-devices";

    public static ScrcpySettings DeriveDefaults(ScrcpyProbeResult probe)
    {
        var hasH265 = probe.VideoEncoders.Any(e => e.Contains("265") || e.Contains("hevc", StringComparison.OrdinalIgnoreCase));
        var hasAudio = probe.AudioEncoders.Length > 0;
        var hasOpus = probe.AudioEncoders.Any(e => e.Contains("opus", StringComparison.OrdinalIgnoreCase));
        var nativeSize = Math.Max(probe.Width, probe.Height);

        return new ScrcpySettings(
            Codec: hasH265 ? "h265" : "h264",
            Encoder: null,
            Bitrate: probe.Width * probe.Height * 4,
            MaxFps: 60,
            MaxSize: nativeSize,
            Audio: hasAudio,
            AudioSource: "playback",
            AudioCodec: hasOpus ? "opus" : probe.AudioEncoders.FirstOrDefault());
    }

    public static async Task<ScrcpySettings?> LoadAsync(IConfigurationService config, Guid deviceId)
    {
        var codec = await config.GetSettingAsync($"scrcpy-codec-{deviceId}", Module);
        if (codec is null) return null;

        return new ScrcpySettings(
            Codec: codec,
            Encoder: await config.GetSettingAsync($"scrcpy-encoder-{deviceId}", Module),
            Bitrate: int.TryParse(await config.GetSettingAsync($"scrcpy-bitrate-{deviceId}", Module), out var br) ? br : null,
            MaxFps: int.TryParse(await config.GetSettingAsync($"scrcpy-maxfps-{deviceId}", Module), out var fps) ? fps : null,
            MaxSize: int.TryParse(await config.GetSettingAsync($"scrcpy-maxsize-{deviceId}", Module), out var sz) ? sz : null,
            Audio: bool.TryParse(await config.GetSettingAsync($"scrcpy-audio-{deviceId}", Module), out var aud) ? aud : null,
            AudioSource: await config.GetSettingAsync($"scrcpy-audiosource-{deviceId}", Module),
            AudioCodec: await config.GetSettingAsync($"scrcpy-audiocodec-{deviceId}", Module));
    }

    public async Task SaveAsync(IConfigurationService config, Guid deviceId)
    {
        if (Codec is not null) await config.SetSettingAsync($"scrcpy-codec-{deviceId}", Codec, Module);
        if (Encoder is not null) await config.SetSettingAsync($"scrcpy-encoder-{deviceId}", Encoder, Module);
        if (Bitrate.HasValue) await config.SetSettingAsync($"scrcpy-bitrate-{deviceId}", Bitrate.Value.ToString(), Module);
        if (MaxFps.HasValue) await config.SetSettingAsync($"scrcpy-maxfps-{deviceId}", MaxFps.Value.ToString(), Module);
        if (MaxSize.HasValue) await config.SetSettingAsync($"scrcpy-maxsize-{deviceId}", MaxSize.Value.ToString(), Module);
        if (Audio.HasValue) await config.SetSettingAsync($"scrcpy-audio-{deviceId}", Audio.Value.ToString(), Module);
        if (AudioSource is not null) await config.SetSettingAsync($"scrcpy-audiosource-{deviceId}", AudioSource, Module);
        if (AudioCodec is not null) await config.SetSettingAsync($"scrcpy-audiocodec-{deviceId}", AudioCodec, Module);
    }

    public static async Task SaveProbeCacheAsync(IConfigurationService config, Guid deviceId, ScrcpyProbeResult probe)
    {
        await config.SetSettingAsync($"scrcpy-videoencoders-{deviceId}", string.Join(",", probe.VideoEncoders), Module);
        await config.SetSettingAsync($"scrcpy-audioencoders-{deviceId}", string.Join(",", probe.AudioEncoders), Module);
        await config.SetSettingAsync($"scrcpy-screendims-{deviceId}", $"{probe.Width}x{probe.Height}x{probe.Density}", Module);
    }

    public static async Task ClearAllAsync(IConfigurationService config, Guid deviceId)
    {
        string[] keys = [
            "scrcpy-codec", "scrcpy-encoder", "scrcpy-bitrate", "scrcpy-maxfps",
            "scrcpy-maxsize", "scrcpy-audio", "scrcpy-audiosource", "scrcpy-audiocodec",
            "scrcpy-videoencoders", "scrcpy-audioencoders", "scrcpy-screendims"
        ];
        foreach (var key in keys)
            await config.DeleteSettingAsync($"{key}-{deviceId}", Module);
    }
}
