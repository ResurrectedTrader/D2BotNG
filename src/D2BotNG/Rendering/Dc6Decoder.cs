using System.Runtime.InteropServices;

namespace D2BotNG.Rendering;

/// <summary>
/// DC6 file format header structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Dc6Header
{
    public int Version;
    public int SubVersion;
    public int Zeros;
    public int Termination;
    public int Directions;
    public int FramesPerDirection;
}

/// <summary>
/// DC6 frame header structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Dc6FrameHeader
{
    public int Flip;
    public int Width;
    public int Height;
    public int OffsetX;
    public int OffsetY;
    public int Zeros;
    public int NextBlock;
    public int Length;
}

/// <summary>
/// Decoded DC6 frame data
/// </summary>
public class Dc6Frame
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[,] Pixels { get; init; } = null!;
}

/// <summary>
/// Decodes Diablo 2 DC6 sprite files
/// </summary>
public static class Dc6Decoder
{
    private const int HeaderSize = 24; // sizeof(Dc6Header)
    private const int FrameHeaderSize = 32; // sizeof(Dc6FrameHeader)

    /// <summary>
    /// Decodes the first frame from a DC6 file
    /// </summary>
    public static Dc6Frame DecodeFirstFrame(byte[] dc6Data)
    {
        if (dc6Data.Length < HeaderSize)
            throw new ArgumentException("DC6 data too small for header");

        // Read main header
        var header = ReadStruct<Dc6Header>(dc6Data, 0);

        if (header.Directions < 1 || header.FramesPerDirection < 1)
            throw new ArgumentException("Invalid DC6 header: no frames");

        // Read first frame pointer (located after main header)
        int framePointer = BitConverter.ToInt32(dc6Data, HeaderSize);

        // Read frame header
        var frameHeader = ReadStruct<Dc6FrameHeader>(dc6Data, framePointer);

        // Decode the frame pixels
        var pixels = DecodeFramePixels(dc6Data, framePointer + FrameHeaderSize, frameHeader);

        return new Dc6Frame
        {
            Width = frameHeader.Width,
            Height = frameHeader.Height,
            Pixels = pixels
        };
    }

    /// <summary>
    /// Decodes all frames from a DC6 file (used for fonts)
    /// </summary>
    // ReSharper disable once UnusedMember.Global — rendering utility for future font support
    public static Dc6Frame[] DecodeAllFrames(byte[] dc6Data)
    {
        if (dc6Data.Length < HeaderSize)
            throw new ArgumentException("DC6 data too small for header");

        var header = ReadStruct<Dc6Header>(dc6Data, 0);
        int totalFrames = header.Directions * header.FramesPerDirection;

        var frames = new Dc6Frame[totalFrames];

        for (int i = 0; i < totalFrames; i++)
        {
            int framePointer = BitConverter.ToInt32(dc6Data, HeaderSize + i * 4);
            var frameHeader = ReadStruct<Dc6FrameHeader>(dc6Data, framePointer);
            var pixels = DecodeFramePixels(dc6Data, framePointer + FrameHeaderSize, frameHeader);

            frames[i] = new Dc6Frame
            {
                Width = frameHeader.Width,
                Height = frameHeader.Height,
                Pixels = pixels
            };
        }

        return frames;
    }

    /// <summary>
    /// Decodes the RLE-compressed pixel data from a DC6 frame
    /// </summary>
    private static byte[,] DecodeFramePixels(byte[] data, int dataOffset, Dc6FrameHeader header)
    {
        var pixels = new byte[header.Width, header.Height];

        if (header.Width <= 0 || header.Height <= 0)
            return pixels;

        int x = 0;
        int y = header.Height - 1; // DC6 is stored bottom-to-top

        for (int bytesRead = 0; bytesRead < header.Length && dataOffset < data.Length;)
        {
            byte b = data[dataOffset++];
            bytesRead++;

            if (b == 0x80) // Row terminator
            {
                x = 0;
                y--;
                if (y < 0) break;
            }
            else if ((b & 0x80) == 0x80) // Skip pixels (transparent)
            {
                x += b & 0x7F;
            }
            else // Literal run
            {
                int count = b;
                for (int i = 0; i < count && dataOffset < data.Length; i++)
                {
                    if (x < header.Width && y >= 0)
                    {
                        pixels[x, y] = data[dataOffset];
                    }
                    dataOffset++;
                    bytesRead++;
                    x++;
                }
            }
        }

        return pixels;
    }

    private static T ReadStruct<T>(byte[] data, int offset) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        if (offset + size > data.Length)
            throw new ArgumentException($"Not enough data to read {typeof(T).Name}");

        nint ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(data, offset, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
