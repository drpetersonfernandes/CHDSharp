using System;

namespace CHDSharpEncoder.Flac;

/// <summary>
/// Byte-for-byte port of libFLAC 1.4.3's stream encoder (as configured by MAME's chdman:
/// 2ch/16-bit/44100 Hz, fixed block size, compression level 8). Produces headerless FLAC frames
/// (no fLaC marker, no STREAMINFO) for a single input buffer. Every frame is exactly one block
/// of samples (the raw/cd hunks we encode always contain an exact multiple of the block size).
/// </summary>
internal sealed class LibFlacEncoder
{
    private readonly int blockSize;
    private const int BitsPerSample = 16;
    private const uint MaxLpcOrd = 12;
    // qlp_coeff_precision is 0 (auto) at libFLAC level 8; the encoder derives the real
    // precision from bits-per-sample and blocksize (see stream_encoder.c around line 764).
    private readonly uint QlpCoeffPrec;
    private const uint MaxPartOrder = 6;

    /// <summary>libFLAC's auto qlp_coeff_precision for bits-per-sample=16 (chdman level 8).</summary>
    private static uint ComputeQlpPrecision(int blockSize)
    {
        if (blockSize <= 192) return 7;
        if (blockSize <= 384) return 8;
        if (blockSize <= 576) return 9;
        if (blockSize <= 1152) return 10;
        if (blockSize <= 2304) return 11;
        if (blockSize <= 4608) return 12;
        return 13;
    }

    private readonly int[] signal0, signal1, mid, side;
    private readonly float[] window, windowed;
    private readonly double[] autoc, autocRoot, lpCoeff, lpcError;
    private readonly ulong[] absSum;
    private readonly Subframe[] sfW0, sfW1, sfMs0, sfMs1;
    private readonly PartitionedRiceContents[] rice0, rice1, riceM0, rice1b;
    private readonly LibFlacBitWriter bw;

    public LibFlacEncoder(int blockSize)
    {
        this.blockSize = blockSize;
        QlpCoeffPrec = ComputeQlpPrecision(blockSize);
        int maxSamples = blockSize;
        signal0 = new int[maxSamples + 4];
        signal1 = new int[maxSamples + 4];
        mid = new int[maxSamples + 4];
        side = new int[maxSamples + 4];
        window = new float[maxSamples];
        windowed = new float[maxSamples];
        autoc = new double[16];
        autocRoot = new double[16];
        lpCoeff = new double[32 * 32];
        lpcError = new double[32];
        absSum = new ulong[2 * maxSamples];
        sfW0 = [new Subframe(), new Subframe()];
        sfW1 = [new Subframe(), new Subframe()];
        sfMs0 = [new Subframe(), new Subframe()];
        sfMs1 = [new Subframe(), new Subframe()];
        rice0 = [new PartitionedRiceContents(), new PartitionedRiceContents()];
        rice1 = [new PartitionedRiceContents(), new PartitionedRiceContents()];
        riceM0 = [new PartitionedRiceContents(), new PartitionedRiceContents()];
        rice1b = [new PartitionedRiceContents(), new PartitionedRiceContents()];
        bw = new LibFlacBitWriter(maxSamples * 4 + 256);
    }

    public int Encode(byte[] output, ReadOnlySpan<byte> le)
    {
        int samplesPerCh = le.Length / 4;
        int frames = samplesPerCh / blockSize;
        int pos = 0;
        for (int f = 0; f < frames; f++)
        {
            Deinterleave(le, f * blockSize);
            pos += ProcessFrame(output, pos, f);
        }
        return pos;
    }

    private void Deinterleave(ReadOnlySpan<byte> input, int offset)
    {
        for (int i = 0; i < blockSize; i++)
        {
            int idx = (offset + i) * 4;
            signal0[i + 4] = (short)(input[idx] | (input[idx + 1] << 8));
            signal1[i + 4] = (short)(input[idx + 2] | (input[idx + 3] << 8));
        }
    }

    private int ProcessFrame(byte[] output, int outputPos, int frameIndex)
    {
        int maxPo = (int)Math.Min(MaxPartOrder, FlacBitMath.MaxRicePartitionOrderFromBlocksize((uint)blockSize));

        for (int i = 0; i < blockSize; i++)
        {
            side[i + 4] = signal0[i + 4] - signal1[i + 4];
            mid[i + 4] = (signal0[i + 4] + signal1[i + 4]) >> 1;
        }

        int w0 = GetWastedBits(signal0, blockSize);
        int w1 = GetWastedBits(signal1, blockSize);
        int wm = GetWastedBits(mid, blockSize);
        int ws = GetWastedBits(side, blockSize);
        int bps0 = BitsPerSample - Math.Min(w0, BitsPerSample);
        int bps1 = BitsPerSample - Math.Min(w1, BitsPerSample);
        int bpsm = BitsPerSample - Math.Min(wm, BitsPerSample);
        int bpss = BitsPerSample - Math.Min(ws, BitsPerSample) + 1;

        ProcessSubframe(signal0, bps0, w0, maxPo, sfW0, rice0, 0, out uint bi0, out uint bb0);
        ProcessSubframe(signal1, bps1, w1, maxPo, sfW1, rice1, 1, out uint bi1, out uint bb1);
        ProcessSubframe(mid, bpsm, wm, maxPo, sfMs0, riceM0, 0, out uint bmi0, out uint bmb0);
        ProcessSubframe(side, bpss, ws, maxPo, sfMs1, rice1b, 1, out uint bmi1, out uint bmb1);

        int ca = 0;
        uint minB = bb0 + bb1;
        if (bb0 + bmb1 < minB) { minB = bb0 + bmb1; ca = 1; }
        if (bb1 + bmb1 < minB) { minB = bb1 + bmb1; ca = 2; }
        if (bmb0 + bmb1 < minB) { ca = 3; }

        bw.Reset();
        WriteFrameHeader(frameIndex, ca);

        Subframe lsf, rsf;
        int lbs, rbs;
        switch (ca)
        {
            case 0: lsf = sfW0[bi0]; rsf = sfW1[bi1]; lbs = bps0; rbs = bps1; break;
            case 1: lsf = sfW0[bi0]; rsf = sfMs1[bmi1]; lbs = bps0; rbs = bpss; break;
            case 2: lsf = sfMs1[bmi1]; rsf = sfW1[bi1]; lbs = bpss; rbs = bps1; break;
            default: lsf = sfMs0[bmi0]; rsf = sfMs1[bmi1]; lbs = bpsm; rbs = bpss; break;
        }

        WriteSubframe(lsf, lbs);
        WriteSubframe(rsf, rbs);
        bw.ZeroPadToByteBoundary();
        bw.WriteRawUInt32(bw.GetWriteCrc16(), 16);

        int frameBytes = (bw.BitCount + 7) / 8;
        if (frameBytes > output.Length - outputPos)
            throw new InvalidOperationException($"FLAC frame too large: {frameBytes} bytes (buffer {output.Length - outputPos}). L={lsf.Type}/{lbs} R={rsf.Type}/{rbs}");

        return bw.CopyTo(output.AsSpan(outputPos));
    }

    private void ProcessSubframe(int[] sig, int bps, int wasted, int maxPo,
        Subframe[] sf, PartitionedRiceContents[] rice, int ch, out uint bestIdx, out uint bestBits)
    {
        uint riceLimit = 15; // RICE escape parameter for 16-bit
        bestIdx = 0;
        bestBits = VerbatimBits(sf[0], sig, bps, wasted);

        Span<float> rbps = stackalloc float[5];
        uint guessFixed = FlacLpcMath.FixedComputeBestPredictor(sig, 4, (uint)blockSize - 4, rbps);

        if (rbps[1] == 0f && IsConstant(sig, blockSize))
        {
            uint c = ConstantBits(sf[1], sig[4], bps, wasted);
            if (c < bestBits) { bestIdx = 1; bestBits = c; }
        }
        else
        {
            if (rbps[(int)guessFixed] < (float)bps && guessFixed < (uint)blockSize)
            {
                uint ci = bestIdx ^ 1;
                FlacLpcMath.FixedComputeResidual(sig, 4 + (int)guessFixed, (uint)blockSize - guessFixed, guessFixed, sf[ci].Residual.AsSpan(0, blockSize - (int)guessFixed));
                uint c = FixedBits(sf[ci], sig, bps, wasted, guessFixed, riceLimit, maxPo, rice[ci]);
                if (c < bestBits) { bestIdx = ci; bestBits = c; }
            }

            if (MaxLpcOrd > 0)
            {
                uint maxLpcThis = Math.Min(MaxLpcOrd, (uint)blockSize - 1);
                if (maxLpcThis > 0)
                {
                    // subdivide_tukey(3) apodization: full block + sub-block partial/punchout windows
                    float tukeyP = 0.5f / 3.0f;
                    FlacLpcMath.WindowTukey(window, blockSize, tukeyP);

                    // apodization state: a=apodization index, b=depth, c=part
                    int apA = 0, apB = 1, apC = 0;
                    while (apA < 1) // single subdivide_tukey apodization
                    {
                        if (apB == 1)
                        {
                            // full block window
                            FlacLpcMath.WindowData(sig.AsSpan(4), window, windowed, (uint)blockSize);
                            FlacLpcMath.ComputeAutocorrelation(windowed, (uint)blockSize, maxLpcThis + 1, autoc);
                            // libFLAC 1.4.3 quirk (apply_apodization_): the root copy moves only
                            // max_lpc_order (NOT +1) entries -- the dead for-loop around the memcpy
                            // changed nothing. autoc_root[maxLpcThis] stays stale, matching chdman.
                            Array.Copy(autoc, autocRoot, (int)maxLpcThis);
                            apB++;
                        }
                        else
                        {
                            // sub-block window
                            if (blockSize / apB <= FlacBitMath.MaxLpcOrder)
                            {
                                SetNextSubdivideTukey(3, ref apA, ref apB, ref apC);
                                continue;
                            }
                            if (apC % 2 == 0)
                            {
                                // partial window
                                FlacLpcMath.WindowDataPartial(sig.AsSpan(4), window, windowed, (uint)blockSize, (uint)(blockSize / apB / 2), (uint)(apC / 2 * blockSize / apB));
                                FlacLpcMath.ComputeAutocorrelation(windowed, (uint)(blockSize / apB), maxLpcThis + 1, autoc);
                            }
                            else
                            {
                                // punchout: root autocorrelation minus partial. libFLAC 1.4.3 only
                                // subtracts the first max_lpc_order entries, so autoc[maxLpcThis]
                                // keeps the partial window's value and feeds Levinson-Durbin as-is.
                                for (int ai = 0; ai < (int)maxLpcThis; ai++)
                                    autoc[ai] = autocRoot[ai] - autoc[ai];
                            }
                            SetNextSubdivideTukey(3, ref apA, ref apB, ref apC);
                        }

                        if (autoc[0] == 0.0)
                            continue;

                        uint maxOrd = maxLpcThis;
                        FlacLpcMath.ComputeLpCoefficients(autoc, ref maxOrd, new Span2D<double>(lpCoeff, 32), lpcError);
                        uint guessLpc = FlacLpcMath.ComputeBestOrder(lpcError, maxOrd, (uint)blockSize, (uint)(bps + QlpCoeffPrec));

                        double lrbps = FlacLpcMath.ComputeExpectedBitsPerResidualSample(lpcError[guessLpc - 1], (uint)blockSize - guessLpc);
                        if (lrbps >= (double)bps)
                            continue;

                        int[] qlp = new int[32];
                        if (!FlacLpcMath.QuantizeCoefficients(lpCoeff.AsSpan((int)((guessLpc - 1) * 32), (int)guessLpc), guessLpc, QlpCoeffPrec, qlp, out int quant))
                            continue;

                        uint ci = bestIdx ^ 1;
                        bool ok = true;
                        if (FlacLpcMath.MaxResidualBps((uint)bps, qlp, guessLpc, quant) > 32)
                            ok = FlacLpcMath.ComputeResidualFromQlpLimitResidual(sig, 4 + (int)guessLpc, (uint)blockSize - guessLpc, qlp, guessLpc, quant, sf[ci].Residual.AsSpan(0, blockSize - (int)guessLpc));
                        else if (FlacLpcMath.MaxPredictionBeforeShiftBps((uint)bps, qlp, guessLpc) <= 32)
                            FlacLpcMath.ComputeResidualFromQlp(sig, 4 + (int)guessLpc, (uint)blockSize - guessLpc, qlp, guessLpc, quant, sf[ci].Residual.AsSpan(0, blockSize - (int)guessLpc));
                        else
                            FlacLpcMath.ComputeResidualFromQlpWide(sig, 4 + (int)guessLpc, (uint)blockSize - guessLpc, qlp, guessLpc, quant, sf[ci].Residual.AsSpan(0, blockSize - (int)guessLpc));

                        if (!ok) continue;

                        uint c = LpcBits(sf[ci], sig, bps, wasted, guessLpc, quant, riceLimit, maxPo, rice[ci]);
                        if (c > 0 && c < bestBits)
                        {
                            bestIdx = ci;
                            bestBits = c;
                            Array.Copy(qlp, sf[ci].QlpCoeff, (int)guessLpc);
                        }
                    }
                }
            }
        }

        sf[bestIdx].WastedBits = wasted;
        if (sf[bestIdx].Type is SubframeType.Fixed or SubframeType.Lpc)
            for (int i = 0; i < sf[bestIdx].Order; i++) sf[bestIdx].Warmup[i] = sig[4 + i];
    }

    private uint VerbatimBits(Subframe sf, int[] sig, int bps, int wasted)
    {
        sf.Type = SubframeType.Verbatim;
        sf.WastedBits = wasted;
        for (int i = 0; i < blockSize; i++) sf.Samples[i] = sig[4 + i];
        return (uint)(8 + wasted + blockSize * bps);
    }

    private uint ConstantBits(Subframe sf, int val, int bps, int wasted)
    {
        sf.Type = SubframeType.Constant;
        sf.ConstantValue = val;
        sf.WastedBits = wasted;
        return (uint)(8 + wasted + bps);
    }

    private uint FixedBits(Subframe sf, int[] sig, int bps, int wasted, uint order, uint riceLimit, int maxPo, PartitionedRiceContents rice)
    {
        sf.Type = SubframeType.Fixed;
        sf.Order = (int)order;
        FindBestPartitionOrder(sf.Residual, order, riceLimit, maxPo, (uint)bps, rice, sf.EntropyCodingMethod);
        return (uint)(8 + wasted + order * bps) + sf.EntropyCodingMethod.Bits;
    }

    private uint LpcBits(Subframe sf, int[] sig, int bps, int wasted, uint order, int quant, uint riceLimit, int maxPo, PartitionedRiceContents rice)
    {
        sf.Type = SubframeType.Lpc;
        sf.Order = (int)order;
        sf.QlpCoeffPrecision = (int)QlpCoeffPrec;
        sf.QuantizationLevel = quant;
        FindBestPartitionOrder(sf.Residual, order, riceLimit, maxPo, (uint)bps, rice, sf.EntropyCodingMethod);
        return (uint)(8 + wasted + 4 + 5 + order * (QlpCoeffPrec + (uint)bps)) + sf.EntropyCodingMethod.Bits;
    }

    private void FindBestPartitionOrder(Span<int> residual, uint predictorOrder, uint riceLimit, int maxPo, uint bps, PartitionedRiceContents rice, EntropyCodingMethod ecm)
    {
        uint resSamples = (uint)blockSize - predictorOrder;
        maxPo = (int)Math.Min((uint)maxPo, FlacBitMath.MaxRicePartitionOrderLimited((uint)maxPo, (uint)blockSize, predictorOrder));

        PrecomputePartitionSums(residual, resSamples, predictorOrder, (uint)maxPo, bps);

        uint bestBits = 0;
        int bestPo = 0;
        uint sum = 0;
        for (int po = maxPo; po >= 0; po--)
        {
            if (!SetPartitionedRice(sum, resSamples, predictorOrder, riceLimit, (uint)po, out uint bits, out var parms))
                break;

            if (bestBits == 0 || bits < bestBits)
            {
                bestBits = bits;
                bestPo = po;
                for (int p = 0; p < (1 << po); p++) rice.Parameters[p] = parms[p];
            }
            sum += 1u << po;
        }

        ecm.Type = 0;
        ecm.PartitionOrder = (uint)bestPo;
        ecm.Bits = bestBits;
        for (int p = 0; p < (1 << bestPo); p++) ecm.RiceParams[p] = rice.Parameters[p];
    }

    private void PrecomputePartitionSums(Span<int> residual, uint resSamples, uint predOrder, uint maxPo, uint bps)
    {
        uint defaultPs = (resSamples + predOrder) >> (int)maxPo;
        uint partitions = 1u << (int)maxPo;

        uint threshold = 32 - FlacBitMath.ILog2(defaultPs);
        int end = -(int)predOrder;
        if (bps + FlacBitMath.MaxExtraResidualBps < threshold)
        {
            for (uint p = 0, s = 0; p < partitions; p++)
            {
                uint sum = 0; end += (int)defaultPs;
                for (; s < end; s++) sum += (uint)Math.Abs(residual[(int)s]);
                absSum[p] = sum;
            }
        }
        else
        {
            for (uint p = 0, s = 0; p < partitions; p++)
            {
                ulong sum = 0; end += (int)defaultPs;
                for (; s < end; s++) sum += (ulong)Math.Abs((long)residual[(int)s]);
                absSum[p] = sum;
            }
        }

        uint from = 0, to = partitions;
        for (int po = (int)maxPo - 1; po >= 0; po--)
        {
            partitions >>= 1;
            for (uint i = 0; i < partitions; i++)
            {
                absSum[to++] = absSum[from] + absSum[from + 1];
                from += 2;
            }
        }
    }

    private bool SetPartitionedRice(uint sumOffset, uint resSamples, uint predOrder, uint riceLimit, uint po, out uint bits, out uint[] parms)
    {
        uint totalBits = 6; // type(2) + partition_order(4)
        uint psBase = (resSamples + predOrder) >> (int)po;
        uint fpDiv = 0x40000 / psBase;
        parms = new uint[1 << (int)po];

        uint s = 0;
        for (uint part = 0; part < (1u << (int)po); part++)
        {
            uint ps = psBase;
            uint fpd;
            if (part > 0) { fpd = fpDiv; }
            else
            {
                if (ps <= predOrder) { bits = 0; return false; }
                ps -= predOrder;
                fpd = 0x40000 / ps;
            }

            ulong mean = absSum[sumOffset + part];
            uint rp;
            if (mean < 2 || (((mean - 1) * fpd) >> 18) == 0) rp = 0;
            else rp = FlacBitMath.ILog2Wide(((mean - 1) * fpd) >> 18) + 1;
            if (rp >= riceLimit) rp = riceLimit - 1;

            uint pb = 4 + (1 + rp) * ps + (rp != 0 ? (uint)(mean >> (int)(rp - 1)) : (uint)(mean << 1)) - (ps >> 1);
            parms[part] = rp;
            totalBits += pb;
            s += ps;
        }

        bits = totalBits;
        return true;
    }

    private static void SetNextSubdivideTukey(int parts, ref int a, ref int b, ref int c)
    {
        if (b == 2)
        {
            if (c == 0) c = 2;
            else { c = 0; b++; }
        }
        else if (c < 2 * b - 1)
        {
            c++;
        }
        else
        {
            c = 0;
            b++;
        }
        if (b > parts)
        {
            a++;
            b = 1;
            c = 0;
        }
    }

    private static bool IsConstant(int[] sig, int count)
    {
        for (int i = 1; i < count; i++)
            if (sig[i + 4] != sig[4]) return false;
        return true;
    }

    private static int GetWastedBits(int[] sig, int count)
    {
        int x = 0, i;
        for (i = 0; i < count && (x & 1) == 0; i++) x |= sig[i + 4];
        int shift = 0;
        if (x != 0) { while ((x & 1) == 0) { shift++; x >>= 1; } }
        if (shift > 0) for (i = 0; i < count; i++) sig[i + 4] >>= shift;
        return shift;
    }

    private void WriteFrameHeader(int frameNum, int ca)
    {
        bw.WriteRawUInt32(0x3FFE, 14);
        bw.WriteRawUInt32(0, 1);
        bw.WriteRawUInt32(0, 1);

        int bsCode = blockSize switch
        {
            192 => 1, 576 => 2, 1152 => 3, 2304 => 4, 4608 => 5,
            256 => 8, 512 => 9, 1024 => 10, 2048 => 11, 4096 => 12, 8192 => 13, 16384 => 14, 32768 => 15,
            _ => blockSize <= 256 ? 6 : 7
        };
        bw.WriteRawUInt32((uint)bsCode, 4);
        bw.WriteRawUInt32(9, 4); // 44100 Hz
        bw.WriteRawUInt32(ca switch { 0 => 1u, 1 => 8u, 2 => 9u, _ => 10u }, 4);
        bw.WriteRawUInt32(4, 3); // 16 bps
        bw.WriteRawUInt32(0, 1);
        bw.WriteUtf8UInt32((uint)frameNum);
        if (bsCode is 6 or 7) bw.WriteRawUInt32((uint)blockSize - 1, bsCode == 6 ? 8 : 16);
        bw.WriteRawUInt32(bw.GetWriteCrc8(), 8);
    }

    private void WriteSubframe(Subframe sf, int bps)
    {
        switch (sf.Type)
        {
            case SubframeType.Constant:
                bw.WriteRawUInt32(sf.WastedBits != 0 ? 1u : 0u, 8);
                if (sf.WastedBits != 0) bw.WriteUnaryUnsigned((uint)sf.WastedBits - 1);
                bw.WriteRawInt64(sf.ConstantValue, bps);
                break;

            case SubframeType.Verbatim:
                bw.WriteRawUInt32(0x02 | (sf.WastedBits != 0 ? 1u : 0u), 8);
                if (sf.WastedBits != 0) bw.WriteUnaryUnsigned((uint)sf.WastedBits - 1);
                for (int i = 0; i < blockSize; i++) bw.WriteRawInt64(sf.Samples[i], bps);
                break;

            case SubframeType.Fixed:
                bw.WriteRawUInt32(0x10 | ((uint)sf.Order << 1) | (sf.WastedBits != 0 ? 1u : 0u), 8);
                if (sf.WastedBits != 0) bw.WriteUnaryUnsigned((uint)sf.WastedBits - 1);
                for (int i = 0; i < sf.Order; i++) bw.WriteRawInt64(sf.Warmup[i], bps);
                WriteEntropy(sf.EntropyCodingMethod, sf.Residual.AsSpan(0, blockSize - sf.Order), sf.Order);
                break;

            case SubframeType.Lpc:
                bw.WriteRawUInt32(0x40 | ((uint)(sf.Order - 1) << 1) | (sf.WastedBits != 0 ? 1u : 0u), 8);
                if (sf.WastedBits != 0) bw.WriteUnaryUnsigned((uint)sf.WastedBits - 1);
                for (int i = 0; i < sf.Order; i++) bw.WriteRawInt64(sf.Warmup[i], bps);
                bw.WriteRawUInt32((uint)sf.QlpCoeffPrecision - 1, 4);
                bw.WriteRawInt32(sf.QuantizationLevel, 5);
                for (int i = 0; i < sf.Order; i++) bw.WriteRawInt32(sf.QlpCoeff[i], sf.QlpCoeffPrecision);
                WriteEntropy(sf.EntropyCodingMethod, sf.Residual.AsSpan(0, blockSize - sf.Order), sf.Order);
                break;
        }
    }

    private void WriteEntropy(EntropyCodingMethod ecm, Span<int> residual, int predOrder)
    {
        bw.WriteRawUInt32(ecm.Type, 2);
        bw.WriteRawUInt32(ecm.PartitionOrder, 4);
        int parts = 1 << (int)ecm.PartitionOrder;
        int k = 0, kLast = 0;
        int dps = (residual.Length + predOrder) >> (int)ecm.PartitionOrder;
        for (int i = 0; i < parts; i++)
        {
            int ps = dps;
            if (i == 0) ps -= predOrder;
            k += ps;
            bw.WriteRawUInt32(ecm.RiceParams[i], 4);
            bw.WriteRiceSignedBlock(residual.Slice(kLast, k - kLast), ps, ecm.RiceParams[i]);
            kLast = k;
        }
    }
}

internal enum SubframeType { Constant, Fixed, Lpc, Verbatim }

internal sealed class Subframe
{
    public SubframeType Type;
    public int WastedBits, ConstantValue, Order, QlpCoeffPrecision, QuantizationLevel;
    public readonly int[] Warmup = new int[32];
    public readonly int[] Residual = new int[1 << 14];
    public readonly int[] Samples = new int[1 << 14];
    public readonly int[] QlpCoeff = new int[32];
    public readonly EntropyCodingMethod EntropyCodingMethod = new();
}

internal sealed class EntropyCodingMethod
{
    public uint Type, PartitionOrder, Bits;
    public readonly uint[] RiceParams = new uint[1 << 15];
}

internal sealed class PartitionedRiceContents
{
    public readonly uint[] Parameters = new uint[1 << 15];
}