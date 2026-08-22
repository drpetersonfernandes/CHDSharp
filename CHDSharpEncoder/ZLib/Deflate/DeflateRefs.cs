#nullable disable
#pragma warning disable MA0008
using System.Runtime.InteropServices;

namespace CHDSharpEncoder.ZLib.Deflate;

internal ref struct DeflateRefs
{
    public DeflateRefs()
    {
        ConfigurationTable = ref MemoryMarshal.GetReference<Config>(Deflater.SConfigurationTable);
    }

    internal ref byte PendingBuf;
    internal ref byte PendingOut;

    internal ref byte Window;
    internal ref ushort Prev;
    internal ref ushort Head;

    internal ref TreeNode DynLtree;
    internal ref TreeNode DynDtree;
    internal ref TreeNode BlTree;

    internal ref ushort BlCount;
    internal ref int Heap;
    internal ref byte Depth;

    internal ref TreeNode StaLtree;
    internal ref TreeNode StaDtree;

    internal ref ushort BlOrder;
    internal ref byte DistCode;
    internal ref byte LengthCode;
    internal ref int BaseDist;
    internal ref int BaseLength;
    internal ref int ExtraDbits;
    internal ref int ExtraLbits;
    internal ref int ExtraBlbits;
    internal readonly ref Config ConfigurationTable;
}