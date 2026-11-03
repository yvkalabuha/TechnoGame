using System.Globalization;
using Singularity.Core.Science.ToyFunnel;

namespace Singularity.Core.Tests.ToyFunnel;

/// <summary>
/// Bit-for-bit parity with the reference prototype lab/proto/model.py (TASK-0004 acceptance).
/// Expected values come from (repo root):
///     python3 tests/Singularity.Core.Tests/Reference/toy_funnel_refs.py lab/proto
/// state_hash = prototype runs.csv layout after 492 ticks; the three digests cover every yearly row
/// (raw stocks, Y_year, B, eng_binding_months, bits of the mean shock), SETUP multipliers and milestones.
/// </summary>
public class ToyFunnelParityTests
{
    [Theory]
    [InlineData("A", 1uL, 0.25, 0.625, 1.0, 1.0, 0.25, 6, "6a5bbe86d055f6295663ffe54625e7753a05677f5ec18f8fc9833e5296fb635d", "b745412d4aa29ba8863e677736083b6f7fb633765e38429bf326c54efc47d730", "de52755ab73f7ccec668a0dbe457502ca1d8c2af966ad4ceb34ef8c7672f1008", "b2cde01b72146a742f1949e653646dc990245dd17102f3ed5e49c6eeef5238ea")]
    [InlineData("A", 2uL, 1.0, 0.625, 1.0, 1.0, 0.25, 6, "71971b56e6c89d78a2a82b365c0b6a5c986d3044c00376331b3f60ed8508bfa2", "e2258cd1f088b030f0fe6bc461e80875b8ef312168bf1b46756f4669b57d52a9", "34f9ec6cd211369e75371be89045de9395d64a1d22cb535e2fc342a6bb59e810", "0f4bc47f36f6b95e258e7c2e83ddaa0e979d9b6ee01b500b39be3ff035ec2c10")]
    [InlineData("A", 7uL, 0.25, 0.625, 1.0, 1.0, 0.25, 6, "2a29ae14940d576e7cf2c6602a59cc43e3b6c8358f75944514d8619d0313060a", "7d6567712c75207030ae3a11467bb6b26bd11220cbd2cfeb8328f77218541c7a", "515be1f2a100238dd609e41b36f05d0f74e8e5bde1e37c69d7dd80cbc76bc27d", "e5ac3ccd565ccf0e657a29cb7a7dfade088378729bd320abcc09a368301bbfd0")]
    [InlineData("A", 42uL, 1.0, 0.625, 1.0, 1.0, 0.25, 6, "af9982963e684934aae31fb5dc41d538b6a70e2d867558715ecf61dce37cc7f1", "2da50cbd34c7ca88b9b6cd4200cce64c13657619682eb85e4518ff5028b30210", "f970be0d4c317561ab459e0afa80ee0c38c79ebf136c92747734ad8d3d01c6fc", "87194b858c2cc1a2069fe835e8bb4eb18c6756153616a8504ebfdaa76f3b0443")]
    [InlineData("A", 200uL, 0.25, 0.625, 1.0, 1.0, 0.25, 6, "518901a6ba5d24be7827e591b2a711cd8c8986fdcf86aeded874bc631e2663d6", "1ff94c2f2b2b7bb5c90ea0c21d33f7cd00735e8f2563ddd87b037d29ac120987", "a23da692b0fe17fc04369efeb07ffa881ee4a6531e9e7bbe30b951670ced96f9", "3252b10c56d0048d351b618c933dc0fca9252ec7b94396e6867ef3d0da4dd64b")]
    [InlineData("A", 123456789uL, 1.0, 0.625, 1.0, 1.0, 0.25, 6, "80f800d1abc3bd31fafc327bac993c84b303c481ba1849c908917c1c582863d1", "072eba7845cc1dd18e2fc0f689ef607b7bdd1a368e7af6ac6184fe004b3e9af1", "7266e1b8508959da8ec94f9ca810a17ebb7a75539fe8190e6fe4f4ab06daf94c", "8d45c8d044b68459470a1931f5d4d91e600d0f706dfbc8b3aa184057e9c59bc7")]
    [InlineData("A", 0uL, 0.0, 0.625, 1.0, 1.0, 0.25, 6, "90887f52af1f793cbef1420961714490fc24c54b54db8da734b414d8b1c582f4", "c3fa636bc696f90fe6edcef8811bce3c7150295d0c72155cdb5b69267d748348", "60572ee812d9ab93876218d608e25fbbc0b8a97d05a575c4fce44d25ac9e9ec9", "47bcf46092a7054ab06c13f99406e4fe8f25167c719bd22b309caf2d099be309")]
    [InlineData("A", 18446744073709551615uL, 1.0, 0.625, 1.0, 1.0, 0.25, 6, "eb9e484f5b688b071fae58d9737f36a22158d96c4da01c89d49da4e74c106f12", "b7c22863bc1f1022c06532fa7eeb1cd0bff4929e12ddbb58e494caeb38788f54", "89956bb4fd9d0773b1a1089b5117777ce7cc4aa724a0a34774388e67fcfe230c", "79e22c690fc3801b69d76963c12817a04578fd1bb6edffcc708d0400a4c10466")]
    [InlineData("A", 11uL, 0.25, 0.5, 0.5, 1.0, 0.25, 6, "7b4bde6c32339a06833622f275938fae33270a732f385676e0cd8522b3da1662", "0a1438c8f59592e071a87620668fd4c51a0ada174e3bd08c7312e9c4cb26ae11", "3abd609066cd428e12ac73781d978bb0a179fe5a307302d78840effb61128fa3", "d7c874b072b7da91a1c339ff01c9bb010082b828af58552cebe0fea33891f4f5")]
    [InlineData("A", 12uL, 1.0, 0.75, 0.75, 1.0, 0.25, 6, "d44cffb10fc189cd4e860820774a7430c6e5ee770b61636c72ba77cc0113e4a0", "ba6197459b1680076568b2d2a007fc2da29175097435f4c67c1338d3cf59920e", "5a3e005d6a9752c5004d1bdb3d84cdaa3c83e10337addaa0cb9e15eb5c0c8878", "083f6febffb2123fffdf19d7ff1fcec88fd62d9fb821c146f35e2c4dd95a6add")]
    [InlineData("B", 1uL, 0.25, 0.625, 1.0, 0.45605548915918925, 0.1080050200689584, 6, "ca970b357fd587d4912a49fa0695e5611ed7d2b226fbe509b0a92450096b6713", "1af3020bccded48f12bcf41284d9ec6054f7bccc2e8aa4b1f5afd50df6ed5176", "de52755ab73f7ccec668a0dbe457502ca1d8c2af966ad4ceb34ef8c7672f1008", "a2e1da0def51847c6408d37d46611956b85c4b572d296ea8ad6ed90c1fe769aa")]
    [InlineData("B", 3uL, 1.0, 0.625, 1.0, 0.4562846855958923, 0.10798051790334284, 6, "20ce5cc70ca0c67ef4ee2a2156c11753587f8b53c8f04e0a749e9f964484bfbc", "f6f60ac6720f7729e08e02a7dd51fe51f0efe89e69b7ee5044c4c70a6e2f17ce", "8d0efa26a17d89b9241198f0a0422aaa7697202cf6c1765d1c75e504a5f75cdd", "1133f9148cc6ccff40de6cd89295e9c864306027252be0275741e18b19872f03")]
    [InlineData("B", 5uL, 0.25, 0.625, 1.0, 0.45605548915918925, 0.1080050200689584, 6, "05ffded4944a543a420a207c4bb188010f02dfa335234da9c59a2dab4006146e", "601debeb745ca9eed29c20050c3e539e70643c157dace5887648888d46de619f", "7bb7fa9d5d0ddc42e63fc74245dfd47656c2ebde20b79f4cc9a83b8a04bd0fc1", "666b28ff284212995755a3deef3e9aa93e395a6e9b97280f0d8a4f73324e99e2")]
    [InlineData("B", 10001uL, 1.0, 0.625, 1.0, 0.4562846855958923, 0.10798051790334284, 6, "775469230e37540eca85a87c2b8d69f7d451686b76555116f7550196d83d914f", "7b2838f83c76bc219a1b8cee97ec156822ac2533f9ca8d5ce8fc7aff0050bb0e", "6acf822666bda74ad4d5480f565d1a44edfeca05c2de35b76575192c41fdcce4", "2a797b41518101f9f34c1df2dd3b885b0c67fc27b40c2d1bbdb6df66073490dd")]
    [InlineData("C", 1uL, 0.25, 0.625, 1.0, 1.0, 0.25, 6, "fc1d5ec086a5a9ae0f3f60fae1d6ab319609fbd6632362490c370488c77f387f", "511bd2a4ea5a1b6f9fc8d255a4eeb6e9fdb5811eb1a0be42fb58a74a0cbeb098", "de52755ab73f7ccec668a0dbe457502ca1d8c2af966ad4ceb34ef8c7672f1008", "91212faa88d9c1d0ba90553f06e88c3fe697eb6fb6a4e540ae8573207737788b")]
    [InlineData("C", 4uL, 1.0, 0.625, 1.0, 1.0, 0.25, 6, "caa69665d196e74844ded442eb8c047f69c869167b52a96920fca10621ab8511", "6b4ce0ae368c84fca72ab3cf16ebffd2a136a5b48d950b2f6f109ec6f40aa583", "925b0b99381ad494b21d039e50336501b52f03d5a24186b3c55711119c1a09b9", "28f197fccfd5217cc1865c76c7336c93ab2b122656a46f73d2e66db599eb3361")]
    [InlineData("C", 99uL, 0.25, 0.625, 1.0, 1.0, 0.25, 6, "f92d74a238dd563a9678b3df77e880100439985ac724fdf94e5ec8f6ef4918a4", "d582f455877c562d13634a7818f564e9d7282ed582e5503110b67f708e731251", "917efc687dcd37f9cf36932ccfaa60412ec7f8ca0a8076dd5b667a70254b3b7a", "61a6cb39b6fc0ff8ff0be9e52e90e50bcd20031b4eff5b3a67a9522d626cf4c7")]
    [InlineData("C", 150uL, 1.0, 0.625, 1.0, 1.0, 0.25, 6, "60f44bc2d9dd99eb2298bd14e049d5cf8ebaa4af26754de5b819b2874e5942af", "e5af6ff9abe3d5e27e1e5d55c391574f5029349e16b87af6d913b837bf9ae407", "88976413e3bdb5d25627d903a870d97f2fed59239b26cdf63a4ebb8d57580d92", "f24719ef451d94056c6434f3a2a51e7ad86e579a31e2b4e8a48a1a193b5851cc")]
    [InlineData("C", 0uL, 0.0, 0.625, 1.0, 1.0, 0.25, 6, "b5a03ca247f378286acecd04ee8975ab22d365c2529030cfb87d50cff8a1fa76", "7ec6196f99f81295602a746a94204241175ae963aca4379368b0225a524244e7", "60572ee812d9ab93876218d608e25fbbc0b8a97d05a575c4fce44d25ac9e9ec9", "b2d0188025ae644cd0fb80097b2e124cd589a4004ee167f1f7299af88b726091")]
    [InlineData("A", 3uL, 1.0, 0.625, 1.0, 1.0, 0.25, 64, "649e176ec4d249616f4e4c53b4fc191be44ee07829e2a63f7a54f647c0338d91", "80c0614e2204e91cdd8aad2a1d78dd26a934511b39013233960bd018f3a281bb", "fab98292f083bf895631f4a4805d9985a70b305689df5c82dc5efce31effe8bf", "613a677838edde159b56d965fe3b4a1d3db39f413360f94f5b47ec78eb9cbe4f")]
    [InlineData("B", 9uL, 0.25, 0.625, 1.0, 0.45605548915918925, 0.1080050200689584, 64, "ce6c623f7de6a429243dbb0aefadedf4051b70ac80822e12752b28c15a3e5637", "a6dca59662bd62815125ce9029ddfedce7f023eef5da6028e442d615dec52c2d", "b17a1ebf8673bc5554389a6e50f940b01fc1f9a9452c1a13317dbed2930e2920", "2390f3dfd717810f091ab2c18f549989d09eb8c823b1920d21e3833093f9a805")]
    [InlineData("C", 8uL, 0.25, 0.625, 1.0, 1.0, 0.25, 64, "3667da402ca55a0d169e01ec8402c9cca29c74ed4400ad1c188528d905455c7a", "5b87cade0405d4a13aec8368385edc768a75fa66c358d1c1bbf48c006fde95d8", "30f521b5136a9cc82a00c56b241066321685f67231b7d7003b4cbbead001d98b", "2a158918dd1206d921bd376ceed061c8e00d5f0036a7f6a4c0b18ceddfe4a17b")]
    [InlineData("A", 95uL, 1.0, 0.625, 1.375, 1.0, 0.25, 6, "0d20fbc8616dec10d0c543848d961d25d3632307a87e1ad24716166e345b7a9f", "2d16b10cba11713da74e6522fe0120a9da69ee504c2e754a97d6c7ab7aaabae6", "2e522f5447720470b46d8f2a1391f51341517119eaf906656f9492f01f6ac950", "406fd134ea53bcf24e1bda4eff74045dc14ca73f167e498ea7856569408198b3")]
    public void MatchesPrototype_BitForBit(string model, ulong seed, double nu, double beta, double gamma,
        double cTau0, double deltaTau, int countries, string stateHash, string yearly, string mults, string milestones)
    {
        var p = ToyRun.Params(model, nu, beta, gamma, R(cTau0), R(deltaTau), countries);
        ToyRun.Result r = ToyRun.Run(p, seed, threads: 4, chunkSize: 5);

        Assert.Equal(stateHash, r.Model.PrototypeStateHash(r.FinalTick));
        if (r.Model.Config.Variant != ToyFunnelVariant.C)
            Assert.Equal(stateHash, r.GoldenHash); // the Table layout is the prototype's for A and B
        (string y, string m, string ms) = ToyRun.Digests(r, countries);
        Assert.Equal(yearly, y);
        Assert.Equal(mults, m);
        Assert.Equal(milestones, ms);
        Assert.Equal(41 * countries, r.Metrics.Tables[ToyFunnelModel.YearlyTable].Count);
    }

    [Fact]
    public void UncalibratedControlB_Overflows_LikeThePrototype()
    {
        // The prototype raises "raw out of 2^53" for B with c_tau0 = 1, delta_tau = 0.25 (seed 1, nu 0.25);
        // calibration treats this as +inf (spec §2.8).
        var e = Assert.Throws<ToyFunnelInvariantException>(() => ToyRun.Run(ToyRun.Params("B", 0.25), seed: 1));
        Assert.Contains("2^53", e.Message);
    }

    private static string R(double x) => x.ToString("R", CultureInfo.InvariantCulture);
}
