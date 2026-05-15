// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using Multiformats.Address;
using Multiformats.Address.Protocols;
using Multiformats.Base;
using Multiformats.Hash;
using Nethermind.Libp2p.Core.Exceptions;

namespace Nethermind.Libp2p.Core.Tests;

[TestFixture]
public class CerthashTests
{
    // A real SHA-256 multihash: varint(0x12) + varint(32) + 32 bytes of 0xAB
    private static readonly byte[] ValidSha256Multihash =
        [(byte)HashType.SHA2_256, 32, .. Enumerable.Repeat((byte)0xAB, 32)];

    // A real SHA-512 multihash: varint(0x13) + varint(64) + 64 bytes of 0xCD
    private static readonly byte[] ValidSha512Multihash =
        [(byte)HashType.SHA2_512, 64, .. Enumerable.Repeat((byte)0xCD, 64)];

    // ──────────────────────────────────────────────
    // Decode(byte[]) — happy paths
    // ──────────────────────────────────────────────

    [Test]
    public void Decode_Bytes_ValidSha256_Succeeds()
    {
        Certhash c = new();
        Assert.DoesNotThrow(() => c.Decode(ValidSha256Multihash));
        Assert.That(c.Hash, Is.EqualTo(ValidSha256Multihash));
    }

    [Test]
    public void Decode_Bytes_ValidSha512_Succeeds()
    {
        Certhash c = new();
        Assert.DoesNotThrow(() => c.Decode(ValidSha512Multihash));
        Assert.That(c.Hash, Is.EqualTo(ValidSha512Multihash));
    }

    // ──────────────────────────────────────────────
    // Decode(byte[]) — rejection paths
    // ──────────────────────────────────────────────

    [Test]
    public void Decode_Bytes_Empty_ThrowsMultiaddressException()
    {
        Certhash c = new();
        MultiaddressException ex = Assert.Throws<MultiaddressException>(() => c.Decode(Array.Empty<byte>()))!;
        Assert.That(ex.Message, Does.Contain("multihash").IgnoreCase);
    }

    [Test]
    public void Decode_Bytes_TruncatedDigest_ThrowsMultiaddressException()
    {
        // Declares SHA-256 (32 bytes) but only supplies 10 bytes of digest
        byte[] truncated = [(byte)HashType.SHA2_256, 32, .. Enumerable.Repeat((byte)0xAB, 10)];
        Certhash c = new();
        Assert.Throws<MultiaddressException>(() => c.Decode(truncated));
    }

    [Test]
    public void Decode_Bytes_RandomGarbage_ThrowsMultiaddressException()
    {
        byte[] garbage = [0xFF, 0xFE, 0x01, 0x02, 0x03];
        Certhash c = new();
        Assert.Throws<MultiaddressException>(() => c.Decode(garbage));
    }

    [Test]
    public void Decode_Bytes_LengthClaimExceedsBuffer_ThrowsMultiaddressException()
    {
        // Declares SHA-256 digest length = 255 bytes, but only 4 bytes follow.
        // Multihash.Decode must reject this as malformed.
        byte[] malformed = [(byte)HashType.SHA2_256, 0xFF, 0x01, 0x02, 0x03, 0x04];
        Certhash c = new();
        Assert.Throws<MultiaddressException>(() => c.Decode(malformed));
    }

    // ──────────────────────────────────────────────
    // Decode(string) — multibase-encoded happy paths
    // ──────────────────────────────────────────────

    [Test]
    public void Decode_String_ValidBase64UrlEncoded_Succeeds()
    {
        string encoded = Multibase.Encode(MultibaseEncoding.Base64Url, ValidSha256Multihash);
        Certhash c = new();
        Assert.DoesNotThrow(() => c.Decode(encoded));
        Assert.That(c.Hash, Is.EqualTo(ValidSha256Multihash));
    }

    [Test]
    public void Decode_String_ValidBase32Encoded_Succeeds()
    {
        string encoded = Multibase.Encode(MultibaseEncoding.Base32Lower, ValidSha256Multihash);
        Certhash c = new();
        Assert.DoesNotThrow(() => c.Decode(encoded));
    }

    // ──────────────────────────────────────────────
    // Decode(string) — rejection paths
    // ──────────────────────────────────────────────

    [Test]
    public void Decode_String_EncodedGarbage_ThrowsMultiaddressException()
    {
        // Encodes [0x01, 0x02, 0x03] which is not a valid multihash
        string encodedGarbage = Multibase.Encode(MultibaseEncoding.Base64Url, [0x01, 0x02, 0x03]);
        Certhash c = new();
        Assert.Throws<MultiaddressException>(() => c.Decode(encodedGarbage));
    }

    [Test]
    public void Decode_String_NotMultibaseAtAll_ThrowsMultiaddressException()
    {
        Certhash c = new();
        Assert.Throws<MultiaddressException>(() => c.Decode("this-is-not-multibase!!!"));
    }

    // ──────────────────────────────────────────────
    // Constructor(string) convenience path
    // ──────────────────────────────────────────────

    [Test]
    public void Constructor_ValidString_SetsHash()
    {
        string encoded = Multibase.Encode(MultibaseEncoding.Base64Url, ValidSha256Multihash);
        Certhash c = new(encoded);
        Assert.That(c.Hash, Is.EqualTo(ValidSha256Multihash));
    }

    // ──────────────────────────────────────────────
    // Round-trip: ToBytes → Decode → ToString
    // ──────────────────────────────────────────────

    [Test]
    public void RoundTrip_ToBytesAndToString_AreConsistent()
    {
        Certhash c = new();
        c.Decode(ValidSha256Multihash);

        byte[] bytes = c.ToBytes();
        string str = c.ToString();

        Certhash rebuilt = new(str);
        Assert.That(rebuilt.ToBytes(), Is.EqualTo(bytes));
    }
}

[TestFixture]
public class MultiaddressExtensionsTests
{
    // ──────────────────────────────────────────────
    // IsWebRtc() — should return true
    // ──────────────────────────────────────────────

    [Test]
    public void IsWebRtc_WebrtcDirectAddress_ReturnsTrue()
    {
        // /ip4/1.2.3.4/udp/4001/webrtc-direct — no certhash needed for the flag check
        Multiaddress addr = Multiaddress.Decode("/ip4/1.2.3.4/udp/4001/webrtc-direct");
        Assert.That(addr.IsWebRtc(), Is.True);
    }

    [Test]
    public void IsWebRtc_WebrtcAddress_ReturnsTrue()
    {
        Multiaddress addr = Multiaddress.Decode("/ip4/1.2.3.4/udp/4001/webrtc");
        Assert.That(addr.IsWebRtc(), Is.True);
    }

    [Test]
    public void IsWebRtc_WebrtcDirectWithCerthash_ReturnsTrue()
    {
        // Build a real certhash segment
        byte[] validHash = [(byte)HashType.SHA2_256, 32, .. Enumerable.Repeat((byte)0xAB, 32)];
        string encoded = Multibase.Encode(MultibaseEncoding.Base64Url, validHash);
        Multiaddress addr = Multiaddress.Decode($"/ip4/1.2.3.4/udp/4001/webrtc-direct/certhash/{encoded}");
        Assert.That(addr.IsWebRtc(), Is.True);
    }

    // ──────────────────────────────────────────────
    // IsWebRtc() — should return false
    // ──────────────────────────────────────────────

    [Test]
    public void IsWebRtc_TcpAddress_ReturnsFalse()
    {
        Multiaddress addr = Multiaddress.Decode("/ip4/1.2.3.4/tcp/4001");
        Assert.That(addr.IsWebRtc(), Is.False);
    }

    [Test]
    public void IsWebRtc_QuicAddress_ReturnsFalse()
    {
        Multiaddress addr = Multiaddress.Decode("/ip4/1.2.3.4/udp/4001/quic-v1");
        Assert.That(addr.IsWebRtc(), Is.False);
    }

    [Test]
    public void IsWebRtc_NullAddress_ReturnsFalse()
    {
        Multiaddress? addr = null;
        Assert.That(addr!.IsWebRtc(), Is.False);
    }

    [Test]
    public void IsWebRtc_Ip4Only_ReturnsFalse()
    {
        Multiaddress addr = Multiaddress.Decode("/ip4/1.2.3.4");
        Assert.That(addr.IsWebRtc(), Is.False);
    }
}
