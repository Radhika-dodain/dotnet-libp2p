// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using System;

namespace Nethermind.Libp2p.Core.Exceptions;

public class MultiaddressException : Libp2pException
{
    public MultiaddressException()
    {
    }

    public MultiaddressException(string message) : base(message)
    {
    }
}
