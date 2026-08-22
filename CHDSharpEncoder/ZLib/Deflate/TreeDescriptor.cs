#nullable disable
// Original code and comments Copyright (C) 1995-2024 Jean-loup Gailly
// Managed C#/.NET code Copyright (C) 2022-2024 Magnus Montin

namespace CHDSharpEncoder.ZLib.Deflate;

internal sealed class TreeDescriptor
{
    internal readonly TreeNode[] dyn_tree; // the dynamic tree
    internal readonly StaticTree StatDesc; // the corresponding static tree
    internal int MaxCode; // largest code with non zero frequency

    internal TreeDescriptor(TreeNode[] dynTree, StaticTree statDesc)
    {
        this.dyn_tree = dynTree;
        this.StatDesc = statDesc;
    }
}