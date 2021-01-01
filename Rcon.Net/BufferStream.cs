using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rcon.Net
{
    public class BufferStream
    {
        private readonly Stream _stream;

        public Stream Stream => _stream;

        public BufferStream(Stream stream)
        {
            _stream = stream;
        }

        public async Task<uint> ReadUInt32Async()
        {
            var buffer = new byte[sizeof(int)];
            if (await _stream.ReadAsync(buffer, 0, buffer.Length) != buffer.Length)
                throw new TimeoutException("Unable to read data");

            if (!BitConverter.IsLittleEndian)
                buffer.Reverse();

            return BitConverter.ToUInt32(buffer, 0);
        }

        public async Task<string> ReadWordAsync(string encoding = "UTF-8")
        {
            // Read length
            var length = await ReadUInt32Async();

            // Read string
            var buffer = new byte[length + 1]; // Read extra null byte
            if (await _stream.ReadAsync(buffer, 0, buffer.Length) != buffer.Length)
                throw new TimeoutException("Unable to read data");

            return Encoding.GetEncoding(encoding).GetString(buffer, 0, buffer.Length - 1);
        }

        public async Task WriteUInt32Async(uint obj)
        {
            var buffer = BitConverter.GetBytes(obj);
            if (!BitConverter.IsLittleEndian)
                buffer.Reverse();

            await _stream.WriteAsync(buffer, 0, sizeof(int));
        }

        public async Task WriteWordAsync(string obj, string encoding = "UTF-8")
        {
            var buffer = Encoding.GetEncoding(encoding).GetBytes(obj);
            await WriteUInt32Async((uint)buffer.Length);
            await _stream.WriteAsync(buffer, 0, buffer.Length);
            await _stream.WriteAsync(new byte[1] { 0 }, 0, 1); // Write null byte
        }
    }
}
