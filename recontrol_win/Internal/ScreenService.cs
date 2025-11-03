using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace recontrol_win.Internal
{
    internal class ScreenService : IDisposable
    {
        private CancellationTokenSource? _cts;
        private Task? _streamTask;

        public bool IsRunning => _streamTask != null && !_streamTask.IsCompleted;

        /// <summary>
        /// Start streaming screenshots in background.
        /// The provided onFrame callback receives JPEG bytes for each captured frame.
        /// </summary>
        public void Start(Action<byte[]> onFrame, int qualityPercent = 30, int intervalMs = 200)
        {
            if (IsRunning) return;
            InternalLogger.Log($"ScreenService.Start called: quality={qualityPercent}, intervalMs={intervalMs}");
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _streamTask = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var bytes = CaptureJpeg(qualityPercent);
                            onFrame?.Invoke(bytes);
                        }
                        catch (Exception ex)
                        {
                            InternalLogger.LogException("ScreenService.Capture/OnFrame", ex);
                            // swallow per-frame errors
                        }

                        await Task.Delay(intervalMs, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        public void Stop()
        {
            InternalLogger.Log("ScreenService.Stop called");
            try
            {
                _cts?.Cancel();
                _streamTask?.Wait(500);
            }
            catch (Exception ex) { InternalLogger.LogException("ScreenService.Stop", ex); }
            finally
            {
                _streamTask = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private byte[] CaptureJpeg(int qualityPercent)
        {
            // capture full primary screen
            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);
            }

            using var ms = new MemoryStream();
            var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
            var encoder = System.Drawing.Imaging.Encoder.Quality;
            using var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(encoder, Math.Clamp(qualityPercent, 1, 100));
            bmp.Save(ms, jpgEncoder, encParams);
            return ms.ToArray();
        }

        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var c in codecs)
            {
                if (c.FormatID == format.Guid) return c;
            }
            return null;
        }

        public void Dispose()
        {
            InternalLogger.Log("ScreenService.Dispose called");
            Stop();
        }
    }
}
