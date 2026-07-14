namespace CHDSharp.Interfaces.LZMA;

/// <summary>
/// Provides callback for reporting progress during compression or decompression.
/// </summary>
public interface ICodeProgress
{
    /// <summary>
    /// Callback progress.
    /// </summary>
    /// <param name="inSize">
    /// input size. -1 if unknown.
    /// </param>
    /// <param name="outSize">
    /// output size. -1 if unknown.
    /// </param>
    void SetProgress(Int64 inSize, Int64 outSize);
};
