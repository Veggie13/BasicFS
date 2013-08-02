using System;
using System.IO;
using System.Collections.Generic;

namespace BasicFS
{
    using FRP = FileRetrievalPermissions;

    [Flags]
    public enum FileRetrievalPermissions
    {
        NeedsWrite = 0x01,
        NeedsRead = 0x02,
        DoesAppend = (0x04 | NeedsWrite),
        MustExist = 0x08,
        MustNotExist = (0x10 | NeedsWrite),
        CreatesFile = 0x20,
        DoesTrunc = (0x40 | NeedsWrite)
    }

    public enum FileRetrievalMode
    {
        CreateNew = (FRP.CreatesFile | FRP.MustNotExist),
        Create = (FRP.CreatesFile | FRP.DoesTrunc),
        Open = (FRP.MustExist),
        OpenOrCreate = (FRP.CreatesFile),
        Truncate = (FRP.MustExist | FRP.DoesTrunc),
        Append = (FRP.CreatesFile | FRP.DoesAppend)
    }

    public enum FileAccessMode
    {
        Read = 0x1, Write = 0x2, ReadWrite = (Read | Write)
    }

    public interface IFileSystem
    {
        FileSystemNode Root { get; }
        FileSystemNode GetNode(string path);

        FileSystemNode[] LoadChildren(FileSystemNode node);
        FileSystemNode[] LoadChildren(FileSystemNode node, int depth);

        Stream GetFileStream(FileSystemNode node, FileRetrievalMode retMode, FileAccessMode accMode);

        ulong GetFileSize(FileSystemNode node);

        bool IsFileReadOnly(FileSystemNode node);
    }

    public class SystemFileSystem : IFileSystem
    {
        private const string RootName = "root";
        private const int DefaultDepth = 1;

        private DirectoryInfo _info;
        public SystemFileSystem(DirectoryInfo info)
        {
            _info = info;
            _root = new FileSystemNode(RootName, this);
        }

        private FileSystemNode _root;
        public FileSystemNode Root
        {
            get { return _root; }
        }

        private FileSystemInfo GetPathInfo(string path)
        {
            string[] elements = path.Split(new string[] { FileSystemNode.Separator }, StringSplitOptions.None);
            if (!elements[0].Equals(RootName))
                return null;

            DirectoryInfo curNode = _info;
            for (int i = 1; i < elements.Length - 1; i++)
            {
                DirectoryInfo[] children = curNode.GetDirectories();
                DirectoryInfo child = Array.Find(children, delegate(DirectoryInfo x)
                {
                    return x.Name.Equals(elements[i]);
                });
                if (child == null)
                    return null;
                curNode = child;
            }

            FileSystemInfo[] finalChildren = curNode.GetFileSystemInfos();
            FileSystemInfo final;
            if (elements.Length > 1)
            {
                final = Array.Find(finalChildren, delegate(FileSystemInfo x)
                {
                    return x.Name.Equals(elements[elements.Length - 1]);
                });
            }
            else
                final = _info;

            return final;
        }

        public FileSystemNode GetNode(string path)
        {
            if (GetPathInfo(path) == null)
                return null;

            return new FileSystemNode(path, this);
        }

        public FileSystemNode[] LoadChildren(FileSystemNode node)
        {
            return LoadChildren(node, DefaultDepth);
        }

        public FileSystemNode[] LoadChildren(FileSystemNode node, int depth)
        {
            if (depth < 1)
                return null;

            FileSystemInfo final = GetPathInfo(node.Path);
            if (final == null)
                return null;
            else if ((final.Attributes & FileAttributes.Directory) != FileAttributes.Directory)
                return new FileSystemNode[0];

            List<FileSystemNode> childList = new List<FileSystemNode>();
            foreach (FileSystemInfo info in ((DirectoryInfo)final).GetFileSystemInfos())
            {
                FileSystemNode child = new FileSystemNode(node.Path + FileSystemNode.Separator + info.Name, this);
                if (depth > 1)
                    child = new FileSystemNode(info.Name, node, LoadChildren(child, depth - 1));

                childList.Add(child);
            }

            return childList.ToArray();
        }

        public Stream GetFileStream(FileSystemNode node, FileRetrievalMode retMode, FileAccessMode accMode)
        {
            FileSystemInfo info = GetPathInfo(node.Path);
            bool exists = (info != null && info.Exists);
            if (retMode.hasPermission(FRP.MustExist) && !exists)
                return null;
            if (retMode.hasPermission(FRP.MustNotExist) && exists)
                return null;

            FileMode fMode = FileMode.Open;
            switch (retMode)
            {
                case FileRetrievalMode.Open: fMode = FileMode.Open; break;
                case FileRetrievalMode.Append: fMode = FileMode.Append; break;
                case FileRetrievalMode.Create: fMode = FileMode.Create; break;
                case FileRetrievalMode.CreateNew: fMode = FileMode.CreateNew; break;
                case FileRetrievalMode.OpenOrCreate: fMode = FileMode.OpenOrCreate; break;
                case FileRetrievalMode.Truncate: fMode = FileMode.Truncate; break;
            }

            FileAccess fAcc = FileAccess.Read;
            switch (accMode)
            {
                case FileAccessMode.Read: fAcc = FileAccess.Read; break;
                case FileAccessMode.Write: fAcc = FileAccess.Write; break;
                case FileAccessMode.ReadWrite: fAcc = FileAccess.ReadWrite; break;
            }

            return new FileStream(info.FullName, fMode, fAcc);
        }

        private ulong GetDirectorySize(DirectoryInfo info)
        {
            ulong total = 0;
            FileInfo[] childFiles = info.GetFiles();
            foreach (FileInfo cf in childFiles)
                total += (ulong)cf.Length;
            DirectoryInfo[] childDirs = info.GetDirectories();
            foreach (DirectoryInfo cd in childDirs)
                total += GetDirectorySize(cd);

            return total;
        }

        public ulong GetFileSize(FileSystemNode node)
        {
            FileSystemInfo info = GetPathInfo(node.Path);
            if (info == null || !info.Exists)
                return 0;

            if (info is DirectoryInfo)
            {
                return GetDirectorySize((DirectoryInfo)info);
            }
            else if (info is FileInfo)
            {
                return (ulong)((FileInfo)info).Length;
            }

            return 0;
        }

        public bool IsFileReadOnly(FileSystemNode node)
        {
            FileSystemInfo info = GetPathInfo(node.Path);
            if (info == null || !info.Exists)
                return true;

            return info.Attributes.HasFlag(FileAttributes.ReadOnly);
        }
    }

    static class Extension
    {
        public static bool hasPermission(this FileRetrievalMode mode, FRP perm)
        {
            return (((int)mode & (int)perm) == (int)perm);
        }
    }
}
