namespace CHDSharp.Models.LZMA;

/// <summary>Represents the LZMA probability state used during literal and match decoding.</summary>
public struct State
{
    /// <summary>The current probability state index (0-11).</summary>
    public uint Index;

    /// <summary>Resets the state to its initial value.</summary>
    public void Init()
    {
        Index = 0;
    }

    /// <summary>Updates the state after decoding a literal character.</summary>
    public void UpdateChar()
    {
        switch (Index)
        {
            case < 4:
                Index = 0;
                break;
            case < 10:
                Index -= 3;
                break;
            default:
                Index -= 6;
                break;
        }
    }

    /// <summary>Updates the state after decoding a match.</summary>
    public void UpdateMatch()
    {
        Index = (uint)(Index < 7 ? 7 : 10);
    }

    /// <summary>Updates the state after decoding a repeated match.</summary>
    public void UpdateRep()
    {
        Index = (uint)(Index < 7 ? 8 : 11);
    }

    /// <summary>Updates the state after decoding a short repeated match.</summary>
    public void UpdateShortRep()
    {
        Index = (uint)(Index < 7 ? 9 : 11);
    }

    /// <summary>Determines whether the current state represents a character state.</summary>
    /// <returns><c>true</c> if <see cref="Index"/> is less than 7; otherwise <c>false</c>.</returns>
    public readonly bool IsCharState()
    {
        return Index < 7;
    }
}
