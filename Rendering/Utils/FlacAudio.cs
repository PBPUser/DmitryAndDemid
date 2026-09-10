namespace DmitryAndDemid.Utils;

/// <summary>
/// A self-contained FLAC decoder, producing interleaved 16-bit PCM.
///
/// Why this exists: raylib only decodes FLAC when it is built with SUPPORT_FILEFORMAT_FLAC, which is OFF in
/// raylib's default config — and Raylib-cs ships stock natives. A .flac handed to Raylib.LoadSound therefore
/// matches no branch in LoadWave, comes back as an empty Wave, and becomes a sound that loads "successfully"
/// and plays silence. Enabling it upstream would mean rebuilding the native for every RID the game ships
/// (win-x64, win-x86, linux-x64, linux-arm64, …), so the decode happens here instead.
///
/// It is hand-rolled rather than a package reference on purpose: the csproj notes that desktop-only packages
/// have to be stripped for the SwitchBuild, and pure C# with no dependencies compiles everywhere the game
/// runs — desktop, Android, and the mono-nx interpreter.
///
/// Output is 16-bit because that is the one sample size every audio backend here accepts, and because raylib
/// resamples to the device format on load regardless. Sources deeper than 16 bits (the shipped assets are
/// 24-bit) are right-shifted with rounding; on short SFX at normal levels that difference is inaudible.
///
/// Scope: everything a standard encoder (libFLAC, ffmpeg) emits — CONSTANT / VERBATIM / FIXED / LPC
/// subframes, both Rice partition methods, all four channel assignments, and wasted-bits. Not supported:
/// Ogg-encapsulated FLAC (.oga), which has a different container entirely.
/// </summary>
public static class FlacAudio
{
    /// <summary>Interleaved signed 16-bit PCM, plus what is needed to play it back.</summary>
    public readonly record struct PcmSound(short[] Samples, int SampleRate, int Channels);

    public static bool IsFlac(string path) =>
        path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);

    private const int MaxChannels = 8;
    private const int MaxBlockSize = 65535;

    /// <summary>
    /// Decodes a complete FLAC stream. Throws <see cref="InvalidDataException"/> on anything malformed —
    /// callers treat that as "this file is not playable" rather than trying to continue with partial audio.
    /// </summary>
    public static PcmSound Decode(byte[] data)
    {
        int pos = FindMagic(data);
        if (pos < 0)
            throw new InvalidDataException("not a FLAC stream (no fLaC marker)");
        pos += 4;

        // ---- metadata blocks; only STREAMINFO matters, the rest (SEEKTABLE, VORBIS_COMMENT, PICTURE…) is skipped
        int sampleRate = 0, channels = 0, bitsPerSample = 0;
        long totalSamples = 0;
        bool sawStreamInfo = false, last = false;
        while (!last)
        {
            if (pos + 4 > data.Length)
                throw new InvalidDataException("truncated metadata header");
            last = (data[pos] & 0x80) != 0;
            int type = data[pos] & 0x7F;
            int length = (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            if (pos + length > data.Length)
                throw new InvalidDataException("truncated metadata block");
            if (type == 0)
            {
                if (length < 34)
                    throw new InvalidDataException("short STREAMINFO");
                BitReader si = new(data, pos);
                si.ReadBits(16);                        // min block size
                si.ReadBits(16);                        // max block size
                si.ReadBits(24);                        // min frame size
                si.ReadBits(24);                        // max frame size
                sampleRate = (int)si.ReadBits(20);
                channels = (int)si.ReadBits(3) + 1;
                bitsPerSample = (int)si.ReadBits(5) + 1;
                totalSamples = (long)si.ReadBitsLong(36);
                sawStreamInfo = true;
            }
            pos += length;
        }
        if (!sawStreamInfo)
            throw new InvalidDataException("no STREAMINFO block");
        if (sampleRate <= 0)
            throw new InvalidDataException("STREAMINFO declares no sample rate");
        if (channels < 1 || channels > MaxChannels)
            throw new InvalidDataException($"unsupported channel count {channels}");

        // totalSamples is 0 when the encoder did not know the length up front (piped input). Start with a
        // reasonable guess and let the list grow; when it IS known this allocates exactly once.
        List<short> output = new(totalSamples > 0
            ? checked((int)(totalSamples * channels))
            : Math.Max(4096, data.Length));

        int[][] buffers = new int[channels][];
        for (int c = 0; c < channels; c++)
            buffers[c] = new int[MaxBlockSize];

        BitReader r = new(data, pos);
        while (true)
        {
            // A frame starts on a byte boundary with a 14-bit sync code. Stop at end of stream, and tolerate
            // trailing junk (some taggers append bytes after the last frame) rather than throwing.
            r.AlignToByte();
            if (r.BytePosition + 2 > data.Length)
                break;
            if (data[r.BytePosition] != 0xFF || (data[r.BytePosition + 1] & 0xFC) != 0xF8)
                break;

            DecodeFrame(ref r, buffers, sampleRate, channels, bitsPerSample, out int blockSize);

            int shift = bitsPerSample - 16;
            for (int i = 0; i < blockSize; i++)
                for (int c = 0; c < channels; c++)
                    output.Add(ToPcm16(buffers[c][i], shift));

            if (totalSamples > 0 && output.Count >= totalSamples * channels)
                break;
        }

        if (output.Count == 0)
            throw new InvalidDataException("FLAC stream decoded to no audio");

        return new PcmSound(output.ToArray(), sampleRate, channels);
    }

    /// <summary>
    /// Narrows one decoded sample to 16 bits. <paramref name="shift"/> is <c>bitsPerSample - 16</c>: positive
    /// for deeper sources (round-to-nearest, then clamp — rounding a full-scale sample can carry past
    /// short.MaxValue), negative for shallower ones (scale up so an 8-bit source is not 48 dB quiet).
    /// </summary>
    private static short ToPcm16(int sample, int shift)
    {
        if (shift > 0)
        {
            int rounded = (sample + (1 << (shift - 1))) >> shift;
            return (short)Math.Clamp(rounded, short.MinValue, short.MaxValue);
        }
        if (shift < 0)
            return (short)Math.Clamp(sample << -shift, short.MinValue, short.MaxValue);
        return (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
    }

    /// <summary>An ID3 tag (or other junk) can precede the marker, so search rather than assume offset 0.</summary>
    private static int FindMagic(byte[] data)
    {
        for (int i = 0; i + 4 <= data.Length && i < 65536; i++)
            if (data[i] == 'f' && data[i + 1] == 'L' && data[i + 2] == 'a' && data[i + 3] == 'C')
                return i;
        return -1;
    }

    private static readonly int[] BlockSizeTable =
        { 0, 192, 576, 1152, 2304, 4608, 0, 0, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768 };

    private static readonly int[] SampleRateTable =
        { 0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000, 0, 0, 0, 0 };

    private static readonly int[] BitsPerSampleTable = { 0, 8, 12, 0, 16, 20, 24, 32 };

    private static void DecodeFrame(ref BitReader r, int[][] buffers, int streamRate, int streamChannels,
        int streamBits, out int blockSize)
    {
        if (r.ReadBits(14) != 0b11111111111110)
            throw new InvalidDataException("lost frame sync");
        r.ReadBits(1);                              // reserved
        r.ReadBits(1);                              // blocking strategy — the sample/frame number below covers both
        int blockSizeCode = (int)r.ReadBits(4);
        int sampleRateCode = (int)r.ReadBits(4);
        int channelAssignment = (int)r.ReadBits(4);
        int sampleSizeCode = (int)r.ReadBits(3);
        r.ReadBits(1);                              // reserved

        ReadUtf8Number(ref r);                      // frame or sample number; playback is sequential, so unused

        blockSize = blockSizeCode switch
        {
            0 => throw new InvalidDataException("reserved block size code"),
            6 => (int)r.ReadBits(8) + 1,            // stored after the header, not in the table
            7 => (int)r.ReadBits(16) + 1,
            _ => BlockSizeTable[blockSizeCode],
        };
        if (blockSize <= 0 || blockSize > MaxBlockSize)
            throw new InvalidDataException($"bad block size {blockSize}");

        switch (sampleRateCode)
        {
            case 12: r.ReadBits(8); break;          // kHz
            case 13: r.ReadBits(16); break;         // Hz
            case 14: r.ReadBits(16); break;         // tens of Hz
            case 15: throw new InvalidDataException("invalid sample rate code");
        }

        int frameBits = sampleSizeCode == 0 ? streamBits : BitsPerSampleTable[sampleSizeCode];
        if (frameBits == 0)
            throw new InvalidDataException("reserved sample size code");
        if (frameBits != streamBits)
            throw new InvalidDataException("per-frame sample size differs from STREAMINFO");

        int frameChannels = channelAssignment < 8 ? channelAssignment + 1 : 2;
        if (frameChannels != streamChannels)
            throw new InvalidDataException("per-frame channel count differs from STREAMINFO");

        r.ReadBits(8);                              // CRC-8 of the header; the container is trusted

        // In a stereo-decorrelated frame the difference channel needs one extra bit of range.
        for (int c = 0; c < frameChannels; c++)
        {
            int bits = frameBits + channelAssignment switch
            {
                8 => c == 1 ? 1 : 0,                // left / side
                9 => c == 0 ? 1 : 0,                // side / right
                10 => c == 1 ? 1 : 0,               // mid / side
                _ => 0,
            };
            DecodeSubframe(ref r, buffers[c], blockSize, bits);
        }

        r.AlignToByte();
        r.ReadBits(16);                             // CRC-16 of the frame

        UndoStereoDecorrelation(buffers, blockSize, channelAssignment);
    }

    private static void UndoStereoDecorrelation(int[][] b, int n, int channelAssignment)
    {
        switch (channelAssignment)
        {
            case 8:     // channel 0 is left, channel 1 is (left - right)
                for (int i = 0; i < n; i++)
                    b[1][i] = b[0][i] - b[1][i];
                break;
            case 9:     // channel 0 is (left - right), channel 1 is right
                for (int i = 0; i < n; i++)
                    b[0][i] += b[1][i];
                break;
            case 10:    // channel 0 is mid ((l+r)>>1), channel 1 is side (l-r)
                for (int i = 0; i < n; i++)
                {
                    int side = b[1][i];
                    // The encoder dropped mid's low bit; side's low bit carries it, so restore it before
                    // splitting. Without this every mid/side frame decodes one LSB off.
                    int mid = (b[0][i] << 1) | (side & 1);
                    b[0][i] = (mid + side) >> 1;
                    b[1][i] = (mid - side) >> 1;
                }
                break;
        }
    }

    private static void DecodeSubframe(ref BitReader r, int[] output, int blockSize, int bits)
    {
        if (r.ReadBits(1) != 0)
            throw new InvalidDataException("subframe padding bit set");
        int type = (int)r.ReadBits(6);

        // Wasted bits: the encoder shifted every sample right by `wasted` because those low bits were all
        // zero (common in upsampled or quiet material). Decode at the reduced depth, shift back at the end.
        int wasted = 0;
        if (r.ReadBits(1) != 0)
            wasted = r.ReadUnary() + 1;
        bits -= wasted;
        if (bits <= 0)
            throw new InvalidDataException("wasted bits exceed sample size");

        if (type == 0)
        {
            int constant = r.ReadSigned(bits);
            for (int i = 0; i < blockSize; i++)
                output[i] = constant;
        }
        else if (type == 1)
        {
            for (int i = 0; i < blockSize; i++)
                output[i] = r.ReadSigned(bits);
        }
        else if (type >= 8 && type <= 12)
        {
            DecodeFixed(ref r, output, blockSize, bits, type - 8);
        }
        else if (type >= 32)
        {
            DecodeLpc(ref r, output, blockSize, bits, type - 31);
        }
        else
        {
            throw new InvalidDataException($"reserved subframe type {type}");
        }

        if (wasted > 0)
            for (int i = 0; i < blockSize; i++)
                output[i] <<= wasted;
    }

    private static void DecodeFixed(ref BitReader r, int[] output, int blockSize, int bits, int order)
    {
        for (int i = 0; i < order; i++)
            output[i] = r.ReadSigned(bits);
        DecodeResidual(ref r, output, blockSize, order);

        // The fixed predictors are the 0th–4th order differences; each pass here is the running sum that
        // inverts one order of differencing.
        switch (order)
        {
            case 0: break;
            case 1:
                for (int i = 1; i < blockSize; i++) output[i] += output[i - 1];
                break;
            case 2:
                for (int i = 2; i < blockSize; i++)
                    output[i] += 2 * output[i - 1] - output[i - 2];
                break;
            case 3:
                for (int i = 3; i < blockSize; i++)
                    output[i] += 3 * output[i - 1] - 3 * output[i - 2] + output[i - 3];
                break;
            case 4:
                for (int i = 4; i < blockSize; i++)
                    output[i] += 4 * output[i - 1] - 6 * output[i - 2] + 4 * output[i - 3] - output[i - 4];
                break;
        }
    }

    private static void DecodeLpc(ref BitReader r, int[] output, int blockSize, int bits, int order)
    {
        for (int i = 0; i < order; i++)
            output[i] = r.ReadSigned(bits);

        int precision = (int)r.ReadBits(4) + 1;
        if (precision == 16)
            throw new InvalidDataException("invalid LPC precision");
        int shift = r.ReadSigned(5);
        if (shift < 0)
            throw new InvalidDataException("negative LPC shift");

        Span<int> coefficients = stackalloc int[32];
        for (int i = 0; i < order; i++)
            coefficients[i] = r.ReadSigned(precision);

        DecodeResidual(ref r, output, blockSize, order);

        // 64-bit accumulator: at 24-bit samples with 15-bit coefficients and order 32 the sum overflows
        // 32 bits, which shows up as loud clicks rather than as an error.
        for (int i = order; i < blockSize; i++)
        {
            long sum = 0;
            for (int j = 0; j < order; j++)
                sum += (long)coefficients[j] * output[i - 1 - j];
            output[i] += (int)(sum >> shift);
        }
    }

    private static void DecodeResidual(ref BitReader r, int[] output, int blockSize, int predictorOrder)
    {
        int method = (int)r.ReadBits(2);
        if (method > 1)
            throw new InvalidDataException($"reserved residual coding method {method}");
        int parameterBits = method == 0 ? 4 : 5;
        int escapeParameter = method == 0 ? 0xF : 0x1F;

        int partitionOrder = (int)r.ReadBits(4);
        int partitions = 1 << partitionOrder;
        if (blockSize % partitions != 0)
            throw new InvalidDataException("block size not divisible by partition count");
        int partitionSamples = blockSize >> partitionOrder;
        if (partitionSamples < predictorOrder)
            throw new InvalidDataException("first partition smaller than the predictor order");

        int index = predictorOrder;
        for (int p = 0; p < partitions; p++)
        {
            // The first partition is short by the warm-up samples already read into output[].
            int count = p == 0 ? partitionSamples - predictorOrder : partitionSamples;
            int parameter = (int)r.ReadBits(parameterBits);
            if (parameter == escapeParameter)
            {
                // Escape: the partition is stored as raw fixed-width samples instead of Rice-coded.
                int raw = (int)r.ReadBits(5);
                for (int i = 0; i < count; i++)
                    output[index++] = raw == 0 ? 0 : r.ReadSigned(raw);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    uint quotient = (uint)r.ReadUnary();
                    uint value = parameter == 0 ? quotient : (quotient << parameter) | (uint)r.ReadBits(parameter);
                    // zig-zag: LSB is the sign, so odd values are negative.
                    output[index++] = (int)(value >> 1) ^ -(int)(value & 1);
                }
            }
        }
    }

    /// <summary>
    /// The frame/sample number, stored in a UTF-8-like variable-length encoding (extended to 7 bytes, so it
    /// covers 36-bit sample numbers). Decoded only to advance past it.
    /// </summary>
    private static ulong ReadUtf8Number(ref BitReader r)
    {
        uint first = r.ReadBits(8);
        int extra;
        ulong value;
        if ((first & 0x80) == 0) return first;
        else if ((first & 0xE0) == 0xC0) { extra = 1; value = first & 0x1Fu; }
        else if ((first & 0xF0) == 0xE0) { extra = 2; value = first & 0x0Fu; }
        else if ((first & 0xF8) == 0xF0) { extra = 3; value = first & 0x07u; }
        else if ((first & 0xFC) == 0xF8) { extra = 4; value = first & 0x03u; }
        else if ((first & 0xFE) == 0xFC) { extra = 5; value = first & 0x01u; }
        else if (first == 0xFE) { extra = 6; value = 0; }
        else throw new InvalidDataException("bad frame number encoding");

        for (int i = 0; i < extra; i++)
        {
            uint b = r.ReadBits(8);
            if ((b & 0xC0) != 0x80)
                throw new InvalidDataException("bad frame number continuation byte");
            value = (value << 6) | (b & 0x3Fu);
        }
        return value;
    }

    /// <summary>Big-endian bit reader over a byte array — FLAC packs everything MSB-first.</summary>
    private struct BitReader(byte[] data, int offset)
    {
        private readonly byte[] Data = data;
        private int Byte = offset;
        private int Bit;    // bits already consumed from Data[Byte], counted from the MSB

        public readonly int BytePosition => Byte;

        public ulong ReadBitsLong(int count)
        {
            ulong value = 0;
            while (count > 0)
            {
                if (Byte >= Data.Length)
                    throw new InvalidDataException("ran off the end of the FLAC stream");
                int available = 8 - Bit;
                int take = Math.Min(available, count);
                int keep = available - take;
                value = (value << take) | (uint)((Data[Byte] >> keep) & ((1 << take) - 1));
                Bit += take;
                count -= take;
                if (Bit == 8) { Bit = 0; Byte++; }
            }
            return value;
        }

        public uint ReadBits(int count) => (uint)ReadBitsLong(count);

        /// <summary>Reads a two's-complement value of <paramref name="count"/> bits and sign-extends it.</summary>
        public int ReadSigned(int count)
        {
            if (count == 0)
                return 0;
            uint raw = ReadBits(count);
            uint sign = 1u << (count - 1);
            return (int)((raw ^ sign) - sign);
        }

        /// <summary>Counts zero bits up to and including the terminating one — Rice quotients are unary.</summary>
        public int ReadUnary()
        {
            int zeros = 0;
            while (ReadBitsLong(1) == 0)
            {
                // A corrupt stream can otherwise spin here until the end of the buffer; the largest legal
                // quotient is bounded by the sample depth many times over.
                if (++zeros > 1 << 20)
                    throw new InvalidDataException("runaway unary code");
            }
            return zeros;
        }

        public void AlignToByte()
        {
            if (Bit != 0) { Bit = 0; Byte++; }
        }
    }
}
