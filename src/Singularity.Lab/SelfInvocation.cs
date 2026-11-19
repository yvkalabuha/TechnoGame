using System.Diagnostics;

namespace Singularity.Lab;

/// <summary>
/// How the Lab starts a child copy of itself (matrix runs, bench series children). The own process is the Lab apphost only if its
/// file name equals the Lab assembly name; anything else (the <c>dotnet</c> muxer, <c>testhost</c>/<c>testhost.exe</c> under
/// <c>dotnet test</c>, another host) starts <c>&lt;dotnet&gt; &lt;Singularity.Lab.dll&gt;</c>. The dotnet host is the own process
/// when that is <c>dotnet</c>, else <c>DOTNET_HOST_PATH</c>, else "dotnet" from PATH (TASK-0116: on Windows the testhost is
/// <c>testhost.exe</c>, not <c>dotnet</c>, and was started as if it were the Lab).
/// </summary>
public static class SelfInvocation
{
    /// <summary>The executable and the leading arguments of a self-invocation. Pure: no I/O, no environment reads.</summary>
    public static (string Exe, string[] Prefix) Decide(string? processPath, string assemblyLocation, string? dotnetHostPath)
    {
        string asm = StripExtension(FileName(assemblyLocation), ".dll");
        if (!string.IsNullOrEmpty(processPath))
        {
            string self = StripExtension(FileName(processPath), ".exe");
            if (asm.Length > 0 && string.Equals(self, asm, StringComparison.OrdinalIgnoreCase))
                return (processPath, []);
            if (string.Equals(self, "dotnet", StringComparison.OrdinalIgnoreCase))
                return (processPath, [assemblyLocation]);
        }
        string host = string.IsNullOrEmpty(dotnetHostPath) ? "dotnet" : dotnetHostPath;
        return (host, [assemblyLocation]);
    }

    /// <summary><see cref="Decide"/> for the running process and the Lab assembly.</summary>
    public static (string Exe, string[] Prefix) Current() =>
        Decide(Environment.ProcessPath, typeof(SelfInvocation).Assembly.Location, Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"));

    /// <summary>A <see cref="ProcessStartInfo"/> of <see cref="Current"/> with the prefix already in the argument list.</summary>
    public static ProcessStartInfo StartInfo()
    {
        (string exe, string[] prefix) = Current();
        var psi = new ProcessStartInfo(exe);
        foreach (string a in prefix)
            psi.ArgumentList.Add(a);
        return psi;
    }

    // Both separators on every OS: the decision is tested with Windows paths on Linux and the reverse.
    private static string FileName(string path)
    {
        int i = path.LastIndexOfAny(['/', '\\']);
        return i < 0 ? path : path[(i + 1)..];
    }

    // Only the known extension is stripped: the Linux apphost "Singularity.Lab" has none, and GetFileNameWithoutExtension would cut ".Lab".
    private static string StripExtension(string name, string ext) =>
        name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name[..^ext.Length] : name;

    /// <summary>Selftest: the decision for the dotnet host, the apphost (Linux and Windows), testhost.exe on Windows, testhost on Linux.</summary>
    public static string? Check()
    {
        const string linDll = "/repo/src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab.dll";
        const string winDll = @"D:\a\repo\src\Singularity.Lab\bin\Release\net10.0\Singularity.Lab.dll";
        (string? Proc, string Asm, string? Host, string Exe, string[] Prefix)[] cases =
        [
            ("/usr/share/dotnet/dotnet", linDll, null, "/usr/share/dotnet/dotnet", [linDll]),
            (@"C:\Program Files\dotnet\dotnet.exe", winDll, @"C:\other\dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe", [winDll]),
            ("/repo/src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab", linDll, "/usr/bin/dotnet", "/repo/src/Singularity.Lab/bin/Release/net10.0/Singularity.Lab", []),
            (@"D:\a\repo\src\Singularity.Lab\bin\Release\net10.0\Singularity.Lab.exe", winDll, null, @"D:\a\repo\src\Singularity.Lab\bin\Release\net10.0\Singularity.Lab.exe", []),
            (@"D:\a\repo\tests\bin\Release\net10.0\testhost.exe", winDll, @"C:\Program Files\dotnet\dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe", [winDll]),
            (@"D:\a\repo\tests\bin\Release\net10.0\testhost.exe", winDll, null, "dotnet", [winDll]),
            ("/repo/tests/bin/Release/net10.0/testhost", linDll, "/usr/share/dotnet/dotnet", "/usr/share/dotnet/dotnet", [linDll]),
            ("/repo/tests/bin/Release/net10.0/testhost", linDll, "", "dotnet", [linDll]),
            (null, linDll, null, "dotnet", [linDll]),
        ];
        foreach (var c in cases)
        {
            (string exe, string[] prefix) = Decide(c.Proc, c.Asm, c.Host);
            if (exe != c.Exe || !prefix.SequenceEqual(c.Prefix))
                return $"process '{c.Proc}', host '{c.Host}': got '{exe}' [{string.Join(' ', prefix)}], expected '{c.Exe}' [{string.Join(' ', c.Prefix)}]";
        }
        return null;
    }
}
