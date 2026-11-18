using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DeskBoxToDo
{
    /// <summary>Writes a multi-resolution .ico (PNG frames) for the exe resource.</summary>
    internal static class IconExport
    {
        public static void Write(string path, int[] sizes)
        {
            var frames = new List<byte[]>();
            var dims = new List<int>();

            foreach (int size in sizes)
            {
                using (Icon icon = TrayIconFactory.Create(size))
                using (Bitmap bitmap = icon.ToBitmap())
                using (var buffer = new MemoryStream())
                {
                    bitmap.Save(buffer, ImageFormat.Png);
                    frames.Add(buffer.ToArray());
                    dims.Add(size);
                }
            }

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);              // reserved
                writer.Write((ushort)1);              // type: icon
                writer.Write((ushort)frames.Count);   // image count

                int offset = 6 + 16 * frames.Count;
                for (int index = 0; index < frames.Count; index++)
                {
                    int size = dims[index];
                    writer.Write((byte)(size >= 256 ? 0 : size)); // width
                    writer.Write((byte)(size >= 256 ? 0 : size)); // height
                    writer.Write((byte)0);                        // palette
                    writer.Write((byte)0);                        // reserved
                    writer.Write((ushort)1);                      // planes
                    writer.Write((ushort)32);                     // bpp
                    writer.Write(frames[index].Length);
                    writer.Write(offset);
                    offset += frames[index].Length;
                }

                foreach (byte[] frame in frames)
                {
                    writer.Write(frame);
                }
            }
        }
    }
}
