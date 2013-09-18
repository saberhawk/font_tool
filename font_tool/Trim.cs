using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace font_tool
{
    class Trim
    {
        public static void GetBitmapBytes(Bitmap bitmap, out byte[] out_bytes, out BitmapData out_bitmap_data)
        {
            BitmapData bitmap_data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int byte_count = bitmap_data.Stride * bitmap_data.Height;
            byte[] bytes = new byte[byte_count];
            Marshal.Copy(bitmap_data.Scan0, bytes, 0, byte_count);

            bitmap.UnlockBits(bitmap_data);

            out_bytes = bytes;
            out_bitmap_data = bitmap_data;
        }

        public static Rectangle CalculateTrimmedRect(byte[] bytes, BitmapData bitmap_data)
        {
            int min_x = int.MaxValue, min_y = int.MaxValue, max_x = 0, max_y = int.MinValue;
            int row_start = 0;
            for (int y = 0; y < bitmap_data.Height; ++y)
            {
                bool pixels_in_row = false;
                for (int x = 0; x < bitmap_data.Width; ++x)
                {
                    if (bytes[row_start + x * 4 + 3] != 0)
                    {
                        pixels_in_row = true;
                        min_x = Math.Min(min_x, x);
                        max_x = Math.Max(max_x, x);
                    }
                }

                if (pixels_in_row)
                {
                    min_y = Math.Min(min_y, y);
                    max_y = Math.Max(max_y, y);
                }

                row_start += bitmap_data.Stride;
            }

            if (min_x == int.MaxValue && min_y == int.MaxValue)
            {
                return Rectangle.Empty;
            }
            else
            {
                return new Rectangle(min_x, min_y, max_x - min_x + 1, max_y - min_y + 1);
            }
        }

        public static Bitmap CopyTrimmedRect(BitmapData original_data, byte[] original_bytes, Rectangle solidRect)
        {
            Bitmap trimmed = new Bitmap(solidRect.Width, solidRect.Height);
            BitmapData bitmap_data = trimmed.LockBits(new Rectangle(0, 0, trimmed.Width, trimmed.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] bytes = new byte[bitmap_data.Stride * bitmap_data.Height];

            for (int y = 0; y < bitmap_data.Height; ++y)
            {
                for (int x = 0; x < bitmap_data.Width; ++x)
                {
                    bytes[y * bitmap_data.Stride + x * 4]     = original_bytes[(y + solidRect.Y) * original_data.Stride + (x + solidRect.X) * 4];
                    bytes[y * bitmap_data.Stride + x * 4 + 1] = original_bytes[(y + solidRect.Y) * original_data.Stride + (x + solidRect.X) * 4 + 1];
                    bytes[y * bitmap_data.Stride + x * 4 + 2] = original_bytes[(y + solidRect.Y) * original_data.Stride + (x + solidRect.X) * 4 + 2];
                    bytes[y * bitmap_data.Stride + x * 4 + 3] = original_bytes[(y + solidRect.Y) * original_data.Stride + (x + solidRect.X) * 4 + 3];
                }
            }

            Marshal.Copy(bytes, 0, bitmap_data.Scan0, bytes.Length);
            trimmed.UnlockBits(bitmap_data);

            return trimmed;
        }

        public static Bitmap TrimBitmapAlpha(Bitmap original, int padding, out Point offset)
        {
            byte[] bytes;
            BitmapData bitmap_data;

            GetBitmapBytes(original, out bytes, out bitmap_data);

            Rectangle solidRect = CalculateTrimmedRect(bytes, bitmap_data);

            if (solidRect.Width == 0)
            {
                offset = Point.Empty;
                return new Bitmap(1, 1);
            }

            Bitmap trimmed = new Bitmap(solidRect.Width, solidRect.Height);
            BitmapTools.CopyRect(trimmed, Point.Empty, original, solidRect);

            offset = solidRect.Location;
            //offset.X += padding;
            //offset.Y += padding;
            return trimmed;
        }
    }
}
