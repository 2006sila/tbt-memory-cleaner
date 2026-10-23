using System.Text.Json;

namespace TbtMemoryCleaner.Core;

/// <summary>插件设置的强类型视图。页面与工作进程共用同一套解析规则。</summary>
public sealed record MemoryCleanerSettings(MemoryArea Areas, bool AutoEnabled, double Threshold, double IntervalSeconds, bool Notify,
    double StandbyThresholdGb)
{
    public static MemoryCleanerSettings Default { get; } = new(MemoryArea.Default, false, 85d, 600d, true, 4d);

    public static MemoryCleanerSettings From(IReadOnlyDictionary<string, JsonElement>? values)
    {
        var areas = MemoryArea.Default;
        var auto = false;
        var threshold = 85d;
        var interval = 600d;
        var notify = true;
        var standbyThreshold = 4d;

        if (values is not null)
        {
            if (values.TryGetValue("areas", out var area) && area.ValueKind == JsonValueKind.Number && area.TryGetDouble(out var areaValue))
                areas = (MemoryArea)(uint)Math.Clamp((int)Math.Round(areaValue), 0, 127);

            if (values.TryGetValue("auto-enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                auto = enabled.GetBoolean();

            if (values.TryGetValue("auto-threshold", out var thresholdValue) && thresholdValue.ValueKind == JsonValueKind.Number &&
                thresholdValue.TryGetDouble(out var thresholdNumber))
                threshold = Math.Clamp(thresholdNumber, 50, 95);

            if (values.TryGetValue("auto-interval", out var intervalValue) && intervalValue.ValueKind == JsonValueKind.Number &&
                intervalValue.TryGetDouble(out var intervalNumber))
                interval = Math.Clamp(intervalNumber, 60, 86400);

            if (values.TryGetValue("notify", out var notifyValue) && notifyValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                notify = notifyValue.GetBoolean();

            if (values.TryGetValue("auto-standby-gb", out var standbyValue) && standbyValue.ValueKind == JsonValueKind.Number &&
                standbyValue.TryGetDouble(out var standbyNumber))
                standbyThreshold = Math.Clamp(standbyNumber, 0, 64);
        }

        return new MemoryCleanerSettings(areas, auto, threshold, interval, notify, standbyThreshold);
    }
}
