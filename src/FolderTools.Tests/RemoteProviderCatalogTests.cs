using System.Linq;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class RemoteProviderCatalogTests
    {
        [Fact]
        public void Parse_ReadsActualRcloneRegistryArray()
        {
            const string json = "[{\"Name\":\"alias\",\"Description\":\"Alias for an existing remote\",\"Prefix\":\"alias\",\"Options\":[{\"Name\":\"remote\",\"FieldName\":\"\",\"Help\":\"Remote to alias\"}]},{\"Name\":\"drive\",\"Description\":\"Google Drive\",\"Prefix\":\"drive\",\"Options\":[]}]";

            var providers = RemoteProviderCatalog.Parse(json);

            Assert.Equal(2, providers.Count);
            Assert.Equal("alias", providers[0].Name);
            Assert.Equal("Alias for an existing remote", providers[0].Description);
            Assert.Equal("drive", providers[1].Name);
            Assert.Equal("Google Drive", providers[1].Description);
        }

        [Fact]
        public void Parse_ReadsRegistryKeyedByBackendName()
        {
            const string json = "{\"dropbox\":{\"Name\":\"dropbox\",\"Description\":\"Dropbox\"},\"drive\":{\"Name\":\"drive\",\"Description\":\"Google Drive\"}}";

            var providers = RemoteProviderCatalog.Parse(json).OrderBy(item => item.Name).ToArray();

            Assert.Equal(2, providers.Length);
            Assert.Equal("drive", providers[0].Name);
            Assert.Equal("Google Drive", providers[0].Description);
            Assert.Equal("dropbox", providers[1].Name);
            Assert.Equal("Dropbox", providers[1].Description);
        }

        [Fact]
        public void Parse_ContinuesToAcceptProvidersArrayWrapper()
        {
            const string json = "{\"Providers\":[{\"Name\":\"sftp\",\"Description\":\"SSH\"}]}";

            var providers = RemoteProviderCatalog.Parse(json);

            Assert.Single(providers);
            Assert.Equal("sftp", providers[0].Name);
            Assert.Equal("SSH", providers[0].Description);
        }

        [Fact]
        public void Parse_UsesRegistryKeyWhenProviderNameIsMissing()
        {
            const string json = "{\"terabox\":{\"Description\":\"bclone storage\"}}";

            var providers = RemoteProviderCatalog.Parse(json);

            Assert.Single(providers);
            Assert.Equal("terabox", providers[0].Name);
            Assert.Equal("bclone storage", providers[0].Description);
        }

        [Fact]
        public void Parse_RejectsUnrecognizedObject()
        {
            Assert.Throws<JsonException>(() => RemoteProviderCatalog.Parse("{\"unexpected\":true}"));
        }
    }
}
