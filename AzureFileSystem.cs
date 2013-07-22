namespace N2.Edit.FileSystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using N2.Engine;

	[Service(typeof(IFileSystem))]
    public class AzureFileSystem : IFileSystem
    {
        public event EventHandler<FileEventArgs> DirectoryCreated;

        public event EventHandler<FileEventArgs> DirectoryDeleted;

        public event EventHandler<FileEventArgs> DirectoryMoved;

        public event EventHandler<FileEventArgs> FileCopied;

        public event EventHandler<FileEventArgs> FileDeleted;

        public event EventHandler<FileEventArgs> FileMoved;

        public event EventHandler<FileEventArgs> FileWritten;

        public IEnumerable<FileData> GetFiles(string parentVirtualPath)
        {
            return AzureFileSystemProvider.Instance.GetFiles(parentVirtualPath);
        }

        public FileData GetFile(string virtualPath)
        {
            return AzureFileSystemProvider.Instance.GetFileData(virtualPath);
        }

        public IEnumerable<DirectoryData> GetDirectories(string parentVirtualPath)
        {
            return AzureFileSystemProvider.Instance.GetDirectories(parentVirtualPath);
        }

        public DirectoryData GetDirectory(string virtualPath)
        {
            return AzureFileSystemProvider.Instance.GetDirectoryData(virtualPath);
        }

        public bool FileExists(string virtualPath)
        {
            return AzureFileSystemProvider.Instance.FileExists(virtualPath);
        }

        public void MoveFile(string fromPath, string destinationPath)
        {
            this.CopyFile(fromPath, destinationPath);
            this.DeleteFile(fromPath);
        }

        public void DeleteFile(string path)
        {
            var blob = AzureFileSystemProvider.Instance.GetFile(path);

            blob.DeleteIfExists();

            if (FileDeleted != null)
            {
                FileDeleted.Invoke(this, new FileEventArgs(path, null));
            }

            AzureFileSystemProvider.Instance.RemoveFromCache(path);
        }

        public void CopyFile(string fromPath, string destinationPath)
        {
            var source = AzureFileSystemProvider.Instance.GetFile(fromPath);
            var destination = AzureFileSystemProvider.Instance.GetFile(destinationPath);

            destination.StartCopyFromBlob(source.Uri);

            if (this.FileCopied != null)
            {
                this.FileCopied.Invoke(this, new FileEventArgs(destinationPath, null));
            }

            AzureFileSystemProvider.Instance.RemoveFromCache(destinationPath);
        }

        public Stream OpenFile(string virtualPath)
        {
            var stream = new MemoryStream();
            var blob = AzureFileSystemProvider.Instance.GetFile(virtualPath);

            blob.DownloadToStream(stream);

            return stream;
        }

        public ServedFile ServeFile(string virtualPath)
        {
            var blob = AzureFileSystemProvider.Instance.GetFile(virtualPath);

            if (!blob.Exists())
            {
                return null;
            }

            blob.FetchAttributes();

            return new ServedFile 
            {
                ContentType = blob.Properties.ContentType,
                Stream = () =>
                {
                    var output = new MemoryStream();

                    blob.DownloadToStream(output);
                    output.Position = 0;

                    return output;
                }
            };
        }

        public void WriteFile(string virtualPath, Stream inputStream)
        {
            string fileName = virtualPath.Substring(virtualPath.LastIndexOf("/") + 1);

            virtualPath = virtualPath.Remove(virtualPath.LastIndexOf("/") + 1);
            virtualPath += fileName;

            var blob = AzureFileSystemProvider.Instance.GetFile(virtualPath);

            blob.UploadFromStream(inputStream);
        }

        public void ReadFileContents(string virtualPath, Stream outputStream)
        {
            var blob = AzureFileSystemProvider.Instance.GetFile(virtualPath);

            blob.DownloadToStream(outputStream);
        }

        public bool DirectoryExists(string virtualPath)
        {
            return AzureFileSystemProvider.Instance.DirectoryExists(virtualPath);
        }

        public void MoveDirectory(string fromVirtualPath, string destinationVirtualPath)
        {
            throw new System.NotImplementedException();
        }

        public void DeleteDirectory(string path)
        {
            foreach (var directoryData in AzureFileSystemProvider.Instance.GetDirectories(path))
            {
                this.DeleteDirectory(directoryData.VirtualPath);
            }

            foreach (var fileData in AzureFileSystemProvider.Instance.GetFiles(path))
            {
                this.DeleteFile(fileData.VirtualPath);
            }

            var filename = path + AzureFileSystemProvider._DIRECTORY_PLACE_HOLDER;
            var blob = AzureFileSystemProvider.Instance.GetFile(filename);

            blob.DeleteIfExists();

            if (this.DirectoryDeleted != null)
            {
                this.DirectoryDeleted.Invoke(this, new FileEventArgs(path, null));
            }

            AzureFileSystemProvider.Instance.RemoveFromCache(path);
        }

        public void CreateDirectory(string virtualPath)
        {
            //create a text file to use as directory placeholder (no way to create an empty directory in blob storage)
            var escapedPath = AzureFileSystemProvider.Instance.EscapedPath(virtualPath);
            var formattedPath = !escapedPath.EndsWith("/") ? escapedPath += "/" : escapedPath;
            var filename = formattedPath + AzureFileSystemProvider._DIRECTORY_PLACE_HOLDER;
            var content = AzureFileSystemProvider._DIRECTORY_PLACE_HOLDER
                .ToCharArray()
                .Select(b => (byte)b)
                .ToArray();
            var blob = AzureFileSystemProvider.Instance.Container.GetBlockBlobReference(filename);

            blob.UploadFromStream(new MemoryStream(content));
        }
    }

    public class ServedFile
    {
        public Func<Stream> Stream { get; set; }
        public string ContentType { get; set; }
    }
}