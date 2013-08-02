using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace Cipher
{
    public class CipherStream : Stream
    {
        private Stream _stream1, _stream2;

        public CipherStream(Stream stream1, Stream stream2)
        {
            _stream1 = stream1;
            _stream2 = stream2;
        }

        protected override void Dispose(bool disposing)
        {
            _stream1.Dispose();
            _stream2.Dispose();

            base.Dispose(disposing);
        }

        public override bool CanRead
        {
            get { return _stream1.CanRead && _stream2.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _stream1.CanSeek && _stream2.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _stream1.CanWrite && _stream2.CanWrite; }
        }

        public override void Flush()
        {
            _stream1.Flush();
            _stream2.Flush();
        }

        public override long Length
        {
            get
            {
                Debug.Assert(_stream1.Length == _stream2.Length);
                return _stream1.Length;
            }
        }

        public override long Position
        {
            get
            {
                Debug.Assert(_stream1.Position == _stream2.Position);
                return _stream1.Position;
            }
            set
            {
                _stream1.Position = value;
                _stream2.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            byte[] subbuf = new byte[count];
            int res1 = _stream1.Read(subbuf, 0, count);
            int res2 = _stream2.Read(buffer, offset, count);
            count = Math.Min(res1, res2);
            for (int i = 0; i < count; i++)
                buffer[offset + i] ^= subbuf[i];

            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long res1 = _stream1.Seek(offset, origin);
            long res2 = _stream2.Seek(offset, origin);
            Debug.Assert(res1 == res2);
            return res1;
        }

        public override void SetLength(long value)
        {
            _stream1.SetLength(value);
            _stream2.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            byte[] buf1 = new byte[count];
            byte[] buf2 = new byte[count];
            Random r = new Random();
            r.NextBytes(buf1);
            Array.Copy(buffer, offset, buf2, 0, count);
            for (int i = 0; i < count; i++)
            {
                buf2[i] ^= buf1[i];
            }

            _stream1.Write(buf1, 0, count);
            _stream2.Write(buf2, 0, count);
        }
    }
}
