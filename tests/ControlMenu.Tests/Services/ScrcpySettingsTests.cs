using ControlMenu.Services;

namespace ControlMenu.Tests.Services;

public class ScrcpySettingsTests
{
    private static ScrcpyProbeResult MakeProbe(
        int w = 1920, int h = 1080, int density = 480,
        string[]? videoEncoders = null, string[]? audioEncoders = null)
        => new(w, h, density,
            videoEncoders ?? ["OMX.qcom.video.encoder.avc", "c2.android.hevc.encoder"],
            audioEncoders ?? ["c2.android.opus.encoder", "c2.android.aac.encoder"]);

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
    public void DeriveDefaults_EncoderIsNull()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Null(settings.Encoder);
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
        Assert.Equal("c2.android.aac.encoder", settings.AudioCodec);
    }

    [Fact]
    public void DeriveDefaults_AudioSourceIsPlayback()
    {
        var probe = MakeProbe();
        var settings = ScrcpySettings.DeriveDefaults(probe);
        Assert.Equal("playback", settings.AudioSource);
    }
}
