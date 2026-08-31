using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class ModelStoreTests
{
    [Theory]
    [InlineData("https://huggingface.co/x/resolve/main/model.bin")]
    [InlineData("https://cdn-lfs-us-1.hf.co/repos/aa/model.bin")]
    [InlineData("https://storage.googleapis.com/bergamot/models/x.gz")]
    [InlineData("https://github.com/vladvysotsky/oops/releases/download/v2.0.0/m.bin")]
    public void TrustedHostsAreAccepted(string url) =>
        Assert.True(ModelStore.IsTrustedUrl(url));

    [Theory]
    [InlineData("http://huggingface.co/x/model.bin")]          // без TLS
    [InlineData("https://evil.com/model.bin")]
    [InlineData("https://huggingface.co.evil.com/model.bin")]  // не поддомен, а обманка
    [InlineData("https://notgithub.com/model.bin")]
    [InlineData("file:///C:/Windows/System32/model.bin")]
    [InlineData("не ссылка вовсе")]
    public void EverythingElseIsRejected(string url) =>
        Assert.False(ModelStore.IsTrustedUrl(url));

    [Fact]
    public void PackageKnowsHowMuchToDownload()
    {
        var package = new ModelPackage("test", new[]
        {
            new ModelFile("a.bin", "https://huggingface.co/a.bin", new string('a', 64), 100),
            new ModelFile("b.bin", "https://huggingface.co/b.bin", new string('b', 64), 250),
        });
        Assert.Equal(350, package.TotalBytes);
    }

    [Fact]
    public void FilesLandInsideTheModelsFolder()
    {
        var package = new ModelPackage("test", new[]
        {
            new ModelFile("m.bin", "https://huggingface.co/m.bin", new string('a', 64), 1),
        });
        var path = ModelStore.PathTo(package, package.Files[0]);
        Assert.StartsWith(ModelStore.DirectoryFor(package), path);
        Assert.EndsWith("m.bin", path);
    }

    [Fact]
    public void PathTraversalInAFileNameCannotEscapeTheFolder()
    {
        // Описание пакета — наши данные, но ошибиться в нём легко, а цена
        // ошибки — запись куда угодно под именем пользователя.
        var package = new ModelPackage("test", new[]
        {
            new ModelFile(@"..\..\evil.exe", "https://huggingface.co/m.bin", new string('a', 64), 1),
        });
        var path = ModelStore.PathTo(package, package.Files[0]);
        Assert.Equal(Path.Combine(ModelStore.DirectoryFor(package), "evil.exe"), path);
    }
}
