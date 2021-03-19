using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace Parallelator
{
    public class Grayscale
    {
        public delegate void ProgressReportedDelegate(double progress);
        public event ProgressReportedDelegate ProgressChanged;

        public SemaphoreSlim pauseSemaphore = new SemaphoreSlim(1);

        public bool IsCompleted { get; private set; } = false;

        public async Task<WriteableBitmap> ConvertAsync(ImageEdit inputFile)
        {
            var imageData = await inputFile.GetStorageFile().OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(imageData);

            var width = Convert.ToInt32(decoder.PixelWidth);
            var height = Convert.ToInt32(decoder.PixelHeight);
            var bmp = new WriteableBitmap(width, height); 

            imageData.Seek(0);
            await bmp.SetSourceAsync(imageData);

            var srcPixelStream = bmp.PixelBuffer.AsStream();
            byte[] srcPixels = new byte[4 * width * height];
            int length = await srcPixelStream.ReadAsync(srcPixels, 0, 4 * width * height);
            byte[] destPixels = new byte[4 * width * height];

            // <----------------->
            destPixels = await ToGrayscaleParallel(srcPixels, width, height, inputFile);

            srcPixelStream.Seek(0, SeekOrigin.Begin);
            await srcPixelStream.WriteAsync(destPixels, 0, length);
            bmp.Invalidate();

            if (inputFile.GetState().Equals(ImageEdit.EditState.Cancelled))
                return bmp;

            inputFile.SetState(ImageEdit.EditState.Finished);

            return bmp;
        }

        private async Task<byte[]> ToGrayscaleParallel(byte[] srcPixels, int width, int height, ImageEdit ie)
        {
            byte[] destPixels = new byte[4 * width * height];

            await Task.Run(() =>
            {
                ParallelLoopResult outer = new ParallelLoopResult();
                ParallelLoopResult inner = new ParallelLoopResult();

                try
                {
                    outer = Parallel.For(0, height, new ParallelOptions() { MaxDegreeOfParallelism = 1, CancellationToken = ie.GetCancellationToken().Token },
                    (y, outerState) =>
                    {
                        pauseSemaphore.Wait();
                        // pauseSemaphore.Release();

                        inner = Parallel.For(0, width, new ParallelOptions() { MaxDegreeOfParallelism = ie.GetParallelism(), CancellationToken = ie.GetCancellationToken().Token },
                        async (x, innerState) =>
                        {
                            await Task.Delay(100);
                            AdjustPixels(x, y, width, height, srcPixels, ref destPixels);
                        });
                        this.ProgressChanged?.Invoke((double)(y + 1) / (double)height * 100.0);
                        pauseSemaphore.Release();
                    });

                    IsCompleted = true;
                    destPixels = ToGrayscaleSequential(srcPixels, width, height, ie);
                }

                catch (Exception)
                {
                    pauseSemaphore.Release();
                    ie.SetState(ImageEdit.EditState.Cancelled);
                    return;
                }
            });

            return destPixels;

        }

        private void AdjustPixels(int x, int y, int width, int height, byte[] srcPixels, ref byte[] destPixels)
        {
            byte b, g, r, a, luminance;

            b = srcPixels[(x + y * width) * 4];
            g = srcPixels[(x + y * width) * 4 + 1];
            r = srcPixels[(x + y * width) * 4 + 2];
            a = srcPixels[(x + y * width) * 4 + 3];

            luminance = Convert.ToByte(0.299 * r + 0.587 * g + 0.114 * b);

            destPixels[(x + y * width) * 4] = luminance;     // B
            destPixels[(x + y * width) * 4 + 1] = luminance; // G
            destPixels[(x + y * width) * 4 + 2] = luminance; // R
            destPixels[(x + y * width) * 4 + 3] = luminance; // A
        }

        private byte[] ToGrayscaleSequential(byte[] srcPixels, int width, int height, ImageEdit ie)
        {
            byte b, g, r, a, luminance;

            byte[] destPixels = new byte[4 * width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {

                    b = srcPixels[(x + y * width) * 4];
                    g = srcPixels[(x + y * width) * 4 + 1];
                    r = srcPixels[(x + y * width) * 4 + 2];
                    a = srcPixels[(x + y * width) * 4 + 3];

                    luminance = Convert.ToByte(0.299 * r + 0.587 * g + 0.114 * b);

                    destPixels[(x + y * width) * 4] = luminance;     // B
                    destPixels[(x + y * width) * 4 + 1] = luminance; // G
                    destPixels[(x + y * width) * 4 + 2] = luminance; // R
                    destPixels[(x + y * width) * 4 + 3] = luminance; // A
                }

                // this.ProgressChanged?.Invoke((double)(y + 1) / (double)height * 100.0);
            }

            return destPixels;
        }
        
        public static async Task<StorageFile> WriteableBitmapToStorageFile(WriteableBitmap WB, StorageFile file)
        {
            Guid BitmapEncoderGuid = BitmapEncoder.JpegEncoderId;

            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoderGuid, stream);
                Stream pixelStream = WB.PixelBuffer.AsStream();
                byte[] pixels = new byte[pixelStream.Length];
                await pixelStream.ReadAsync(pixels, 0, pixels.Length);

                encoder.SetPixelData(BitmapPixelFormat.Bgra8,
                                     BitmapAlphaMode.Ignore,
                                     (uint)WB.PixelWidth,
                                     (uint)WB.PixelHeight,
                                     96.0,
                                     96.0,
                                     pixels);
                await encoder.FlushAsync();
            }
            return file;
        }
    }
}
