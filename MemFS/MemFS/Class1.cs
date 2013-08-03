using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MemFS
{
    public sealed class FileSystemNode
    {
        public const string Separator = "/";

        public FileSystemNode(string name, FileSystemNode parent, FileSystemNode[] children)
        {
            _name = name;
            _parent = parent;
            _children = children.ToArray<FileSystemNode>();
        }

        private string _name;
        public string Name
        {
            get { return _name; }
        }

        private FileSystemNode _parent;
        public FileSystemNode Parent
        {
            get { return _parent; }
        }

        private FileSystemNode[] _children;
        public FileSystemNode this[int i]
        {
            get { return _children[i]; }
        }
        public IList<FileSystemNode> GetChildren()
        {
            return _children.ToArray<FileSystemNode>();
        }

        public string GetPath()
        {
            if (_parent == null)
                return _name;
            else
                return _parent.GetPath() + Separator + _name;
        }
    }

    public interface IFileSystem
    {
        FileSystemNode Root { get; }

        FileSystemNode Rename(FileSystemNode node);
        FileSystemNode GetNode(string path);
        int GetNodeSize(FileSystemNode node);
        byte[] GetNodeData(FileSystemNode node);
    }
}
