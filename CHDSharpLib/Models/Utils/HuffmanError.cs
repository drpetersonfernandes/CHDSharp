namespace CHDSharp.Models.Utils;

/// <summary>Error codes returned by Huffman decoder operations.</summary>
internal enum HuffmanError
{
    /// <summary>No error; operation completed successfully.</summary>
    HufferrNone = 0,

    /// <summary>The Huffman tree contains a code with more bits than allowed.</summary>
    HufferrTooManyBits,

    /// <summary>The input data is malformed or corrupted.</summary>
    HufferrInvalidData,

    /// <summary>The input buffer is too small to contain the expected Huffman data.</summary>
    HufferrInputBufferTooSmall,

    /// <summary>The output buffer is too small to hold the decoded result.</summary>
    HufferrOutputBufferTooSmall,

    /// <summary>An internal inconsistency was detected in the Huffman tree structure.</summary>
    HufferrInternalInconsistency,

    /// <summary>Too many Huffman contexts were requested.</summary>
    HufferrTooManyContexts
}
