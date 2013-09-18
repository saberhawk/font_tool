using System.Drawing;
using System.Drawing.Imaging;

namespace font_tool
{
    static class BitmapTools
    {
        public static unsafe void CopyRect(BitmapData dest_data, Point dest_position, BitmapData src_data, Rectangle src_rect)
        {
            for (int y = 0; y < src_rect.Height; ++y)
            {
                int* src_start = (int*)(src_data.Scan0  + y * src_data.Stride);
                int* dst_start = (int*)(dest_data.Scan0 + (y + dest_position.Y) * dest_data.Stride) + dest_position.X;

                for (int x = 0; x < src_rect.Width; ++x)
                {
                    int data = *src_start++;
                    *dst_start++ = data;
                }
            }
        }

        public static void CopyRect(Bitmap dest, Point dest_position, Bitmap src, Rectangle src_rect)
        {
            BitmapData dest_data = dest.LockBits(new Rectangle(Point.Empty, dest.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            BitmapData src_data = src.LockBits(src_rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            CopyRect(dest_data, dest_position, src_data, src_rect);

            src.UnlockBits(src_data);
            dest.UnlockBits(dest_data);
        }

        public static void CopyRect(BitmapData dest_data, Point dest_position, Bitmap src, Rectangle src_rect)
        {
            BitmapData src_data = src.LockBits(src_rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            CopyRect(dest_data, dest_position, src_data, src_rect);

            src.UnlockBits(src_data);
        }

        public static void CopyRect(BitmapData dest_data, Point dest_position, Bitmap src)
        {
            Rectangle src_rect = new Rectangle(Point.Empty, src.Size);
            CopyRect(dest_data, dest_position, src, src_rect);
        }

        public static void CopyRect(Bitmap dest, Bitmap src, Rectangle src_rect)
        {
            CopyRect(dest, Point.Empty, src, src_rect);
        }


    }
}
