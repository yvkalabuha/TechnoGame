using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Singularity.Kernel;

namespace Singularity.Core.Data;

/// <summary>
/// SHA-256 over a canonical byte form of loaded data (L1 data identity, ADR-0002).
/// Layout, all little-endian: int32/int64 as is; bool as one byte; Fx6 as its raw int64;
/// string as int32 UTF-8 byte length + bytes. Hashes the loaded tables, not the YAML text,
/// so comments and formatting do not change it; references are written as IDs.
/// </summary>
internal sealed class CanonicalWriter : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void Int(int value)
    {
        Span<byte> b = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        _hash.AppendData(b);
    }

    public void Long(long value)
    {
        Span<byte> b = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(b, value);
        _hash.AppendData(b);
    }

    public void Bool(bool value) => _hash.AppendData([value ? (byte)1 : (byte)0]);

    public void Fixed(Fx6 value) => Long(value.Raw);

    public void Str(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Int(bytes.Length);
        _hash.AppendData(bytes);
    }

    public string FinishHex() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public void Dispose() => _hash.Dispose();
}
