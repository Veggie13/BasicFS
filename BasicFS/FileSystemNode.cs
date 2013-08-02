using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace BasicFS
{
    public sealed class FileSystemNode
    {
        public const string Separator = "/";

        public FileSystemNode(string name, FileSystemNode parent, FileSystemNode[] children)
        {
            _name = name;
            _parent = parent;
            _children = new List<FileSystemNode>(children).ToArray();
        }

        public FileSystemNode(string path, IFileSystem srcFS)
        {
            _path = path;
            _src = srcFS;
        }

        private void Copy(FileSystemNode other)
        {
            _src = other._src;
            _path = other._path;
            _name = other._name;
            _parent = other._parent;
            _children = new List<FileSystemNode>(other._children).ToArray();
        }

        private IFileSystem _src;

        private string _path;
        public string Path
        {
            get
            {
                if (_parent != null)
                    return _parent.Path + Separator + Name;
                else
                    return _path;
            }
        }

        private string _name;
        public string Name
        {
            get
            {
                if (_name != null)
                    return _name;
                else
                    return _path.Substring(_path.LastIndexOf(Separator) + 1);
            }
        }

        private FileSystemNode _parent;
        public FileSystemNode Parent
        {
            get
            {
                if (_parent != null)
                    return _parent;
                else if (!_path.Contains(Separator))
                    return null;
                else
                    return (_parent = _src.GetNode(_path.Substring(0, _path.LastIndexOf(Separator))));
            }
        }
        
        private FileSystemNode[] _children;
        private void LoadChildren()
        {
            _children = _src.LoadChildren(this);
        }

        public IList<FileSystemNode> GetChildren()
        {
            if (_children == null)
                LoadChildren();

            return new List<FileSystemNode>(_children);
        }

        public FileSystemNode this[int i]
        {
            get
            {
                if (_children == null)
                    LoadChildren();

                return _children[i];
            }
        }

        public int ChildCount
        {
            get
            {
                if (_children == null)
                    LoadChildren();

                return _children.Length;
            }
        }
    }
}
