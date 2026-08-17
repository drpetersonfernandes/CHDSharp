namespace CHDSharpEncoder.Interfaces;

/// <summary>A hunk compression codec; compression type 0-3 in the map maps to codecs[0-3].</summary>
public interface IChdCodec
{
    /// <summary>The four-character codec tag (see <see cref="CodecTags"/>).</summary>
    uint Tag { get; }

    /// <summary>Compresses a full hunk. Returns <c>null</c> when the codec does not reduce the size.</summary>
    byte[]? Compress(byte[] data);
}