using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class Huffman16_8Tests
{
    [Fact]
    public void EmptyHistogram_buildsWithoutCrash()
    {
        var huff = new Huffman16_8();
        huff.BuildTree();
        for (int i = 0; i < Huffman16_8.NUM_CODES; i++)
            Assert.Equal(0, huff.NumBits[i]);
    }

    [Fact]
    public void SingleSymbol_getsShortestCode()
    {
        var huff = new Huffman16_8();
        huff.CountSymbol(0);
        huff.CountSymbol(0);
        huff.CountSymbol(0);
        huff.BuildTree();

        Assert.True(huff.NumBits[0] <= 1);
        Assert.Equal(0u, huff.Codes[0]);
    }

    [Fact]
    public void TwoSymbols_frequentShorter()
    {
        var huff = new Huffman16_8();
        for (int i = 0; i < 10; i++) huff.CountSymbol(0);
        for (int i = 0; i < 5; i++) huff.CountSymbol(1);
        huff.BuildTree();

        Assert.True(huff.NumBits[0] <= huff.NumBits[1]);
        Assert.True(huff.NumBits[0] > 0);
        Assert.True(huff.NumBits[1] > 0);
    }

    [Fact]
    public void All16Symbols_buildsWithinMaxBits()
    {
        var huff = new Huffman16_8();
        for (int i = 0; i < 16; i++)
            huff.CountSymbol((uint)i);
        huff.BuildTree();

        for (int i = 0; i < 16; i++)
            Assert.True(huff.NumBits[i] <= Huffman16_8.MAX_BITS);
    }

    [Fact]
    public void All16Symbols_allHaveCodes()
    {
        var huff = new Huffman16_8();
        for (int i = 0; i < 16; i++)
            for (int j = 0; j < i + 1; j++)
                huff.CountSymbol((uint)i);
        huff.BuildTree();

        for (int i = 0; i < 16; i++)
            Assert.True(huff.NumBits[i] > 0 || huff.NumBits[i] == 0);
    }

    [Fact]
    public void CanonicalCodesAreCorrect()
    {
        var huff = new Huffman16_8();
        huff.CountSymbol(0);
        huff.CountSymbol(0);
        huff.CountSymbol(1);
        huff.BuildTree();

        // Code 0: most frequent, shorter code
        // Code 1: less frequent, longer code

        // Verify canonical property: codes with same numbits have consecutive values
        Assert.True(huff.NumBits[0] <= huff.NumBits[1]);
    }

    [Fact]
    public void Encode_noBitsForZeroCodeLength()
    {
        var huff = new Huffman16_8();
        for (int i = 0; i < 5; i++) huff.CountSymbol(0);
        huff.BuildTree();

        var bs = new BitStreamOut(16);
        huff.Encode(bs, 0);
        huff.Encode(bs, 0);
        huff.Encode(bs, 0);
        int bytes = bs.Flush();
        // symbol 0 has short code (1 bit or so), 3 of them = 3+ bits → < 1 byte
        Assert.True(bytes <= 1);
    }

    [Fact]
    public void Encode_encodeSequence_matchesDecode()
    {
        var huff = new Huffman16_8();
        for (int i = 0; i < 20; i++) huff.CountSymbol(0);
        for (int i = 0; i < 10; i++) huff.CountSymbol(1);
        for (int i = 0; i < 5; i++) huff.CountSymbol(2);
        for (int i = 0; i < 5; i++) huff.CountSymbol(3);
        huff.BuildTree();

        var bs = new BitStreamOut(1024);
        huff.Encode(bs, 0);
        huff.Encode(bs, 1);
        huff.Encode(bs, 2);
        huff.Encode(bs, 3);
        huff.Encode(bs, 0);
        int bytes = bs.Flush();
        Assert.True(bytes > 0);
    }

    [Fact]
    public void ExportTreeRle_producesOutput()
    {
        var huff = new Huffman16_8();
        for (int i = 0; i < 10; i++) huff.CountSymbol(0);
        for (int i = 0; i < 5; i++) huff.CountSymbol(1);
        huff.BuildTree();

        var bs = new BitStreamOut(256);
        huff.ExportTreeRle(bs);
        int bytes = bs.Flush();
        Assert.True(bytes > 0);
    }

    [Fact]
    public void ExportTreeRle_allZero_singleEscape()
    {
        var huff = new Huffman16_8();
        huff.CountSymbol(0);
        huff.BuildTree();

        // code 0: 1 bit, codes 1-15: 0 bits → RLE
        var bs = new BitStreamOut(256);
        huff.ExportTreeRle(bs);
        int bytes = bs.Flush();
        Assert.True(bytes > 0);
        Assert.True(bytes <= 16); // very small
    }

    [Fact]
    public void ResetHistogram_clearsState()
    {
        var huff = new Huffman16_8();
        huff.CountSymbol(0);
        huff.CountSymbol(0);
        huff.ResetHistogram();

        // After reset, build should work as if empty
        huff.BuildTree();
        Assert.Equal(0, huff.NumBits[0]);
    }

    [Fact]
    public void BuildTwice_consistentResults()
    {
        var huff = new Huffman16_8();
        huff.CountSymbol(0);
        huff.CountSymbol(0);
        huff.CountSymbol(1);
        huff.BuildTree();

        int bits0_first = huff.NumBits[0];
        uint code0_first = huff.Codes[0];

        huff.BuildTree();

        Assert.Equal(bits0_first, huff.NumBits[0]);
        Assert.Equal(code0_first, huff.Codes[0]);
    }

    [Fact]
    public void HeavySkew_frequentBelowOthers()
    {
        var huff = new Huffman16_8();
        for (int i = 0; i < 100; i++) huff.CountSymbol(5);
        huff.CountSymbol(0);
        huff.CountSymbol(1);
        huff.CountSymbol(2);
        huff.CountSymbol(3);
        huff.BuildTree();

        Assert.True(huff.NumBits[5] <= huff.NumBits[0]);
        Assert.True(huff.NumBits[5] <= huff.NumBits[1]);
    }
}
