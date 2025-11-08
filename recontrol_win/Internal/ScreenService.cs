using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace recontrol_win.Internal
{
    internal sealed record FrameRegion(byte[] Jpeg, bool IsFullFrame, int X, int Y, int Width, int Height);
    internal sealed record FrameBatch(List<FrameRegion> Regions);

    internal class ScreenService : IDisposable
    {
        private CancellationTokenSource? _cts;
        private Task? _streamTask;

        // Producer-consumer queue for captured bitmaps awaiting encoding
        private readonly BlockingCollection<Bitmap> _captureQueue = new BlockingCollection<Bitmap>(boundedCapacity: 2);

        // Pool for MemoryStream buffers - simple reuse to avoid reallocations
        private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

        public bool IsRunning => _streamTask != null && !_streamTask.IsCompleted;

        /// <summary>
        /// Start streaming screenshots in background.
        /// The provided onFrame callback receives a batch of encoded regions/fullframe for each captured frame.
        /// </summary>
        /// <param name="onFrame">Callback invoked for each encoded batch.</param>
        public void Start(Action<FrameBatch> onFrame, int qualityPercent = 30, int intervalMs = 200, int tileSize = 32, double downscale = 1.0)
        {
            if (IsRunning) return;
            InternalLogger.Log($"ScreenService.Start called: quality={qualityPercent}, intervalMs={intervalMs}, tileSize={tileSize}, downscale={downscale}");
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Encoder task consumes bitmaps from _captureQueue, performs tile diff and encoding
            _streamTask = Task.Run(async () =>
            {
                Bitmap? previous = null;
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // Capture current screen (scaled)
                            var bmp = CaptureBitmap(downscale);

                            // Try to enqueue for encoding; if queue is full, drop oldest and enqueue to avoid blocking capture
                            if (!_captureQueue.TryAdd(bmp))
                            {
                                // drop the oldest
                                if (_captureQueue.TryTake(out var old))
                                {
                                    old.Dispose();
                                }
                                _captureQueue.TryAdd(bmp);
                            }

                            // Process any queued bitmaps (only one at a time here)
                            while (_captureQueue.TryTake(out var toProcess))
                            {
                                try
                                {
                                    var regions = ComputeDirtyRegions(previous, toProcess, tileSize);
                                    if (regions.Count == 0)
                                    {
                                        // nothing changed
                                        toProcess.Dispose();
                                    }
                                    else
                                    {
                                        var outputs = new List<FrameRegion>();

                                        // If a single region is large (>50% area) send full frame instead
                                        var totalArea = toProcess.Width * toProcess.Height;
                                        var changedArea = regions.Sum(r => r.Width * r.Height);
                                        if (changedArea * 2 >= totalArea)
                                        {
                                            // Encode full frame
                                            var jpg = EncodeJpeg(toProcess, qualityPercent);
                                            outputs.Add(new FrameRegion(jpg, true, 0, 0, toProcess.Width, toProcess.Height));
                                        }
                                        else if (regions.Count == 1)
                                        {
                                            var r = regions[0];
                                            using var regionBmp = toProcess.Clone(r, toProcess.PixelFormat);
                                            var jpg = EncodeJpeg(regionBmp, qualityPercent);
                                            outputs.Add(new FrameRegion(jpg, false, r.X, r.Y, r.Width, r.Height));
                                        }
                                        else
                                        {
                                            // Encode multiple regions in parallel with bounded concurrency and collect outputs
                                            var maxParallel = Math.Min(Environment.ProcessorCount, regions.Count);
                                            var tasks = new List<Task<FrameRegion>>();
                                            using var semaphore = new SemaphoreSlim(maxParallel);
                                            foreach (var r in regions)
                                            {
                                                await semaphore.WaitAsync(token).ConfigureAwait(false);
                                                var regionRect = r;
                                                var regionCopy = toProcess.Clone(regionRect, toProcess.PixelFormat);
                                                var t = Task.Run(() =>
                                                {
                                                    try
                                                    {
                                                        var jpg = EncodeJpeg(regionCopy, qualityPercent);
                                                        return new FrameRegion(jpg, false, regionRect.X, regionRect.Y, regionRect.Width, regionRect.Height);
                                                    }
                                                    finally
                                                    {
                                                        regionCopy.Dispose();
                                                        semaphore.Release();
                                                    }
                                                }, token);
                                                tasks.Add(t);
                                            }

                                            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                                            outputs.AddRange(results);
                                        }

                                        // Invoke single batch callback with all outputs for this capture
                                        if (outputs.Count > 0)
                                        {
                                            onFrame(new FrameBatch(outputs));
                                        }

                                        // previous becomes this processed bitmap (keep for next diff)
                                        previous?.Dispose();
                                        previous = toProcess; // keep reference, do not dispose here
                                    }
                                }
                                catch (Exception ex)
                                {
                                    InternalLogger.LogException("ScreenService.ProcessFrame", ex);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            InternalLogger.LogException("ScreenService.Capture/Enqueue", ex);
                        }

                        try
                        {
                            await Task.Delay(intervalMs, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { break; }
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    previous?.Dispose();
                    // drain queue
                    while (_captureQueue.TryTake(out var b)) b.Dispose();
                }
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

        private Bitmap CaptureBitmap(double downscale)
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var targetW = Math.Max(1, (int)Math.Round(bounds.Width * downscale));
            var targetH = Math.Max(1, (int)Math.Round(bounds.Height * downscale));

            var bmp = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                // Use low-quality scaling on draw to reduce CPU where possible
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;

                // Capture into a temporary bitmap at full size if downscale < 1
                if (downscale == 1.0)
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);
                }
                else
                {
                    using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
                    using (var gf = Graphics.FromImage(full))
                    {
                        gf.CopyFromScreen(bounds.X, bounds.Y, 0, 0, full.Size);
                    }
                    g.DrawImage(full, new Rectangle(0, 0, targetW, targetH));
                }
            }
            return bmp;
        }

        private static List<Rectangle> ComputeDirtyRegions(Bitmap? previous, Bitmap current, int tileSize)
        {
            var regions = new List<Rectangle>();
            if (previous == null)
            {
                // first frame: whole area changed
                regions.Add(new Rectangle(0, 0, current.Width, current.Height));
                return regions;
            }

            var cols = (current.Width + tileSize - 1) / tileSize;
            var rows = (current.Height + tileSize - 1) / tileSize;

            // Lock bits for performance
            var curData = current.LockBits(new Rectangle(0, 0, current.Width, current.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var prevData = previous.LockBits(new Rectangle(0, 0, previous.Width, previous.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var curStride = Math.Abs(curData.Stride);
                var prevStride = Math.Abs(prevData.Stride);
                var bytesPerPixel = 3; // 24bpp

                for (int ry = 0; ry < rows; ry++)
                {
                    for (int rx = 0; rx < cols; rx++)
                    {
                        int x = rx * tileSize;
                        int y = ry * tileSize;
                        int w = Math.Min(tileSize, current.Width - x);
                        int h = Math.Min(tileSize, current.Height - y);

                        bool diff = false;
                        for (int row = 0; row < h && !diff; row++)
                        {
                            var curPtr = IntPtr.Add(curData.Scan0, (y + row) * curStride + x * bytesPerPixel);
                            var prevPtr = IntPtr.Add(prevData.Scan0, (y + row) * prevStride + x * bytesPerPixel);

                            // Compare memory blocks
                            if (!MemoryEquals(curPtr, prevPtr, w * bytesPerPixel))
                            {
                                diff = true;
                            }
                        }

                        if (diff)
                        {
                            regions.Add(new Rectangle(x, y, w, h));
                        }
                    }
                }
            }
            finally
            {
                current.UnlockBits(curData);
                previous.UnlockBits(prevData);
            }

            // merge nearby rectangles to reduce count
            return MergeRectangles(regions);
        }

        private static bool MemoryEquals(IntPtr a, IntPtr b, int length)
        {
            const int chunk = sizeof(long);
            int i = 0;
            // compare 8 bytes at a time
            for (; i + chunk <= length; i += chunk)
            {
                long va = Marshal.ReadInt64(a, i);
                long vb = Marshal.ReadInt64(b, i);
                if (va != vb) return false;
            }
            // tail
            for (; i < length; i++)
            {
                byte ba = Marshal.ReadByte(a, i);
                byte bb = Marshal.ReadByte(b, i);
                if (ba != bb) return false;
            }
            return true;
        }

        private static List<Rectangle> MergeRectangles(List<Rectangle> rects)
        {
            if (rects.Count <= 1) return rects;
            // Simple merge: create bounding boxes by greedy union of overlapping/adjacent rects
            var output = new List<Rectangle>(rects);
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < output.Count; i++)
                {
                    for (int j = i + 1; j < output.Count; j++)
                    {
                        var a = output[i];
                        var b = output[j];
                        var expanded = Rectangle.Union(a, b);
                        // If union area is not much larger than sum of areas, merge
                        if (expanded.Width * expanded.Height <= (a.Width * a.Height + b.Width * b.Height) * 2)
                        {
                            output[i] = expanded;
                            output.RemoveAt(j);
                            merged = true;
                            break;
                        }
                    }
                    if (merged) break;
                }
            } while (merged);

            return output;
        }

        private byte[] EncodeJpeg(Bitmap bmp, int qualityPercent)
        {
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
            _captureQueue?.Dispose();
        }
    }
}
