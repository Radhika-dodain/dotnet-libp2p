// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using Multiformats.Address;
using Multiformats.Base;
using Nethermind.Libp2p.Protocols.WebRtc;
using Nethermind.Libp2p.Protocols.WebRtc.Internals;
using System.Net;

namespace Nethermind.Libp2p.Protocols.WebRtc.Tests;

[TestFixture]
public class WebRtcDirectMultiaddrTests
{
    [Test]
    public void BuildParseBuild_Roundtrip()
    {
        IPEndPoint endpoint = new(IPAddress.Parse("192.0.2.10"), 12345);
        DtlsFingerprint fingerprint = new("sha-256", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

        Multiaddress addr = WebRtcDirectMultiaddr.Build(endpoint, fingerprint);
        (IPEndPoint parsedEndpoint, DtlsFingerprint parsedFingerprint) = WebRtcDirectMultiaddr.Parse(addr);
        Multiaddress rebuilt = WebRtcDirectMultiaddr.Build(parsedEndpoint, parsedFingerprint);

        Assert.That(rebuilt.ToString(), Is.EqualTo(addr.ToString()));
    }

    [Test]
    public void Parse_ThrowsWhenCerthashMissing()
    {
        Multiaddress addr = Multiaddress.Decode("/ip4/127.0.0.1/udp/9999/webrtc-direct");
        Assert.Throws<FormatException>(() => WebRtcDirectMultiaddr.Parse(addr));
    }

    [Test]
    public void Parse_ThrowsOnMalformedMultihash()
    {
        string malformedMultihash = Multibase.Encode(MultibaseEncoding.Base64Url, [0x01, 0x02, 0x03]);
        // With our hardened Certhash.Decode, malformed multihash bytes now cause
        // MultiaddressException to be raised during Multiaddress.Decode itself —
        // i.e., at address-parse time, before WebRtcDirectMultiaddr.Parse is called.
        // This is stricter and better: we reject bad addresses as early as possible.
        Assert.Throws<Nethermind.Libp2p.Core.Exceptions.MultiaddressException>(
            () => Multiaddress.Decode($"/ip4/127.0.0.1/udp/9999/webrtc-direct/certhash/{malformedMultihash}"));
    }
}
