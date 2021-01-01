using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Rcon.Net
{
    public delegate void WordEventDelegate(IList<string> words);

    public sealed class Client
    {
        private class Request
        {
            public ManualResetEvent ResetEvent;
            public uint Sequence;
            public Packet Packet;
        }

        private const int HeaderSize = 12;

        private Socket _socket;
        private Thread _readThread;

        private uint _sequence;
        private bool _isOpen;
        private List<Request> _requests;

        public bool IsOpen => _isOpen;

        public event WordEventDelegate WordsReceived;

        public Client()
        {
            _sequence = 1;
            _isOpen = false;
            _requests = new List<Request>();
        }

        public void Open(IPAddress address, int port)
        {
            if (_isOpen)
                throw new InvalidOperationException("Already open");

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Connect(address, port);
            _isOpen = true;

            _readThread = new Thread(ReadThreadWorker);
            _readThread.Name = "RCON Read Thread";
            _readThread.Start();
        }

        public void Close()
        {
            if (!_isOpen)
                throw new InvalidOperationException("Not open");

            _socket.Close();

            // Reset state
            _sequence = 1;
            _isOpen = false;
            _requests.Clear();
        }

        private async void ReadThreadWorker()
        {
            try
            {
                // State information
                byte[] buffer = new byte[16384];
                int bufferSize = 0;
                
                // Header information
                uint encodedSequence = 0;
                uint size = 0;
                uint numWords = 0;

                while (_isOpen)
                {
                    if (bufferSize < HeaderSize)
                    {
                        // Read header
                        bufferSize += _socket.Receive(buffer, bufferSize, HeaderSize - bufferSize, SocketFlags.None);
                        if (bufferSize == HeaderSize)
                        {
                            // Read header
                            var memoryStream = new MemoryStream(buffer);
                            var stream = new BufferStream(memoryStream);
                            encodedSequence = await stream.ReadUInt32Async();
                            size = await stream.ReadUInt32Async();
                            numWords = await stream.ReadUInt32Async();
                        }
                    }
                    
                    if (bufferSize >= HeaderSize)
                    {
                        // Read remaining data
                        bufferSize += _socket.Receive(buffer, bufferSize, (int)size - bufferSize, SocketFlags.None);
                        if (bufferSize == size)
                        {
                            // Read words
                            var memoryStream = new MemoryStream(buffer); 
                            var stream = new BufferStream(memoryStream);
                            memoryStream.Position = HeaderSize;

                            var words = new List<string>();
                            for (int i = 0; i < numWords; i++)
                                words.Add(await stream.ReadWordAsync());

                            // Build packet
                            var type = (PacketType)(encodedSequence >> 30 & 0x01);
                            var fromServer = ((encodedSequence >> 31) & 0x01) == 1;
                            var sequence = encodedSequence & 0x3FFFFFFF;
                            var packet = new Packet { Type = type, Words = words, FromServer = fromServer, Sequence = sequence, Size = size };

                            // Handle packet
                            var handled = false;
                            for (var i = 0; i < _requests.Count; i++)
                            {
                                var request = _requests[i];
                                if (request.Sequence == packet.Sequence
                                    && packet.Type == PacketType.Response)
                                {
                                    // Set response
                                    request.Packet = packet;
                                    request.ResetEvent.Set();
                                    _requests.RemoveAt(i);
                                    handled = true;
                                    break;
                                }
                            }

                            if (!handled && words.Count > 0)
                                WordsReceived?.Invoke(words);

                            // Clear buffer size
                            bufferSize = 0;
                        }
                    }
                }
            }
            catch
            {
                // Set connection as not open and reset state
                _sequence = 1;
                _isOpen = false;
                _requests.Clear();
            }
        }

        public async Task<IList<string>> SendMessageAsync(IList<string> words)
        {
            if (!_isOpen)
                throw new InvalidOperationException("Not open");

            var packet = new Packet { Type = PacketType.Request, Words = words, FromServer = false, Sequence = _sequence, Size = 0 };
            var request = new Request { Sequence = _sequence, ResetEvent = new ManualResetEvent(false) };
            _requests.Add(request);
            _sequence++;

            var memoryStream = new MemoryStream();
            var stream = new BufferStream(memoryStream);

            // Create encoded sequence
            var encodedSequence = packet.Sequence & 0x3FFFFFFF;
            if (packet.FromServer)
                encodedSequence += 0x80000000;

            if (packet.Type == PacketType.Response)
                encodedSequence += 0x40000000;

            // Calculate length of data
            int size = HeaderSize // Header length
                + packet.Words.Count * sizeof(uint) // Number of lengths for each word
                + packet.Words.Select(w => w.Length + 1).Sum(); // Sum of lengths of all words

            // Write to stream
            await stream.WriteUInt32Async(encodedSequence);
            await stream.WriteUInt32Async((uint)size);
            await stream.WriteUInt32Async((uint)packet.Words.Count);
            foreach (var word in packet.Words)
                await stream.WriteWordAsync(word);

            // Send data
            var buffer = memoryStream.GetBuffer();
            _socket.Send(buffer, size, SocketFlags.None);

            // Wait for response
            request.ResetEvent.WaitOne();
            var response = request.Packet;
            var status = response.Words[0];
            if (status != "OK")
                throw new ArgumentException(status);

            return response.Words.Skip(1).ToList();
        }

        public async Task<IList<string>> SendMessageAsync(params string[] words)
        {
            return await SendMessageAsync(words.ToList());
        }
    }
}
