using CHDSharp.Models;

namespace CHDSharp;

/// <summary>Provides extension methods for <see cref="ChdError"/> values.</summary>
public static class ChdErrorExtensions
{
    /// <summary>Returns a human-readable message describing the error code.</summary>
    /// <param name="error">The error code to describe.</param>
    /// <returns>A user-friendly error message string.</returns>
    public static string GetMessage(this ChdError error)
    {
        return error switch
        {
            ChdError.Chderrnone => "No error",
            ChdError.Chderrnointerface => "No interface available",
            ChdError.Chderroutofmemory => "Out of memory",
            ChdError.Chderrinvalidfile => "Not a valid CHD file",
            ChdError.Chderrinvalidparameter => "Invalid parameter",
            ChdError.Chderrinvaliddata => "Invalid or corrupt data",
            ChdError.Chderrfilenotfound => "File not found",
            ChdError.Chderrrequiresparent => "Child CHD requires a parent",
            ChdError.Chderrfilenotwriteable => "File is not writable",
            ChdError.Chderrreaderror => "Read error",
            ChdError.Chderrwriteerror => "Write error",
            ChdError.Chderrcodecerror => "Codec error",
            ChdError.Chderrinvalidparent => "Invalid or incompatible parent CHD",
            ChdError.Chderrhunkoutofrange => "Hunk index out of range",
            ChdError.Chderrdecompressionerror => "Decompression failed",
            ChdError.Chderrcompressionerror => "Compression failed",
            ChdError.Chderrcantcreatefile => "Cannot create file",
            ChdError.Chderrcantverify => "Cannot verify CHD",
            ChdError.Chderrnotsupported => "Feature not supported",
            ChdError.Chderrmetadatanotfound => "Metadata not found",
            ChdError.Chderrinvalidmetadatasize => "Invalid metadata size",
            ChdError.Chderrunsupportedversion => "Unsupported CHD version",
            ChdError.Chderrverifyincomplete => "Verification incomplete",
            ChdError.Chderrinvalidmetadata => "Invalid or corrupt metadata",
            ChdError.Chderrinvalidstate => "Invalid state",
            ChdError.Chderroperationpending => "Operation already pending",
            ChdError.Chderrnoasyncoperation => "No async operation in progress",
            ChdError.Chderrunsupportedformat => "Unsupported format",
            ChdError.Chderrcannotopenfile => "Cannot open file",
            _ => $"Unknown error ({error})"
        };
    }
}
