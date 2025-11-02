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
    /// Adds verbose logging to trace the full lifecycle.
    /// </summary>
    internal sealed class WebRTCClient : IDisposable
    {
        private readonly Func<object, Task> _sendSignal;
        private RTCPeerConnection? _pc;
        private WindowsVideoEndPoint? _videoSource; // video source/encoder
        private bool _disposed;

        public event Action<string>? Log;

        public WebRTCClient(Func<object, Task> sendSignal)
        {
            _sendSignal = sendSignal ?? throw new ArgumentNullException(nameof(sendSignal));
            Log?.Invoke("WebRTCClient constructed");
        }

        private void EnsurePeerConnection()
        {
            if (_pc != null) return;

            LogInfo("Creating RTCPeerConnection...");

            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            try
            {
                _pc = new RTCPeerConnection(config);
                LogInfo("RTCPeerConnection created.");
                LogInfo($"ICE servers: {string.Join(", ", config.iceServers?.ConvertAll(s => s.urls) ?? new List<string>())}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to create RTCPeerConnection: {ex.Message}");
                throw;
            }

            _pc.onicecandidate += async (candidate) =>
            {
                try
                {
                    if (candidate == null)
                    {
                        LogInfo("onicecandidate: null (end of gathering or no candidate)");
                        return;
                    }

                    LogInfo($"onicecandidate: {candidate.candidate} | sdpMid={candidate.sdpMid} | sdpMLineIndex={candidate.sdpMLineIndex}");

                    await _sendSignal(new
                    {
                        type = "ice_candidate",
                        candidate = candidate.candidate,
                        sdpMid = candidate.sdpMid,
                        sdpMLineIndex = (int)candidate.sdpMLineIndex
                    });
                    LogInfo("ICE candidate signal sent.");
                }
                catch (Exception ex)
                {
                    LogError($"onicecandidate send failed: {ex.Message}");
                }
            };

            _pc.onconnectionstatechange += (state) =>
            {
                LogInfo($"PeerConnection state: {state}");
                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
                {
                    LogWarn("PC in terminal state. Stopping streaming.");
                    StopStreaming();
                }
            };

            // Optional RTCP hooks: just note they fired.
            try
            {
                _pc.OnReceiveReport += (ep, media, rr) => LogInfo($"RTCP RR received: remote={ep}, media={media}");
                _pc.OnSendReport += (media, sr) => LogInfo($"RTCP SR sent: media={media}");
            }
            catch { }
        }

        public async Task StartAsOffererAsync()
        {
            EnsurePeerConnection();
            if (_pc == null)
            {
                LogError("StartAsOffererAsync: PC is null");
                return;
            }

            try
            {
                LogInfo("Creating WindowsVideoEndPoint with VPX encoder...");
                _videoSource = new WindowsVideoEndPoint(new VpxVideoEncoder());
                var formats = _videoSource.GetVideoSourceFormats();
                LogInfo($"Video formats available: {formats?.Count}");
                if (formats != null)
                {
                    for (int i = 0; i < formats.Count; i++)
                    {
                        LogInfo($"Format[{i}]: {formats[i]}");
                    }
                }

                var videoTrack = new MediaStreamTrack(formats);
                _pc.addTrack(videoTrack);
                LogInfo("Video track added to RTCPeerConnection.");

                LogInfo("Starting video capture...");
                await _videoSource.StartVideo();
                LogInfo("Video capture started.");
            }
            catch (Exception ex)
            {
                LogError($"Failed to add video track/start capture: {ex.Message}");
                return;
            }

            try
            {
                LogInfo("Creating SDP offer...");
                var offer = _pc.CreateOffer(IPAddress.Any);

                if (offer == null)
                {
                    LogError("CreateOffer returned null.");
                    return;
                }

                // --- CORRECTED BLOCK ---
                LogInfo("Setting local description (offer)...");
                var offerInit = new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = offer.ToString()
                };
                await _pc.setLocalDescription(offerInit); // <-- Use lowercase 's', await, and pass the Init object
                LogInfo("Local description (offer) set.");
                // --- END CORRECTION ---

                var sdp = offer.ToString(); // We still send the full SDP string
                LogInfo($"Offer created. SDP length={sdp?.Length}");
                await _sendSignal(new { type = "offer", sdp });
                LogInfo("Offer signal sent.");
            }
            catch (Exception ex)
            {
                LogError($"CreateOffer/setLocalDescription/send failed: {ex.Message}");
            }
        }

        public async Task HandleSignalAsync(JsonElement signalingPayload)
        {
            try
            {
                LogInfo($"HandleSignalAsync payload: {signalingPayload.GetRawText()}");
            }
            catch { }

            var type = signalingPayload.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(type))
            {
                LogWarn("Signal missing 'type'. Ignored.");
                return;
            }

            EnsurePeerConnection();
            if (_pc == null)
            {
                LogError("HandleSignalAsync: PC is null");
                return;
            }

            switch (type)
            {
                case "offer":
                    {
                        LogWarn("Received offer (unexpected for desktop client)");
                        var sdpStr = signalingPayload.GetProperty("sdp").GetString();
                        LogInfo($"Remote offer SDP length={sdpStr?.Length}");
                        if (string.IsNullOrWhiteSpace(sdpStr)) break;

                        try
                        {
                            var remote = SDP.ParseSDPDescription(sdpStr);
                            _pc.SetRemoteDescription(SdpType.offer, remote);
                            LogInfo("Remote offer set.");

                            LogInfo("Creating answer...");
                            var answer = _pc.CreateAnswer(IPAddress.Any);
                            var asdp = answer.ToString();
                            LogInfo($"Answer created. SDP length={asdp?.Length}");
                            await _sendSignal(new { type = "answer", sdp = asdp });
                            LogInfo("Answer signal sent.");
                        }
                        catch (Exception ex)
                        {
                            LogError($"Error handling offer: {ex.Message}");
                        }
                        break;
                    }
                case "answer":
                    {
                        var sdpStr = signalingPayload.TryGetProperty("sdp", out var sdp) ? sdp.GetString() : null;
                        LogInfo($"Remote answer SDP length={sdpStr?.Length}");
                        if (string.IsNullOrWhiteSpace(sdpStr)) break;

                        try
                        {
                            // --- START FIX ---
                            // Use the ASYNC method that takes an RTCSessionDescriptionInit
                            // This correctly wires up the internal state machine.
                            var answerInit = new RTCSessionDescriptionInit
                            {
                                type = RTCSdpType.answer,
                                sdp = sdpStr
                            };
                            var result = _pc.setRemoteDescription(answerInit);
                            // --- END FIX ---

                            LogInfo($"Remote answer set. Result: {result}"); // Log the result
                            // --- END FIX ---

                            LogInfo("Remote answer set.");
                        }
                        catch (Exception ex)
                        {
                            LogError($"setRemoteDescription(answer) failed: {ex.Message}");
                        }
                        break;
                    }
                case "ice_candidate":
                    {
                        var candStr = signalingPayload.TryGetProperty("candidate", out var c) ? c.GetString() : null;
                        var sdpMid = signalingPayload.TryGetProperty("sdpMid", out var mid) ? mid.GetString() : null;
                        var sdpMLineIndex = signalingPayload.TryGetProperty("sdpMLineIndex", out var mline) ? mline.GetInt32() : (int?)null;
                        LogInfo($"ICE candidate recv: cand={(candStr ?? "null").Substring(0, Math.Min(64, candStr?.Length ?? 0))}..., mid={sdpMid}, mline={sdpMLineIndex}");
                        if (!string.IsNullOrWhiteSpace(candStr) && sdpMLineIndex.HasValue)
                        {
                            try
                            {
                                var init = new RTCIceCandidateInit
                                {
                                    candidate = candStr,
                                    sdpMid = sdpMid,
                                    sdpMLineIndex = (ushort)sdpMLineIndex.Value
                                };
                                _pc.addIceCandidate(init);
                                LogInfo("ICE candidate added to PC.");
                            }
                            catch (Exception ex)
                            {
                                LogError($"addIceCandidate failed: {ex.Message}");
                            }
                        }
                        else
                        {
                            LogWarn("ICE candidate missing fields. Ignored.");
                        }
                        break;
                    }
                default:
                    LogWarn($"Unknown signal type '{type}'.");
                    break;
            }
        }

        public void StopStreaming()
        {
            LogInfo("Stopping WebRTC Connection...");
            try { _videoSource?.CloseVideo(); LogInfo("Video source closed."); } catch (Exception ex) { LogError($"CloseVideo error: {ex.Message}"); }
            try { _pc?.Close("stop"); LogInfo("Peer connection closed."); } catch (Exception ex) { LogError($"PC close error: {ex.Message}"); }
            _videoSource = null;
            _pc = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            LogInfo("Disposing WebRTCClient...");
            StopStreaming();
        }

        private void LogInfo(string msg) => Log?.Invoke($"INFO: {msg}");
        private void LogWarn(string msg) => Log?.Invoke($"WARN: {msg}");
        private void LogError(string msg) => Log?.Invoke($"ERROR: {msg}");
    }
}
