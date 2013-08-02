using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicFS;
using System.IO;
using System.Runtime.InteropServices;

namespace DataFS
{
    using FRP = FileRetrievalPermissions;
    using System.Diagnostics;

    public class DataNodeFileSystem : IFileSystem, IDisposable
    {
        private const string RootName = "root";
        private const int DefaultDepth = 1;
        private const uint NodeSize = 1024;

        private Stream _src;
        private FileTable _fileTable = new FileTable();
        private Dictionary<uint, FileSystemNode> _fileAssoc = new Dictionary<uint, FileSystemNode>();
        private FileSystemNode _root;
        private ulong _rootSize = 0;

        #region Byte Conversion Helpers
        private byte[] ReadBuf<T>()
        {
            byte[] buf = new byte[Marshal.SizeOf(typeof(T))];
            _src.Read(buf, 0, Marshal.SizeOf(typeof(T)));
            Array.Reverse(buf);
            return buf;
        }

        private ulong ReadULong()
        {
            return BitConverter.ToUInt64(ReadBuf<ulong>(), 0);
        }

        private uint ReadUInt()
        {
            return BitConverter.ToUInt32(ReadBuf<uint>(), 0);
        }

        private uint[] ReadUIntArray()
        {
            uint len = ReadUInt();
            uint[] arr = new uint[len];
            byte[] buf = new byte[len * sizeof(uint)];
            _src.Read(buf, 0, buf.Length);
            Buffer.BlockCopy(buf, 0, arr, 0, buf.Length);
            return arr;
        }

        private string ReadString()
        {
            uint len = ReadUInt();
            uint roundup = (uint)((len % sizeof(uint) == 0) ?
                len :
                sizeof(uint) * ((len / sizeof(uint)) + 1));
            byte[] buf = new byte[roundup];
            _src.Read(buf, 0, (int)roundup);
            byte[] str = new byte[len];
            Array.Copy(buf, str, len);
            return Encoding.ASCII.GetString(str);
        }
        #endregion

        private sealed class FileTableEntry
        {
            public ulong size;
            public List<uint> nodes;
            public string name;
            public bool readOnly;
            public bool directory;
        }

        private sealed class FileTable
        {
            const uint Mask_Directory = 0x0001;
            const uint Mask_ReadOnly = 0x0002;

            public List<FileTableEntry> entries;
            public List<uint> voidNodes;
            public uint maxNode;

            public void Parse(DataNodeFileSystem fs)
            {
                fs._rootSize = 0;
                uint fileCount = fs.ReadUInt();
                for (uint i = 1; i <= fileCount; i++)
                {
                    var entry = ParseEntry(fs);
                    if (entry.parentId == 0)
                        fs._rootSize += entry.size;
                    entries.Add(entry);
                }

                maxNode = fs.ReadUInt();
                voidNodes = new List<uint>(fs.ReadUIntArray());
            }

            private FileTableEntry ParseEntry(DataNodeFileSystem fs)
            {
                FileTableEntry entry = new FileTableEntry();
                entry.parentId = fs.ReadUInt();
                entry.size = fs.ReadULong();
                entry.nodes = new List<uint>(fs.ReadUIntArray());
                entry.name = fs.ReadString();
                return entry;
            }
        }

        private void GenerateAllFileNodes()
        {
            _root = new FileSystemNode(RootName, this);
            _fileAssoc[0] = _root;
            for (int i = 0; i < _fileTable.entries.Count; i++)
            {
                var entry = _fileTable.entries[i];
                var parentNode = _fileAssoc[entry.parentId];
                _fileAssoc[(uint)(i + 1)] = new FileSystemNode(parentNode.Path + FileSystemNode.Separator + entry.name, this);
            }
        }

        private class DataNodeFileStream : Stream
        {
            private Stream _stream;
            private FileRetrievalMode _retMode;
            private FileAccessMode _accMode;
            private DataNodeFileSystem _fs;
            private FileSystemNode _node;

            public DataNodeFileStream(DataNodeFileSystem fs, FileSystemNode node, FileRetrievalMode retMode, FileAccessMode accMode, Stream stream)
            {
                _stream = stream;
                _retMode = retMode;
                _accMode = accMode;
                _fs = fs;
                _node = node;
            }

            public override bool CanRead
            {
                get { return _stream.CanRead && _accMode.HasFlag(FileAccessMode.Read); }
            }

            public override bool CanSeek
            {
                get { return _stream.CanSeek; }
            }

            public override bool CanWrite
            {
                get { return _stream.CanWrite && _accMode.HasFlag(FileAccessMode.Write); }
            }

            public override void Flush()
            {
                _stream.Flush();
            }

            public override long Length
            {
                get { return (long)_fs.GetFileSize(_node); }
            }

            public override long Position
            {
                get;
                set;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (buffer == null)
                    throw new ArgumentNullException("buffer");
                if (offset < 0)
                    throw new ArgumentOutOfRangeException("offset");
                if (count < 0)
                    throw new ArgumentOutOfRangeException("count");
                if (offset + count > buffer.Length)
                    throw new ArgumentException("Buffer not long enough.");
                if (!_accMode.HasFlag(FileAccessMode.Read))
                    throw new NotSupportedException("Cannot read.");

                int trueCount = 0;
                int firstNodeIndex = (int)(Position / (long)NodeSize);
                int firstNodeOffset = (int)(Position % (long)NodeSize);

                long len = Length;
                long lastPosition = Position + (long)count - 1;
                if (lastPosition > len)
                {
                    count -= (int)(lastPosition - len);
                    lastPosition = len;
                }
                int lastNodeIndex = (int)(lastPosition / (long)NodeSize);
                int lastNodeOffset = (int)(lastPosition % (long)NodeSize);

                uint entryId = _fs._fileAssoc.First((n) => (n.Value.Equals(_node))).Key;
                Debug.Assert(entryId > 0);
                var entry = _fs._fileTable.entries[(int)(entryId - 1)];

                uint firstNodeId = entry.nodes[firstNodeIndex];
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }

        private class NodeController
        {
            private Stream _src;
            private List<uint> _unused;

            private object _streamLocker = new object();
            private uint _curLocker = 0;

            public NodeController(Stream src, IEnumerable<uint> unused)
            {
                _src = src;
                _unused = new List<uint>(unused);
                _unused.Sort();

                _contexts.Add(null);
            }

            public class StreamContext
            {
                public long Position { get; internal set; }
                public long Length { get; internal set; }

                internal List<uint> _nodes = new List<uint>();
                public IEnumerable<uint> Nodes { get { return _nodes; } }
            }

            private List<StreamContext> _contexts = new List<StreamContext>();

            public uint GetStreamContextHandle(IEnumerable<uint> nodes, long length)
            {
                long extra = ((long)nodes.Count() * NodeSize) - length;
                if (extra < 0 || extra > NodeSize)
                    throw new ArgumentOutOfRangeException("length must match the node count");
                if (_unused.Where((n) => (nodes.Contains(n))).Count() > 0)
                    throw new ArgumentOutOfRangeException("cannot use unallocated nodes");

                var ctx = new StreamContext();
                ctx.Position = NodeSize * nodes.ElementAt(0);
                ctx.Length = length;
                ctx._nodes.AddRange(nodes);
                _contexts.Add(ctx);

                return (uint)_contexts.Count - 1;
            }

            public delegate void StreamOperation(Stream src, StreamContext ctx);

            public void DoStreamOperation(uint streamContextId, StreamOperation op)
            {
                var ctx = _contexts[(int)streamContextId];

                if (_curLocker == streamContextId)
                {
                    op(_src, ctx);
                }
                else lock (_streamLocker)
                {
                    _curLocker = streamContextId;
                    _src.Position = ctx.Position;
                    op(_src, ctx);
                    ctx.Position = _src.Position;
                    _curLocker = 0;
                }
            }

            public delegate T StreamResult<T>(Stream src, StreamContext ctx);

            public T GetStreamResult<T>(uint streamContextId, StreamResult<T> op)
            {
                var ctx = _contexts[(int)streamContextId];

                T result;
                if (_curLocker == streamContextId)
                {
                    result = op(_src, ctx);
                }
                else lock (_streamLocker)
                {
                    _curLocker = streamContextId;
                    _src.Position = ctx.Position;
                    result = op(_src, ctx);
                    ctx.Position = _src.Position;
                    _curLocker = 0;
                }

                return result;
            }

            public bool SetLength(uint streamContextId, long newLength)
            {
                var ctx = _contexts[(int)streamContextId];

                int nNodes = (int)((newLength + (long)NodeSize - 1) / (long)NodeSize);
                if (newLength < ctx.Length)
                {
                    if (ctx.Position > newLength)
                        ctx.Position = newLength;
                    _unused.AddRange(ctx._nodes.Skip(nNodes).Take(ctx._nodes.Count - nNodes));
                    ctx._nodes.RemoveRange(nNodes, ctx._nodes.Count - nNodes);
                }
                else if (newLength > ctx.Length)
                {
                    int nNew = nNodes - ctx._nodes.Count;
                    int nRequired = nNew - _unused.Count;
                    if (nRequired > 0)
                    {
                        DoStreamOperation(streamContextId, (s, c) =>
                        {
                            uint nOldNodes = (uint)(s.Length / (long)NodeSize);
                            s.SetLength(s.Length + (nRequired * NodeSize));
                            int nLeft = nRequired;
                            uint i = nOldNodes;
                            while (0 < nLeft--)
                                _unused.Add(i++);
                        });
                    }

                    ctx._nodes.AddRange(_unused.Take(nNew));
                    _unused.RemoveRange(0, nNew);
                }

                ctx.Length = newLength;
                return true;
            }
        }

        private class NodalStreamAdaptor : Stream
        {
            private NodeController _ctrl;
            private uint _ctxHandle;

            public NodalStreamAdaptor(NodeController ctrl, uint ctxHandle)
            {
                _ctrl = ctrl;
                _ctxHandle = ctxHandle;
            }

            public override bool CanRead
            {
                get { return _ctrl.GetStreamResult(_ctxHandle, (s, ctx) => (s.CanRead)); }
            }

            public override bool CanSeek
            {
                get { return _ctrl.GetStreamResult(_ctxHandle, (s, ctx) => (s.CanSeek)); }
            }

            public override bool CanWrite
            {
                get { return _ctrl.GetStreamResult(_ctxHandle, (s, ctx) => (s.CanWrite)); }
            }

            public override void Flush()
            {
                _ctrl.DoStreamOperation(_ctxHandle, (s, ctx) => { s.Flush(); });
            }

            public override long Length
            {
                get { return _ctrl.GetStreamResult(_ctxHandle, (s, ctx) => (ctx.Length)); }
            }

            public override long Position
            {
                get
                {
                    return _ctrl.GetStreamResult(_ctxHandle, (src, ctx) =>
                    {
                        long realPos = src.Position;
                        uint nNode = (uint)(realPos / (long)NodeSize);
                        uint offset = (uint)(realPos % (long)NodeSize);
                        if (!ctx.Nodes.Contains(nNode))
                        {
                            Position = 0;
                            return 0;
                        }
                        return (long)offset + (long)(ctx.Nodes.ToList().IndexOf(nNode) * NodeSize);
                    });
                }
                set
                {
                    _ctrl.DoStreamOperation(_ctxHandle, (src, ctx) =>
                    {
                        uint nNodeIndex = (uint)(value / (long)NodeSize);
                        uint offset = (uint)(value % (long)NodeSize);
                        src.Position = (long)offset + (long)(ctx.Nodes.ElementAt((int)nNodeIndex) * NodeSize);
                    });
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (buffer == null)
                    throw new ArgumentNullException("buffer");
                if (offset < 0)
                    throw new ArgumentOutOfRangeException("offset");
                if (count < 0)
                    throw new ArgumentOutOfRangeException("count");
                if (offset + count > buffer.Length)
                    throw new ArgumentException("Buffer not long enough.");

                return _ctrl.GetStreamResult(_ctxHandle, (src, ctx) =>
                {
                    uint curNode = (uint)(Position / (long)NodeSize);
                    int curNodeIndex = ctx.Nodes.ToList().IndexOf(curNode);
                    uint curNodeOffset = (uint)(Position % (long)NodeSize);
                    uint maxRead = NodeSize - curNodeOffset;
                    int total = 0;
                    int nRead = -1;
                    while (nRead != 0)
                    {
                        int thisCount = Math.Min((int)maxRead, count);
                        nRead = src.Read(buffer, offset, thisCount);
                        total += nRead;
                        offset += nRead;
                        count -= nRead;
                        maxRead = NodeSize;
                        curNode = ctx.Nodes.ElementAt(++curNodeIndex);
                        Position = (long)(NodeSize * curNode);
                    }

                    return total;
                });
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.Current:
                        Position += offset;
                        break;
                    case SeekOrigin.End:
                        Position = Length + offset;
                        break;
                }

                return Position;
            }

            public override void SetLength(long value)
            {
                _ctrl.SetLength(_ctxHandle, value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }

        #region IFileSystem Members
        public FileSystemNode Root
        {
            get { throw new NotImplementedException(); }
        }

        public FileSystemNode GetNode(string path)
        {
            throw new NotImplementedException();
        }

        public FileSystemNode[] LoadChildren(FileSystemNode node)
        {
            throw new NotImplementedException();
        }

        public FileSystemNode[] LoadChildren(FileSystemNode node, int depth)
        {
            throw new NotImplementedException();
        }

        public System.IO.Stream GetFileStream(FileSystemNode node, FileRetrievalMode retMode, FileAccessMode accMode)
        {
            throw new NotImplementedException();
        }

        public ulong GetFileSize(FileSystemNode node)
        {
            throw new NotImplementedException();
        }

        public bool IsFileReadOnly(FileSystemNode node)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region IDisposable Members
        public void Dispose()
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
