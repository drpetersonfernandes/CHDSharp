// CUETools.Flake: pure managed FLAC audio encoder
// Copyright (c) 2009-2023 Grigory Chudov
// Based on Flake encoder, http://flake-enc.sourceforge.net/
// Copyright (c) 2006-2009 Justin Ruggles
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA

namespace CHDSharp.Flac;

/// <summary>
/// FLAC encoding constants as defined by the FLAC specification.
/// </summary>
internal class FlakeConstants
{
    /// <summary>
    /// Maximum block size in samples (65535).
    /// </summary>
    public const int Maxblocksize = 65535;
    /// <summary>
    /// Maximum Rice coding parameter (14).
    /// </summary>
    public const int Maxriceparam = 14;
    /// <summary>
    /// Maximum partition order for Rice coding (8).
    /// </summary>
    public const int Maxpartitionorder = 8;
    /// <summary>
    /// Maximum number of Rice coding partitions (256).
    /// </summary>
    public const int Maxpartitions = 1 << Maxpartitionorder;

    /// <summary>
    /// Table of FLAC block sizes indexed by the block size code from the frame header.
    /// </summary>
    public static readonly int[] FlacBlocksizes = [0, 192, 576, 1152, 2304, 4608, 0, 0, 256, 512, 1024, 2048, 4096, 8192, 16384];
    //0110 : get 8 bit (blocksize-1) from end of header
    //0111 : get 16 bit (blocksize-1) from end of header
    /// <summary>
    /// Table of FLAC bit depths indexed by the bits-per-sample code from the stream info header.
    /// </summary>
    public static readonly int[] FlacBitdepths = [0, 8, 12, 0, 16, 20, 24, 0];
}