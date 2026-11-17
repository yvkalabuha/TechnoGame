using System.Globalization;
using System.Runtime.InteropServices;
using Linguini.Bundle;
using Linguini.Shared.Types.Bundle;
using FluentS1;

var results = new List<(string Id, bool Ok)>();
void Check(string id, bool ok, string detail)
{
    results.Add((id, ok));
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {id}: {detail}");
}
string Q(string s) => "«" + s.Replace(" ", "<NBSP>").Replace("−", "<MINUS>").Replace("⁨", "<FSI>").Replace("⁩", "<PDI>") + "»";

const string RuFtl = """
-actor-DEU = { $case ->
    [gen] Германии
    [ins] Германией
   *[nom] Германия
  }
    .gender = f
-actor-FRA = { $case ->
    [gen] Франции
    [ins] Францией
   *[nom] Франция
  }
    .gender = f
-actor-SUN = { $case ->
    [gen] СССР
    [ins] СССР
   *[nom] СССР
  }
    .gender = masc
-germany = Германия
    .gen = Германии
    .ins = Германией
    .gender = f

war-with = война с { NAME($c, case: "ins") }
declared = { GENDER($a) ->
    [f] { NAME($a) } объявила
    [neut] { NAME($a) } объявило
   *[masc] { NAME($a) } объявил
  }
war-static = война с { -actor-DEU(case: "ins") }

items = { NUMBER($n) ->
    [one] { NUMBER($n) } предмет
    [few] { NUMBER($n) } предмета
    [many] { NUMBER($n) } предметов
   *[other] { NUMBER($n) } предмета
  }
items-raw = { $n ->
    [one] ONE
    [few] FEW
    [many] MANY
   *[other] OTHER
  }
items-min1 = { NUMBER($n, minimumFractionDigits: 1) ->
    [one] ONE
    [few] FEW
    [many] MANY
   *[other] OTHER
  }
bare = { $n }
pct = { NUMBER($n, style: "percent") }

date-month-nom = { $month ->
    [1] январь
    [2] февраль
    [3] март
   *[other] ?месяц
  }
date-month-gen = { $month ->
    [1] января
    [2] февраля
    [3] марта
   *[other] ?месяца
  }
fmt-date-dmy = { $day } { date-month-gen } { $year }
fmt-date-my = { date-month-nom } { $year }
fmt-date-y = { $year }
date-call = { DATE($d, format: "dmy") }
date-call-my = { DATE($d, format: "my") }

fmt-number-decimal = ,
fmt-number-group = { " " }
fmt-number-minus = { "−" }
fmt-percent = { $n }{ " " }%

iso = Привет, { $who }!
node-s8-eng-turbojet_g1 =
    .name = Турбореактивный двигатель
    .short = ТРД
""";

const string EnFtl = """
-actor-DEU = { $case ->
   *[nom] Germany
  }
    .gender = neut
fmt-number-decimal = .
fmt-number-group = ,
fmt-number-minus = { "−" }
fmt-percent = { $n }%
date-month-nom = { $month ->
    [1] January
    [2] February
    [3] March
   *[other] ?month
  }
fmt-date-dmy = { date-month-nom } { $day }, { $year }
bare = { $n }
pct = { NUMBER($n, style: "percent") }
items = { NUMBER($n) ->
    [one] { NUMBER($n) } item
   *[other] { NUMBER($n) } items
  }
""";

Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}, OS: {RuntimeInformation.OSDescription}");
Console.WriteLine($"Linguini.Bundle: {typeof(FluentBundle).Assembly.GetName().Version}");
Check("S1-1 restore+run net10", Environment.Version.Major >= 10, Environment.Version.ToString());

var ru = Loc.Build("ru", RuFtl, useIsolating: false);
var en = Loc.Build("en", EnFtl, useIsolating: false);
Check("S1-1b FrozenBundle built", ru is FrozenBundle && en is FrozenBundle, ru.GetType().Name + ", culture=" + ru.Culture.Name);

// ---- (2) case and gender through DataRef
string W(string id, params (string, IFluentType)[] a) => Loc.Msg(ru, id, a);
var deu = new DataRef("actor-DEU");
var fra = new DataRef("actor-FRA");
var sun = new DataRef("actor-SUN");
var war = W("war-with", ("c", deu));
var decl = W("declared", ("a", fra));
var declSun = W("declared", ("a", sun));
Check("S1-2a DataRef variable + case (selector in term)", war == "война с Германией", Q(war));
Check("S1-2b DataRef variable + gender selector", decl == "Франция объявила" && declSun == "СССР объявил", Q(decl) + ", " + Q(declSun));
var warAttr = W("war-with", ("c", new DataRef("germany")));
Check("S1-2c term with attributes (`.ins`, `.gender`) as in the task", warAttr == "война с Германией", Q(warAttr));
var warStatic = W("war-static");
Check("S1-2d static term reference with case", warStatic == "война с Германией", Q(warStatic));
Check("S1-2e GENDER on attribute-form term", W("declared", ("a", new DataRef("germany"))) == "Германия объявила", Q(W("declared", ("a", new DataRef("germany")))));
Check("S1-2f message attribute lookup (node-*.name)",
    ru.TryGetAttrMessage("node-s8-eng-turbojet_g1.name", new Dictionary<string, IFluentType>(), out _, out var nm) && nm == "Турбореактивный двигатель", Q(nm ?? "null"));

// ---- (3) plurals
var ok3 = true;
var sb3 = new System.Text.StringBuilder();
(double n, string exp)[] cases = { (1, "ONE"), (2, "FEW"), (5, "MANY"), (11, "MANY"), (21, "ONE"), (111, "MANY"), (1.5, "OTHER") };
foreach (var (n, exp) in cases)
{
    var raw = W("items-raw", ("n", (FluentNumber)n));
    var viaNumber = W("items-min1", ("n", (FluentNumber)n));
    var shown = W("items", ("n", (FluentNumber)n));
    var good = raw == exp;
    ok3 &= good;
    sb3.AppendLine($"    n={n.ToString(CultureInfo.InvariantCulture),-4} raw=$n -> {raw,-5} (expected {exp}) {(good ? "ok" : "BAD")}; NUMBER(min1)-> {viaNumber}; items -> {Q(shown)}");
}
Check("S1-3a RU categories for raw numeric argument (1,2,5,11,21,111,1.5)", ok3, "\n" + sb3);
var okNum = new[] { (1d, "ONE"), (2, "FEW"), (5, "MANY"), (11, "MANY"), (21, "ONE"), (111, "MANY"), (1.5, "OTHER") }
    .All(c => W("items", ("n", (FluentNumber)c.Item1)).EndsWith(c.Item2 switch { "ONE" => "предмет", "FEW" => "предмета", "MANY" => "предметов", _ => "предмета" }));
Check("S1-3b same categories via custom NUMBER (selector = function result)", okNum,
    string.Join("; ", new[] { 1d, 2, 5, 11, 21, 111, 1.5 }.Select(x => Q(W("items", ("n", (FluentNumber)x))))));
var one0 = W("items-min1", ("n", (FluentNumber)1d));
var rawOne = W("items-raw", ("n", (FluentNumber)1d));
Check("S1-3c formatted \"1,0\" selects other", one0 == "OTHER", $"NUMBER(1, minimumFractionDigits:1) -> {one0}; for contrast raw 1.0 -> {rawOne}; text {Q(Loc.Format(ru, 1, 1))}");
Check("S1-3d en: 1 -> one, 1.0 text -> other", Loc.Msg(en, "items", ("n", (FluentNumber)1d)) == "1 item" && Loc.Msg(en, "items", ("n", (FluentNumber)2d)) == "2 items", Q(Loc.Msg(en, "items", ("n", (FluentNumber)1d))) + ", " + Q(Loc.Msg(en, "items", ("n", (FluentNumber)1234.5d))));

// Contrast: with FormatterFunc set, a function result used as selector loses plural matching.
{
    var o = new Linguini.Bundle.Builder.FluentBundleOption { Locales = { "ru" }, UseIsolating = false, FormatterFunc = t => t.AsString() };
    var cb = FluentBundle.MakeUnchecked(o);
    cb.AddFunctionOverriding("NUM", (a, n) => FluentNumber.FromString(a[0].AsString()));
    cb.AddResource("s = { NUM($n) ->\n [one] ONE\n *[other] OTHER\n}\nr = { $n ->\n [one] ONE\n *[other] OTHER\n}", out _);
    var d = new Dictionary<string, IFluentType> { ["n"] = (FluentNumber)21d };
    Console.WriteLine($"INFO FormatterFunc contrast (n=21, expected ONE): function-result selector -> {cb.GetMessage("s", null, d)}, variable selector -> {cb.GetMessage("r", null, d)}");
}

// ---- (4) message reference sees caller's args
var dmy = W("fmt-date-dmy", ("day", new FluentString("9")), ("month", (FluentNumber)2d), ("year", new FluentString("1945")));
Check("S1-4a message reference sees caller args ($month in date-month-gen)", dmy == "9 февраля 1945", Q(dmy));
var dc = W("date-call", ("d", new GameDate(1945, 3, 9)));
var dcm = W("date-call-my", ("d", new GameDate(1945, 1, 1)));
Check("S1-4b DATE(...) function reaches the bundle and formats message with args", dc == "9 марта 1945" && dcm == "январь 1945", Q(dc) + ", " + Q(dcm));
Check("S1-4c en date", Loc.Msg(en, "fmt-date-dmy", ("day", new FluentString("9")), ("month", (FluentNumber)3d), ("year", new FluentString("1945"))) == "March 9, 1945", Q(Loc.Msg(en, "fmt-date-dmy", ("day", new FluentString("9")), ("month", (FluentNumber)3d), ("year", new FluentString("1945")))));

// ---- (в) function reaches bundle and term AST
Check("S1-c function reaches bundle + term AST", Loc.SeenBundleType == "FrozenBundle" && ru.TryGetAstTerm("actor-DEU", out var t) && t.Attributes.Any(a => a.Id.ToString() == "gender"),
    $"NAME saw {Loc.SeenBundleType}; term attributes: {string.Join(",", ru.TryGetAstTerm("actor-DEU", out var t2) ? t2.Attributes.Select(a => a.Id.ToString()) : Array.Empty<string>())}");

// ---- (г) UseIsolating
var isoOff = Loc.Msg(ru, "iso", ("who", new FluentString("Мир")));
var ruOn = Loc.Build("ru", RuFtl, useIsolating: true);
var isoOn = Loc.Msg(ruOn, "iso", ("who", new FluentString("Мир")));
Check("S1-g UseIsolating=false: no U+2068/U+2069", !isoOff.Contains('⁨') && !isoOff.Contains('⁩') && isoOn.Contains('⁨'),
    $"false -> {Q(isoOff)}; true (contrast) -> {Q(isoOn)}");

// ---- (5) determinism of NUMBER/DATE under cultures
string Sample(FrozenBundle b)
{
    var l = new List<string>
    {
        Loc.Msg(b, "items", ("n", (FluentNumber)1234567.891d)),
        Loc.Msg(b, "bare", ("n", Loc.Num(b, -1234.5d))),
        Loc.Msg(b, "pct", ("n", (FluentNumber)0.255d)),
        Loc.Msg(b, "items", ("n", (FluentNumber)0.5d)),
        Loc.Msg(b, "items", ("n", (FluentNumber)2.675d)),
    };
    if (b == ru) { l.Add(W("date-call", ("d", new GameDate(1945, 3, 9)))); l.Add(W("items", ("n", (FluentNumber)1_000_000d))); }
    return string.Join(" | ", l);
}
var cultures = new[] { "", "ru-RU", "en-US", "de-DE", "tr-TR", "ar-SA", "fa-IR", "ja-JP", "th-TH" };
var samples = new List<string>();
foreach (var c in cultures)
{
    string? s = null;
    var th = new Thread(() =>
    {
        CultureInfo ci; try { ci = new CultureInfo(c); } catch (CultureNotFoundException) { ci = CultureInfo.InvariantCulture; }
        CultureInfo.CurrentCulture = ci; CultureInfo.CurrentUICulture = ci;
        s = Sample(ru);
    });
    th.Start(); th.Join();
    samples.Add(s!);
}
Check("S1-5 NUMBER/DATE identical under 9 cultures", samples.Distinct().Count() == 1, "\n    " + Q(samples[0]) + "\n    cultures: " + string.Join(",", cultures.Select(c => c == "" ? "invariant" : c)));
Console.WriteLine("    en sample: " + Q(Sample(en)));
var neg = Loc.Format(ru, -1234.5);
Check("S1-5b separators from locale messages", neg == "−1 234,5" && Loc.Format(en, -1234.5) == "−1,234.5", $"ru {Q(neg)}; en {Q(Loc.Format(en, -1234.5))}");

// ---- (д) multithreaded reads of FrozenBundle
var expected = new List<string>();
string[] Work(int i)
{
    var n = (double)(i % 120) + (i % 3 == 0 ? 0.5 : 0);
    return new[]
    {
        W("items", ("n", (FluentNumber)n)),
        W("war-with", ("c", i % 2 == 0 ? deu : fra)),
        W("declared", ("a", i % 2 == 0 ? fra : sun)),
        W("date-call", ("d", new GameDate(1910 + i % 190, 1 + i % 3, 1 + i % 28))),
        W("bare", ("n", Loc.Num(ru, n * 1000))),
    };
}
var baseline = Enumerable.Range(0, 400).Select(i => string.Join("|", Work(i))).ToArray();
var bad = 0; var exceptions = 0; long calls = 0;
var threads = Enumerable.Range(0, 16).Select(t => new Thread(() =>
{
    var ci = new CultureInfo(cultures[t % cultures.Length] is { Length: > 0 } cn ? cn : "en-US");
    CultureInfo.CurrentCulture = ci;
    for (var r = 0; r < 25; r++)
        for (var i = 0; i < 400; i++)
        {
            var idx = (i * 7 + t * 13 + r) % 400;
            try { if (string.Join("|", Work(idx)) != baseline[idx]) Interlocked.Increment(ref bad); }
            catch { Interlocked.Increment(ref exceptions); }
            Interlocked.Increment(ref calls);
        }
})).ToList();
threads.ForEach(t => t.Start()); threads.ForEach(t => t.Join());
Check("S1-d FrozenBundle read from 16 threads", bad == 0 && exceptions == 0, $"{calls} iterations (x5 formats each), mismatches={bad}, exceptions={exceptions}");

Console.WriteLine();
Console.WriteLine($"TOTAL: {results.Count(r => r.Ok)} PASS, {results.Count(r => !r.Ok)} FAIL");
return results.All(r => r.Ok) ? 0 : 1;
