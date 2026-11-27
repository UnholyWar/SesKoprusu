using System.Diagnostics;
using System.IO;
using NAudio.Codecs;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SesKoprusu;

internal static class CodexVoice
{
    public const string Default = "Codex default";

    public static async Task<string[]> Choices(CancellationToken token)
    {
        using var server = await AccountServer.Connect(token, true);
        var catalog = await server.Call("thread/realtime/listVoices", new { }, token);
        return [Default, .. catalog.GetProperty("voices").GetProperty("v3").EnumerateArray().Select(x => x.GetString()!)];
    }

    public static async Task Run(string voice, string instructions, MMDevice? microphone, MMDevice speakers,
        CancellationToken token, Action? captureReady = null, bool physicalMicrophone = false,
        AudioLevels? levels = null)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(TimeSpan.FromSeconds(45));
        using var server = await AccountServer.Connect(startup.Token, true);
        using var peer = new RTCPeerConnection(null);
        peer.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.onconnectionstatechange += state =>
        {
            if (state == RTCPeerConnectionState.connected) connected.TrySetResult();
            if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
            {
                var error = new IOException("Codex WebRTC audio disconnected.");
                connected.TrySetException(error);
                ended.TrySetException(error);
            }
        };
        var format = new WaveFormat(8000, 16, 1);
        var buffer = new BufferedWaveProvider(format) { DiscardOnBufferOverflow = true, ReadFully = true };
        using var player = new WasapiOut(speakers, AudioClientShareMode.Shared, false, 80);
        player.Init(buffer);
        long lastSpeech = 0;
        peer.OnRtpPacketReceived += (_, media, packet) =>
        {
            if (media != SDPMediaTypesEnum.audio || packet.Header.PayloadType != 0) return;
            var pcm = new byte[packet.Payload.Length * 2];
            bool audible = false;
            for (int i = 0; i < packet.Payload.Length; i++)
            {
                var sample = MuLawDecoder.MuLawToLinearSample(packet.Payload[i]);
                pcm[i * 2] = (byte)sample;
                pcm[i * 2 + 1] = (byte)(sample >> 8);
                audible |= Math.Abs((int)sample) > 128;
            }
            buffer.AddSamples(pcm, 0, pcm.Length);
            levels?.RecordTranslation(pcm);
            if (audible)
                Interlocked.Exchange(ref lastSpeech, Environment.TickCount64);
        };
        await peer.createDataChannel("oai-events").WaitAsync(startup.Token);
        var offer = peer.createOffer(null);
        await peer.setLocalDescription(offer);
        var thread = await server.Call("thread/start", new
        {
            model = "gpt-6.2-sol",
            config = new Dictionary<string, object> { ["model_reasoning_effort"] = "low" },
            ephemeral = true,
            sandbox = "read-only",
            approvalPolicy = "never"
        }, startup.Token);
        var threadId = thread.GetProperty("thread").GetProperty("id").GetString()!;
        var options = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["version"] = "v3",
            ["outputModality"] = "audio",
            ["voice"] = voice == Default ? null : voice,
            ["prompt"] = instructions,
            ["transport"] = new { type = "webrtc", sdp = offer.sdp }
        };
        if (microphone != null)
        {
            options["delegationAckFiller"] = false;
            options["includeStartupContext"] = false;
            options["initialItems"] = new[] { new { role = "developer", text = instructions } };
        }
        await server.Call("thread/realtime/start", options, startup.Token);

        using var eventsLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var negotiated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = ReadEvents();
        try
        {
            await await Task.WhenAny(events, connected.Task.WaitAsync(startup.Token));
            player.Play();
            if (microphone == null)
            {
                // Only one reader may consume App Server notifications. Pause the event pump
                // after negotiation while issuing the preview request.
                await negotiated.Task.WaitAsync(startup.Token);
                await server.Call("thread/realtime/appendText", new { threadId, text = instructions }, startup.Token);
                resume.TrySetResult();
                while (true)
                {
                    await await Task.WhenAny(events, ended.Task, Task.Delay(100, token));
                    long last = Interlocked.Read(ref lastSpeech);
                    if (last != 0 && Environment.TickCount64 - last > 1800) return;
                }
            }
            resume.TrySetResult();
            using var capture = new WasapiCapture(microphone, false, physicalMicrophone ? 40 : 100) { WaveFormat = format };
            // Device capture callbacks may deliver several RTP frames together, or stop
            // during silence. The microphone sender supplies a continuous 8 kHz clock.
            var micQueue = physicalMicrophone ? new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(240),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            } : null;
            capture.DataAvailable += (_, e) =>
            {
                levels?.RecordOriginal(e.Buffer.AsSpan(0, e.BytesRecorded));
                if (micQueue == null) SendPcm(e.Buffer, e.BytesRecorded);
                else micQueue.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };
            capture.RecordingStopped += (_, e) => ended.TrySetException(e.Exception ?? new IOException("Audio capture stopped."));
            capture.StartRecording();
            captureReady?.Invoke();
            if (micQueue == null)
                await await Task.WhenAny(events, ended.Task.WaitAsync(token));
            else
            {
                using var sending = CancellationTokenSource.CreateLinkedTokenSource(token);
                var sender = SendMicrophone(micQueue, sending.Token);
                try { await await Task.WhenAny(sender, events, ended.Task); }
                finally
                {
                    sending.Cancel();
                    try { await sender; } catch (OperationCanceledException) when (sending.IsCancellationRequested) { }
                }
            }
        }
        finally
        {
            eventsLifetime.Cancel();
            resume.TrySetCanceled();
            try { await events; } catch { /* Preserve the original result. */ }
        }

        async Task SendMicrophone(BufferedWaveProvider queue, CancellationToken ct)
        {
            const int frameMs = 20;
            var pcm = new byte[format.AverageBytesPerSecond * frameMs / 1000];
            var clock = Stopwatch.StartNew();
            long due = 40; // Buffer the first capture callback before sending starts.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int delay = (int)Math.Max(0, due - clock.ElapsedMilliseconds);
                if (delay > 0) await Task.Delay(delay, ct);
                ct.ThrowIfCancellationRequested();
                queue.Read(pcm, 0, pcm.Length); // ReadFully pads capture gaps with PCM silence.
                SendPcm(pcm, pcm.Length);
                long now = clock.ElapsedMilliseconds;
                due = now - due >= frameMs ? now + frameMs : due + frameMs;
            }
        }

        void SendPcm(byte[] pcm, int length)
        {
            for (int offset = 0; offset + 1 < length;)
            {
                int count = Math.Min(160, (length - offset) / 2);
                var payload = new byte[count];
                for (int i = 0; i < count; i++, offset += 2)
                {
                    var sample = BitConverter.ToInt16(pcm, offset);
                    payload[i] = MuLawEncoder.LinearToMuLawSample(sample);
                }
                peer.SendAudio((uint)count, payload);
            }
        }

        async Task ReadEvents()
        {
            while (true)
            {
                var message = await server.NextEvent(eventsLifetime.Token);
                if (!message.TryGetProperty("method", out var method) || !message.TryGetProperty("params", out var p) ||
                    !p.TryGetProperty("threadId", out var id) || id.GetString() != threadId) continue;
                if (method.GetString() == "thread/realtime/sdp")
                {
                    var result = peer.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = p.GetProperty("sdp").GetString() });
                    if (result != SetDescriptionResultEnum.OK) throw new IOException($"Codex WebRTC negotiation failed ({result}).");
                    negotiated.TrySetResult();
                    await resume.Task.WaitAsync(eventsLifetime.Token);
                }
                if (method.GetString() == "thread/realtime/error") throw new IOException("Codex voice: " + AccountServer.Detail(p, "message"));
                if (method.GetString() == "thread/realtime/closed") throw new IOException("Codex voice closed: " + AccountServer.Detail(p, "reason"));
            }
        }
    }

    public static async Task Preview(string voice, string language, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(1));
        using var devices = new MMDeviceEnumerator();
        using var speakers = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        await Run(voice, $"Say the following sentence in {language}, once: Hello, this is my interpreter voice.", null, speakers, timeout.Token);
    }
}
