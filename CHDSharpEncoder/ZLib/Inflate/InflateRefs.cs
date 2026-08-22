#nullable disable
#pragma warning disable MA0008
namespace CHDSharpEncoder.ZLib.Inflate;

internal ref struct InflateRefs
{
    internal ref Code Codes;
    internal ref ushort Lens;
    internal ref ushort Work;
    internal ref byte Window;
    internal ref Code Lencode;
    internal ref Code Distcode;
    internal ref ushort Order;
    internal ref ushort Lbase;
    internal ref ushort Lext;
    internal ref ushort Dbase;
    internal ref ushort Dext;
}