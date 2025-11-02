using recontrol_win.Internal;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows; // <-- ADD THIS
using System;
using System.Collections.Generic; // For List
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;


namespace recontrol_win.Tools
{
    /// <summary>
    /// Minimal WebRTC helper that handles signaling and streams the screen as a NATIVE VIDEO TRACK.
    /// </summary>
    internal sealed class WebRTCClient : IDisposable
    {
        private readonly Func<object, Task> _sendSignal;
        // private readonly ScreenService _screenService; // <-- REMOVED
        private RTCPeerConnection? _pc;
        // private RTCDataChannel? _dc; // <-- REMOVED
        private WindowsVideoEndPoint? _videoSource; // <-- ADDED
        private bool _disposed;

        public event Action<string>? Log;

        // UPDATED CONSTRUCTOR
        public WebRTCClient(Func<object, Task> sendSignal)
        {
            _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
            // _screenService = screenService ?? throw new ArgumentNullException(nameof(screenService)); // <-- REMOVED
        }

        private void EnsurePeerConnection()
        {
            if (_pc != null) return;

            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            _pc = new RTCPeerConnection(config);

            _pc.onicecandidate += async (candidate) =>
            {
                try
                {
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.candidate)) return;
                    await _sendSignal(new
                    {
                        type = "ice_candidate",
                        candidate = candidate.candidate,
                        sdpMid = candidate.sdpMid,
                        sdpMLineIndex = (int)candidate.sdpMLineIndex
                    });
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"onicecandidate send failed: {ex.Message}");
                }
            };

            _pc.onconnectionstatechange += (state) =>
            {
                Log?.Invoke($"PeerConnection state: {state}");
                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
                {
                    StopStreaming(); // This will now close the whole connection
                }
            };

            // REMOVED: _pc.ondatachannel
        }

        // REMOVED: private void AttachDataChannel(RTCDataChannel dc) { ... }

        public async Task StartAsOffererAsync()
        {
            EnsurePeerConnection();
            if (_pc == null) return;

            try
            {
                // --- THIS IS THE KEY CHANGE ---
                // 1. Create the native video source
                _videoSource = new WindowsVideoEndPoint(new VpxVideoEncoder());
                var videoTrack = new MediaStreamTrack(_videoSource.GetVideoSourceFormats());
                _pc.addTrack(videoTrack);

                // 2. Start capturing
                await _videoSource.StartVideo();
                // ------------------------------
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Failed to add video track: {ex.Message}");
                return;
            }

            // REMOVED: _pc.createDataChannel("screen");

            var offer = _pc.CreateOffer(IPAddress.Any);
            await _sendSignal(new { type = "offer", sdp = offer.ToString() });
        }

        public async Task HandleSignalAsync(JsonElement signalingPayload)
        {
            var type = signalingPayload.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(type)) return;

            EnsurePeerConnection();
            if (_pc == null) return;

            switch (type)
            {
                case "offer":
                    {
                        // This client (WPF) should not be receiving offers, only creating them.
                        // But we can implement it for completeness.
                        Log?.Invoke("Received offer (unexpected for desktop client)");
                        var sdpStr = signalingPayload.GetProperty("sdp").GetString();
                        if (string.IsNullOrWhiteSpace(sdpStr)) break;

                        var remote = SDP.ParseSDPDescription(sdpStr);
                        _pc.SetRemoteDescription(SdpType.offer, remote);

                        var answer = _pc.CreateAnswer(IPAddress.Any);
                        await _sendSignal(new { type = "answer", sdp = answer.ToString() });
                        break;
                    }
                case "answer":
                    {
                        var sdpStr = signalingPayload.GetProperty("sdp").GetString();
                        if (string.IsNullOrWhiteSpace(sdpStr)) break;
                        var remote = SDP.ParseSDPDescription(sdpStr);
                        _pc.SetRemoteDescription(SdpType.answer, remote);
                        break;
                    }
                case "ice_candidate":
                    {
                        var candStr = signalingPayload.TryGetProperty("candidate", out var c) ? c.GetString() : null;
                        var sdpMid = signalingPayload.TryGetProperty("sdpMid", out var mid) ? mid.GetString() : null;
                        var sdpMLineIndex = signalingPayload.TryGetProperty("sdpMLineIndex", out var mline) ? mline.GetInt32() : (int?)null;
                        if (!string.IsNullOrWhiteSpace(candStr) && sdpMLineIndex.HasValue)
                        {
                            var init = new RTCIceCandidateInit
                            {
                                candidate = candStr,
                                sdpMid = sdpMid,
                                sdpMLineIndex = (ushort)sdpMLineIndex.Value
                            };
                            _pc.addIceCandidate(init);
                        }
                        break;
                    }
            }
        }

        // This method now stops the *entire* connection
        public void StopStreaming()
        {
            Log?.Invoke("Stopping WebRTC Connection...");
            try { _videoSource?.CloseVideo(); } catch { }
            try { _pc?.Close("stop"); } catch { }
            _videoSource = null;
            _pc = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopStreaming();
        }
    }
}
