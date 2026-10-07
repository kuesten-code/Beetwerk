using Kuestencode.Beetwerk.Domain.Devices;

namespace Kuestencode.Beetwerk.Tests.Devices;

/// <summary>Gerät zum Anfassen für API-Tests: Status lässt sich setzen, Befehle werden mitgeschrieben.</summary>
public sealed class FakeDeviceProvider : IDeviceProvider
{
    public string Key => "fake";
    public string DisplayName => "Testgerät";
    public bool IsConfigured => true;
    public TimeSpan PollInterval => TimeSpan.FromMinutes(5);
    public IReadOnlySet<DeviceCommand> SupportedCommands { get; } = new HashSet<DeviceCommand> { DeviceCommand.Start, DeviceCommand.Pause };

    public DeviceStatus Status { get; set; } = Ok();
    public List<(string Device, DeviceCommand Command, int? Duration)> Commands { get; } = [];
    public List<int> CuttingHeights { get; } = [];
    public int StatusCalls { get; private set; }

    public static DeviceStatus Ok() => new()
    {
        ExternalId = "mower-1", Name = "Robi", Activity = DeviceActivity.Charging, BatteryPercent = 90, CuttingHeight = 4, UpdatedAt = DateTimeOffset.UnixEpoch
    };

    public static DeviceStatus Broken() => Ok() with { Activity = DeviceActivity.Error, ErrorCode = 15, ErrorText = "Angehoben (Code 15)" };

    public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo("mower-1", "Robi", "Testmodell")]);

    public Task<DeviceStatus?> GetStatusAsync(DeviceRef device, CancellationToken ct)
    {
        StatusCalls++;
        return Task.FromResult<DeviceStatus?>(device.ExternalId == Status.ExternalId ? Status : null);
    }

    public Task SendCommandAsync(DeviceRef device, DeviceCommand command, int? durationMinutes, CancellationToken ct)
    {
        Commands.Add((device.ExternalId, command, durationMinutes));
        return Task.CompletedTask;
    }

    public Task SetCuttingHeightAsync(DeviceRef device, int height, CancellationToken ct)
    {
        CuttingHeights.Add(height);
        return Task.CompletedTask;
    }
}
