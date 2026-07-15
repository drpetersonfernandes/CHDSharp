namespace CHDSharp.LZMA;

/// <summary>
/// The exception that is thrown when an error in input stream occurs during decoding.
/// </summary>
public class DataErrorException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DataErrorException"/> class with a default message.</summary>
    public DataErrorException() : base("Data Error") { }
}

/// <summary>
/// The exception that is thrown when the value of an argument is outside the allowable range.
/// </summary>
internal class InvalidParamException : Exception
{
    public InvalidParamException() : base("Invalid Parameter") { }
}
