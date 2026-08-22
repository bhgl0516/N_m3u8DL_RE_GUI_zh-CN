#nullable enable
using System;
using System.IO;
using System.Linq;
using N_m3u8DL_RE_GUI.Core.Resume;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Resume;

public class ResumePathsTests
{
    [Fact]
    public void DeriveTmpDir_IsStableAcrossCalls()
    {
        // Resume depends entirely on this being reproducible after a restart.
        var first = ResumePaths.DeriveTmpDir(@"D:\Videos", "Episode 4");
        var second = ResumePaths.DeriveTmpDir(@"D:\Videos", "Episode 4");

        Assert.Equal(first, second);
    }

    [Fact]
    public void DeriveTmpDir_NestsUnderTheSaveFolder()
    {
        var path = ResumePaths.DeriveTmpDir(@"D:\Videos", "Episode 4");

        Assert.StartsWith(@"D:\Videos", path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".nre-tmp", path);
    }

    [Fact]
    public void DeriveTmpDir_SeparatesDifferentNames()
    {
        Assert.NotEqual(
            ResumePaths.DeriveTmpDir(@"D:\Videos", "Episode 4"),
            ResumePaths.DeriveTmpDir(@"D:\Videos", "Episode 5"));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b?c\"d<e>f|g")]
    public void DeriveTmpDir_StripsCharactersIllegalInAPathSegment(string saveName)
    {
        var leaf = Path.GetFileName(ResumePaths.DeriveTmpDir(@"D:\Videos", saveName));

        Assert.DoesNotContain(leaf, c => Path.GetInvalidFileNameChars().Contains(c));
        Assert.NotEqual(string.Empty, leaf);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("PRN")]
    [InlineData("LPT1")]
    public void DeriveTmpDir_AvoidsReservedDeviceNames(string saveName)
    {
        // A directory named CON cannot be created on Windows. The existing
        // UtilityService sanitiser hit this same wall.
        var leaf = Path.GetFileName(ResumePaths.DeriveTmpDir(@"D:\Videos", saveName));

        Assert.NotEqual("CON", leaf, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("PRN", leaf, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("LPT1", leaf, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeriveTmpDir_BoundsAVeryLongName()
    {
        var path = ResumePaths.DeriveTmpDir(@"D:\Videos", new string('x', 400));

        Assert.True(Path.GetFileName(path).Length <= 80);
    }

    [Fact]
    public void DeriveTmpDir_TwoLongNamesSharingAPrefixDoNotCollide()
    {
        // Truncation alone would map both onto the same folder and cross-
        // contaminate two different downloads' segments.
        var a = ResumePaths.DeriveTmpDir(@"D:\Videos", new string('x', 300) + "-A");
        var b = ResumePaths.DeriveTmpDir(@"D:\Videos", new string('x', 300) + "-B");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DeriveTmpDir_WithNoNameStillProducesAUsableLeaf(string? saveName)
    {
        var path = ResumePaths.DeriveTmpDir(@"D:\Videos", saveName!);

        Assert.False(string.IsNullOrWhiteSpace(Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeriveTmpDir_WithNoSaveDirReturnsEmptySoTheCallerCanSkipTheFlag(string? saveDir)
    {
        // No save folder means no stable anchor; fall back to RE's own choice
        // rather than inventing a location the user did not ask for.
        Assert.Equal(string.Empty, ResumePaths.DeriveTmpDir(saveDir!, "Episode 4"));
    }
}
