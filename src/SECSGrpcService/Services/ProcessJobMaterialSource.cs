using System;
using System.Collections.Generic;
using System.Linq;
using Secs4Net;
using SECShandler.Interfaces;

namespace SECSGrpcService.Services;

/// <summary>
/// 从独立类型化 VID 缓存及旧字符串缓存读取 LP1/LP2 物料，不把 Carrier 到位当成 Mapping 完成。
/// </summary>
public sealed class ProcessJobMaterialSource : IProcessJobMaterialSource, IProcessJobMaterialObserver
{
    private readonly VidMap _map;
    private readonly IVidValueStorage _values;
    private readonly ITypedVidValueStorage _typed;
    private readonly IPortContextStorage _ports;
    private readonly object _sync = new();
    private readonly Dictionary<int, ProcessJobPortSnapshot> _reported = new();

    /// <summary>
    /// 注入 VID 名称映射、两种运行值缓存及可选 LOT 上下文。
    /// </summary>
    public ProcessJobMaterialSource(VidMap map, IVidValueStorage values, ITypedVidValueStorage typed, IPortContextStorage ports)
    {
        _map = map;
        _values = values;
        _typed = typed;
        _ports = ports;
    }

    /// <summary>
    /// 返回已成功报告的物料快照；重启后等待新事件，不把旧缓存视为物料就绪证据。
    /// </summary>
    public IReadOnlyList<ProcessJobPortSnapshot> GetPorts()
    {
        lock (_sync)
        {
            return _reported.Values.Select(port => port with { MapSlotList = port.MapSlotList.ToArray() }).ToList();
        }
    }

    /// <summary>
    /// 入站物料字段更新前清除就绪证据，即使本次报告失败也不能继续使用旧 Mapping 启动。
    /// </summary>
    public void InvalidateReportedValues(IReadOnlyCollection<string> parameterNames)
    {
        var names = parameterNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            foreach (var index in _reported.Keys.ToArray())
            {
                if (names.Contains($"LP{index}CarrierID") || names.Contains($"LP{index}MapSlotList"))
                    _reported[index] = _reported[index] with { MapSlotList = Array.Empty<byte>() };
            }
        }
    }

    /// <summary>
    /// 成功报告 Carrier 事件时清除旧 Mapping；只有后续或同次成功 Mapping 报告才能重新就绪。
    /// </summary>
    public void ConfirmReportedValues(IReadOnlyCollection<string> parameterNames, IReadOnlyCollection<string> typedParameterNames)
    {
        var strings = parameterNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var typed = typedParameterNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            for (var index = 1; index <= 2; index++)
            {
                var carrierName = $"LP{index}CarrierID";
                var mapName = $"LP{index}MapSlotList";
                var carrierChanged = strings.Contains(carrierName) || typed.Contains(carrierName);
                var mappingChanged = strings.Contains(mapName) || typed.Contains(mapName);
                if (!carrierChanged && !mappingChanged)
                    continue;
                var portId = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _reported.TryGetValue(index, out var previous);
                var carrierId = carrierChanged ? ReadCarrierId(carrierName, typed.Contains(carrierName)).Trim() : previous?.CarrierId ?? string.Empty;
                // Carrier 字段再次报告也保守清除 Mapping，避免同名载具重新入站后复用上次结果。
                var mapping = mappingChanged ? ReadMapping(mapName, typed.Contains(mapName))
                    : carrierChanged ? Array.Empty<byte>() : previous?.MapSlotList ?? Array.Empty<byte>();
                var lotId = _ports.TryGetByPortId(portId, out var context) ? context.LotId : string.Empty;
                _reported[index] = new ProcessJobPortSnapshot(portId, carrierId, mapping, lotId);
            }
        }
    }

    /// <summary>
    /// 按本次报告来源读取 CarrierID，不使用其他来源的过期值或 CSV 默认值。
    /// </summary>
    private string ReadCarrierId(string name, bool typed)
    {
        if (typed)
        {
            if (_map.TryGetVid(name, out var vid) && _typed.TryGetByVid(vid, out var item))
            {
                using (item)
                    return item.Format == SecsFormat.ASCII ? item.GetString() : string.Empty;
            }
            return string.Empty;
        }
        return _values.TryGetByName(name, out var value) ? value : string.Empty;
    }

    /// <summary>
    /// 读取 List、无符号数组或旧字符串 Mapping；无效类型和数值不回退到可能过期的旧值。
    /// </summary>
    private IReadOnlyList<byte> ReadMapping(string name, bool typed)
    {
        if (typed && _map.TryGetVid(name, out var vid) && _typed.TryGetByVid(vid, out var item))
        {
            using (item)
            {
                // List 中每项必须是一个 0/1/2 标量；数组写法保持同一槽位顺序。
                if (item.Format == SecsFormat.List)
                {
                    var values = new List<byte>();
                    foreach (var child in item.Items)
                    {
                        if (child.Count != 1)
                            return Array.Empty<byte>();
                        var scalar = child.Format switch
                        {
                            SecsFormat.U1 or SecsFormat.Binary => (uint)child.FirstValue<byte>(),
                            SecsFormat.U2 => child.FirstValue<ushort>(),
                            SecsFormat.U4 => child.FirstValue<uint>(),
                            _ => uint.MaxValue
                        };
                        if (scalar > 2)
                            return Array.Empty<byte>();
                        values.Add((byte)scalar);
                    }
                    return values;
                }
                return item.Format switch
                {
                    SecsFormat.ASCII => ParseMapping(item.GetString()),
                    SecsFormat.U1 or SecsFormat.Binary => item.GetMemory<byte>().ToArray(),
                    _ => Array.Empty<byte>()
                };
            }
        }
        return !typed && _values.TryGetByName(name, out var text) ? ParseMapping(text) : Array.Empty<byte>();
    }

    /// <summary>
    /// 兼容 25 位连续数字或逗号分隔的状态值，不能将槽位编号列表误当 Mapping 状态。
    /// </summary>
    private static IReadOnlyList<byte> ParseMapping(string text)
    {
        var raw = text.Trim();
        var tokens = raw.Contains(',') ? raw.Split(',', StringSplitOptions.TrimEntries) : raw.Select(value => value.ToString()).ToArray();
        if (tokens.Length != 25)
            return Array.Empty<byte>();
        var values = new List<byte>();
        foreach (var token in tokens)
        {
            if (!byte.TryParse(token, out var value) || value > 2)
                return Array.Empty<byte>();
            values.Add(value);
        }
        return values;
    }
}
