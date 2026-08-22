#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core.Resume;

public static class ResumePaths
{
    private const string TmpSubfolder = ".nre-tmp";
    private const int MaxCleanLength = 50;

    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM0",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT0"
    };

    /// <summary>
    /// Derives a deterministic, predictable temporary directory under the save directory
    /// based on the save name. Returns empty string if saveDir is not specified.
    /// </summary>
    public static string DeriveTmpDir(string? saveDir, string? saveName)
    {
        if (string.IsNullOrWhiteSpace(saveDir))
        {
            return string.Empty;
        }

        var leaf = SanitizeSaveName(saveName);
        return Path.Combine(saveDir.Trim(), TmpSubfolder, leaf);
    }

    private static string SanitizeSaveName(string? rawName)
    {
        var input = rawName?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return "download";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);

        foreach (var ch in input)
        {
            if (ch < 32 || Array.IndexOf(invalidChars, ch) >= 0)
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(ch);
            }
        }

        var clean = sb.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "download";
        }

        // Check for reserved Windows device names (e.g. CON, PRN, AUX, NUL, COM1, etc.)
        var stem = Path.GetFileNameWithoutExtension(clean);
        foreach (var reserved in ReservedDeviceNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
            {
                clean = "_" + clean;
                break;
            }
        }

        // If the name is long, bound it and append a short hash of the full original name
        // to avoid prefix collisions.
        if (clean.Length > MaxCleanLength || input.Length > MaxCleanLength)
        {
            var hash = ComputeShortHash(input);
            var prefix = clean.Length > MaxCleanLength ? clean.Substring(0, MaxCleanLength).TrimEnd('.', ' ', '_') : clean;
            clean = $"{prefix}_{hash}";
        }

        return clean;
    }

    private static string ComputeShortHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        // Take first 6 bytes (12 hex chars) for a compact unique signature
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
