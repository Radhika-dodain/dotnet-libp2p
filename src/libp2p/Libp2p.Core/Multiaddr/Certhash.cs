// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using Multiformats.Base;
using Multiformats.Hash;
using Nethermind.Libp2p.Core.Exceptions;
using System;

namespace Multiformats.Address.Protocols;

public class Certhash : MultiaddressProtocol
{
    public Certhash()
        : base("certhash", 466, -1)
    {
    }

    public Certhash(string value)
        : this()
    {
        Decode(value);
    }

    public byte[] Hash => Value as byte[] ?? [];

    public override void Decode(string value)
    {
        try
        {
            byte[] bytes = Multibase.Decode(value, out MultibaseEncoding _);
            Decode(bytes);
        }
        catch (MultiaddressException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MultiaddressException($"Invalid certhash format: {ex.Message}");
        }
    }

    public override void Decode(byte[] bytes)
    {
        try
        {
            Multihash.Decode(bytes);
        }
        catch (Exception ex)
        {
            throw new MultiaddressException($"Invalid certhash: must be a valid multihash. {ex.Message}");
        }
        Value = bytes;
    }

    public override byte[] ToBytes() => Hash;

    public override string ToString() => Multibase.Encode(MultibaseEncoding.Base64Url, Hash);
}
