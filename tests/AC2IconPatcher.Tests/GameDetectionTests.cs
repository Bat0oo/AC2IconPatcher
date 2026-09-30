using System;
using System.IO;
using AC2IconPatcher;
using Xunit;

namespace AC2IconPatcher.Tests;

public class GameDetectionTests : IDisposable
{
    private readonly string _dir;

    public GameDetectionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ac2patcher-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Touch(string relativeName) => File.WriteAllBytes(Path.Combine(_dir, relativeName), Array.Empty<byte>());

    [Fact]
    public void NoDataPcForge_IsRejected()
    {
        Assert.False(GameDetection.IsAssassinsCreed2(_dir));
    }

    [Fact]
    public void DataPcForgeAlone_IsRejected()
    {
        // This is exactly the gap from issue #9: Black Flag (and other Anvil games)
        // ship a DataPC.forge too, so the name alone must not be enough.
        Touch("DataPC.forge");
        Assert.False(GameDetection.IsAssassinsCreed2(_dir));
    }

    [Fact]
    public void DataPcForgePlusAc2Exe_IsAccepted()
    {
        Touch("DataPC.forge");
        Touch("AssassinsCreedII.exe");
        Assert.True(GameDetection.IsAssassinsCreed2(_dir));
    }

    [Fact]
    public void DataPcForgePlusAlternateAc2Exe_IsAccepted()
    {
        Touch("DataPC.forge");
        Touch("AssassinsCreedIIGame.exe");
        Assert.True(GameDetection.IsAssassinsCreed2(_dir));
    }

    [Fact]
    public void DataPcForgePlusUnrelatedExe_IsRejected()
    {
        // This is the Black Flag case: it ships its own exe, not AC2's.
        Touch("DataPC.forge");
        Touch("AC4BFSP.exe");
        Assert.False(GameDetection.IsAssassinsCreed2(_dir));
    }

    [Fact]
    public void DataPcForgePlusBothRegionForges_IsAccepted()
    {
        // Fallback path for installs without a recognized exe name.
        Touch("DataPC.forge");
        Touch("DataPC_Firenze.forge");
        Touch("DataPC_Venezia.forge");
        Assert.True(GameDetection.IsAssassinsCreed2(_dir));
    }

    [Fact]
    public void DataPcForgePlusOnlyOneRegionForge_IsRejected()
    {
        Touch("DataPC.forge");
        Touch("DataPC_Firenze.forge");
        Assert.False(GameDetection.IsAssassinsCreed2(_dir));
    }

    [Theory]
    [InlineData(@"E:\SteamLibrary\steamapps\common\Assassin's Creed 2", @"E:\SteamLibrary\steamapps\common\Assassin's Creed 2")]
    [InlineData("\"E:\\SteamLibrary\\steamapps\\common\\Assassin's Creed 2\"", @"E:\SteamLibrary\steamapps\common\Assassin's Creed 2")]
    [InlineData("  E:\\Games\\AC2  ", @"E:\Games\AC2")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizePathInput_StripsQuotesAndWhitespace(string? raw, string expected)
    {
        Assert.Equal(expected, GameDetection.NormalizePathInput(raw));
    }
}
