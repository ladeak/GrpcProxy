using System.Buffers;
using System.Buffers.Binary;
using Grpc.Core;

namespace GrpcProxy.Grpc
{
    internal sealed class DefaultSerializationContext : SerializationContext
    {
        private const int MessageSize = 4;
        private const int HeaderSize = MessageSize + 1;
        private readonly IBufferWriter<byte> _writer;
        private int? _payloadLength;
        private bool _headerWritten = false;

        public DefaultSerializationContext(IBufferWriter<byte> writer)
        {
            _writer = writer;
        }

        public override void Complete(byte[] payload)
        {
            WriteHeader(payload.Length);
            _writer.Write(payload);
        }

        public override void SetPayloadLength(int payloadLength) => _payloadLength = payloadLength;

        private void WriteHeader(int length)
        {
            if (_headerWritten)
                return;
            var headerData = _writer.GetSpan(HeaderSize);

            // No compression
            headerData[0] = 0;

            // Message length
            BinaryPrimitives.WriteUInt32BigEndian(headerData.Slice(1), (uint)length);

            _writer.Advance(HeaderSize);
            _headerWritten = true;
        }

        public override IBufferWriter<byte> GetBufferWriter()
        {
            if (!_headerWritten && _payloadLength.HasValue)
                WriteHeader(_payloadLength.Value);
            return _writer;
        }

        public override void Complete()
        {
        }
    }
}
