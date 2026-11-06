using System;
using System.Collections.Generic;
using FluentAssertions;
using SECSparser;
using Xunit;

namespace Secs4Net.UnitTests;

public class S16F15ParserUnitTests
{
    /// <summary>
    /// 验证整个合法批次的载具、槽位、配方、显式启动标志和暂停列表均被读取。
    /// </summary>
    [Fact]
    public void Parse_ValidBatch_ReadsAllJobsWithoutDefaultingStart()
    {
        using var request = new SecsMessage(16, 15)
        {
            SecsItem = L(U4(123), L(BuildJob("PJ1", false), BuildJob("PJ2", true)))
        };

        var data = S16F15_parser.Parse(request);

        data.DATAID.Should().Be(123);
        data.ProcessJobs.Should().HaveCount(2);
        data.ProcessJobs[0].PJID.Should().Be("PJ1");
        data.ProcessJobs[0].MF.Should().Be("0D");
        data.ProcessJobs[0].Carriers.Should().ContainSingle();
        data.ProcessJobs[0].Carriers[0].CarrierId.Should().Be("CARRIER1");
        data.ProcessJobs[0].Carriers[0].Slots.Should().Equal(1u, 2u);
        data.ProcessJobs[0].RecipeId.Should().Be("RECIPE1");
        data.ProcessJobs[0].PRPROCESSSTART.Should().BeFalse();
        data.ProcessJobs[0].PRPAUSEEVENT.Should().Equal(100u);
        data.ProcessJobs[1].PJID.Should().Be("PJ2");
        data.ProcessJobs[1].PRPROCESSSTART.Should().BeTrue();
    }

    /// <summary>
    /// 保持 DATAID 与 Slot 的既有整数格式兼容性，但每项必须是一个标量。
    /// </summary>
    [Theory]
    [InlineData(SecsFormat.U1)]
    [InlineData(SecsFormat.U2)]
    [InlineData(SecsFormat.U4)]
    [InlineData(SecsFormat.Binary)]
    public void Parse_CompatibleIntegerFormats_ArePreserved(SecsFormat format)
    {
        using var request = new SecsMessage(16, 15) { SecsItem = BuildRoot() };
        Replace(request.SecsItem!, Scalar(format), 0);
        Replace(request.SecsItem!, Scalar(format), 1, 0, 2, 0, 1, 0);

        var data = S16F15_parser.Parse(request);

        data.DATAID.Should().Be(7);
        data.ProcessJobs[0].Carriers[0].Slots.Should().Equal(7u, 2u);
    }

    /// <summary>
    /// 仅校验结构，允许空列表或业务层才能判断的内容，不擅自执行设备能力校验。
    /// </summary>
    [Fact]
    public void Parse_StructurallyValidData_LeavesBusinessRulesToCaller()
    {
        using var request = new SecsMessage(16, 15)
        {
            SecsItem = L(U4(0), L(L(A(""), A("legacy MF"), L(),
                L(U1(1), A(""), L(L(A("parameter"), L(U4(1), Boolean(true))))),
                Boolean(false), L())))
        };

        var data = S16F15_parser.Parse(request);

        data.ProcessJobs.Should().ContainSingle();
        data.ProcessJobs[0].MF.Should().Be("legacy MF");
        data.ProcessJobs[0].Carriers.Should().BeEmpty();
        data.ProcessJobs[0].PRPROCESSSTART.Should().BeFalse();
        data.ProcessJobs[0].PRPAUSEEVENT.Should().BeEmpty();
    }

    /// <summary>
    /// 提供固定列表数量、字段类型及标量数量错误，覆盖各层结构。
    /// </summary>
    public static IEnumerable<object[]> InvalidStructures()
    {
        yield return new object[] { L(U4(1)), "ROOT" };
        yield return new object[] { L(U4(1), L(), A("extra")), "ROOT" };
        yield return Case(U4(), "DATAID", 0);
        yield return Case(U4(1, 2), "DATAID", 0);
        yield return Case(A("1"), "DATAID", 0);
        yield return Case(A("jobs"), "PROCESS_JOB_LIST", 1);
        yield return Case(A("job"), "PROCESS_JOB_LIST[0]", 1, 0);
        yield return Case(L(A("PJ"), B(13), L(), L()), "PROCESS_JOB_LIST[0]", 1, 0);
        yield return Case(L(A("PJ"), B(13), L(), L(), Boolean(true)), "PROCESS_JOB_LIST[0]", 1, 0);
        yield return Case(L(A("PJ"), B(13), L(), L(), Boolean(true), L(), A("extra")), "PROCESS_JOB_LIST[0]", 1, 0);
        yield return Case(U4(1), ".PJID", 1, 0, 0);
        yield return Case(B(), ".MF", 1, 0, 1);
        yield return Case(B(1, 2), ".MF", 1, 0, 1);
        yield return Case(L(), ".MF", 1, 0, 1);
        yield return Case(A("materials"), ".MTRLSPEC", 1, 0, 2);
        yield return Case(A("carrier"), ".MTRLSPEC[0]", 1, 0, 2, 0);
        yield return Case(L(A("CARRIER1")), ".MTRLSPEC[0]", 1, 0, 2, 0);
        yield return Case(L(A("CARRIER1"), L(), A("extra")), ".MTRLSPEC[0]", 1, 0, 2, 0);
        yield return Case(U4(1), ".CarrierID", 1, 0, 2, 0, 0);
        yield return Case(A("slots"), ".Slots", 1, 0, 2, 0, 1);
        yield return Case(U1(), ".Slots[0]", 1, 0, 2, 0, 1, 0);
        yield return Case(U1(1, 2), ".Slots[0]", 1, 0, 2, 0, 1, 0);
        yield return Case(I4(-1), ".Slots[0]", 1, 0, 2, 0, 1, 0);
        yield return Case(A("1"), ".Slots[0]", 1, 0, 2, 0, 1, 0);
        yield return Case(A("recipe"), ".PROCESS_SPEC", 1, 0, 3);
        yield return Case(L(U1(1), A("recipe")), ".PROCESS_SPEC", 1, 0, 3);
        yield return Case(L(U1(1), A("recipe"), L(), A("extra")), ".PROCESS_SPEC", 1, 0, 3);
        yield return Case(A("method"), ".Method", 1, 0, 3, 0);
        yield return Case(U1(), ".Method", 1, 0, 3, 0);
        yield return Case(U1(1, 2), ".Method", 1, 0, 3, 0);
        yield return Case(U4(1), ".RecipeID", 1, 0, 3, 1);
        yield return Case(A("parameters"), ".Parameters", 1, 0, 3, 2);
        yield return Case(A("parameter"), ".Parameters[0]", 1, 0, 3, 2, 0);
        yield return Case(L(A("parameter")), ".Parameters[0]", 1, 0, 3, 2, 0);
        yield return Case(L(A("parameter"), U4(1), U4(2)), ".Parameters[0]", 1, 0, 3, 2, 0);
        yield return Case(U4(1), ".Name", 1, 0, 3, 2, 0, 0);
        yield return Case(A("true"), ".PRPROCESSSTART", 1, 0, 4);
        yield return Case(Boolean(), ".PRPROCESSSTART", 1, 0, 4);
        yield return Case(Boolean(true, false), ".PRPROCESSSTART", 1, 0, 4);
        yield return Case(U4(1), ".PRPAUSEEVENT", 1, 0, 5);
        yield return Case(U2(1), ".PRPAUSEEVENT[0]", 1, 0, 5, 0);
        yield return Case(U4(), ".PRPAUSEEVENT[0]", 1, 0, 5, 0);
        yield return Case(U4(1, 2), ".PRPAUSEEVENT[0]", 1, 0, 5, 0);
    }

    /// <summary>
    /// 无效结构不能静默转换为默认值，异常必须包含错误字段路径。
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidStructures))]
    public void Parse_InvalidStructure_ThrowsWithFieldPath(Item root, string path)
    {
        using var request = new SecsMessage(16, 15) { SecsItem = root };
        Action parse = () => S16F15_parser.Parse(request);

        parse.Should().Throw<InvalidOperationException>().WithMessage($"*{path}*");
    }

    /// <summary>
    /// 前一个 PJ 合法也不能掩盖后一个 PJ 的结构错误，整批必须解析失败。
    /// </summary>
    [Fact]
    public void Parse_MixedValidAndInvalidJobs_RejectsWholeBatch()
    {
        using var request = new SecsMessage(16, 15)
        {
            SecsItem = L(U4(1), L(BuildJob("VALID", true), A("invalid job")))
        };
        Action parse = () => S16F15_parser.Parse(request);

        parse.Should().Throw<InvalidOperationException>().WithMessage("*PROCESS_JOB_LIST[1]*");
    }

    /// <summary>
    /// 非 S16F15、空引用和缺失消息体均应明确失败。
    /// </summary>
    [Fact]
    public void Parse_NullWrongHeaderOrMissingBody_IsRejected()
    {
        using var wrong = new SecsMessage(16, 1);
        using var missing = new SecsMessage(16, 15);
        Action parseNull = () => S16F15_parser.Parse(null!);
        Action parseWrong = () => S16F15_parser.Parse(wrong);
        Action parseMissing = () => S16F15_parser.Parse(missing);

        parseNull.Should().Throw<ArgumentNullException>();
        parseWrong.Should().Throw<ArgumentException>();
        parseMissing.Should().Throw<InvalidOperationException>().WithMessage("*ROOT*");
    }

    /// <summary>
    /// 创建结构完整且带参数、暂停事件的 PJ，以覆盖六个固定字段。
    /// </summary>
    private static Item BuildJob(string id, bool autoStart)
    {
        return L(A(id), B(13), L(L(A("CARRIER1"), L(U1(1), U4(2)))),
            L(U1(1), A("RECIPE1"), L(L(A("parameter"), F8(1.25)))),
            Boolean(autoStart), L(U4(100)));
    }

    /// <summary>
    /// 为结构错误测试提供一个完整根列表。
    /// </summary>
    private static Item BuildRoot() => L(U4(1), L(BuildJob("PJ1", true)));

    /// <summary>
    /// 在有效报文的指定字段注入无效项，并携带预期异常路径。
    /// </summary>
    private static object[] Case(Item replacement, string path, params int[] indices)
    {
        var root = BuildRoot();
        Replace(root, replacement, indices);
        return new object[] { root, path };
    }

    /// <summary>
    /// 替换前释放原字段，避免测试数据的嵌套 Item 丢失释放机会。
    /// </summary>
    private static void Replace(Item root, Item replacement, params int[] indices)
    {
        var parent = root;
        // 仅定位父列表，最后一项由当前用例替换。
        for (var index = 0; index < indices.Length - 1; index++)
            parent = parent[indices[index]];
        parent[indices[^1]].Dispose();
        parent[indices[^1]] = replacement;
    }

    /// <summary>
    /// 按兼容编码构建一个合法无符号整数标量。
    /// </summary>
    private static Item Scalar(SecsFormat format) => format switch
    {
        SecsFormat.U1 => U1(7),
        SecsFormat.U2 => U2(7),
        SecsFormat.U4 => U4(7),
        _ => B(7)
    };

}
