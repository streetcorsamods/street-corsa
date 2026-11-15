using StreetCorsa.Looks;

namespace StreetCorsa.Services.Race.Traffic.Conversion
{
    /// <summary>
    /// Textures decoded on the graphics card (<see cref="TexelReader"/>: any DDS, PNG or JPEG). Made on the thread that
    /// uses it and used by that one alone, as the reader wants.
    /// </summary>
    public sealed class GpuTexels : ITexels, IDisposable
    {
        private readonly TexelReader _reader = new();

        public (int Width, int Height, byte[] Rgba)? Read(byte[] bytes, int most)
        {
            try
            {
                // The reader draws a texture at the size asked, bigger too: a cap only ever shrinks one. A texture whose
                // header says it fits is read as it is; one that cannot say is read as it is and made smaller here
                if (TexelReader.HeaderSize(bytes) is var (w, h) && Math.Max(w, h) > most) return _reader.Read(bytes, most);
                var read = _reader.Read(bytes, null);
                if (Math.Max(read.Width, read.Height) <= most) return read;
                var (toWidth, toHeight) = TexelReader.SizeOf(read.Width, read.Height, most);
                return (toWidth, toHeight, TrafficTextures.Resize(read.Rgba, read.Width, read.Height, toWidth, toHeight));
            }
            catch (Exception)
            {
                // A texture of a kind the loader does not take (a TGA, a broken file): left as it is
                return null;
            }
        }

        public void Dispose() => _reader.Dispose();
    }
}
