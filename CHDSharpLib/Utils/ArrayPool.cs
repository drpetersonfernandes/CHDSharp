namespace CHDSharp.Utils;

/// <summary>
/// A simple thread-safe pool for reusing byte arrays of a fixed size,
/// reducing GC pressure when many temporary buffers are needed.
/// </summary>
internal class ArrayPool
{
    private readonly uint _arraySize;

    private readonly List<byte[]> _array;

    private int _count;

    private int _issuedArraysTotal;

    /// <summary>
    /// Initializes a new pool that manages byte arrays of the specified size.
    /// </summary>
    /// <param name="arraySize">The fixed size in bytes for each array in this pool.</param>
    internal ArrayPool(uint arraySize)
    {
        _array = new List<byte[]>();
        _arraySize = arraySize;
        _count = 0;
        _issuedArraysTotal = 0;
    }

    /// <summary>
    /// Rents a byte array from the pool, allocating a new one if the pool is empty.
    /// </summary>
    /// <returns>A byte array of size <see cref="_arraySize"/>.</returns>
    internal byte[] Rent()
    {
        byte[] ret;
        lock (_array)
        {
            if (_count == 0)
            {
                ret = new byte[_arraySize];
                _issuedArraysTotal++;
            }
            else
            {
                _count--;
                ret = _array[_count];
                _array.RemoveAt(_count);
            }
        }

        return ret;
    }

    /// <summary>
    /// Returns a previously rented byte array back to the pool for reuse.
    /// </summary>
    /// <param name="ret">The byte array to return. Must have been originally obtained from <see cref="Rent"/>.</param>
    internal void Return(byte[] ret)
    {
        lock (_array)
        {
            _array.Add(ret);
            _count++;
        }
    }

    /// <summary>
    /// Reads statistics about array pool usage.
    /// </summary>
    /// <param name="issuedArraysTotal">Total number of arrays allocated since creation.</param>
    /// <param name="returnedArraysTotal">Number of arrays currently held in the pool.</param>
    internal void ReadStats(out int issuedArraysTotal, out int returnedArraysTotal)
    {
        issuedArraysTotal = _issuedArraysTotal;
        returnedArraysTotal = _count;
    }
}
