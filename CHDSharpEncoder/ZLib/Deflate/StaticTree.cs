#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Deflate;

internal sealed class StaticTree
{
    internal readonly TreeNode[] static_tree; // static tree or null
    internal readonly uint ExtraBase; // base index for extra_bits
    internal readonly uint Elems; // max number of elements in the tree
    internal readonly uint MaxLength; // max bit length for the codes

    public StaticTree(TreeNode[] staticTree, uint extraBase, uint elems, uint maxLength)
    {
        static_tree = staticTree;
        ExtraBase = extraBase;
        Elems = elems;
        MaxLength = maxLength;
    }
}