using System.IO;

namespace AC2IconPatcher;

/// <summary>
/// Pure, filesystem-checking logic for picking out an AC2 install, kept separate
/// from Wizard's interactive prompts so it can be unit tested without a console.
/// </summary>
public static class GameDetection
{
    /// <summary>
    /// DataPC.forge alone is not enough - Black Flag and other Anvil games ship a
    /// file with the same name. Check for an AC2-specific executable as well.
    /// </summary>
    public static bool IsAssassinsCreed2(string dir)
    {
        if (!File.Exists(Path.Combine(dir, "DataPC.forge"))) return false;

        foreach (var exe in new[] { "AssassinsCreedII.exe", "AssassinsCreedIIGame.exe" })
            if (File.Exists(Path.Combine(dir, exe))) return true;

        // Some installs differ, so fall back to region archives AC2 always has.
        return File.Exists(Path.Combine(dir, "DataPC_Firenze.forge"))
            && File.Exists(Path.Combine(dir, "DataPC_Venezia.forge"));
    }

    /// <summary>
    /// Cleans up a pasted path: strips surrounding whitespace and the quotes
    /// Explorer's "Copy as path" wraps around it (Windows paths never legitimately
    /// start or end with a literal quote character, so this is always safe to strip).
    /// </summary>
    public static string NormalizePathInput(string? raw) => (raw ?? "").Trim().Trim('"');
}
