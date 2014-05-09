using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicFS;
using Cipher;
using DataFS;
using Dokan;
using System.IO;

namespace ConsoleApplication1
{
    class DokanFileSystemProxy : DokanOperations
    {
        public IFileSystem FileSystem
        {
            get;
            set;
        }

        private string GetPath(string filename)
        {
            string path = FileSystem.Root.Name + filename.Replace("\\", FileSystemNode.Separator);
            if (path.EndsWith("/"))
                path = path.Remove(path.Length - 1);
            return path;
        }

        public int Cleanup(string filename, DokanFileInfo info)
        {
            Console.WriteLine("Cleanup {0}", filename);
            return 0;
        }

        public int CloseFile(string filename, DokanFileInfo info)
        {
            Console.WriteLine("CloseFile {0}", filename);
            return 0;
        }

        public int CreateDirectory(string filename, DokanFileInfo info)
        {
            Console.WriteLine("CreateDir {0}", filename);
            return -1;
        }

        public int CreateFile(string filename, System.IO.FileAccess access, System.IO.FileShare share, System.IO.FileMode mode, System.IO.FileOptions options, DokanFileInfo info)
        {
            Console.WriteLine("CreateFile {0}", filename);
            string path = GetPath(filename);
            var node = FileSystem.GetNode(path);
            info.Context = _count++;
            if (node != null)
            {
                if (node.ChildCount > 0)
                    info.IsDirectory = true;
                return 0;
            }
            else
            {
                return -DokanNet.ERROR_FILE_NOT_FOUND;
            }
        }

        public int DeleteDirectory(string filename, DokanFileInfo info)
        {
            Console.WriteLine("DeleteDir {0}", filename);
            return -1;
        }

        public int DeleteFile(string filename, DokanFileInfo info)
        {
            Console.WriteLine("DeleteFile {0}", filename);
            return -1;
        }

        public int FindFiles(string filename, System.Collections.ArrayList files, DokanFileInfo info)
        {
            Console.WriteLine("FindFiles {0}", filename);
            string path = GetPath(filename);
            FileSystemNode node = FileSystem.GetNode(path);
            if (node != null)
            {
                var children = node.GetChildren();
                foreach (var f in children)
                {
                    FileInformation fi = new FileInformation();
                    fi.Attributes = System.IO.FileAttributes.ReadOnly;
                    if (f.ChildCount > 0)
                        fi.Attributes |= System.IO.FileAttributes.Directory;
                    fi.CreationTime = DateTime.Now;
                    fi.LastAccessTime = DateTime.Now;
                    fi.LastWriteTime = DateTime.Now;
                    if (f.ChildCount > 0)
                        fi.Length = 0;
                    else
                        fi.Length = (long)FileSystem.GetFileSize(f);
                    fi.FileName = f.Name;
                    files.Add(fi);
                }
                return 0;
            }
            else
            {
                return -1;
            }
        }

        public int FlushFileBuffers(string filename, DokanFileInfo info)
        {
            Console.WriteLine("FlushFileBuffers {0}", filename);
            return -1;
        }

        public int GetDiskFreeSpace(ref ulong freeBytesAvailable, ref ulong totalBytes, ref ulong totalFreeBytes, DokanFileInfo info)
        {
            Console.WriteLine("GetDiskFreeSpace");
            freeBytesAvailable = 0;
            totalBytes = FileSystem.GetFileSize(FileSystem.Root);
            totalFreeBytes = 0;
            return 0;
        }

        public int GetFileInformation(string filename, FileInformation fileinfo, DokanFileInfo info)
        {
            Console.WriteLine("GetFileInfo {0}", filename);
            string path = GetPath(filename);
            var node = FileSystem.GetNode(path);
            if (node == null)
            {
                return -1;
            }
            else if (node.ChildCount > 0)
            {
                fileinfo.Attributes = System.IO.FileAttributes.Directory | System.IO.FileAttributes.ReadOnly;
                fileinfo.CreationTime = DateTime.Now;
                fileinfo.LastAccessTime = DateTime.Now;
                fileinfo.LastWriteTime = DateTime.Now;
                fileinfo.Length = 0;
                return 0;
            }
            else
            {
                fileinfo.Attributes = System.IO.FileAttributes.ReadOnly;
                fileinfo.CreationTime = DateTime.Now;
                fileinfo.LastAccessTime = DateTime.Now;
                fileinfo.LastWriteTime = DateTime.Now;
                fileinfo.Length = (long)FileSystem.GetFileSize(node);
                return 0;
            }
        }

        public int LockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            Console.WriteLine("LockFile {0}", filename);
            return 0;
        }

        public int MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
        {
            Console.WriteLine("MoveFile {0}", filename);
            return -1;
        }

        private int _count = 1;
        public int OpenDirectory(string filename, DokanFileInfo info)
        {
            Console.WriteLine("OpenDir {0}", filename);
            info.Context = _count++;
            string path = GetPath(filename);
            var node = FileSystem.GetNode(path);
            if (node != null && node.ChildCount > 0)
                return 0;
            else
                return -DokanNet.ERROR_PATH_NOT_FOUND;
        }

        public int ReadFile(string filename, byte[] buffer, ref uint readBytes, long offset, DokanFileInfo info)
        {
            Console.WriteLine("ReadFile {0}", filename);
            try
            {
                string path = GetPath(filename);
                var node = FileSystem.GetNode(path);
                Stream stream = FileSystem.GetReadableStream(node);
                stream.Seek(offset, SeekOrigin.Begin);
                readBytes = (uint)stream.Read(buffer, 0, buffer.Length);
                return 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public int SetAllocationSize(string filename, long length, DokanFileInfo info)
        {
            Console.WriteLine("SetAllocSize {0}", filename);
            return -1;
        }

        public int SetEndOfFile(string filename, long length, DokanFileInfo info)
        {
            Console.WriteLine("SetEOF {0}", filename);
            return -1;
        }

        public int SetFileAttributes(string filename, System.IO.FileAttributes attr, DokanFileInfo info)
        {
            Console.WriteLine("SetFileAttr {0}", filename);
            return -1;
        }

        public int SetFileTime(string filename, DateTime ctime, DateTime atime, DateTime mtime, DokanFileInfo info)
        {
            Console.WriteLine("SetFileTime {0}", filename);
            return -1;
        }

        public int UnlockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            Console.WriteLine("UnlockFile {0}", filename);
            return 0;
        }

        public int Unmount(DokanFileInfo info)
        {
            Console.WriteLine("Unmount");
            return 0;
        }

        public int WriteFile(string filename, byte[] buffer, ref uint writtenBytes, long offset, DokanFileInfo info)
        {
            Console.WriteLine("WriteFile {0}", filename);
            return -1;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            DokanOptions opt = new DokanOptions();
            opt.DebugMode = true;
            opt.MountPoint = "n:\\";
            opt.ThreadCount = 1;

            {
                CipherStream stream1 = new CipherStream(
                    new FileStream(@"C:\Corey\TestOut1.dat", FileMode.Create, FileAccess.ReadWrite),
                    new FileStream(@"C:\Corey\TestOut2.dat", FileMode.Create, FileAccess.ReadWrite));
                DataFileSystem.CreateAndLoad(new DirectoryInfo(@"C:\Corey Derochie\fstest"), stream1);
                stream1.Close();
                stream1.Dispose();
            }
            CipherStream stream = new CipherStream(
                new FileStream(@"C:\Corey\TestOut1.dat", FileMode.Open, FileAccess.Read),
                new FileStream(@"C:\Corey\TestOut2.dat", FileMode.Open, FileAccess.Read));
            var fs = new DataFileSystem(stream);

            var proxy = new DokanFileSystemProxy();
            proxy.FileSystem = fs;

            int status = DokanNet.DokanMain(opt, proxy);
            switch (status)
            {
                case DokanNet.DOKAN_DRIVE_LETTER_ERROR:
                    Console.WriteLine("Drvie letter error");
                    break;
                case DokanNet.DOKAN_DRIVER_INSTALL_ERROR:
                    Console.WriteLine("Driver install error");
                    break;
                case DokanNet.DOKAN_MOUNT_ERROR:
                    Console.WriteLine("Mount error");
                    break;
                case DokanNet.DOKAN_START_ERROR:
                    Console.WriteLine("Start error");
                    break;
                case DokanNet.DOKAN_ERROR:
                    Console.WriteLine("Unknown error");
                    break;
                case DokanNet.DOKAN_SUCCESS:
                    Console.WriteLine("Success");
                    break;
                default:
                    Console.WriteLine("Unknown status: %d", status);
                    break;
            }
        }
    }
}
