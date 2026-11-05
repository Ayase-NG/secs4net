using System;
using FluentAssertions;
using SECSparser;
using Xunit;

namespace Secs4Net.UnitTests;

public class S14F9ParserUnitTests
{
    /// <summary>
    /// 第三项中的多个属性对必须汇总为同一个 ControlJob，而不是创建多个作业。
    /// </summary>
    [Fact]
    public void Parse_AttributePairs_CreateOneObject()
    {
        using var request = new SecsMessage(14, 9)
        {
            SecsItem = L(A("Equipment"), A("ControlJob"), L(
                L(A("ObjID"), A("CJ001")),
                L(A("ProcessingCtrlSpec"), L(A("PJ001"), A("PJ002"))),
                L(A("CarrierInputSpec"), L(A("CARRIER001"), A("CARRIER002"))),
                L(A("StartMethod"), Boolean(true))))
        };

        var data = S14F9_parser.Parse(request);

        data.OBJSPEC.Should().Be("Equipment");
        data.OBJTYPE.Should().Be("ControlJob");
        data.ObjectCount.Should().Be(1);
        var job = data.ControlJobs.Should().ContainSingle().Subject;
        job.ObjID.Should().Be("CJ001");
        job.ProcessingCtrlSpec.Should().Equal("PJ001", "PJ002");
        job.CarrierInputSpec.Should().Equal("CARRIER001", "CARRIER002");
        job.StartMethod.Should().BeTrue();
    }

    /// <summary>
    /// 属性顺序不影响对象归属；未知属性的数据可以是任意嵌套列表。
    /// </summary>
    [Fact]
    public void Parse_ReorderedAttributes_KeepsAttributeDataInsideOneObject()
    {
        using var request = new SecsMessage(14, 9)
        {
            SecsItem = L(A(""), A("ControlJob"), L(
                L(A("VendorAttribute"), L(L(U4(123)), A("nested"))),
                L(A("StartMethod"), Boolean(false)),
                L(A("ObjID"), A("CJ002"))))
        };

        var job = S14F9_parser.Parse(request).ControlJobs.Should().ContainSingle().Subject;

        job.ObjID.Should().Be("CJ002");
        job.StartMethod.Should().BeFalse();
    }

    /// <summary>
    /// 仅一个属性时仍解析为一个对象，不能把属性名和值再次当成属性列表遍历。
    /// </summary>
    [Fact]
    public void Parse_SingleAttribute_ReadsObjectId()
    {
        using var request = new SecsMessage(14, 9)
        {
            SecsItem = L(A(""), A("ControlJob"), L(L(A("ObjID"), A("CJ003"))))
        };

        var job = S14F9_parser.Parse(request).ControlJobs.Should().ContainSingle().Subject;

        job.ObjID.Should().Be("CJ003");
        job.StartMethod.Should().BeNull();
    }

    /// <summary>
    /// 拒绝缺失属性列表、根列表项数错误、属性不是二元组及额外的对象列表层级。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Parse_MalformedStructure_IsRejected(int variant)
    {
        using var request = new SecsMessage(14, 9)
        {
            SecsItem = variant switch
            {
                0 => L(A(""), A("ControlJob")),
                1 => L(A(""), A("ControlJob"), A("not a list")),
                2 => L(A(""), A("ControlJob"), L(), A("extra")),
                3 => L(A(""), A("ControlJob"), L(A("not a pair"))),
                4 => L(A(""), A("ControlJob"), L(L(A("ObjID")))),
                5 => L(A(""), A("ControlJob"), L(L(A("ObjID"), A("CJ"), A("extra")))),
                _ => L(A(""), A("ControlJob"), L(L(
                    L(A("ObjID"), A("CJ")),
                    L(A("StartMethod"), Boolean(true)))))
            }
        };

        Action parse = () => S14F9_parser.Parse(request);

        parse.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// 查询消息或空引用不能作为对象创建请求解析。
    /// </summary>
    [Fact]
    public void Parse_NullOrWrongMessage_IsRejected()
    {
        using var request = new SecsMessage(14, 1);
        Action parseNull = () => S14F9_parser.Parse(null!);
        Action parseWrongMessage = () => S14F9_parser.Parse(request);

        parseNull.Should().Throw<ArgumentNullException>();
        parseWrongMessage.Should().Throw<ArgumentException>();
    }
}
