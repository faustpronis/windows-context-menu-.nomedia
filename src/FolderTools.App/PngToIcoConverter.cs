using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace FolderTools.App
{
    /// <summary>Writes a standard multi-size ICO; PNG input is rasterized with alpha preserved.</summary>
    internal static class PngToIcoConverter
    {
        private static readonly int[] IconSizes = { 16, 24, 32, 48, 64, 128, 256 };

        public static void Convert(string pngPath, string icoPath)
        {
            using (Image source = Image.FromFile(pngPath))
            {
                if (source.RawFormat.Guid != ImageFormat.Png.Guid)
                {
                    throw new InvalidDataException("Выбранный файл не является изображением PNG.");
                }

                byte[][] images = new byte[IconSizes.Length][];
                for (int i = 0; i < IconSizes.Length; i++)
                {
                    images[i] = BuildDibImage(source, IconSizes[i]);
                }

                using (var stream = new FileStream(icoPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((ushort)0); // reserved
                    writer.Write((ushort)1); // icon resource
                    writer.Write((ushort)images.Length);

                    int offset = 6 + (16 * images.Length);
                    for (int i = 0; i < images.Length; i++)
                    {
                        int size = IconSizes[i];
                        writer.Write((byte)(size == 256 ? 0 : size));
                        writer.Write((byte)(size == 256 ? 0 : size));
                        writer.Write((byte)0); // palette
                        writer.Write((byte)0); // reserved
                        writer.Write((ushort)1); // color planes
                        writer.Write((ushort)32); // bits per pixel
                        writer.Write((uint)images[i].Length);
                        writer.Write((uint)offset);
                        offset = checked(offset + images[i].Length);
                    }

                    foreach (byte[] image in images)
                    {
                        writer.Write(image);
                    }
                }
            }
        }

        private static byte[] BuildDibImage(Image source, int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Min(size / (float)source.Width, size / (float)source.Height);
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                int x = (size - width) / 2;
                int y = (size - height) / 2;
                graphics.DrawImage(source, new Rectangle(x, y, width, height));

                int xorLength = checked(size * size * 4);
                int maskStride = ((size + 31) / 32) * 4;
                int maskLength = checked(maskStride * size);
                using (var data = new MemoryStream(40 + xorLength + maskLength))
                using (var writer = new BinaryWriter(data))
                {
                    // BITMAPINFOHEADER; ICO DIB height includes both XOR and AND planes.
                    writer.Write((uint)40);
                    writer.Write(size);
                    writer.Write(size * 2);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)0);
                    writer.Write((uint)xorLength);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);

                    byte[] mask = new byte[maskLength];
                    for (int row = size - 1; row >= 0; row--)
                    {
                        for (int column = 0; column < size; column++)
                        {
                            Color color = bitmap.GetPixel(column, row);
                            writer.Write(color.B);
                            writer.Write(color.G);
                            writer.Write(color.R);
                            writer.Write(color.A);
                            if (color.A < 128)
                            {
                                int maskRow = size - 1 - row;
                                mask[(maskRow * maskStride) + (column / 8)] |= (byte)(1 << (7 - (column % 8)));
                            }
                        }
                    }
                    writer.Write(mask);
                    writer.Flush();
                    return data.ToArray();
                }
            }
        }
    }

    internal static class IconImporter
    {
        public static string Import(string baseDirectory, string destinationId, string sourcePath)
        {
            if (!string.Equals(Path.GetExtension(sourcePath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return FolderTools.Core.IconStore.Import(baseDirectory, destinationId, sourcePath);
            }

            string temporary = Path.Combine(Path.GetTempPath(), "foldertools-" + Guid.NewGuid().ToString("N") + ".ico");
            try
            {
                PngToIcoConverter.Convert(sourcePath, temporary);
                return FolderTools.Core.IconStore.Import(baseDirectory, destinationId, temporary);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
