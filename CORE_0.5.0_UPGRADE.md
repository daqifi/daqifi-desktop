# Daqifi.Core 0.5.0 Upgrade Notes

## Overview

This upgrade brings `Daqifi.Core` from version 0.4.1 to 0.5.0, which includes **Phase 4: Device Discovery Framework** implementation.

## What's New in Core 0.5.0

### Device Discovery Framework (Phase 4)

Core 0.5.0 introduces a complete device discovery system that can be optionally adopted by desktop:

#### New Interfaces & Classes

- **`IDeviceFinder`** - Base interface for device discovery
  - `DiscoverAsync(CancellationToken)` - Async discovery with cancellation support
  - `DiscoverAsync(TimeSpan timeout)` - Async discovery with timeout
  - `DeviceDiscovered` event - Fires when each device is found
  - `DiscoveryCompleted` event - Fires when discovery finishes

- **`IDeviceInfo`** - Device metadata interface
  - Properties: `Name`, `SerialNumber`, `FirmwareVersion`, `IPAddress`, `MacAddress`, `Port`, `Type`, `IsPowerOn`, `ConnectionType`, `PortName`, `DevicePath`

- **`WiFiDeviceFinder`** - UDP broadcast discovery (port 30303)
  - Network interface enumeration
  - Protobuf message parsing
  - Duplicate device detection
  - Async/await pattern

- **`SerialDeviceFinder`** - USB/Serial port enumeration
  - Discovers all available serial ports
  - Configurable baud rate (default: 115200)

- **`HidDeviceFinder`** - HID bootloader mode discovery (stub implementation)
  - VendorId: 0x4D8, ProductId: 0x03C
  - Full implementation pending HID library

### DeviceType Enum Update

**BREAKING CHANGE (But Already Compatible)**

Core 0.5.0 updated the `DeviceType` enum to match desktop's implementation:

```csharp
public enum DeviceType
{
    Unknown,    // 0
    Nyquist1,   // 1  (was "Daqifi" in 0.4.1)
    Nyquist3    // 2  (was "Nyquist" in 0.4.1)
}
```

**Good news**: Desktop already uses `Nyquist1` and `Nyquist3`, so **no code changes required**.

### Connection Types

```csharp
public enum ConnectionType
{
    Unknown,
    WiFi,
    Serial,
    Hid
}
```

## Compatibility

### ✅ No Breaking Changes for Desktop

- Desktop's `DeviceType` enum already matches Core 0.5.0
- All existing code continues to work
- No code changes required for this upgrade

### 📦 What's Included

- **Phase 1-2**: Foundation and message system (unchanged)
- **Phase 3**: Connection management - TCP/Serial/UDP transports
- **Phase 4**: Device discovery framework (NEW)

### ⏳ What's Not Included (Future Phases)

- **Phase 5**: Channel Management
- **Phase 6**: Protocol Implementation
- **Phase 7**: Advanced Features (Network config, SD card, firmware updates)
- **Phase 8**: Full desktop integration

## Optional: Using Core Device Discovery

Desktop can optionally start using Core's device discovery, but **this is not required**. The current desktop device finders work fine and full integration is planned for a future phase.

### Example: Using Core WiFi Discovery

```csharp
using Daqifi.Core.Device.Discovery;

// Create WiFi device finder
using var wifiFinder = new WiFiDeviceFinder(port: 30303);

// Subscribe to discovery events
wifiFinder.DeviceDiscovered += (sender, e) =>
{
    var deviceInfo = e.DeviceInfo;

    // Convert IDeviceInfo to desktop's IDevice if needed
    var desktopDeviceInfo = new Daqifi.Desktop.DataModel.Device.DeviceInfo
    {
        DeviceName = deviceInfo.Name,
        IpAddress = deviceInfo.IPAddress?.ToString(),
        MacAddress = deviceInfo.MacAddress,
        Port = (uint)(deviceInfo.Port ?? 0),
        IsPowerOn = deviceInfo.IsPowerOn,
        DeviceSerialNo = deviceInfo.SerialNumber,
        DeviceVersion = deviceInfo.FirmwareVersion
    };

    // Create desktop device from info...
};

// Discover devices with 5-second timeout
var devices = await wifiFinder.DiscoverAsync(TimeSpan.FromSeconds(5));
```

## Testing

All 215 core tests passing on .NET 8.0 and .NET 9.0.

Desktop build successful with Core 0.5.0 (806 warnings, 0 errors related to core).

## Migration Path

Per the [DAQiFi Core Migration Plan](DAQIFI_CORE_MIGRATION_PLAN.md):

1. **Phase 4 (Current)**: Device discovery available in core
2. **Future**: Full desktop integration planned for Phase 8
3. **Recommendation**: Continue using desktop device finders until Phase 8

## References

- Core 0.5.0 Release: https://www.nuget.org/packages/Daqifi.Core/0.5.0
- Core Phase 4 PR: https://github.com/daqifi/daqifi-core/pull/54
- Migration Plan: [DAQIFI_CORE_MIGRATION_PLAN.md](DAQIFI_CORE_MIGRATION_PLAN.md)
