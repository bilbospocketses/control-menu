using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class ScrcpySettingsTests
{
    private static ScrcpyProbeResult MakeProbe(
        int w = 1920, int h = 1080, int density = 480,
        string[]? videoEncoders = null, string[]? audioEncoders = null, int sdkInt = 34)
        => new(w, h, density,
            videoEncoders ?? ["OMX.qcom.video.encoder.avc", "c2.android.hevc.encoder"],
            audioEncoders ?? ["c2.android.opus.encoder", "c2.android.aac.encoder"],
            sdkInt);

    [Fact]
    public void DeriveDefaults_WithH265Encoder_SelectsH265()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("h265", settings.Codec);
    }

    [Fact]
    public void DeriveDefaults_WithoutH265Encoder_SelectsH264()
    {
        var probe = MakeProbe(videoEncoders: ["OMX.qcom.video.encoder.avc"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("h264", settings.Codec);
    }

    [Fact]
    public void DeriveDefaults_EncoderPicksHardware()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("c2.android.hevc.encoder", settings.Encoder);
    }

    [Fact]
    public void DeriveDefaults_EncoderPicksHardwareOverSoftware()
    {
        var probe = MakeProbe(videoEncoders: ["c2.android.avc.encoder", "c2.mtk.avc.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("c2.mtk.avc.encoder", settings.Encoder);
    }

    [Fact]
    public void DeriveDefaults_BitrateScalesToResolution()
    {
        var probe = MakeProbe(w: 1920, h: 1080);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal(1920 * 1080 * 4, settings.Bitrate);
    }

    [Fact]
    public void DeriveDefaults_MaxFpsIs60()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal(60, settings.MaxFps);
    }

    [Fact]
    public void DeriveDefaults_MaxSizeIsLargerDimension()
    {
        var probe = MakeProbe(w: 1080, h: 1920);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal(1920, settings.MaxSize);
    }

    [Fact]
    public void DeriveDefaults_AudioTrueWhenEncodersPresent()
    {
        var probe = MakeProbe(audioEncoders: ["c2.android.opus.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.True(settings.Audio);
    }

    [Fact]
    public void DeriveDefaults_AudioFalseWhenNoEncoders()
    {
        var probe = MakeProbe(audioEncoders: []);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.False(settings.Audio);
    }

    [Fact]
    public void DeriveDefaults_AudioCodecPrefersOpus()
    {
        var probe = MakeProbe(audioEncoders: ["c2.android.aac.encoder", "c2.android.opus.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("opus", settings.AudioCodec);
    }

    [Fact]
    public void DeriveDefaults_AudioCodecFallsBackToFirst()
    {
        var probe = MakeProbe(audioEncoders: ["c2.android.aac.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("aac", settings.AudioCodec);
    }

    [Fact]
    public void DeriveDefaults_AudioSourceIsOutput()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("output", settings.AudioSource);
    }

    [Fact]
    public void DetectVideoCodecs_AvcAndHevc()
    {
        var codecs = ScrcpySettings.DetectVideoCodecs(["OMX.qcom.video.encoder.avc", "c2.android.hevc.encoder"]);
        Assert.Equal(["h264", "h265"], codecs);
    }

    [Fact]
    public void DetectVideoCodecs_OnlyAvc()
    {
        var codecs = ScrcpySettings.DetectVideoCodecs(["OMX.qcom.video.encoder.avc"]);
        Assert.Equal(["h264"], codecs);
    }

    [Fact]
    public void DetectVideoCodecs_WithAv1()
    {
        var codecs = ScrcpySettings.DetectVideoCodecs(["c2.qti.avc.encoder", "c2.qti.hevc.encoder", "c2.qualcomm.av1.encoder"]);
        Assert.Equal(["h264", "h265", "av1"], codecs);
    }

    [Fact]
    public void DetectVideoCodecs_EmptyFallsBackToH264()
    {
        var codecs = ScrcpySettings.DetectVideoCodecs([]);
        Assert.Equal(["h264"], codecs);
    }

    [Fact]
    public void DetectVideoCodecs_H264AlternateNaming()
    {
        var codecs = ScrcpySettings.DetectVideoCodecs(["OMX.mtk.video.encoder.h264"]);
        Assert.Equal(["h264"], codecs);
    }

    [Fact]
    public void DetectAudioCodecs_OpusAndAac()
    {
        var codecs = ScrcpySettings.DetectAudioCodecs(["c2.android.opus.encoder", "c2.android.aac.encoder"]);
        Assert.Equal(["opus", "aac", "raw"], codecs);
    }

    [Fact]
    public void DetectAudioCodecs_AllThree()
    {
        var codecs = ScrcpySettings.DetectAudioCodecs(["c2.android.opus.encoder", "c2.android.aac.encoder", "c2.android.flac.encoder"]);
        Assert.Equal(["opus", "aac", "flac", "raw"], codecs);
    }

    [Fact]
    public void DetectAudioCodecs_EmptyStillHasRaw()
    {
        var codecs = ScrcpySettings.DetectAudioCodecs([]);
        Assert.Equal(["raw"], codecs);
    }

    [Fact]
    public void EncoderMatchesCodec_AvcMatchesH264()
    {
        Assert.True(ScrcpySettings.EncoderMatchesCodec("OMX.qcom.video.encoder.avc", "h264"));
    }

    [Fact]
    public void EncoderMatchesCodec_HevcMatchesH265()
    {
        Assert.True(ScrcpySettings.EncoderMatchesCodec("c2.android.hevc.encoder", "h265"));
    }

    [Fact]
    public void EncoderMatchesCodec_AvcDoesNotMatchH265()
    {
        Assert.False(ScrcpySettings.EncoderMatchesCodec("OMX.qcom.video.encoder.avc", "h265"));
    }

    [Fact]
    public void EncoderMatchesCodec_Av1MatchesAv1()
    {
        Assert.True(ScrcpySettings.EncoderMatchesCodec("c2.qualcomm.av1.encoder", "av1"));
    }

    [Fact]
    public void EncoderMatchesCodec_H264AlternateNaming()
    {
        Assert.True(ScrcpySettings.EncoderMatchesCodec("OMX.mtk.video.encoder.h264", "h264"));
    }

    [Fact]
    public void DeriveDefaults_AudioCodecRawWhenNoEncoders()
    {
        var probe = MakeProbe(audioEncoders: []);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("raw", settings.AudioCodec);
    }

    [Fact]
    public void DeriveDefaults_AudioFalseWhenSdkBelow30()
    {
        var probe = MakeProbe(sdkInt: 29);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.False(settings.Audio);
    }

    [Fact]
    public void DeriveDefaults_AudioTrueWhenSdk30()
    {
        var probe = MakeProbe(sdkInt: 30);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.True(settings.Audio);
    }

    [Fact]
    public void DeriveDefaults_AudioSourceAlwaysOutput()
    {
        var probe = MakeProbe(sdkInt: 33);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("output", settings.AudioSource);
    }

    [Fact]
    public void DeriveDefaults_SdkZeroTreatedAsUnknown_AudioEnabled()
    {
        var probe = MakeProbe(sdkInt: 0);
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.True(settings.Audio);
        Assert.Equal("output", settings.AudioSource);
    }

    [Fact]
    public void AudioCaptureSupported_BelowThreshold() =>
        Assert.False(ScrcpySettings.AudioCaptureSupported(29));

    [Fact]
    public void AudioCaptureSupported_AtThreshold() =>
        Assert.True(ScrcpySettings.AudioCaptureSupported(30));

    [Fact]
    public void AudioDupSupported_BelowThreshold() =>
        Assert.False(ScrcpySettings.AudioDupSupported(32));

    [Fact]
    public void AudioDupSupported_AtThreshold() =>
        Assert.True(ScrcpySettings.AudioDupSupported(33));

    [Fact]
    public void IsHardwareEncoder_MtkIsHardware() =>
        Assert.True(ScrcpySettings.IsHardwareEncoder("c2.mtk.hevc.encoder"));

    [Fact]
    public void IsHardwareEncoder_QcomIsHardware() =>
        Assert.True(ScrcpySettings.IsHardwareEncoder("OMX.qcom.video.encoder.avc"));

    [Fact]
    public void IsHardwareEncoder_AndroidIsSoftware() =>
        Assert.False(ScrcpySettings.IsHardwareEncoder("c2.android.avc.encoder"));

    [Fact]
    public void IsHardwareEncoder_GoogleIsSoftware() =>
        Assert.False(ScrcpySettings.IsHardwareEncoder("OMX.google.h264.encoder"));

    [Fact]
    public void PickBestEncoder_PrefersHardware()
    {
        var encoders = new[] { "c2.android.avc.encoder", "c2.mtk.avc.encoder" };
        Assert.Equal("c2.mtk.avc.encoder", ScrcpySettings.PickBestEncoder(encoders, "h264"));
    }

    [Fact]
    public void PickBestEncoder_FallsBackToSoftware()
    {
        var encoders = new[] { "c2.android.hevc.encoder" };
        Assert.Equal("c2.android.hevc.encoder", ScrcpySettings.PickBestEncoder(encoders, "h265"));
    }

    [Fact]
    public void PickBestEncoder_ReturnsNullWhenNoMatch()
    {
        var encoders = new[] { "c2.android.avc.encoder" };
        Assert.Null(ScrcpySettings.PickBestEncoder(encoders, "h265"));
    }

    [Fact]
    public void DeriveDefaults_BrowserCodecsFilterH265()
    {
        var probe = MakeProbe(videoEncoders: ["c2.mtk.avc.encoder", "c2.mtk.hevc.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe, ["h264"]);
        Assert.Equal("h264", settings.Codec);
        Assert.Equal("c2.mtk.avc.encoder", settings.Encoder);
    }

    [Fact]
    public void DeriveDefaults_BrowserCodecsAllowH265()
    {
        var probe = MakeProbe(videoEncoders: ["c2.mtk.avc.encoder", "c2.mtk.hevc.encoder"]);
        var settings = ScrcpySettings.DeriveDefaults(probe, ["h264", "h265"]);
        Assert.Equal("h265", settings.Codec);
        Assert.Equal("c2.mtk.hevc.encoder", settings.Encoder);
    }

    [Fact]
    public void PickBestEncoder_RealGoogleTvEncoders()
    {
        var encoders = new[] { "c2.android.av1.encoder", "c2.mtk.avc.encoder", "c2.android.avc.encoder", "c2.mtk.hevc.encoder" };
        Assert.Equal("c2.mtk.hevc.encoder", ScrcpySettings.PickBestEncoder(encoders, "h265"));
        Assert.Equal("c2.mtk.avc.encoder", ScrcpySettings.PickBestEncoder(encoders, "h264"));
        Assert.Equal("c2.android.av1.encoder", ScrcpySettings.PickBestEncoder(encoders, "av1"));
    }
}
