using System.Diagnostics;
using System.Text;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Flac;

namespace CHDSharpEncoderTest;

/// <summary>Debug helper: pinpoints the pcm16/flac byte-parity divergence vs chdman.</summary>
public class FlacPcm16Debug : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flac_dbg_" + Guid.NewGuid().ToString("N"));
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), "flac_dbg.txt");

    public FlacPcm16Debug() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void DumpDivergence()
    {
        // battle pcm16 corpus (seed 1337) — replicate TestDataGenerator.Pcm16 exactly
        var source = Pcm16(512 * 1024, 1337);

        string srcPath = Path.Combine(_dir, "pcm16.bin");
        string oursPath = Path.Combine(_dir, "pcm16.ours.chd");
        string refPath = Path.Combine(_dir, "pcm16.ref.chd");
        File.WriteAllBytes(srcPath, source);

        ChdEncoder.EncodeRaw(srcPath, oursPath, 4096, 512, [CodecTags.Flac]);
        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "flac", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed\n{cOut}{cErr}");

        var ours = ChdFile.Open(oursPath, out var oFile);
        var refs = ChdFile.Open(refPath, out var rFile);
        Assert.Equal(ChdError.Chderrnone, ours);
        Assert.Equal(ChdError.Chderrnone, refs);

        var sb = new StringBuilder();
        sb.AppendLine($"hunks ours={oFile!.HunkCount} ref={rFile!.HunkCount}");

        int firstDiff = -1;
        for (uint h = 0; h < oFile.HunkCount; h++)
        {
            byte[]? oRaw = oFile.ReadRawHunk(h);
            byte[]? rRaw = rFile.ReadRawHunk(h);
            if (oRaw == null || rRaw == null) continue;
            if (!oRaw.AsSpan().SequenceEqual(rRaw))
            {
                firstDiff = (int)h;
                sb.AppendLine($"FIRST DIFFING HUNK {h}: oursLen={oRaw.Length} refLen={rRaw.Length}");
                // strip marker byte
                var oFrame = oRaw.AsSpan(1).ToArray();
                var rFrame = rRaw.AsSpan(1).ToArray();
                DumpFrame(sb, "OURS", oFrame);
                DumpFrame(sb, "REF ", rFrame);
                break;
            }
        }
        if (firstDiff < 0) sb.AppendLine("NO DIFFERING HUNKS FOUND");

        File.WriteAllText(_logPath, sb.ToString());
        // regression check: pcm16 flac must now be byte-identical to chdman
        Assert.True(firstDiff < 0, $"pcm16 flac divergence regressed at hunk {firstDiff}; log={_logPath}");
    }

    [Fact]
    public void DiagnoseMidChannelAutocorrelation()
    {
        // battle pcm16 corpus (seed 1337); hunk 1 = bytes [4096, 8192)
        var source = Pcm16(512 * 1024, 1337);
        var hunk = source.AsSpan(4096, 4096);

        // deinterleave -> signal0/signal1 (mid = (s0+s1)>>1), wasted-bits shift
        var s0 = new int[1028];
        var s1 = new int[1028];
        var mid = new int[1028];
        for (int i = 0; i < 1024; i++)
        {
            int idx = i * 4;
            s0[i + 4] = (short)(hunk[idx] | (hunk[idx + 1] << 8));
            s1[i + 4] = (short)(hunk[idx + 2] | (hunk[idx + 3] << 8));
        }
        for (int i = 0; i < 1024; i++)
        {
            s1[i + 4] = (s0[i + 4] + s1[i + 4]) >> 1; // mid reused in s1[]; s0 left as ch0
        }

        const int blockSize = 1024;
        const int maxLpc = 12;
        int wasted = 0;
        for (int i = 0; i < blockSize && (wasted & 1) == 0; i++) wasted |= s1[i + 4];
        if (wasted != 0) { int sh = 0; while ((wasted & 1) == 0) { sh++; wasted >>= 1; } for (int i = 0; i < blockSize; i++) s1[i + 4] >>= sh; }
        int bps = 16;

        // subdivide_tukey(3): window + windowed full block
        var window = new float[blockSize];
        var windowed = new float[blockSize];
        WindowTukey(window, blockSize, 0.5f / 3.0f);
        for (int i = 0; i < blockSize; i++) windowed[i] = s1[i + 4] * window[i];

        var sb = new StringBuilder();
        sb.AppendLine($"mid hunk1 wastedBits={bps - 16} bps={bps}");

        // variant A: current port forward scalar (maxLag=14)
        var autoc = new double[14];
        AutoForward14(autoc, windowed, blockSize);
        DumpGuess(sb, "A portForward14", autoc, blockSize, maxLpc, bps);

        // variant B: libFLAC scalar exact (lag=13, j-major first loop, i from 13)
        var autocB = new double[14];
        AutoScalar13(autocB, windowed, blockSize);
        DumpGuess(sb, "B scalar13", autocB, blockSize, maxLpc, bps);

        // variant C: SSE2 backward (sum i from len-1 down to 0)
        var autocC = new double[14];
        AutoBackwardSse2(autocC, windowed, blockSize);
        DumpGuess(sb, "C sse2Backward", autocC, blockSize, maxLpc, bps);

        File.WriteAllText(_logPath, sb.ToString());
        Assert.Fail($"see {_logPath}");
    }

    [Fact]
    public void DiagnoseFullLpcSearch()
    {
        // battle pcm16 corpus (seed 1337); hunk 1 = bytes [4096, 8192)
        var source = Pcm16(512 * 1024, 1337);
        var hunk = source.AsSpan(4096, 4096);

        var s0 = new int[1028];
        var s1 = new int[1028];
        for (int i = 0; i < 1024; i++)
        {
            int idx = i * 4;
            s0[i + 4] = (short)(hunk[idx] | (hunk[idx + 1] << 8));
            s1[i + 4] = (short)(hunk[idx + 2] | (hunk[idx + 3] << 8));
        }
        for (int i = 0; i < 1024; i++) s1[i + 4] = (s0[i + 4] + s1[i + 4]) >> 1; // mid

        const int blockSize = 1024;
        const int maxLpc = 12;
        for (int i = 0; i < blockSize && (s1[i + 4] & 1) == 0; i++) { }
        int bps = 16;

        var window = new float[blockSize];
        var windowed = new float[blockSize];
        WindowTukey(window, blockSize, 0.5f / 3.0f);

        var sb = new StringBuilder();
        sb.AppendLine("full subdivide_tukey(3) LPC search for mid hunk1");

        var variants = new (string Label, Action<double[], float[], int> Fn)[]
        {
            ("A portForward14", AutoForward14),
            ("B scalar13", AutoScalar13),
            ("C sse2Backward", AutoBackwardSse2),
        };

        foreach (var (label, fn) in variants)
        {
            sb.AppendLine($"--- {label} ---");
            var result = FullLpcSearch(s1, window, windowed, blockSize, maxLpc, bps, fn);
            sb.AppendLine($"  BEST: order={result.BestOrder} bits={result.BestBits}");
            foreach (var c in result.Candidates)
                sb.AppendLine($"  cand apB={c.B} apC={c.C} guess={c.Guess} order={c.Order} lrbps={c.Lrbps:F6} bits={c.Bits}");
        }

        File.WriteAllText(_logPath, sb.ToString());
        Assert.Fail($"see {_logPath}");
    }

    [Fact]
    public void CaptureEncoderCandidates()
    {
        var source = Pcm16(512 * 1024, 1337);
        var hunk = source.AsSpan(4096, 4096).ToArray();
        var swapped = new byte[hunk.Length];
        for (int i = 0; i < hunk.Length; i += 2) { swapped[i] = hunk[i + 1]; swapped[i + 1] = hunk[i]; }

        var encoder = new LibFlacEncoder(1024);
        var candidates = new StringBuilder();
        LibFlacEncoder.DebugCandidateHook = (name, order, bits) =>
            candidates.AppendLine($"  {name}: order={order} bits={bits}");
        LibFlacEncoder.DebugResultHook = (ch, idx, bits, type, order) =>
            candidates.AppendLine($"  RESULT ch{ch}: idx={idx} bits={bits} type={type} order={order}");

        var leOut = new byte[4096 * 2];
        var beOut = new byte[4096 * 2];
        candidates.AppendLine("=== LE pass ===");
        int leLen = encoder.Encode(leOut, hunk);
        candidates.AppendLine("=== BE pass ===");
        int beLen = encoder.Encode(beOut, swapped);
        candidates.AppendLine($"leLen={leLen} beLen={beLen}");

        // parse the stored frame structure (LE wins)
        var frameSb = new StringBuilder();
        DumpFrame(frameSb, "STORED", leOut.AsSpan(0, leLen).ToArray());
        candidates.Append(frameSb);

        File.WriteAllText(_logPath, candidates.ToString());
        Assert.Fail($"see {_logPath}");
    }

    [Fact]
    public void DumpBothFrames()
    {
        var source = Pcm16(512 * 1024, 1337);

        string srcPath = Path.Combine(_dir, "pcm16.bin");
        string oursPath = Path.Combine(_dir, "pcm16.ours.chd");
        string refPath = Path.Combine(_dir, "pcm16.ref.chd");
        File.WriteAllBytes(srcPath, source);
        ChdEncoder.EncodeRaw(srcPath, oursPath, 4096, 512, [CodecTags.Flac]);
        var (createExit, cOut, cErr) = RunChdman("createraw", "-i", srcPath, "-o", refPath, "-c", "flac", "-hs", "4096", "-us", "512", "-f");
        Assert.True(createExit == 0, $"chdman createraw failed\n{cOut}{cErr}");

        var ours = ChdFile.Open(oursPath, out var oFile);
        var refs = ChdFile.Open(refPath, out var rFile);
        Assert.Equal(ChdError.Chderrnone, ours);
        Assert.Equal(ChdError.Chderrnone, refs);

        var oRaw = oFile!.ReadRawHunk(1)!;
        var rRaw = rFile!.ReadRawHunk(1)!;
        var sb = new StringBuilder();
        sb.AppendLine($"oursLen={oRaw.Length} refLen={rRaw.Length}");
        sb.AppendLine("ours first bytes: " + string.Join(" ", oRaw.AsSpan(0, Math.Min(16, oRaw.Length)).ToArray().Select(b => b.ToString("X2"))));
        sb.AppendLine("ref first bytes:  " + string.Join(" ", rRaw.AsSpan(0, Math.Min(16, rRaw.Length)).ToArray().Select(b => b.ToString("X2"))));
        sb.AppendLine("=== OURS frame ===");
        DecodeFullFrame(sb, oRaw.AsSpan(1).ToArray());
        sb.AppendLine("=== REF frame ===");
        DecodeFullFrame(sb, rRaw.AsSpan(1).ToArray());
        File.WriteAllText(_logPath, sb.ToString());
        Assert.Fail($"see {_logPath}");
    }

    private static void DecodeFullFrame(StringBuilder sb, byte[] frame)
    {
        var br = new BitReader(frame);
        uint sync = br.Read(14);
        br.Read(1); // reserved
        uint bsStrategy = br.Read(1);
        uint bsCode = br.Read(4);
        uint srCode = br.Read(4);
        uint chMode = br.Read(4);
        uint bpsCode = br.Read(3);
        br.Read(1);
        uint fn = br.ReadUtf8();
        uint blocksize = 0;
        if (bsCode == 6) blocksize = br.Read(8) + 1;
        else if (bsCode == 7) blocksize = br.Read(16) + 1;
        else blocksize = bsCode switch { 1 => 192u, 2 => 576u, 3 => 1152u, 4 => 2304u, 5 => 4608u, 8 => 256u, 9 => 512u, 10 => 1024u, 11 => 2048u, 12 => 4096u, 13 => 8192u, 14 => 16384u, 15 => 32768u, _ => 0u };
        br.Read(8); // crc8

        sb.AppendLine($"sync={sync:x} bsCode={bsCode} blocksize={blocksize} sr={srCode} chMode={chMode} bpsCode={bpsCode} fn={fn}");
        int frameBps = bpsCode switch { 1 => 8, 2 => 12, 4 => 16, 5 => 20, 6 => 24, 7 => 32, _ => 0 };
        string[] chNames = chMode switch
        {
            0 or 1 => ["L", "R"],
            8 => ["L", "S"],
            9 => ["R", "S"],
            10 => ["M", "S"],
            _ => ["?", "?"]
        };
        for (int ch = 0; ch < 2; ch++)
        {
            uint x = br.Read(8);
            uint wasted = x & 1;
            x &= 0xFE;
            int subframeBps = frameBps - (int)wasted + (ch == 1 && chMode is 8 or 9 or 10 ? 1 : 0);
            if (wasted == 1)
            {
                uint u = 0; while (br.Read(1) == 1) u++;
                sb.AppendLine($"  ch{ch} ({chNames[ch]}) wastedBits={u + 1} subframeBps={subframeBps}");
            }
            else
                sb.AppendLine($"  ch{ch} ({chNames[ch]}) subframeBps={subframeBps}");

            if (x == 0)
            {
                sb.AppendLine($"    CONST val={br.ReadSigned(subframeBps)}");
            }
            else if (x == 2)
            {
                sb.AppendLine($"    VERBATIM {blocksize} samples");
                for (int i = 0; i < (int)blocksize; i++) br.ReadSigned(subframeBps);
            }
            else if (x is >= 16 and <= 24)
            {
                uint order = (x >> 1) & 7;
                sb.AppendLine($"    FIXED o={order}");
                for (int i = 0; i < (int)order; i++) br.ReadSigned(subframeBps);
                ReadEntropy(sb, br, blocksize, order);
            }
            else
            {
                uint order = ((x >> 1) & 31) + 1;
                sb.AppendLine($"    LPC o={order}");
                for (int i = 0; i < (int)order; i++) br.ReadSigned(subframeBps);
                uint prec = br.Read(4) + 1;
                int shift = br.ReadSigned(5);
                sb.AppendLine($"      qlp_prec={prec} shift={shift}");
                var coeffs = new int[order];
                for (int i = 0; i < order; i++) coeffs[i] = br.ReadSigned((int)prec);
                sb.AppendLine($"      coeffs=[{string.Join(",", coeffs)}]");
                ReadEntropy(sb, br, blocksize, order);
            }
        }
    }

    private static void ReadEntropy(StringBuilder sb, BitReader br, uint blocksize, uint predOrder)
    {
        uint type = br.Read(2);
        uint po = br.Read(4);
        sb.AppendLine($"      entropy type={type} po={po}");
        int parts = 1 << (int)po;
        int dps = (int)(blocksize >> (int)po);
        int k = 0, kLast = 0;
        var rices = new uint[parts];
        for (int p = 0; p < parts; p++)
        {
            int ps = dps;
            if (p == 0) ps -= (int)predOrder;
            k += ps;
            uint rice = br.Read(type == 0 ? 4 : 5);
            rices[p] = rice;
            if (rice >= (type == 0 ? 15u : 31u))
                br.Read(ps * 5); // escape: raw bits
            else
                for (int s = kLast; s < k; s++)
                {
                    while (br.Read(1) == 1) { }
                    if (rice > 0) br.Read((int)rice);
                }
            kLast = k;
        }
        sb.AppendLine($"        rices=[{string.Join(",", rices)}] pos={br.PositionBits}");
    }

    private sealed class BitReader
    {
        private readonly byte[] _b;
        private long _bit;

        public BitReader(byte[] b) => _b = b;

        public uint Read(int n)
        {
            uint v = 0;
            for (int i = 0; i < n; i++)
            {
                if (_bit >= _b.Length * 8L) { _bit++; continue; }
                int byteIdx = (int)(_bit >> 3);
                int bitIdx = 7 - (int)(_bit & 7);
                v = (v << 1) | (uint)((_b[byteIdx] >> bitIdx) & 1);
                _bit++;
            }
            return v;
        }

        public long PositionBits => _bit;

        public uint ReadUtf8()
        {
            uint x = Read(8);
            if ((x & 0x80) == 0) return x;
            if ((x & 0xE0) == 0xC0) return ((x & 0x1F) << 6) | Read(6);
            if ((x & 0xF0) == 0xE0) return ((x & 0x0F) << 12) | (Read(6) << 6) | Read(6);
            return ((x & 0x07) << 18) | (Read(6) << 12) | (Read(6) << 6) | Read(6);
        }

        public int ReadSigned(int n)
        {
            uint v = Read(n);
            return (int)(v - (1u << (n - 1)));
        }
    }

    private static (int BestOrder, long BestBits, List<(int B, int C, uint Guess, uint Order, double Lrbps, long Bits)> Candidates)
        FullLpcSearch(int[] sig, float[] window, float[] windowed, int blockSize, uint maxLpc, int bps,
            Action<double[], float[], int> autocFn)
    {
        var autoc = new double[14];
        var autocRoot = new double[14];
        var lpCoeff = new double[32 * 32];
        var lpcError = new double[32];

        long bestBits = long.MaxValue;
        int bestOrder = 0;
        var candidates = new List<(int, int, uint, uint, double, long)>();

        int apA = 0, apB = 1, apC = 0;
        uint maxLpcThis = Math.Min(maxLpc, (uint)blockSize - 1);
        while (apA < 1)
        {
            if (apB == 1)
            {
                for (int i = 0; i < blockSize; i++) windowed[i] = sig[i + 4] * window[i];
                autocFn(autoc, windowed, blockSize);
                Array.Copy(autoc, autocRoot, (int)maxLpcThis + 1);
                apB++;
            }
            else
            {
                if (blockSize / apB <= 32)
                {
                    SetNextSubdivideTukey(3, ref apA, ref apB, ref apC);
                    continue;
                }
                if (apC % 2 == 0)
                {
                    int partSize = blockSize / apB / 2;
                    int dataShift = apC / 2 * blockSize / apB;
                    for (int i = 0; i < blockSize; i++) windowed[i] = 0f;
                    for (int i = 0; i < partSize && dataShift + i < blockSize; i++)
                        windowed[i] = sig[dataShift + i + 4] * window[i];
                    int i2 = Math.Min(partSize, blockSize - partSize - dataShift);
                    for (int j = blockSize - partSize; j < blockSize; i2++, j++)
                        if (dataShift + i2 < blockSize) windowed[i2] = sig[dataShift + i2 + 4] * window[j];
                    autocFn(autoc, windowed, blockSize / apB);
                }
                else
                {
                    for (int ai = 0; ai < (int)maxLpcThis + 1; ai++)
                        autoc[ai] = autocRoot[ai] - autoc[ai];
                }
                SetNextSubdivideTukey(3, ref apA, ref apB, ref apC);
            }

            if (autoc[0] == 0.0) continue;

            uint maxOrd = maxLpcThis;
            FlacLpcMath.ComputeLpCoefficients(autoc, ref maxOrd, new Span2D<double>(lpCoeff, 32), lpcError);
            uint guess = FlacLpcMath.ComputeBestOrder(lpcError, maxOrd, (uint)blockSize, (uint)(bps + 10));

            double lrbps = FlacLpcMath.ComputeExpectedBitsPerResidualSample(lpcError[guess - 1], (uint)blockSize - guess);
            if (lrbps >= bps) continue;

            var qlp = new int[32];
            if (!FlacLpcMath.QuantizeCoefficients(lpCoeff.AsSpan((int)((guess - 1) * 32), (int)guess), guess, 10, qlp, out int quant))
                continue;

            bool ok = true;
            var residual = new int[blockSize - (int)guess];
            if (FlacLpcMath.MaxResidualBps((uint)bps, qlp, guess, quant) > 32)
                ok = FlacLpcMath.ComputeResidualFromQlpLimitResidual(sig, 4 + (int)guess, (uint)blockSize - guess, qlp, guess, quant, residual);
            else if (FlacLpcMath.MaxPredictionBeforeShiftBps((uint)bps, qlp, guess) <= 32)
                FlacLpcMath.ComputeResidualFromQlp(sig, 4 + (int)guess, (uint)blockSize - guess, qlp, guess, quant, residual);
            else
                FlacLpcMath.ComputeResidualFromQlpWide(sig, 4 + (int)guess, (uint)blockSize - guess, qlp, guess, quant, residual);
            if (!ok) continue;

            int maxPo = (int)Math.Min(6, MaxRicePartitionOrderFromBlocksize((uint)blockSize));
            long bits = 8 + 4 + 5 + guess * (10u + (uint)bps) + BestPartitionBits(residual, guess, 15, maxPo, (uint)bps);
            candidates.Add((apB, apC, guess, guess, lrbps, bits));
            if (bits < bestBits) { bestBits = bits; bestOrder = (int)guess; }
        }

        return (bestOrder, bestBits, candidates);
    }

    private static void SetNextSubdivideTukey(int parts, ref int a, ref int b, ref int c)
    {
        if (b == 2) { if (c == 0) c = 2; else { c = 0; b++; } }
        else if (c < 2 * b - 1) c++;
        else { c = 0; b++; }
        if (b > parts) { a++; b = 1; c = 0; }
    }

    private static uint MaxRicePartitionOrderFromBlocksize(uint blocksize)
    {
        uint maxOrder = 0;
        while ((blocksize & 1) == 0) { maxOrder++; blocksize >>= 1; }
        return Math.Min(15u, maxOrder);
    }

    private static uint ILog2(uint v)
    {
        uint l = 0;
        while ((v >>= 1) != 0) l++;
        return l;
    }

    private static uint ILog2Wide(ulong v)
    {
        uint l = 0;
        while ((v >>= 1) != 0) l++;
        return l;
    }

    private static long BestPartitionBits(int[] residual, uint predOrder, uint riceLimit, int maxPo, uint bps)
    {
        uint resSamples = 1024 - predOrder;
        maxPo = (int)Math.Min((uint)maxPo, MaxRicePartitionOrderFromBlocksize(1024));
        var absSum = new ulong[1 << 15];
        var parameters = new uint[1 << 15];

        uint defaultPs = (resSamples + predOrder) >> maxPo;
        uint partitions = 1u << maxPo;
        uint threshold = 32 - ILog2(defaultPs);
        int end = -(int)predOrder;
        if (bps + 4 < threshold)
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
        for (int po = maxPo - 1; po >= 0; po--)
        {
            partitions >>= 1;
            for (uint i = 0; i < partitions; i++)
            {
                absSum[to++] = absSum[from] + absSum[from + 1];
                from += 2;
            }
        }

        long bestBits = 0;
        uint sumOff = 0;
        for (int po = maxPo; po >= 0; po--)
        {
            uint totalBits = 6;
            uint psBase = (resSamples + predOrder) >> po;
            uint fpDiv = 0x40000 / psBase;
            uint s = 0;
            bool ok = true;
            for (uint part = 0; part < (1u << po); part++)
            {
                uint ps = psBase;
                uint fpd;
                if (part > 0) fpd = fpDiv;
                else
                {
                    if (ps <= predOrder) { ok = false; break; }
                    ps -= predOrder;
                    fpd = 0x40000 / ps;
                }
                ulong mean = absSum[sumOff + part];
                uint rp;
                if (mean < 2 || (((mean - 1) * fpd) >> 18) == 0) rp = 0;
                else rp = ILog2Wide(((mean - 1) * fpd) >> 18) + 1;
                if (rp >= riceLimit) rp = riceLimit - 1;
                uint pb = 4 + (1 + rp) * ps + (rp != 0 ? (uint)(mean >> (int)(rp - 1)) : (uint)(mean << 1)) - (ps >> 1);
                totalBits += pb;
                s += ps;
            }
            if (!ok) { sumOff += 1u << po; continue; }
            if (bestBits == 0 || totalBits < bestBits) bestBits = totalBits;
            sumOff += 1u << po;
        }
        return bestBits;
    }

    [Fact]
    public void DumpCandidateCoeffs()
    {
        var source = Pcm16(512 * 1024, 1337);
        var hunk = source.AsSpan(4096, 4096);
        var s0 = new int[1028];
        var s1 = new int[1028];
        for (int i = 0; i < 1024; i++)
        {
            int idx = i * 4;
            s0[i + 4] = (short)(hunk[idx] | (hunk[idx + 1] << 8));
            s1[i + 4] = (short)(hunk[idx + 2] | (hunk[idx + 3] << 8));
        }
        for (int i = 0; i < 1024; i++) s1[i + 4] = (s0[i + 4] + s1[i + 4]) >> 1; // mid

        const int blockSize = 1024;
        const int bps = 16;
        var window = new float[blockSize];
        var windowed = new float[blockSize];
        WindowTukey(window, blockSize, 0.5f / 3.0f);

        var sb = new StringBuilder();
        var variants = new (string Label, Action<double[], float[], int> Fn)[]
        {
            ("A portForward14", AutoForward14),
            ("C sse2Backward", AutoBackwardSse2),
        };

        foreach (var (label, fn) in variants)
        {
            sb.AppendLine($"--- {label} ---");
            var cands = LpcSearchWithCoeffs(s1, window, windowed, blockSize, 12, bps, fn);
            foreach (var c in cands)
                sb.AppendLine($"  apB={c.B} apC={c.C} guess={c.Guess} coeffs=[{string.Join(",", c.Coeffs)}]");
        }

        sb.AppendLine("--- STORED OURS mid: order=12 coeffs=[-140,-246,-317,-310,-425,-402,-511,-438,495,423,431,285]");
        sb.AppendLine("--- STORED REF mid:  order=10 coeffs=[-73,-205,-294,-308,-415,-432,476,500,380,277]");
        File.WriteAllText(_logPath, sb.ToString());
        Assert.Fail($"see {_logPath}");
    }

    private static List<(int B, int C, uint Guess, int[] Coeffs)> LpcSearchWithCoeffs(int[] sig, float[] window, float[] windowed,
        int blockSize, uint maxLpc, int bps, Action<double[], float[], int> autocFn)
    {
        var autoc = new double[14];
        var autocRoot = new double[14];
        var lpCoeff = new double[32 * 32];
        var lpcError = new double[32];
        var result = new List<(int, int, uint, int[])>();

        int apA = 0, apB = 1, apC = 0;
        uint maxLpcThis = Math.Min(maxLpc, (uint)blockSize - 1);
        while (apA < 1)
        {
            if (apB == 1)
            {
                for (int i = 0; i < blockSize; i++) windowed[i] = sig[i + 4] * window[i];
                autocFn(autoc, windowed, blockSize);
                Array.Copy(autoc, autocRoot, (int)maxLpcThis + 1);
                apB++;
            }
            else
            {
                if (blockSize / apB <= 32) { SetNextSubdivideTukey(3, ref apA, ref apB, ref apC); continue; }
                if (apC % 2 == 0)
                {
                    int partSize = blockSize / apB / 2;
                    int dataShift = apC / 2 * blockSize / apB;
                    for (int i = 0; i < blockSize; i++) windowed[i] = 0f;
                    for (int i = 0; i < partSize && dataShift + i < blockSize; i++)
                        windowed[i] = sig[dataShift + i + 4] * window[i];
                    int i2 = Math.Min(partSize, blockSize - partSize - dataShift);
                    for (int j = blockSize - partSize; j < blockSize; i2++, j++)
                        if (dataShift + i2 < blockSize) windowed[i2] = sig[dataShift + i2 + 4] * window[j];
                    autocFn(autoc, windowed, blockSize / apB);
                }
                else
                {
                    for (int ai = 0; ai < (int)maxLpcThis + 1; ai++)
                        autoc[ai] = autocRoot[ai] - autoc[ai];
                }
                SetNextSubdivideTukey(3, ref apA, ref apB, ref apC);
            }

            if (autoc[0] == 0.0) continue;

            uint maxOrd = maxLpcThis;
            FlacLpcMath.ComputeLpCoefficients(autoc, ref maxOrd, new Span2D<double>(lpCoeff, 32), lpcError);
            uint guess = FlacLpcMath.ComputeBestOrder(lpcError, maxOrd, (uint)blockSize, (uint)(bps + 10));
            double lrbps = FlacLpcMath.ComputeExpectedBitsPerResidualSample(lpcError[guess - 1], (uint)blockSize - guess);
            if (lrbps >= bps) continue;

            var qlp = new int[32];
            if (!FlacLpcMath.QuantizeCoefficients(lpCoeff.AsSpan((int)((guess - 1) * 32), (int)guess), guess, 10, qlp, out _))
                continue;
            result.Add((apB, apC, guess, qlp.AsSpan(0, (int)guess).ToArray()));
        }
        return result;
    }

    private static void DumpGuess(StringBuilder sb, string label, double[] autoc, uint blockSize, uint maxLpc, int bps)
    {
        var lpCoeff = new double[32 * 32];
        var lpcError = new double[32];
        uint maxOrd = maxLpc;
        FlacLpcMath.ComputeLpCoefficients(autoc, ref maxOrd, new Span2D<double>(lpCoeff, 32), lpcError);
        uint guess = FlacLpcMath.ComputeBestOrder(lpcError, maxOrd, blockSize, (uint)(bps + 10));
        sb.AppendLine($"{label}: maxOrd={maxOrd} guess={guess}");
        for (int i = 0; i < (int)maxOrd; i++)
            sb.AppendLine($"  lpcError[{i}] = {lpcError[i]:R}");
    }

    private static void AutoForward14(double[] autoc, float[] data, int dataLen)
    {
        const int maxLag = 14;
        for (int i = 0; i < maxLag; i++) autoc[i] = 0.0;
        for (int i = 0; i < maxLag; i++)
            for (int j = 0; j <= i; j++)
                autoc[j] += (double)data[i] * data[i - j];
        for (int i = maxLag; i < dataLen; i++)
            for (int j = 0; j < maxLag; j++)
                autoc[j] += (double)data[i] * data[i - j];
    }

    private static void AutoScalar13(double[] autoc, float[] data, int dataLen)
    {
        const uint lag = 13;
        for (int i = 0; i < (int)lag; i++) autoc[i] = 0.0;
        for (int j = 0; j < (int)lag; j++)
            for (int i = j; i < (int)lag; i++)
                autoc[i - j] += (double)data[j] * data[i];
        for (int i = (int)lag; i < dataLen; i++)
            for (int j = 0; j < (int)lag; j++)
                autoc[j] += (double)data[i] * data[i - j];
    }

    private static void AutoBackwardSse2(double[] autoc, float[] data, int dataLen)
    {
        const int maxLag = 14;
        for (int i = 0; i < maxLag; i++) autoc[i] = 0.0;
        for (int i = dataLen - 1; i >= 0; i--)
            for (int j = 0; j < maxLag; j++)
                if (i - j >= 0)
                    autoc[j] += (double)data[i] * data[i - j];
    }

    private static void WindowTukey(Span<float> window, int length, float p)
    {
        int np = (int)(p / 2.0f * length) - 1;
        for (int n = 0; n < length; n++) window[n] = 1.0f;
        if (np > 0)
        {
            for (int n = 0; n <= np; n++)
            {
                window[n] = (float)(0.5f - 0.5f * Math.Cos(Math.PI * n / np));
                window[length - np - 1 + n] = (float)(0.5f - 0.5f * Math.Cos(Math.PI * (n + np) / np));
            }
        }
    }

    private static void DumpFrame(StringBuilder sb, string tag, byte[] frame)
    {
        // minimal FLAC frame header + subframe-type parser
        uint bit = 0;
        uint Read(int n)
        {
            uint v = 0;
            for (int i = 0; i < n; i++)
            {
                int byteIdx = (int)(bit >> 3);
                int bitIdx = 7 - (int)(bit & 7);
                int b = (frame[byteIdx] >> bitIdx) & 1;
                v = (v << 1) | (uint)b;
                bit++;
            }
            return v;
        }

        uint sync = Read(14);
        Read(1); // variable blocksize flag (reserved 0)
        uint bsStrategy = Read(1);
        uint bsCode = Read(4);
        uint srCode = Read(4);
        uint chMode = Read(4);
        uint bpsCode = Read(3);
        Read(1); // reserved 0
        // frame number (utf8, but for small numbers 1 byte)
        uint fn = Read(8);
        if (bsCode == 6) Read(8);
        else if (bsCode == 7) Read(16);
        Read(8); // header crc

        string[] chNames = chMode switch
        {
            0 => ["L", "R"],
            1 => ["L", "R"],
            2 => ["L", "R"],
            3 => ["L", "R"],
            8 => ["L", "S"],
            9 => ["R", "S"],
            10 => ["M", "S"],
            _ => ["?", "?"]
        };

        sb.AppendLine($"  [{tag}] sync={sync:x} bsCode={bsCode} sr={srCode} chMode={chMode} bps={bpsCode} fn={fn}");
        for (int ch = 0; ch < 2; ch++)
        {
            uint x = Read(8);
            uint wasted = x & 1;
            x &= 0xFE;
            string kind;
            if (x == 0) kind = "CONST";
            else if (x == 2) kind = "VERBATIM";
            else if (x is >= 16 and <= 24) kind = $"FIXED o={(x >> 1) & 7}";
            else if (x >= 64) kind = $"LPC o={((x >> 1) & 31) + 1}";
            else kind = $"reserved x={x:x}";
            sb.AppendLine($"    ch{ch} ({chNames[ch]}): {kind} wasted={wasted}");
        }
    }

    private static byte[] Pcm16(int size, int seed)
    {
        var rng = new Random(seed);
        var samples = size / 2;
        var b = new byte[samples * 2];
        double freq = 220 + rng.NextDouble() * 200;
        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            if (i % 4096 == 0) freq = 180 + rng.NextDouble() * 1200;
            phase += 2 * Math.PI * freq / 44100.0;
            var sample = (short)(Math.Sin(phase) * 11000 + (rng.NextDouble() - 0.5) * 400);
            b[i * 2] = (byte)sample;
            b[i * 2 + 1] = (byte)(sample >> 8);
        }
        return b;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunChdman(params string[] args)
    {
        string chdmanPath = ChdmanPath ?? throw new InvalidOperationException("chdman.exe not available");
        var psi = new ProcessStartInfo
        {
            FileName = chdmanPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, tOut.Result, tErr.Result);
    }

    private static string? ResolveChdmanPath()
    {
        string exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate)) return candidate;
        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate)) return candidate;
        return null;
    }
}
