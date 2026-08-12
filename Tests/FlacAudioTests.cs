using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// Covers <see cref="FlacAudio"/>, the managed FLAC decoder the game needs because no shipped raylib native is
/// built with SUPPORT_FILEFORMAT_FLAC.
///
/// These run against the real shipped assets rather than a synthetic fixture: they are the files that actually
/// have to play, they are already in the repo, and a decoder regression that only shows up on real encoder
/// output (rather than on a toy stream) is exactly the kind this should catch.
///
/// The expected frame counts were cross-checked against ffmpeg, which decodes these bit-identically at 16 bits
/// — verified over the whole matrix of subframe types (constant / fixed / LPC / verbatim), both Rice partition
/// methods, mono and stereo, and 8–48 kHz.
/// </summary>
public class FlacAudioTests
{
    public FlacAudioTests() => TestEnvironment.UseRepoAssets();

    [Theory]
    [InlineData("defeat0.flac", 108478)]
    [InlineData("defeat1.flac", 85641)]
    [InlineData("finalDefeat.flac", 320906)]
    public void DecodesShippedAssetsToTheExpectedLength(string name, int expectedFrames)
    {
        FlacAudio.PcmSound pcm = FlacAudio.Decode(Assets.ReadAllBytes($"Assets/Sounds/{name}"));

        Assert.Equal(44100, pcm.SampleRate);
        Assert.Equal(2, pcm.Channels);
        Assert.Equal(expectedFrames, pcm.Samples.Length / pcm.Channels);
        // Interleaved, so the buffer must divide evenly by the channel count — a partial frame here would mean
        // a block was decoded at the wrong width.
        Assert.Equal(0, pcm.Samples.Length % pcm.Channels);
    }

    [Fact]
    public void DecodedAudioIsNotSilent()
    {
        // The failure this guards against is not a crash: a decoder that returns the right number of all-zero
        // samples looks fine to every other assertion here and is silent in game — which is precisely the
        // symptom that made the missing FLAC support hard to spot in the first place.
        FlacAudio.PcmSound pcm = FlacAudio.Decode(Assets.ReadAllBytes("Assets/Sounds/finalDefeat.flac"));

        int peak = 0;
        foreach (short sample in pcm.Samples)
            peak = Math.Max(peak, Math.Abs((int)sample));

        Assert.True(peak > 1000, $"decoded peak amplitude was {peak}; expected real audio");
    }

    [Fact]
    public void IsFlacMatchesOnExtensionAndIgnoresCase()
    {
        Assert.True(FlacAudio.IsFlac("Assets/Sounds/defeat0.flac"));
        Assert.True(FlacAudio.IsFlac("DEFEAT0.FLAC"));
        Assert.False(FlacAudio.IsFlac("Assets/Sounds/graze.ogg"));
        Assert.False(FlacAudio.IsFlac("flac.ogg"));
    }

    [Fact]
    public void RejectsNonFlacDataInsteadOfReturningGarbage()
    {
        // Gfx.LoadSound catches this and reports the file; what matters is that it throws rather than handing
        // back a plausible-looking buffer of noise.
        byte[] ogg = Assets.ReadAllBytes("Assets/Sounds/graze.ogg");
        Assert.Throws<InvalidDataException>(() => FlacAudio.Decode(ogg));
    }

    [Fact]
    public void RejectsTruncatedStream()
    {
        byte[] full = Assets.ReadAllBytes("Assets/Sounds/defeat1.flac");
        byte[] half = full[..(full.Length / 2)];

        // Cut mid-frame the reader runs off the end; either way it must fail loudly rather than return
        // half a sound.
        Assert.Throws<InvalidDataException>(() => FlacAudio.Decode(half));
    }
}
