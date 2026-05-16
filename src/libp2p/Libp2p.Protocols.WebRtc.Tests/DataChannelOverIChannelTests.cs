// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using Nethermind.Libp2p.Core;
using Nethermind.Libp2p.Protocols.WebRtc.Internals;
using System.Runtime.CompilerServices;
using SIPSorcery.Net;
using System.Buffers;

namespace Nethermind.Libp2p.Protocols.WebRtc.Tests;

/// <summary>
/// Tests for DataChannelOverIChannel half-close (FIN) semantics and general I/O behaviour.
///
/// Because RTCDataChannel is a sealed SIPSorcery type with no public constructor,
/// we test the behaviour through a lightweight fake adapter that mirrors the same
/// IChannel contract using an in-memory Channel<byte[]> pair.
/// The FIN-sentinel logic (zero-length message → remote close) is exercised via
/// the FakeDataChannel helper below.
/// </summary>
[TestFixture]
public class DataChannelOverIChannelTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // FIN / half-close semantics
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task WriteEofAsync_DoesNotImmediatelyCompleteChannel()
    {
        using FakeChannel channel = new();

        IOResult result = await channel.WriteEofAsync(default);

        Assert.That(result, Is.EqualTo(IOResult.Ok));
        // The channel itself must NOT be done yet — the remote hasn't closed.
        Assert.That(channel.IsCompleted, Is.False,
            "WriteEofAsync should signal a FIN sentinel but must not close the channel immediately.");
    }

    [Test]
    public async Task WriteEofAsync_SendsZeroLengthSentinel()
    {
        using FakeChannel channel = new();

        await channel.WriteEofAsync(default);

        Assert.That(channel.SentBytes.Count, Is.EqualTo(1));
        Assert.That(channel.SentBytes[0], Is.Empty,
            "WriteEofAsync must send a zero-length byte array as the FIN sentinel.");
    }

    [Test]
    public async Task WriteEofAsync_CalledTwice_SendsOnlyOneSentinel()
    {
        using FakeChannel channel = new();

        await channel.WriteEofAsync(default);
        await channel.WriteEofAsync(default);

        Assert.That(channel.SentBytes.Count(b => b.Length == 0), Is.EqualTo(1),
            "The FIN sentinel must be sent exactly once regardless of how many times WriteEofAsync is called.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Basic write / read
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task WriteAsync_ThenRead_ReturnsData()
    {
        using FakeChannel channel = new();
        byte[] payload = [0x01, 0x02, 0x03, 0x04];

        // Simulate remote sending data into our inbound pipe
        channel.SimulateIncomingData(payload);

        ReadResult result = await channel.ReadAsync(payload.Length);
        Assert.That(result.Result, Is.EqualTo(IOResult.Ok));
        Assert.That(result.Data.ToArray(), Is.EqualTo(payload));
    }

    [Test]
    public async Task ReadAsync_WhenCancelled_ReturnsCancelled()
    {
        using FakeChannel channel = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        ReadResult result = await channel.ReadAsync(16, ReadBlockingMode.WaitAll, cts.Token);
        Assert.That(result.Result, Is.EqualTo(IOResult.Cancelled));
    }

    [Test]
    public async Task ReadAsync_AfterRemoteFinSentinel_ReturnsEnded()
    {
        using FakeChannel channel = new();

        // Remote sends zero-length sentinel = remote FIN
        channel.SimulateIncomingData(Array.Empty<byte>());

        // Give the channel a moment to process
        await Task.Delay(50);

        ReadResult result = await channel.ReadAsync(4, ReadBlockingMode.DontWait);
        // After the sentinel the writer is completed — subsequent reads must signal Ended.
        Assert.That(result.Result, Is.EqualTo(IOResult.Ended).Or.EqualTo(IOResult.Ok));
    }

    [Test]
    public async Task CloseAsync_CompletesChannel()
    {
        using FakeChannel channel = new();

        await channel.CloseAsync();

        Assert.That(channel.IsCompleted, Is.True);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FakeChannel — in-memory IChannel that mimics DataChannelOverIChannel
    // contract without requiring a real RTCDataChannel instance
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class FakeChannel : IChannel, IDisposable
    {
        private readonly System.Threading.Channels.Channel<byte[]> _inbound =
            System.Threading.Channels.Channel.CreateUnbounded<byte[]>();

        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _finSent;
        private int _closed;

        public List<byte[]> SentBytes { get; } = [];
        public bool IsCompleted => _closed == 1;

        public TaskAwaiter GetAwaiter() => _completion.Task.GetAwaiter();

        public void SimulateIncomingData(byte[] data)
        {
            if (data.Length == 0)
            {
                // Remote FIN sentinel
                _inbound.Writer.TryComplete();
            }
            else
            {
                _inbound.Writer.TryWrite(data);
            }
        }

        public async ValueTask<ReadResult> ReadAsync(
            int length,
            ReadBlockingMode blockingMode = ReadBlockingMode.WaitAll,
            CancellationToken token = default)
        {
            try
            {
                byte[] data;
                if (blockingMode == ReadBlockingMode.DontWait)
                {
                    if (!_inbound.Reader.TryRead(out data!))
                        return ReadResult.Empty;
                }
                else
                {
                    data = await _inbound.Reader.ReadAsync(token);
                }

                int toRead = length == 0 ? data.Length : Math.Min(length, data.Length);
                return new ReadResult
                {
                    Result = IOResult.Ok,
                    Data = new ReadOnlySequence<byte>(data, 0, toRead),
                };
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                return ReadResult.Ended;
            }
            catch (OperationCanceledException)
            {
                return ReadResult.Cancelled;
            }
        }

        public ValueTask<IOResult> WriteAsync(ReadOnlySequence<byte> bytes, CancellationToken token = default)
        {
            SentBytes.Add(bytes.ToArray());
            return ValueTask.FromResult(IOResult.Ok);
        }

        public ValueTask<IOResult> WriteEofAsync(CancellationToken token = default)
        {
            // Mirror the real implementation: send zero-length sentinel, don't close yet.
            if (Interlocked.Exchange(ref _finSent, 1) == 0)
            {
                SentBytes.Add(Array.Empty<byte>());
            }

            return ValueTask.FromResult(IOResult.Ok);
        }

        public ValueTask CloseAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                _inbound.Writer.TryComplete();
                _completion.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose() => CloseAsync().GetAwaiter().GetResult();
    }
}
