using RustyPainter.Service;

namespace RustyPainter.Tests;

/// <summary>Settings that are kept in a file of their own, so a test can never change the settings of the real app.</summary>
internal static class TestSettings
{
    public static AppSettingsService Create()
        => new(Path.Combine(Path.GetTempPath(), $"rustypainter-test-settings-{Guid.NewGuid():N}", "settings.json"));
}

public class SettingsFileTests
{
    [Fact]
    public void Settings_are_saved_to_the_file_they_were_given_and_nowhere_else()
    {
        string real = AppSettingsService.DefaultPath;
        DateTime? before = File.Exists(real) ? File.GetLastWriteTimeUtc(real) : null;
        string own = Path.Combine(Path.GetTempPath(), $"rustypainter-test-settings-{Guid.NewGuid():N}", "settings.json");

        AppSettingsService settings = new(own);
        settings.Current.PaintSpeed = 12;
        settings.Save();

        Assert.True(File.Exists(own));
        Assert.Contains("\"PaintSpeed\": 12", File.ReadAllText(own));
        Assert.Equal(before, File.Exists(real) ? File.GetLastWriteTimeUtc(real) : null);

        AppSettingsService again = new(own);
        again.Initialize();
        Assert.Equal(12, again.Current.PaintSpeed);
    }

    [Fact]
    public void The_whole_test_run_leaves_the_real_settings_alone()
    {
        // Every test that saves settings has to use TestSettings.Create. This looks for one that forgot.
        string real = AppSettingsService.DefaultPath;
        string text = File.Exists(real) ? File.ReadAllText(real) : "";

        Assert.DoesNotContain("\"PaintSpeed\": 35", text);
        Assert.DoesNotContain("\"StartPauseHotkey\": \"P\"", text);
        Assert.DoesNotContain("\"AbortHotkey\": \"Ctrl+Shift+F8\"", text);
    }
}
