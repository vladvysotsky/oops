using Oops.Settings;
using Xunit;

namespace Oops.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oops-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public SettingsFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void FieldsFromANewerVersionSurviveARoundTrip()
    {
        // Старая версия, запущенная поверх файла новой, не должна стирать то,
        // чего не знает: так пропали сниппеты 2.4.0.
        File.WriteAllText(FilePath, "{\"FromTheFuture\":{\"Items\":[1,2,3]},\"Enabled\":true}");

        var settings = AppSettings.Load(FilePath);
        Assert.Null(settings.Save(FilePath));

        Assert.Contains("FromTheFuture", File.ReadAllText(FilePath));
    }

    [Fact]
    public void SavingKeepsThePreviousVersionAsBackup()
    {
        var settings = new AppSettings { BufferIdleTimeoutSeconds = 41 };
        Assert.Null(settings.Save(FilePath));
        settings.BufferIdleTimeoutSeconds = 42;
        Assert.Null(settings.Save(FilePath));

        Assert.Equal(41, AppSettings.Load(AppSettings.BackupPath(FilePath)).BufferIdleTimeoutSeconds);
        Assert.Equal(42, AppSettings.Load(FilePath).BufferIdleTimeoutSeconds);
    }

    [Fact]
    public void AnUnreadableFileIsSetAsideAndTheBackupIsUsed()
    {
        var good = new AppSettings { BufferIdleTimeoutSeconds = 41 };
        Assert.Null(good.Save(FilePath));
        Assert.Null(good.Save(FilePath));               // теперь есть .bak
        File.WriteAllText(FilePath, "{ \"Enabled\": tru");   // оборванная запись

        var loaded = AppSettings.Load(FilePath);

        Assert.NotNull(loaded.LoadError);
        Assert.Equal(41, loaded.BufferIdleTimeoutSeconds);
        Assert.Single(Directory.GetFiles(_dir, "settings.unreadable-*.json"));
    }
}
