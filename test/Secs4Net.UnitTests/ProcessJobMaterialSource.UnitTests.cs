using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using SECShandler.Interfaces;
using SECShandler.Storage;
using SECSGrpcService.Services;
using Xunit;

namespace Secs4Net.UnitTests;

public class ProcessJobMaterialSourceUnitTests
{
    /// <summary>
    /// 成功报告 Carrier 及 Mapping 后才确认物料就绪，后续换盒必须重新报告 Mapping。
    /// </summary>
    [Fact]
    public void Confirm_CarrierChanges_InvalidatesPreviousMapping()
    {
        var fixture = new Fixture();
        fixture.Values["LP1CarrierID"] = "C1";
        fixture.Values["LP1MapSlotList"] = new string('1', 25);
        fixture.Source.GetPorts().Should().BeEmpty();

        fixture.Source.ConfirmReportedValues(new[] { "LP1CarrierID", "LP1MapSlotList" }, Array.Empty<string>());
        fixture.Source.GetPorts()[0].MapSlotList.Should().HaveCount(25);

        fixture.Values["LP1CarrierID"] = "C2";
        fixture.Source.ConfirmReportedValues(new[] { "LP1CarrierID" }, Array.Empty<string>());
        fixture.Source.GetPorts()[0].CarrierId.Should().Be("C2");
        fixture.Source.GetPorts()[0].MapSlotList.Should().BeEmpty();

        fixture.Source.ConfirmReportedValues(new[] { "LP1MapSlotList" }, Array.Empty<string>());
        fixture.Source.GetPorts()[0].MapSlotList.Should().HaveCount(25);
    }

    /// <summary>
    /// 入站物料事件即使未成功报告，也必须撤销旧 Mapping 就绪证据。
    /// </summary>
    [Fact]
    public void Invalidate_UnconfirmedUpdate_CannotReuseOldMapping()
    {
        var fixture = new Fixture();
        fixture.Values["LP1CarrierID"] = "C1";
        fixture.Values["LP1MapSlotList"] = new string('1', 25);
        fixture.Source.ConfirmReportedValues(fixture.Values.Keys.ToArray(), Array.Empty<string>());

        fixture.Source.InvalidateReportedValues(new[] { "LP1CarrierID" });

        fixture.Source.GetPorts()[0].MapSlotList.Should().BeEmpty();
    }

    /// <summary>
    /// 本次字符串报告不能被旧类型化值覆盖，类型化报告也必须读取实际类型化 Mapping。
    /// </summary>
    [Fact]
    public void Confirm_MixedSources_UsesCurrentReportSource()
    {
        var fixture = new Fixture();
        using var carrier = A("OLD");
        using var mapping = L(Enumerable.Range(1, 25).Select(_ => U1(1)).ToArray());
        fixture.Typed.UpsertByVid(1006, carrier);
        fixture.Typed.UpsertByVid(1014, mapping);
        fixture.Values["LP1CarrierID"] = "NEW";
        fixture.Values["LP1MapSlotList"] = string.Join(',', Enumerable.Repeat(0, 25));
        fixture.Source.ConfirmReportedValues(new[] { "LP1CarrierID", "LP1MapSlotList" }, Array.Empty<string>());
        fixture.Source.GetPorts()[0].CarrierId.Should().Be("NEW");
        fixture.Source.GetPorts()[0].MapSlotList.Should().OnlyContain(value => value == 0);

        fixture.Source.ConfirmReportedValues(Array.Empty<string>(), new[] { "LP1CarrierID", "LP1MapSlotList" });
        fixture.Source.GetPorts()[0].CarrierId.Should().Be("OLD");
        fixture.Source.GetPorts()[0].MapSlotList.Should().OnlyContain(value => value == 1);
    }

    /// <summary>
    /// 故障状态保留供执行入口拒绝；返回值不能通过修改数组污染后续快照。
    /// </summary>
    [Fact]
    public void GetPorts_ReturnsIndependentMappingCopies()
    {
        var fixture = new Fixture();
        fixture.Values["LP1CarrierID"] = "C1";
        fixture.Values["LP1MapSlotList"] = "2" + new string('1', 24);
        fixture.Source.ConfirmReportedValues(fixture.Values.Keys.ToArray(), Array.Empty<string>());
        var map = (byte[])fixture.Source.GetPorts()[0].MapSlotList;
        map[0] = 0;
        fixture.Source.GetPorts()[0].MapSlotList[0].Should().Be(2);
    }

    /// <summary>
    /// 项目默认无表头六列文件必须以第三列作为名称，而不是把 SVID 当名称。
    /// </summary>
    [Fact]
    public void VidMap_HeaderlessSixColumns_ResolvesMaterialNames()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "1006,SVID,LP1CarrierID,A,,Carrier\n1014,SVID,LP1MapSlotList,List,,Mapping");
            var map = VidMap.LoadFromCsv(path);
            map.TryGetVid("LP1CarrierID", out var carrier).Should().BeTrue();
            map.TryGetVid("LP1MapSlotList", out var mapping).Should().BeTrue();
            carrier.Should().Be(1006);
            mapping.Should().Be(1014);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 使用独立运行值字典及真实类型化缓存验证物料适配器，不读取进程全局状态。
    /// </summary>
    private sealed class Fixture
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public TypedVidValueStorage Typed { get; } = new();
        public ProcessJobMaterialSource Source { get; }

        /// <summary>
        /// 设置按名称读取的字符串替身和实际 VID 名称映射。
        /// </summary>
        public Fixture()
        {
            var values = Substitute.For<IVidValueStorage>();
            values.TryGetByName(Arg.Any<string>(), out Arg.Any<string>()).Returns(call =>
            {
                var found = Values.TryGetValue(call.ArgAt<string>(0), out var value);
                call[1] = value ?? string.Empty;
                return found;
            });
            Source = new ProcessJobMaterialSource(new VidMap(new Dictionary<string, ushort>
            {
                ["LP1CarrierID"] = 1006, ["LP1MapSlotList"] = 1014,
                ["LP2CarrierID"] = 1007, ["LP2MapSlotList"] = 1015
            }), values, Typed, Substitute.For<IPortContextStorage>());
        }
    }
}
