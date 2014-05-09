using System;
using System.IO;
using System.Collections.Generic;

namespace BasicFS
{

    public interface IFileSystem
    {
        FileSystemNode Root { get; }
        FileSystemNode GetNode(string path);

        FileSystemNode[] LoadChildren(FileSystemNode node);
        FileSystemNode[] LoadChildren(FileSystemNode node, int depth);

        Stream GetReadableStream(FileSystemNode node);

        ulong GetFileSize(FileSystemNode node);
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

        public Stream GetReadableStream(FileSystemNode node)
        {
            FileSystemInfo info = GetPathInfo(node.Path);
            if (info == null || !info.Exists)
                return null;

            return new FileStream(info.FullName, FileMode.Open, FileAccess.Read);
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
    }
}
