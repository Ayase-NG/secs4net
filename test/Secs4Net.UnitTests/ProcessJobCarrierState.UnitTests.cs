using System.Text.Json;
using FluentAssertions;
using SECShandler.Interfaces;
using Xunit;

namespace Secs4Net.UnitTests;

public class ProcessJobCarrierStateUnitTests
{
    /// <summary>
    /// 新 Carrier 及未包含状态字段的旧计划均应进入等待状态。
    /// </summary>
    [Fact]
    public void NewOrLegacyCarrier_DefaultsToWaiting()
    {
        var carrier = new ProcessJobCarrierPlan();
        var legacy = JsonSerializer.Deserialize<ProcessJobPlan>(
            """{"PJID":"PJ1","Carriers":[{"CarrierId":"CARRIER1","Slots":[1,25]}]}""");

        carrier.State.Should().Be(ProcessJobCarrierState.Waiting);
        legacy!.Carriers[0].State.Should().Be(ProcessJobCarrierState.Waiting);
        legacy.Carriers[0].Slots.Should().Equal(1u, 25u);
    }

    /// <summary>
    /// 每个 Carrier 的独立状态及完整定义应能随 PJ 快照往返序列化。
    /// </summary>
    [Theory]
    [InlineData(ProcessJobCarrierState.Waiting)]
    [InlineData(ProcessJobCarrierState.Ready)]
    [InlineData(ProcessJobCarrierState.Processing)]
    [InlineData(ProcessJobCarrierState.Completed)]
    [InlineData(ProcessJobCarrierState.Cancelled)]
    [InlineData(ProcessJobCarrierState.Aborted)]
    public void SerializePlan_PreservesIndependentCarrierStates(ProcessJobCarrierState state)
    {
        var plan = new ProcessJobPlan { PJID = "PJ1" };
        plan.Carriers.Add(new ProcessJobCarrierPlan { CarrierId = "CARRIER1", Slots = new() { 1, 25 }, State = state });
        plan.Carriers.Add(new ProcessJobCarrierPlan { CarrierId = "CARRIER2", Slots = new() { 2 } });

        var restored = JsonSerializer.Deserialize<ProcessJobPlan>(JsonSerializer.Serialize(plan));

        restored!.Carriers.Should().HaveCount(2);
        restored.Carriers[0].State.Should().Be(state);
        restored.Carriers[0].CarrierId.Should().Be("CARRIER1");
        restored.Carriers[0].Slots.Should().Equal(1u, 25u);
        restored.Carriers[1].State.Should().Be(ProcessJobCarrierState.Waiting);
    }
}
