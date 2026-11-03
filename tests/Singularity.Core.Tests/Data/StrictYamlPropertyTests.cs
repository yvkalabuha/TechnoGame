using System.Globalization;
using System.Numerics;
using System.Text;
using Singularity.Core.Data;
using Singularity.Core.Data.Yaml;
using Singularity.Kernel;

namespace Singularity.Core.Tests.Data;

/// <summary>
/// Seeded property and boundary tests of the strict YAML subset and the exact Fx6 parse
/// (ADR-0010, ADR-0003 R1). Test-engineer audit of TASK-0013.
/// </summary>
public class StrictYamlPropertyTests
{
    private const int Iterations = 20_000;

    private static YScalar Value(string yaml) =>
        (YScalar)((YMap)StrictYaml.Parse(yaml, "t.yaml")).Find("k")!;

    /// <summary>Independent reference of the accepted number grammar -?(0|[1-9][0-9]*)(.[0-9]+)?</summary>
    private static bool ReferencePlainDecimal(string s)
    {
        int i = s.StartsWith('-') ? 1 : 0;
        string rest = s[i..];
        int dot = rest.IndexOf('.');
        string whole = dot < 0 ? rest : rest[..dot];
        string frac = dot < 0 ? "" : rest[(dot + 1)..];
        if (whole.Length == 0 || !whole.All(char.IsAsciiDigit) || (whole.Length > 1 && whole[0] == '0'))
            return false;
        return dot < 0 || (frac.Length > 0 && frac.All(char.IsAsciiDigit));
    }

    /// <summary>Text a YAML 1.1/1.2 resolver might type: optional sign, then a digit or a dot.</summary>
    private static bool ReferenceStartsNumeric(string s)
    {
        int i = s.Length > 0 && s[0] is '+' or '-' ? 1 : 0;
        return i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.');
    }

    private static BigInteger? ReferenceRaw(string s)
    {
        if (!ReferencePlainDecimal(s))
            return null;
        bool neg = s[0] == '-';
        string body = neg ? s[1..] : s;
        int dot = body.IndexOf('.');
        string whole = dot < 0 ? body : body[..dot];
        string frac = dot < 0 ? "" : body[(dot + 1)..];
        if (frac.Length > 6)
            return null;
        BigInteger raw = BigInteger.Parse(whole, CultureInfo.InvariantCulture) * 1_000_000 +
                         BigInteger.Parse(frac.PadRight(6, '0'), CultureInfo.InvariantCulture);
        if (neg)
            raw = -raw;
        return raw < long.MinValue || raw > long.MaxValue ? null : raw;
    }

    // Plain scalars that start like a number: digit, dot or sign first, then a soup of
    // characters YAML resolvers care about (exponents, hex/octal/binary prefixes, separators).
    private static string RandomNumericLooking(Random rnd)
    {
        const string first = "0123456789.-+";
        const string soup = "0123456789000.._eExXoObB:-+aAfFinINT";
        var sb = new StringBuilder();
        sb.Append(first[rnd.Next(first.Length)]);
        int len = rnd.Next(0, 12);
        for (int i = 0; i < len; i++)
            sb.Append(rnd.Next(3) == 0 ? soup[rnd.Next(soup.Length)] : (char)('0' + rnd.Next(10)));
        return sb.ToString();
    }

    [Fact]
    public void NumericLookingPlainScalars_AreNumbersOnlyIfPlainDecimal_ElseLoadError()
    {
        var rnd = new Random(1301);
        int numbers = 0, rejected = 0;
        for (int i = 0; i < Iterations; i++)
        {
            string text = RandomNumericLooking(rnd);
            YScalar scalar;
            try
            {
                scalar = Value("k: " + text);
            }
            catch (DataLoadException)
            {
                rejected++;
                Assert.False(ReferencePlainDecimal(text), $"plain decimal '{text}' was rejected");
                continue;
            }
            // Any exception other than DataLoadException escaping Parse fails the test by itself.
            Assert.Equal(text, scalar.Value);
            if (ReferenceStartsNumeric(text))
            {
                Assert.True(scalar.Kind == ScalarKind.Number, $"'{text}' parsed as {scalar.Kind}, not as a number");
                Assert.True(ReferencePlainDecimal(text), $"'{text}' accepted as a number");
                numbers++;
            }
            else
            {
                Assert.Equal(ScalarKind.String, scalar.Kind);
            }
        }
        Assert.True(numbers > 500 && rejected > 500, $"generator is degenerate: {numbers} numbers, {rejected} rejected");
    }

    [Theory]
    // YAML 1.1 bool (yaml.org/type/bool.html) and null forms; YAML 1.2 core schema floats; 1.1 ints, sexagesimals, timestamps.
    [InlineData("y")] [InlineData("Y")] [InlineData("yes")] [InlineData("Yes")] [InlineData("YES")]
    [InlineData("n")] [InlineData("N")] [InlineData("no")] [InlineData("No")] [InlineData("NO")]
    [InlineData("True")] [InlineData("TRUE")] [InlineData("False")] [InlineData("FALSE")]
    [InlineData("on")] [InlineData("On")] [InlineData("ON")] [InlineData("off")] [InlineData("Off")] [InlineData("OFF")]
    [InlineData("~")] [InlineData("null")] [InlineData("Null")] [InlineData("NULL")]
    [InlineData(".inf")] [InlineData(".Inf")] [InlineData(".INF")] [InlineData("-.inf")] [InlineData("+.inf")]
    [InlineData(".nan")] [InlineData(".NaN")] [InlineData(".NAN")]
    [InlineData("0o17")] [InlineData("0b1010")] [InlineData("0x1f")] [InlineData("-0x1")] [InlineData("017")]
    [InlineData("1:20")] [InlineData("190:20:30.15")] [InlineData("1_000.5")]
    [InlineData("1e3")] [InlineData("1E3")] [InlineData("1.5e-3")] [InlineData("1.")] [InlineData("-.5")]
    [InlineData("+12")] [InlineData("+0.5")] [InlineData("+0")] [InlineData("00")] [InlineData("-00.5")] [InlineData("-01")]
    [InlineData("2001-12-14")] [InlineData("2001-12-14t21:59:43.10-05:00")] [InlineData("2001-12-14 21:59:43.10 -5")]
    public void ImplicitlyTypedPlainScalars_AreLoadErrors(string text)
    {
        var e = Assert.Throws<DataLoadException>(() => StrictYaml.Parse("k: " + text, "t.yaml"));
        Assert.StartsWith("t.yaml", e.Message);
    }

    [Theory]
    [InlineData("y")] [InlineData("yes")] [InlineData("null")] [InlineData("1e3")] [InlineData(".inf")] [InlineData("+1")] [InlineData("2001-12-14")]
    public void QuotedForms_AreStrings(string text)
    {
        Assert.Equal(ScalarKind.String, Value($"k: \"{text}\"").Kind);
        Assert.Equal(ScalarKind.String, Value($"k: '{text}'").Kind);
        Assert.Equal(ScalarKind.String, Value($"k: |\n  {text}\n").Kind);
    }

    [Fact]
    public void ImplicitTyping_InsideFlowAndBlockCollections_IsRejected()
    {
        Assert.Throws<DataLoadException>(() => StrictYaml.Parse("k: [a, yes]", "t.yaml"));
        Assert.Throws<DataLoadException>(() => StrictYaml.Parse("k: {a: off}", "t.yaml"));
        Assert.Throws<DataLoadException>(() => StrictYaml.Parse("k:\n  - 1e5\n", "t.yaml"));
        Assert.Throws<DataLoadException>(() => StrictYaml.Parse("k:\n  a:\n    b: ~\n", "t.yaml"));
    }

    [Fact]
    public void Fx6_ToStringThenParse_RoundTrips()
    {
        var rnd = new Random(1302);
        for (int i = 0; i < Iterations; i++)
        {
            long raw = rnd.Next(4) switch
            {
                0 => rnd.NextInt64(-3_000_000, 3_000_001),
                1 => rnd.NextInt64(-1_000_000_000_000, 1_000_000_000_001),
                _ => rnd.NextInt64(long.MinValue + 1, long.MaxValue),
            };
            Fx6 x = Fx6.FromRaw(raw);
            string text = x.ToString();
            Assert.Equal(x, Scalars.ParseFixed(text));
            Assert.Equal(ScalarKind.Number, Value("k: " + text).Kind);
        }
    }

    [Fact]
    public void ParseFixed_MatchesBigIntegerReference_OnRandomDecimals()
    {
        var rnd = new Random(1303);
        for (int i = 0; i < Iterations; i++)
        {
            var sb = new StringBuilder();
            if (rnd.Next(2) == 0)
                sb.Append('-');
            int wholeLen = rnd.Next(1, 16);
            sb.Append(wholeLen == 1 ? (char)('0' + rnd.Next(10)) : (char)('1' + rnd.Next(9)));
            for (int j = 1; j < wholeLen; j++)
                sb.Append((char)('0' + rnd.Next(10)));
            int fracLen = rnd.Next(-1, 9);
            if (fracLen >= 0)
            {
                sb.Append('.');
                for (int j = 0; j < Math.Max(fracLen, 1); j++)
                    sb.Append((char)('0' + rnd.Next(10)));
            }
            string text = sb.ToString();
            BigInteger? expected = ReferenceRaw(text);
            Fx6? actual = Scalars.ParseFixed(text);
            if (expected is null)
                Assert.True(actual is null, $"'{text}' should not parse, got {actual}");
            else
                Assert.True(actual is {} a && a.Raw == (long)expected.Value, $"'{text}' -> {actual}, expected raw {expected}");
        }
    }

    [Theory]
    [InlineData("9223372036854.775807", long.MaxValue)]
    [InlineData("-9223372036854.775807", -long.MaxValue)]
    [InlineData("1.123456", 1_123_456L)]
    [InlineData("-0.000001", -1L)]
    [InlineData("0.000001", 1L)]
    [InlineData("-0", 0L)]
    [InlineData("-0.0", 0L)]
    [InlineData("-0.000000", 0L)]
    [InlineData("0.100000", 100_000L)]
    public void ParseFixed_Boundaries(string text, long raw)
    {
        Assert.Equal(Fx6.FromRaw(raw), Scalars.ParseFixed(text));
        Assert.Equal(ScalarKind.Number, Value("k: " + text).Kind);
    }

    // BUG (TASK-0013 audit): ParseFixed builds the magnitude as a positive long before negating,
    // so Fx6 raw long.MinValue overflows and is rejected. Remove Skip when fixed.
    [Fact(Skip = "BUG: ParseFixed rejects Fx6 min value -9223372036854.775808 (positive magnitude overflows)")]
    public void ParseFixed_MinValue_IsRepresentable()
    {
        // Fx6 covers raw long.MinValue and Fx6.ToString prints it, so the text must parse back.
        Fx6 min = Fx6.FromRaw(long.MinValue);
        Assert.Equal("-9223372036854.775808", min.ToString());
        Assert.Equal(min, Scalars.ParseFixed("-9223372036854.775808"));
    }

    [Theory]
    [InlineData("9223372036854.775808")]
    [InlineData("-9223372036854.775809")]
    [InlineData("9223372036855")]
    [InlineData("99999999999999999999999")]
    [InlineData("1.1234567")]
    [InlineData("1.1234560")]   // a trailing zero still counts: 7 digits is an error, never rounded
    [InlineData("-0.0000001")]
    [InlineData("+1")]
    [InlineData("1.")]
    [InlineData(".1")]
    [InlineData("01")]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("1 ")]
    [InlineData("\u0661")]      // ARABIC-INDIC DIGIT ONE: not an ASCII digit
    public void ParseFixed_Rejects(string text) => Assert.Null(Scalars.ParseFixed(text));

    [Fact]
    public void NegativeZero_HasTheSameRawAsZero()
    {
        Assert.Equal(0L, Scalars.ParseFixed("-0")!.Value.Raw);
        Assert.Equal(Scalars.ParseFixed("0.0"), Scalars.ParseFixed("-0.000000"));
    }
}
