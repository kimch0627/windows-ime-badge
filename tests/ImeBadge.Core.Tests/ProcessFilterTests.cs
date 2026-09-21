using System.Collections.Generic;
using Xunit;

namespace ImeBadge.Tests;

public sealed class ProcessFilterTests
{
    [Theory]
    [InlineData("mstsc", "mstsc", true)]
    [InlineData("MSTSC.EXE", "mstsc", true)]
    [InlineData("mstsc", @"C:\Windows\System32\mstsc.exe", true)]
    [InlineData("Unreal*", "UnrealEditor", true)]
    [InlineData("unreal*", "UnrealEditor.exe", true)]
    [InlineData("Unreal*", "notunreal", false)]
    [InlineData("mstsc", "mstsc2", false)]
    [InlineData("", "mstsc", false)]
    public void IsExcluded(string pattern, string process, bool expected) =>
        Assert.Equal(expected, ProcessFilter.IsExcluded(new List<string> { pattern }, process));

    [Fact]
    public void NullOrEmptyInputs_AreNotExcluded()
    {
        Assert.False(ProcessFilter.IsExcluded(null, "mstsc"));
        Assert.False(ProcessFilter.IsExcluded(new List<string> { "mstsc" }, null));
        Assert.False(ProcessFilter.IsExcluded(new List<string> { "mstsc" }, ""));
    }

    [Theory]
    [InlineData(@"C:\x\Foo.EXE", "foo")]
    [InlineData("Foo.exe", "foo")]
    [InlineData("foo", "foo")]
    [InlineData("  foo  ", "foo")]
    [InlineData("/usr/bin/foo", "foo")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, ProcessFilter.Normalize(input));
}

public sealed class LogTests
{
    [Fact]
    public void Rotate_MovesOversizedFileAside()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imebadge-log-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            string path = System.IO.Path.Combine(dir, "errors.log");
            System.IO.File.WriteAllBytes(path, new byte[Log.MaxBytes + 1]);
            Log.Rotate(path);
            Assert.False(System.IO.File.Exists(path));
            Assert.True(System.IO.File.Exists(path + ".1"));

            System.IO.File.WriteAllText(path, "small");
            Log.Rotate(path);
            Assert.True(System.IO.File.Exists(path));   // 작은 파일은 그대로
        }
        finally { System.IO.Directory.Delete(dir, recursive: true); }
    }
}
