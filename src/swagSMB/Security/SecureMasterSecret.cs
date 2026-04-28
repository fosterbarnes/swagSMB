using System;
using System.Text;

namespace swagSMB.Security
{
    public sealed class SecureMasterSecret : IDisposable
    {
        private byte[] _bytes;

        public SecureMasterSecret()
        {
            _bytes = Array.Empty<byte>();
        }

        public SecureMasterSecret(string value)
        {
            Set(value);
        }

        public int Length => _bytes?.Length ?? 0;

        public bool IsEmpty => _bytes == null || _bytes.Length == 0;

        public void Set(string value)
        {
            Clear();
            if (string.IsNullOrEmpty(value))
            {
                _bytes = Array.Empty<byte>();
                return;
            }

            _bytes = Encoding.UTF8.GetBytes(value);
        }

        public void SetFrom(SecureMasterSecret other)
        {
            Clear();
            if (other == null || other._bytes == null || other._bytes.Length == 0)
            {
                _bytes = Array.Empty<byte>();
                return;
            }

            _bytes = new byte[other._bytes.Length];
            Buffer.BlockCopy(other._bytes, 0, _bytes, 0, _bytes.Length);
        }

        public byte[] CopyBytes()
        {
            if (_bytes == null || _bytes.Length == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] copy = new byte[_bytes.Length];
            Buffer.BlockCopy(_bytes, 0, copy, 0, _bytes.Length);
            return copy;
        }

        public string AsTransientString()
        {
            return _bytes == null || _bytes.Length == 0
                ? string.Empty
                : Encoding.UTF8.GetString(_bytes);
        }

        public void Clear()
        {
            if (_bytes != null && _bytes.Length > 0)
            {
                Array.Clear(_bytes, 0, _bytes.Length);
            }

            _bytes = Array.Empty<byte>();
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
