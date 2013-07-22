namespace N2.Edit.FileSystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Web;
    using System.Web.Caching;

    using Microsoft.WindowsAzure.Storage.Auth;
    using Microsoft.WindowsAzure.Storage.Blob;

    using N2.Edit.FileSystem;
    
    public class AzureFileSystemProvider
    {
        internal const string _DIRECTORY_PLACE_HOLDER = "dir";
        internal const string _LIST_BLOBS_CACHE_KEY = "_ListBlobs";

        private static Lazy<AzureFileSystemProvider> _instance = new Lazy<AzureFileSystemProvider>();

        public static AzureFileSystemProvider Instance
        {
            get
            {
                return _instance.Value;
            }
        }

        private Cache cache;
        private CloudBlobContainer container;

        public AzureFileSystemProvider()
            : this(HttpContext.Current.Cache) { }

        public AzureFileSystemProvider(Cache cache)
        {
            this.cache = cache;
        }

        public CloudBlobContainer Container
        {
            get
            {
                if (container == null)
                {
                    var credentials = new StorageCredentials("[ACCOUNT NAME]", "[PRIMARY KEY]");
                    var client = new CloudBlobClient(new Uri("[YOUR URI]"), credentials);

                    container = client.GetContainerReference("[CONTAINER NAME]".Trim().ToLower());
                    container.CreateIfNotExists();
                }

                return container;
            }
        }

        public bool DirectoryExists(string path)
        {
            return this.ListItems(path).Any();
        }

        public bool FileExists(string path)
        {
            var blob = this.GetFile(path);
            return blob.Exists();
        }

        public DirectoryData GetDirectoryData(string path)
        {
            var directory = this.GetDirectory(path);
            return GetDirectoryData(directory);
        }

        public FileData GetFileData(string path)
        {
            var blob = this.GetFile(path);
            FileData fileData = null;

            if (blob.Exists())
            {
                blob.FetchAttributes();
                fileData = GetFileData(blob);
            }

            return fileData;
        }

        public IEnumerable<FileData> GetFiles(string path)
        {
            return this.ListItems(path)
                .Where(blob => blob is CloudBlockBlob)
                .Select(blob => GetFileData((CloudBlockBlob)blob))
                .Where(fileData => fileData.Name != AzureFileSystemProvider._DIRECTORY_PLACE_HOLDER);
        }

        public IEnumerable<DirectoryData> GetDirectories(string path)
        {
            return this.ListItems(path)
                .Where(blob => blob is CloudBlobDirectory)
                .Select(blob => GetDirectoryData((CloudBlobDirectory)blob));
        }

        public FileData GetFileData(ICloudBlob blob)
        {
            if (!blob.Exists())
            {
                return new FileData();
            }

            var lastModifiedDate = blob.Properties.LastModified;
            var lastModified = lastModifiedDate.HasValue ? lastModifiedDate.Value.LocalDateTime : DateTime.MinValue;

            return new FileData()
            {
                Name = Path.GetFileName(blob.Name),
                Created = lastModified,
                Length = blob.Properties.Length,
                Updated = lastModified,
                VirtualPath = UnEscapedPath(blob.Name)
            };
        }

        public DirectoryData GetDirectoryData(CloudBlobDirectory directory)
        {
            var path = directory.Prefix;

            return new DirectoryData()
            {
                Name = new DirectoryInfo(path).Name,
                Created = DateTime.Now,
                Updated = DateTime.Now,
                VirtualPath = UnEscapedPath(path)
            };
        }

        public CloudBlockBlob GetFile(string path)
        {
            if (cache[path] == null)
            {
                var escapedPath = this.EscapedPath(path);
                var blob = this.Container.GetBlockBlobReference(escapedPath);

                this.AddToCache(path, blob);
            }

            return (CloudBlockBlob)cache[path];
        }

        public CloudBlobDirectory GetDirectory(string path)
        {
            if (cache[path] == null)
            {
                var escapedPath = this.EscapedPath(path);
                var blob = this.Container.GetDirectoryReference(escapedPath);

                this.AddToCache(path, blob);
            }

            return (CloudBlobDirectory)cache[path];
        }

        public IEnumerable<IListBlobItem> ListItems(string path)
        {
            string cacheKey = path + _LIST_BLOBS_CACHE_KEY;

            if (cache[cacheKey] == null)
            {
                var items = this.GetDirectory(path).ListBlobs(false);
                this.AddToCache(cacheKey, items);
            }

            return (IEnumerable<IListBlobItem>)cache[cacheKey];
        }

        private void AddToCache(string path, object objectToCache)
        {
            this.cache.Insert(path, objectToCache, null, Cache.NoAbsoluteExpiration, new TimeSpan(0, 10, 0));
        }

        public void RemoveFromCache(string path)
        {
            this.cache.Remove(path);
            this.cache.Remove(path + _LIST_BLOBS_CACHE_KEY);
        }

        public string EscapedPath(string virtualPath)
        {
            if (virtualPath.StartsWith("~"))
            {
                virtualPath = virtualPath.Substring(1);
            }

            if (virtualPath.StartsWith("/"))
            {
                virtualPath = virtualPath.Substring(1);
            }

            return virtualPath.Replace("//", "/").ToLower().Trim();
        }

        public string UnEscapedPath(string path)
        {
            return string.Format("/{0}", path);
        }
    }
}