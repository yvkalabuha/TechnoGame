using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Singularity.Lab;

/// <summary>Reproducibility fields of a run: code version and input data version.</summary>
public sealed record Provenance(string CoreCommit, bool? Dirty, string DataHash, IReadOnlyList<DataFileHash> DataFiles)
{
    public static Provenance Collect(Manifest manifest)
    {
        (string commit, bool? dirty) = Git(manifest.BaseDirectory);
        (string dataHash, var files) = HashData(manifest);
        return new Provenance(commit, dirty, dataHash, files);
    }

    /// <summary>
    /// core_commit = `git rev-parse HEAD`; dirty = tracked files differ from HEAD
    /// (untracked files are ignored: lab/results and scratch files would make every run dirty).
    /// Outside a git work tree: ("unknown", null).
    /// </summary>
    public static (string Commit, bool? Dirty) Git(string workDir)
    {
        string? head = RunGit(workDir, "rev-parse", "HEAD");
        if (head is null)
            return ("unknown", null);
        string? status = RunGit(workDir, "status", "--porcelain", "--untracked-files=no");
        return (head.Trim(), status is null ? null : status.Trim().Length > 0);
    }

    /// <summary>
    /// data_hash = SHA-256 over, for each file in ordinal path order: UTF-8 path, 0x00, SHA-256(content).
    /// No data files → SHA-256 of the empty input.
    /// </summary>
    public static (string Hash, List<DataFileHash> Files) HashData(Manifest manifest)
    {
        var files = new List<DataFileHash>();
        using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string rel in manifest.DataFiles)
        {
            string full = Path.Combine(manifest.BaseDirectory, rel);
            if (!File.Exists(full))
                throw new ManifestException($"Data file '{rel}' not found (relative to {manifest.BaseDirectory}).");
            byte[] content = SHA256.HashData(File.ReadAllBytes(full));
            all.AppendData(Encoding.UTF8.GetBytes(rel));
            all.AppendData(new byte[] { 0 });
            all.AppendData(content);
            files.Add(new DataFileHash(rel, Convert.ToHexStringLower(content)));
        }
        return (Convert.ToHexStringLower(all.GetHashAndReset()), files);
    }

    private static string? RunGit(string workDir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string a in args)
                psi.ArgumentList.Add(a);
            using Process p = Process.Start(psi)!;
            Task<string> stderr = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            _ = stderr.Result;
            return p.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // git not installed
        }
    }
}

public sealed record DataFileHash(string Path, string Sha256);
