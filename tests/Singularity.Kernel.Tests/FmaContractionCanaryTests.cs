using System.Runtime.CompilerServices;
using Singularity.Kernel;

namespace Singularity.Kernel.Tests;

// Canary for ADR-0003: the JIT must never contract a*b+c into a fused multiply-add.
// Each case is chosen so that the fused and unfused results differ bit-for-bit.
// Expected bits were computed with exact rational arithmetic (Python fractions):
// unfused = round(round(a*b) + c), fused = round(a*b + c).
public class FmaContractionCanaryTests
{
    // a, b, c, expected unfused bits, expected fused bits.
    private static readonly ulong[][] Table =
    [
        [0x3FF0000000400000UL, 0x3FF0000000400000UL, 0xBFF0000000800000UL, 0x0000000000000000UL, 0x3C30000000000000UL], // (1+2^-30)^2 - (1+2^-29)
        [0x3FF199999999999AUL, 0x3FF199999999999AUL, 0xBFF35C28F5C28F5DUL, 0x0000000000000000UL, 0x3C647AE147AE1480UL], // 1.1 * 1.1 - 1.2100000000000002
        [0x3FB999999999999AUL, 0x4008000000000000UL, 0xBFD3333333333334UL, 0x0000000000000000UL, 0xBC80000000000000UL], // 0.1 * 3.0 - 0.30000000000000004
        [0x3FF000001AD7F29BUL, 0x3FEFFFFFCA501ACBUL, 0xBFEFFFFFFFFFFFA7UL, 0x0000000000000000UL, 0xBC626E1A9AEE1170UL], // 1.0000001 * 0.9999999 - 0.9999999999999901
        [0x40FE240C9FBE76C9UL, 0x3F50624DD2F1A9FCUL, 0xC05EDD3C07EE0B0BUL, 0x0000000000000000UL, 0x3CFC7856C4827B70UL], // 123456.789 * 0.001 - 123.456789
        [0x400921FB54442D18UL, 0x4005BF0A8B145769UL, 0xC02114580B45D474UL, 0x0000000000000000UL, 0x3CB679E124A69B60UL], // pi * e - 8.539734222673566
        [0xBFFB333333333333UL, 0x4007333333333333UL, 0x4013B851EB851EB8UL, 0x0000000000000000UL, 0xBC547AE147AE1480UL], // -1.7 * 2.9 + 4.93
        [0x4202A05F20026666UL, 0x3DDB7CDFD9D7BDBCUL, 0xBFF0000000000000UL, 0x3DC07E2000000000UL, 0x3DC07E232189EF42UL], // 1e10+0.3 * 1e-10+7e-27 - 1.0
    ];

    private const int HotIterations = 200_000;

    private static double D(ulong bits) => BitConverter.UInt64BitsToDouble(bits);
    private static ulong B(double x) => BitConverter.DoubleToUInt64Bits(x);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double MulAdd(double a, double b, double c) => a * b + c;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double MulThenAdd(double a, double b, double c)
    {
        double m = a * b;
        return m + c;
    }

    [Fact]
    public void Table_CasesDistinguishFusedFromUnfused()
    {
        foreach (var row in Table)
        {
            Assert.NotEqual(row[3], row[4]);
            Assert.Equal(row[4], B(Math.FusedMultiplyAdd(D(row[0]), D(row[1]), D(row[2]))));
        }
    }

    [Fact]
    public void NoInliningMethod_IsNotContracted()
    {
        foreach (var row in Table)
        {
            Assert.Equal(row[3], B(MulAdd(D(row[0]), D(row[1]), D(row[2]))));
            Assert.Equal(row[3], B(MulThenAdd(D(row[0]), D(row[1]), D(row[2]))));
        }
    }

    [Fact]
    public void HotLoop_IsNotContracted_AcrossTiering()
    {
        int n = Table.Length;
        var a = new double[n];
        var b = new double[n];
        var c = new double[n];
        var expected = new ulong[n];
        for (int k = 0; k < n; k++)
        {
            a[k] = D(Table[k][0]);
            b[k] = D(Table[k][1]);
            c[k] = D(Table[k][2]);
            expected[k] = Table[k][3];
        }

        // Long enough for OSR/tier-1 to replace the tier-0 body mid-loop.
        int mismatches = 0;
        for (int i = 0; i < HotIterations; i++)
        {
            int k = i % n;
            double r = a[k] * b[k] + c[k];
            if (B(r) != expected[k]) mismatches++;
        }

        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void HotLoop_NoInliningCall_IsNotContracted_AcrossTiering()
    {
        // Repeated calls push MulAdd itself through tier-0 -> tier-1 rejit.
        int mismatches = 0;
        for (int i = 0; i < HotIterations; i++)
        {
            var row = Table[i % Table.Length];
            if (B(MulAdd(D(row[0]), D(row[1]), D(row[2]))) != row[3]) mismatches++;
        }

        Assert.Equal(0, mismatches);
    }

    // Tier-1 treats static readonly fields as constants, so this checks constant folding too.
    private static readonly double FieldA = BitConverter.UInt64BitsToDouble(0x3FF199999999999AUL);
    private static readonly double FieldB = BitConverter.UInt64BitsToDouble(0x3FF199999999999AUL);
    private static readonly double FieldC = BitConverter.UInt64BitsToDouble(0xBFF35C28F5C28F5DUL);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double FieldMulAdd() => FieldA * FieldB + FieldC;

    [Fact]
    public void StaticReadonlyFields_AreNotContracted_AcrossTiering()
    {
        int mismatches = 0;
        for (int i = 0; i < HotIterations; i++)
        {
            if (B(FieldMulAdd()) != 0UL) mismatches++;
        }

        Assert.Equal(0, mismatches);
        Assert.NotEqual(0UL, B(Math.FusedMultiplyAdd(FieldA, FieldB, FieldC)));
    }

    [Fact]
    public void CounterRngCases_UnfusedResidualIsZero()
    {
        // c = -(a*b) rounded, so the unfused sum is exactly +0 while FMA yields the rounding error.
        var rng = new CounterRng(20261103UL);
        const int count = 4096;
        int distinguishing = 0;
        int mismatches = 0;
        for (int i = 0; i < count; i++)
        {
            double a = 1.0 + rng.NextUnit(0, 0, i, 0);
            double b = 1.0 + rng.NextUnit(0, 0, i, 1);
            double c = -MulOnly(a, b);
            if (B(Math.FusedMultiplyAdd(a, b, c)) != 0UL) distinguishing++;
            if (B(MulAdd(a, b, c)) != 0UL) mismatches++;
            if (B(a * b + c) != 0UL) mismatches++;
        }

        Assert.Equal(0, mismatches);
        Assert.True(distinguishing > count * 9 / 10, $"only {distinguishing} of {count} cases distinguish fused from unfused");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double MulOnly(double a, double b) => a * b;
}
