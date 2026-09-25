using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AC2IconPatcher;

/// <summary>IProgress&lt;T&gt; reports synchronously instead of via the thread pool, so console output stays ordered.</summary>
public sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public SyncProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}

public enum PatchOutcome { Patched, NothingToPatch, Skipped }

public sealed record PatchForgeResult(
    string ForgeName,
    PatchOutcome Outcome,
    int IconsPatched,
    int AtlasesPatched,
    string? OutputPath,
    string? Error);

public sealed record PatchProgress(int Index, int Total, string ForgeName);

public sealed class PatchAllResult
{
    public List<PatchForgeResult> Forges { get; } = new();
    public int TotalIconsPatched => Forges.Sum(f => f.IconsPatched + f.AtlasesPatched);
    public int TouchedForges => Forges.Count(f => f.Outcome == PatchOutcome.Patched);
    public int FailedForges => Forges.Count(f => f.Outcome == PatchOutcome.Skipped);
}

/// <summary>
/// Single implementation of the patch pipeline, shared by the CLI and the wizard.
/// </summary>
public sealed class IconPatcher
{
    private readonly IProgress<PatchProgress>? _progress;
    private readonly Dictionary<string, byte[]> _renderedIcons;

    public IconPatcher(IProgress<PatchProgress>? progress = null)
    {
        _progress = progress;
        _renderedIcons = new Dictionary<string, byte[]>();
        foreach (var kv in IconRenderer.IconKeys)
            _renderedIcons[kv.Key] = IconRenderer.Render(32, 32, kv.Value);
    }

    public PatchForgeResult PatchForge(string forgePath, bool apply)
    {
        string forgeName = Path.GetFileName(forgePath);

        ForgeArchive archive;
        try { archive = ForgeArchive.Read(forgePath); }
        catch (Exception ex) { return new PatchForgeResult(forgeName, PatchOutcome.Skipped, 0, 0, null, ex.Message); }

        var replacements = new Dictionary<int, byte[]>();
        int icons = 0, atlases = 0;

        foreach (var e in archive.Entries)
        {
            DataFile df;
            byte[] content;
            try
            {
                df = DataFile.Read(archive.Raw, (int)e.DataOffset, (int)e.DataOffset + e.Size);
                content = df.GetContent();
            }
            catch { continue; }

            bool changed = false;
            foreach (var hit in DataFile.FindEntries(content, DataEntry.TypeTextureMap))
            {
                var tm = new TextureMap(content, hit.Offset);
                if (!tm.LooksValid || tm.Format != TextureMap.FormatRgba) continue;

                if (_renderedIcons.TryGetValue(hit.Name, out var rgba) && tm.Width == 32 && tm.Height == 32)
                {
                    df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(rgba));
                    changed = true;
                    icons++;
                    continue;
                }

                if (hit.Name == "HUD_Controls_0_Map")
                {
                    var current = tm.DecodeTopMipRgba();
                    var patchedAtlas = IconRenderer.PatchAtlas(current, tm.Width, tm.Height);
                    df.PatchBytes(tm.PixelStart, tm.EncodeRgbaWithMips(patchedAtlas));
                    changed = true;
                    atlases++;
                }
            }

            if (changed) replacements[e.Index] = df.Serialize();
        }

        if (replacements.Count == 0)
            return new PatchForgeResult(forgeName, PatchOutcome.NothingToPatch, 0, 0, null, null);

        string outputPath;
        if (apply)
        {
            string backup = forgePath + ".bak";
            if (!File.Exists(backup)) File.Copy(forgePath, backup);

            string tmp = forgePath + ".tmp";
            archive.Write(tmp, replacements);
            long written = new FileInfo(tmp).Length;
            File.Move(tmp, forgePath, overwrite: true);

            // A silent failure here would otherwise be reported as a successful install.
            if (new FileInfo(forgePath).Length != written)
                throw new IOException($"{forgeName}: the write did not take effect - is the game running?");

            outputPath = forgePath;
        }
        else
        {
            outputPath = forgePath + ".patched";
            archive.Write(outputPath, replacements);
        }

        return new PatchForgeResult(forgeName, PatchOutcome.Patched, icons, atlases, outputPath, null);
    }

    public PatchAllResult PatchAll(string gameDir, bool apply, Action<PatchForgeResult>? onForgeDone = null)
        => PatchAll(Directory.GetFiles(gameDir, "*.forge"), apply, onForgeDone);

    public PatchAllResult PatchAll(IReadOnlyList<string> forgePaths, bool apply, Action<PatchForgeResult>? onForgeDone = null)
    {
        var result = new PatchAllResult();

        for (int i = 0; i < forgePaths.Count; i++)
        {
            _progress?.Report(new PatchProgress(i + 1, forgePaths.Count, Path.GetFileName(forgePaths[i])));
            var forgeResult = PatchForge(forgePaths[i], apply);
            result.Forges.Add(forgeResult);
            onForgeDone?.Invoke(forgeResult);
        }

        return result;
    }
}
