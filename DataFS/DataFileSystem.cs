using System;
using System.Collections.Generic;
using System.Text;
using BasicFS;
using System.IO;
using System.Runtime.InteropServices;

namespace DataFS
{
    using FRP = FileRetrievalPermissions;

    public class DataFileSystem : IFileSystem, IDisposable
    {
        private const string RootName = "root";
        private const int DefaultDepth = 1;

        #region Static Helpers
        private static void WriteUInt(byte[] buf, uint data, ref ulong index)
        {
            byte[] d = BitConverter.GetBytes(data);
            WriteData<uint>(buf, d, ref index);
        }

        private static void WriteULong(byte[] buf, ulong data, ref ulong index)
        {
            byte[] d = BitConverter.GetBytes(data);
            WriteData<ulong>(buf, d, ref index);
        }

        private static void WriteData<T>(byte[] buf, byte[] d, ref ulong index)
        {
            Array.Reverse(d);
            d.CopyTo(buf, (int)index);
            index += (ulong)Marshal.SizeOf(typeof(T));
        }

        private static void WriteString(byte[] buf, string str, ref ulong index)
        {
            WriteUInt(buf, (uint)str.Length, ref index);
            int trueLen = (str.Length % sizeof(uint) == 0) ?
                str.Length :
                (sizeof(uint) * ((str.Length / sizeof(uint)) + 1));
            Encoding.ASCII.GetBytes(str, 0, str.Length, buf, (int)index);
            index += (ulong)trueLen;
        }

        private static void TabulateDirectory(int id, DirectoryInfo dir, ref List<FileTableEntry> entries)
        {
            entries[id].size = 0;

            ulong curOffset = entries[id].offset;
            foreach (FileInfo file in dir.GetFiles())
            {
                FileTableEntry e = new FileTableEntry();
                e.parentId = (uint)id;
                e.name = file.Name;
                e.size = (ulong)file.Length;
                e.offset = curOffset;
                entries.Add(e);

                ulong trueSize = (e.size % sizeof(uint) == 0) ? e.size :
                    (sizeof(uint) * ((e.size / sizeof(uint)) + 1));
                entries[id].size += trueSize;
                curOffset += trueSize;
            }

            foreach (DirectoryInfo sub in dir.GetDirectories())
            {
                int subId = entries.Count;
                FileTableEntry e = new FileTableEntry();
                e.parentId = (uint)id;
                e.name = sub.Name;
                e.offset = curOffset;
                entries.Add(e);
                TabulateDirectory(subId, sub, ref entries);
                entries[id].size += entries[subId].size;
                curOffset += entries[subId].size;
            }
        }
        #endregion

        #region Static Factories
        public static byte[] Create(DirectoryInfo dir)
        {
            List<FileTableEntry> table = new List<FileTableEntry>();
            FileTableEntry root = new FileTableEntry();
            root.name = RootName;
            root.offset = 0;
            table.Add(root);
            TabulateDirectory(0, dir, ref table);
            table.RemoveAt(0);

            ulong headerSize = (ulong)(sizeof(uint) + table.Count *
                (2 * (sizeof(uint) + sizeof(ulong))));
            foreach (FileTableEntry e in table)
            {
                headerSize += (ulong)((e.name.Length % sizeof(uint) == 0) ?
                    e.name.Length :
                    (sizeof(uint) * ((e.name.Length / sizeof(uint)) + 1)));
            }
            root.offset = headerSize;
            for (int i = 0; i < table.Count; i++)
            {
                table[i].offset += headerSize;
            }

            byte[] buf = new byte[root.offset + root.size];
            ulong index = 0;

            WriteUInt(buf, (uint)table.Count, ref index);

            Dictionary<uint, DirectoryInfo> lookup = new Dictionary<uint,DirectoryInfo>();
            lookup[0] = dir;
            for (int i = 0; i < table.Count; i++)
            {
                WriteUInt(buf, table[i].parentId, ref index);
                WriteULong(buf, table[i].offset, ref index);
                WriteULong(buf, table[i].size, ref index);
                WriteString(buf, table[i].name, ref index);

                DirectoryInfo parentDir = lookup[table[i].parentId];
                FileSystemInfo thisObj = parentDir.GetFileSystemInfos(table[i].name)[0];

                if ((thisObj.Attributes | FileAttributes.Directory) == FileAttributes.Directory)
                    lookup[(uint)i + 1] = (DirectoryInfo)thisObj;
                else
                {
                    FileStream stream = new FileStream(thisObj.FullName, FileMode.Open, FileAccess.Read);
                    stream.Read(buf, (int)table[i].offset, (int)table[i].size);
                }
            }

            return buf;
        }

        public static DataFileSystem CreateAndLoad(DirectoryInfo dir, Stream stream)
        {
            byte[] buf = Create(dir);
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(buf, 0, buf.Length);
            stream.Flush();
            stream.Seek(0, SeekOrigin.Begin);
            return new DataFileSystem(stream);
        }
        #endregion

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

        public DataFileSystem(Stream stream)
        {
            _src = stream;
            ParseFileTable();
            GenerateFileNodes();
        }

        private sealed class FileTableEntry
        {
            public uint parentId;
            public ulong offset;
            public ulong size;
            public string name;
        }

        #region Private Members
        private Stream _src;
        private List<FileTableEntry> _fileTable = new List<FileTableEntry>();
        private Dictionary<FileTableEntry, FileSystemNode> _fileAssoc = new Dictionary<FileTableEntry, FileSystemNode>();
        private FileSystemNode _root;
        private ulong _rootSize = 0;
        #endregion

        #region Parsing Helpers
        private FileTableEntry ParseFileTableEntry()
        {
            FileTableEntry entry = new FileTableEntry();
            entry.parentId = ReadUInt();
            entry.offset = ReadULong();
            entry.size = ReadULong();
            entry.name = ReadString();
            return entry;
        }
        
        private void ParseFileTable()
        {
            _rootSize = 0;
            uint fileCount = ReadUInt();
            for (uint i = 0; i < fileCount; i++)
            {
                var entry = ParseFileTableEntry();
                if (entry.parentId == 0)
                    _rootSize += entry.size;
                _fileTable.Add(entry);
            }
        }

        private void GenerateFileNodes()
        {
            _root = new FileSystemNode(RootName, this);
            foreach (FileTableEntry entry in _fileTable)
            {
                FileSystemNode parentNode = entry.parentId == 0 ?
                    _root : _fileAssoc[_fileTable[(int)entry.parentId - 1]];
                _fileAssoc[entry] = new FileSystemNode(parentNode.Path + FileSystemNode.Separator + entry.name, this);
            }
        }
        #endregion

        #region IFileSystem Members

        public FileSystemNode Root
        {
            get { return _root; }
        }

        public FileSystemNode GetNode(string path)
        {
            if (RootName.Equals(path))
                return _root;
            return new List<FileSystemNode>(_fileAssoc.Values).Find(delegate(FileSystemNode n)
            {
                return n.Path.Equals(path);
            });
        }

        public FileSystemNode[] LoadChildren(FileSystemNode node)
        {
            return LoadChildren(node, DefaultDepth);
        }

        public FileSystemNode[] LoadChildren(FileSystemNode node, int depth)
        {
            if (depth < 1)
                return null;

            List<FileSystemNode> children =
                new List<FileSystemNode>(_fileAssoc.Values).FindAll(delegate(FileSystemNode n)
                {
                    return n.Parent.Path.Equals(node.Path);
                });
            List<FileTableEntry> entries =
                children.ConvertAll<FileTableEntry>(delegate(FileSystemNode n)
                {
                    return _fileTable.Find(delegate(FileTableEntry e)
                    {
                        return _fileAssoc[e].Path.Equals(n.Path);
                    });
                });
            return entries.ConvertAll<FileSystemNode>(delegate(FileTableEntry e)
            {
                FileSystemNode n = _fileAssoc[e];
                if (depth > 1)
                    n = new FileSystemNode(n.Name, n.Parent, LoadChildren(n, depth - 1));
                return n;
            }).ToArray();
        }

        public Stream GetFileStream(FileSystemNode node, FileRetrievalMode retMode, FileAccessMode accMode)
        {
            FileTableEntry entry = _fileTable.Find(delegate(FileTableEntry e)
            {
                return _fileAssoc[e].Path.Equals(node.Path);
            });

            byte[] buf = new byte[entry.size];
            _src.Seek((long)entry.offset, SeekOrigin.Begin);
            _src.Read(buf, 0, (int)entry.size);
            return new MemoryStream(buf, false);
        }

        public ulong GetFileSize(FileSystemNode node)
        {
            if (node == _root)
                return _rootSize;

            FileTableEntry entry = _fileTable.Find(delegate(FileTableEntry e)
            {
                return _fileAssoc[e].Path.Equals(node.Path);
            });

            return entry.size;
        }

        public bool IsFileReadOnly(FileSystemNode node)
        {
            return true;
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            _src.Dispose();
        }

        #endregion
    }
}
