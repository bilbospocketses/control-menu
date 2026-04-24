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

    public static string[] DetectVideoCodecs(string[] encoders)
    {
        var codecs = new List<string>();
        foreach (var e in encoders)
        {
            var lower = e.ToLowerInvariant();
            if (!codecs.Contains("h264") && (ContainsSegment(lower, "avc") || ContainsSegment(lower, "h264")))
                codecs.Add("h264");
            if (!codecs.Contains("h265") && ContainsSegment(lower, "hevc"))
                codecs.Add("h265");
            if (!codecs.Contains("av1") && ContainsSegment(lower, "av1"))
                codecs.Add("av1");
        }
        if (codecs.Count == 0) codecs.Add("h264");
        return [.. codecs];
    }

    public static string[] DetectAudioCodecs(string[] encoders)
    {
        var codecs = new List<string>();
        foreach (var e in encoders)
        {
            var lower = e.ToLowerInvariant();
            if (!codecs.Contains("opus") && ContainsSegment(lower, "opus"))
                codecs.Add("opus");
            if (!codecs.Contains("aac") && ContainsSegment(lower, "aac"))
                codecs.Add("aac");
            if (!codecs.Contains("flac") && ContainsSegment(lower, "flac"))
                codecs.Add("flac");
        }
        codecs.Add("raw");
        return [.. codecs];
    }

    public static bool EncoderMatchesCodec(string encoderName, string codec)
    {
        var lower = encoderName.ToLowerInvariant();
        return codec switch
        {
            "h264" => ContainsSegment(lower, "avc") || ContainsSegment(lower, "h264"),
            "h265" => ContainsSegment(lower, "hevc"),
            "av1" => ContainsSegment(lower, "av1"),
            _ => true
        };
    }

    private static bool ContainsSegment(string name, string segment)
    {
        var idx = name.IndexOf(segment, StringComparison.Ordinal);
        if (idx < 0) return false;
        var before = idx == 0 || name[idx - 1] == '.';
        var end = idx + segment.Length;
        var after = end >= name.Length || name[end] == '.';
        return before && after;
    }

    private static readonly string[] HwVendorSegments = ["mtk", "qcom", "exynos", "intel", "nvidia"];

    public static bool IsHardwareEncoder(string encoderName)
    {
        var lower = encoderName.ToLowerInvariant();
        return HwVendorSegments.Any(v => lower.Contains($".{v}."));
    }

    public static string? PickBestEncoder(string[] encoders, string codec)
    {
        var matching = encoders.Where(e => EncoderMatchesCodec(e, codec)).ToArray();
        if (matching.Length == 0) return null;
        return matching.FirstOrDefault(IsHardwareEncoder) ?? matching[0];
    }

    public static bool AudioCaptureSupported(int sdkInt) => sdkInt >= 30;
    public static bool AudioDupSupported(int sdkInt) => sdkInt >= 33;

    public static ScrcpySettings DeriveDefaults(ScrcpyProbeResult probe, string[]? browserCodecs = null)
    {
        var deviceCodecs = DetectVideoCodecs(probe.VideoEncoders);
        var usableCodecs = browserCodecs is not null
            ? deviceCodecs.Where(c => browserCodecs.Contains(c)).ToArray()
            : deviceCodecs;
        if (usableCodecs.Length == 0) usableCodecs = ["h264"];

        var audioCodecs = DetectAudioCodecs(probe.AudioEncoders);
        var captureOk = probe.SdkInt == 0 || AudioCaptureSupported(probe.SdkInt);
        var hasAudio = probe.AudioEncoders.Length > 0 && captureOk;
        var nativeSize = Math.Max(probe.Width, probe.Height);
        var bestCodec = usableCodecs.Contains("h265") ? "h265"
            : usableCodecs.Contains("h264") ? "h264"
            : usableCodecs[0];

        return new ScrcpySettings(
            Codec: bestCodec,
            Encoder: PickBestEncoder(probe.VideoEncoders, bestCodec),
            Bitrate: probe.Width * probe.Height * 4,
            MaxFps: 60,
            MaxSize: nativeSize,
            Audio: hasAudio,
            AudioSource: "output",
            AudioCodec: audioCodecs.Contains("opus") ? "opus" : audioCodecs[0]);
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
        if (probe.SdkInt > 0)
            await config.SetSettingAsync($"scrcpy-sdkint-{deviceId}", probe.SdkInt.ToString(), Module);
    }

    public static async Task ClearAllAsync(IConfigurationService config, Guid deviceId)
    {
        string[] keys = [
            "scrcpy-codec", "scrcpy-encoder", "scrcpy-bitrate", "scrcpy-maxfps",
            "scrcpy-maxsize", "scrcpy-audio", "scrcpy-audiosource", "scrcpy-audiocodec",
            "scrcpy-videoencoders", "scrcpy-audioencoders", "scrcpy-screendims", "scrcpy-sdkint"
        ];
        foreach (var key in keys)
            await config.DeleteSettingAsync($"{key}-{deviceId}", Module);
    }
}
