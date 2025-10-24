# DAQiFi Core Migration Plan

## Overview
This document outlines the iterative migration plan to gradually move functionality from `daqifi-desktop` to `daqifi-core`, creating a clean separation between core device communication logic and desktop-specific UI/application concerns.

## Current State (Updated 2025-10-16)

**Desktop (daqifi-desktop)**:
- Production-ready WPF application with 80% test coverage
- Comprehensive device communication (WiFi, Serial, USB)
- 1,073-line `AbstractStreamingDevice` with all device logic
- UDP discovery, channel management, data streaming, SD card, firmware updates
- Currently uses `Daqifi.Core 0.4.1`

**Core (daqifi-core v0.4.1)**:
- ✅ **Phase 1-2 Complete**: Foundation and message system (59/59 tests passing)
- ✅ Clean interfaces (`IDevice`, `IStreamingDevice`, `IMessageProducer`, `IMessageConsumer`)
- ✅ Transport abstractions (TCP, Serial - UDP needed)
- ✅ SCPI command producer (45+ commands)
- ✅ Protocol Buffer support
- ❌ **Missing**: Device discovery, channels, streaming pipeline, advanced features (Phases 3-7)

**Integration Status**:
- ⚠️ **CoreDeviceAdapter (v0.4.1) NOT production-ready** - See Issue #39
- Integration attempted prematurely before core functionality complete
- Resulted in code bloat (143 lines added, 0 removed) instead of drop-in replacement
- **Recommendation**: Focus on Phases 3-7 before attempting integration

**Phase Progress**:
- ✅ Phase 1: Foundation (Complete)
- ✅ Phase 2: Message System (Complete)
- 🔄 Phase 3: Connection Management (Partial - TCP/Serial done, UDP needed)
- ⏳ Phase 4: Device Discovery (Not started)
- ⏳ Phase 5: Channel Management (Not started - Critical gap)
- ⏳ Phase 6: Protocol Implementation (Not started)
- ⏳ Phase 7: Advanced Features (Not started)
- 🚫 Phase 8: Desktop Integration (Deferred until 3-7 complete)

## Migration Principles
1. **Iterative Approach**: Each phase should be deployable and testable
2. **No Breaking Changes**: Preserve all existing desktop functionality
3. **Clean Architecture**: Core handles device communication, desktop handles UI/application logic
4. **Backwards Compatibility**: Maintain existing APIs during transition
5. **Gradual Adoption**: Use adapter patterns to bridge interfaces during migration

## Phase 1: Foundation Update ✅ (Current)
**Goal**: Upgrade to Core 0.3.0 and validate compatibility

- [x] Upgrade `Daqifi.Core` from 0.2.1 to 0.3.0
- [x] Ensure no breaking changes in desktop application
- [x] Validate all existing functionality works

**Deliverable**: Desktop app running on Core 0.3.0

## Phase 2: Message System Migration ✅ (Core 0.4.0)
**Goal**: Migrate to core's message system while preserving desktop functionality

**Status**: Complete - All tests passing (59/59)

### 2.1 Message Interface Alignment ✅
- [x] Extend core `IOutboundMessage<T>` to support desktop's message patterns
- [x] Create adapters for existing desktop message producers/consumers
- [x] Migrate `ScpiMessageProducer` usage to core implementation
- [x] Generic `MessageProducer<T>` with background threading
- [x] Thread-safe message queuing with `ConcurrentQueue<T>`

### 2.2 Message Consumer ✅
- [x] `IMessageConsumer<T>` interface with lifecycle management
- [x] `StreamMessageConsumer<T>` with background thread processing
- [x] Pluggable message parsers (`IMessageParser<T>`)
- [x] Line-based parser for SCPI/text responses
- [x] Protobuf parser for binary messages
- [x] Composite parser for multiple formats

### Success Criteria
- [x] Core `MessageProducer<T>` functionally equivalent to desktop's implementation
- [x] Thread-safe background message processing
- [x] Cross-platform compatibility (no Windows-specific dependencies)
- [x] Backward compatibility maintained
- [x] 80%+ test coverage

**Deliverable**: Core 0.4.0 with enhanced messaging ✅

**GitHub Issues**: [Closed #32](https://github.com/daqifi/daqifi-core/issues/32)

## Phase 3: Connection Management (Core 0.5.0)
**Goal**: Move connection lifecycle management to core

**Status**: Partial - TCP/Serial transports exist, UDP and advanced features needed

### 3.1 Transport Layer Abstraction
- [x] Create `IStreamTransport` base interface
- [x] Implement `TcpStreamTransport` for TCP connections
- [x] Implement `SerialStreamTransport` for serial/USB connections
- [ ] Create `IUdpTransport` interface for UDP communication
- [ ] Implement `UdpStreamTransport` for broadcast/unicast
- [ ] Add WiFi-specific connection logic (buffer clearing, device ports)
- [ ] Connection retry logic with configurable timeouts
- [ ] Connection pooling for multiple simultaneous devices

### 3.2 Connection State Management
- [x] `ConnectionStatus` enum (Disconnected, Connecting, Connected)
- [x] Event-driven status change notifications
- [ ] Connection retry logic with exponential backoff
- [ ] Connection timeout configuration per transport type
- [ ] Thread-safe connection state transitions
- [ ] Proper resource cleanup on connection failures

### 3.3 Desktop-Specific Features
- [ ] WiFi device buffer clearing (`ClearBuffer()`)
- [ ] Device-specific configuration (`IsWifiDevice` flag)
- [ ] Safe shutdown patterns (`StopSafely()` ensuring queue is empty)
- [ ] DTR control for serial devices (power management)

### Success Criteria
- [ ] UDP transport with broadcast discovery support
- [ ] Connection retry with configurable attempts and delays
- [ ] Connection pooling/management for multiple devices
- [ ] Platform-specific serial port handling
- [ ] WiFi-specific buffer clearing capability
- [ ] All transport types support async/await patterns
- [ ] 80%+ test coverage for transport layer

### Desktop Migration Impact
Once complete, desktop can:
- Replace `DaqifiStreamingDevice.Connect()` with `TcpStreamTransport` from core
- Replace `SerialStreamingDevice.Connect()` with `SerialStreamTransport` from core
- Remove desktop connection retry logic, use core's implementation

**Deliverable**: Core 0.5.0 with robust connection management

**GitHub Issues**: [#48](https://github.com/daqifi/daqifi-core/issues/48)

## Phase 4: Device Discovery Framework (Core 0.6.0)
**Goal**: Move device discovery logic from desktop to core

**Status**: Not started - Deferred from Phase 2

### 4.1 Device Discovery Interfaces
- [ ] Create `IDeviceFinder` interface for device discovery
- [ ] Create `IDeviceInfo` interface/class for discovered device metadata
- [ ] Support for multiple discovery mechanisms (WiFi, Serial, USB HID)
- [ ] Async discovery with cancellation token support
- [ ] Event-based discovery notifications (device found, discovery complete)

### 4.2 WiFi Device Discovery
- [ ] Implement `WiFiDeviceFinder` using UDP broadcast
- [ ] UDP broadcast on port 30303 with "DAQiFi?\r\n" query
- [ ] Parse protobuf responses (IP, MAC, port, hostname, serial, firmware)
- [ ] Network interface enumeration and selection
- [ ] Timeout and retry configuration

### 4.3 Serial Device Discovery
- [ ] Implement `SerialDeviceFinder` for USB/Serial enumeration
- [ ] Serial port scanning with device info queries
- [ ] `TryGetDeviceInfo()` pattern with quick connection attempts
- [ ] Configurable baud rates and DTR control

### 4.4 USB HID Device Discovery
- [ ] Implement `HidDeviceFinder` for bootloader mode devices
- [ ] HID device enumeration (VendorId: 0x4D8, ProductId: 0x03C)
- [ ] Device mode detection (normal vs bootloader)

### 4.5 Device Info Extraction
- [ ] Parse device metadata from protobuf messages
- [ ] Extract IP, MAC, TCP port, hostname, serial, firmware version
- [ ] Device type detection from part numbers
- [ ] Power status information

### Success Criteria
- [ ] `IDeviceFinder` interface with async discovery
- [ ] WiFi discovery finds devices on same subnet within 5 seconds
- [ ] Serial discovery enumerates all available ports
- [ ] HID discovery identifies bootloader devices
- [ ] Device info parsing extracts all relevant metadata
- [ ] Event-based notifications during discovery
- [ ] Cancellation token support
- [ ] 80%+ test coverage with mock responses

### Desktop Migration Impact
Once complete, desktop can:
- Replace `DaqifiDeviceFinder` with `WiFiDeviceFinder` from core
- Replace `SerialDeviceFinder` with core implementation
- Replace `HidDeviceFinder` with core implementation
- Remove device enumeration logic from desktop

**Deliverable**: Core 0.6.0 with device discovery framework

**GitHub Issues**: [#49](https://github.com/daqifi/daqifi-core/issues/49)

## Phase 5: Channel Management & Data Streaming ✅ (Core 0.6.0)
**Goal**: Migrate channel configuration and data handling

**Status**: ✅ **Complete in Core 0.6.0** - Desktop Integration in Progress

### 5.1 Channel Abstraction ✅
- [x] Create `IChannel` base interface
- [x] Create `IAnalogChannel` interface for analog inputs
- [x] Create `IDigitalChannel` interface for digital I/O
- [x] Implement `AnalogChannel`, `DigitalChannel` classes
- [x] Channel enable/disable functionality
- [x] Channel configuration (range, resolution, direction)

### 5.2 Data Sample Handling ✅
- [x] Create `IDataSample` interface for data points
- [x] Implement `DataSample` class with timestamp and value
- [x] Thread-safe active sample management
- [x] Sample event notifications

### 5.3 Data Scaling & Calibration ✅
- [x] Calibration parameter support (CalibrationM, CalibrationB)
- [x] Port range configuration
- [x] Resolution-based scaling
- [x] Internal scale factors
- [x] Scaling formula: `(RawValue / Resolution * PortRange * CalibrationM + CalibrationB) * InternalScaleM`
- [x] Desktop keeps expression evaluation as UI-specific feature

### 5.4 Channel Configuration Commands ✅
- [x] ADC channel enable/disable SCPI commands (already in Core 0.5.0)
- [x] Digital I/O direction configuration (already in Core 0.5.0)
- [x] Output value setting (already in Core 0.5.0)

### 5.5 Desktop Integration (This PR)
- [x] Upgrade Daqifi.Core from 0.5.0 to 0.6.0
- [x] Upgrade Google.Protobuf from 3.32.1 to 3.33.0
- [x] Upgrade System.IO.Ports from 9.0.9 to 9.0.10
- [x] Replace desktop `ChannelType` enum with core version (via re-export)
- [x] Replace desktop `ChannelDirection` enum with core version (via re-export)
- [x] Add `Daqifi.Core.IAnalogChannel` to desktop `AnalogChannel` (composition pattern)
- [x] Add `Daqifi.Core.IDigitalChannel` to desktop `DigitalChannel` (composition pattern)
- [x] Desktop channels now use core scaling via `GetScaledValue()`
- [ ] Update `AbstractStreamingDevice` to use core channels for scaling (future PR)
- [ ] Update tests to verify core integration (future PR)

### Success Criteria
- [x] `IChannel` interface provides essential channel capabilities
- [x] Data scaling accuracy matches formula specification
- [x] Thread-safe sample updates for high-frequency streaming
- [x] Channel configuration commands in SCPI producer
- [x] Event-driven sample notifications
- [x] 100% test coverage in core (26/26 tests passing)
- [x] Desktop channels use core for device communication via composition

### Desktop Migration Approach
**Hybrid Composition Pattern** (Recommended and Implemented):
- ✅ Desktop keeps its channel classes for UI/database features
- ✅ Desktop channels internally **compose** core channels for device communication
- ✅ Core handles scaling, calibration, thread-safety
- ✅ Desktop adds WPF bindings, database persistence, color management, expressions
- ✅ Clear separation: Core = device protocol, Desktop = application features

**Files Modified**:
- `Daqifi.Desktop.DataModel/Channel/ChannelType.cs` - Re-exports core enum
- `Daqifi.Desktop.DataModel/Channel/ChannelDirection.cs` - Re-exports core enum
- `Daqifi.Desktop/Channel/AnalogChannel.cs` - Now composes `IAnalogChannel` from core
- `Daqifi.Desktop/Channel/DigitalChannel.cs` - Now composes `IDigitalChannel` from core
- `Daqifi.Desktop.DataModel/Daqifi.Desktop.DataModel.csproj` - Added Core 0.6.0 reference
- `Daqifi.Desktop/Daqifi.Desktop.csproj` - Upgraded to Core 0.6.0, updated dependencies
- `Daqifi.Desktop.IO/Daqifi.Desktop.IO.csproj` - Upgraded to Core 0.6.0, updated Protobuf
- `Daqifi.Desktop.Test/Daqifi.Desktop.Test.csproj` - Updated Protobuf to 3.33.0

**Benefits Achieved**:
✅ Desktop leverages core's thread-safe, tested channel implementations
✅ Scaling logic centralized in core (no duplication)
✅ External developers can use same channel types
✅ Desktop keeps rich UI features (colors, expressions, MVVM)
✅ Clear architecture boundary maintained

**Deliverable**: Core 0.6.0 integrated into Desktop ✅

**GitHub Issues**:
- Core: [#50](https://github.com/daqifi/daqifi-core/issues/50) - Closed
- Core PR: [#57](https://github.com/daqifi/daqifi-core/pull/57) - Merged
- Desktop PR: TBD (this integration)

## Phase 6: Protocol Implementation & Device Logic (Core 0.8.0)
**Goal**: Move protocol-specific communication to core

**Status**: Not started - Protocol foundations exist

### 6.1 Protocol Handlers
- [ ] Complete SCPI command handling (expand existing `ScpiMessageProducer`)
- [ ] Protobuf message serialization/deserialization (expand existing support)
- [ ] Protocol-agnostic message routing
- [ ] Response parsing and validation
- [ ] Error response handling
- [ ] Command/response correlation

### 6.2 Device-Specific Logic
- [ ] Device model detection from part numbers
- [ ] Firmware version parsing and comparison
- [ ] Device capability detection (streaming, SD card, WiFi)
- [ ] Device state machine (initialization, ready, streaming, error)
- [ ] Device metadata management

### 6.3 SCPI Command Extensions
Ensure all desktop SCPI commands are in core:
- [ ] Device control (reboot, bootloader, power)
- [ ] Channel configuration (ADC, digital I/O)
- [ ] Streaming control (start/stop, frequency, format)
- [ ] Network configuration (WiFi SSID/password, LAN settings)
- [ ] SD card operations (enable/disable, file list, file retrieval)
- [ ] Timestamp synchronization
- [ ] Device information queries

### 6.4 Protobuf Message Handling
- [ ] Status message parsing (device info, channel config)
- [ ] Streaming message parsing (continuous data)
- [ ] SD card message parsing (file listings - text format)
- [ ] Error message parsing
- [ ] Message type detection and routing

### 6.5 Device Initialization Sequence
- [ ] Disable device echo
- [ ] Stop any running streaming
- [ ] Turn device on (if needed)
- [ ] Set protobuf message format
- [ ] Query device info and capabilities
- [ ] Configure initial state
- [ ] Validate successful initialization

### Success Criteria
- [ ] All desktop SCPI commands available in core
- [ ] Protocol handlers route messages correctly by type
- [ ] Device initialization sequence matches desktop behavior
- [ ] Device type detection from part numbers
- [ ] Firmware version comparison logic
- [ ] Error responses handled gracefully
- [ ] 80%+ test coverage for protocol handling

### Desktop Migration Impact
Once complete, desktop can:
- Use core's `InitializeDeviceAsync()` for device setup
- Use core protocol handlers for message routing
- Use core's device type detection logic
- Fully migrate to core's `ScpiMessageProducer`
- Use core's protobuf parsers

**Deliverable**: Core 0.8.0 with complete protocol implementation

**GitHub Issues**: [#51](https://github.com/daqifi/daqifi-core/issues/51)

## Phase 7: Advanced Features (Core 0.9.0)
**Goal**: Migrate remaining shared functionality

**Status**: Not started - Desktop-specific features

### 7.1 Network Configuration
- [ ] WiFi SSID and password configuration
- [ ] WiFi security mode selection (Open, WEP, WPA, WPA2)
- [ ] LAN settings (IP address, subnet, gateway)
- [ ] Network configuration persistence on device
- [ ] Network status queries
- [ ] Apply configuration and reboot sequence
- [ ] `INetworkConfigurable` interface

### 7.2 SD Card Operations
- [ ] Enable/disable SD card logging mode
- [ ] File listing (text-based response handling)
- [ ] File retrieval from device
- [ ] SD card format commands
- [ ] Storage capacity queries
- [ ] File management (delete, rename)
- [ ] Text message consumer for SD card responses (non-protobuf)
- [ ] `ISdCardSupport` interface

### 7.3 Device Configuration Persistence
- [ ] Device settings save/load
- [ ] Configuration validation
- [ ] Default configuration restoration
- [ ] Configuration versioning
- [ ] Cross-device configuration compatibility

### 7.4 Firmware Update Support
- [ ] Firmware version comparison and validation
- [ ] Bootloader mode detection and entry
- [ ] Firmware upload protocol (HID-based)
- [ ] Upload progress tracking
- [ ] Firmware verification
- [ ] Device reboot after update
- [ ] Rollback on failure
- [ ] `IFirmwareUpdateSupport` interface

### 7.5 Logging and Diagnostics
- [ ] Device-level logging interface
- [ ] Diagnostic message capture
- [ ] Performance metrics (connection time, message latency)
- [ ] Error tracking and reporting
- [ ] Debug mode with verbose logging

### Success Criteria
- [ ] Network configuration can be set and persisted
- [ ] SD card file operations work with text-based responses
- [ ] Firmware updates complete successfully with progress tracking
- [ ] Configuration persistence across device reboots
- [ ] Diagnostic logging captures device-level events
- [ ] All operations support async/await patterns
- [ ] 80%+ test coverage with mock device responses

### Desktop Migration Impact
Once complete, desktop can:
- Use core's `INetworkConfigurable` for network configuration UI
- Use core's `ISdCardSupport` for SD card operations
- Use core's `IFirmwareUpdateSupport` for firmware updates
- Use core's diagnostic interfaces for device logging
- Use core's configuration persistence

**Deliverable**: Core 0.9.0 with advanced device features

**GitHub Issues**: [#52](https://github.com/daqifi/daqifi-core/issues/52)

## Phase 8: Desktop Integration & Finalization (Core 1.0.0)
**Goal**: Clean up, stabilize APIs, and complete desktop integration

**Status**: Not started - ONLY after Phases 3-7 complete

**IMPORTANT**: This phase should NOT be attempted until Phases 3-7 are complete. Issue #39 demonstrates the problems with premature integration.

### 8.1 Desktop Integration Adapters (NOW is the time)
- [ ] Build `CoreDeviceAdapter` as true drop-in replacement
- [ ] Direct message format compatibility with desktop expectations
- [ ] Desktop-specific extensions (`IDesktopMessageConsumer`, `IDesktopMessageProducer`)
- [ ] Support for legacy desktop interfaces during transition
- [ ] Migration from desktop's `MessageProducer`/`MessageConsumer` to core equivalents

### 8.2 Integration Testing
- [ ] Create `Daqifi.Core.DesktopTests` integration test project
- [ ] Test real-world migration scenarios
- [ ] Validate message format compatibility
- [ ] Test side-by-side legacy and core implementations
- [ ] Performance comparison tests (desktop vs core)
- [ ] Catch integration issues before release

### 8.3 API Stabilization
- [ ] Review and finalize all core interfaces
- [ ] Mark stable APIs with `[Stable]` attribute
- [ ] Remove deprecated adapter patterns
- [ ] Ensure comprehensive XML documentation
- [ ] API design review with desktop team
- [ ] Versioning strategy for breaking changes

### 8.4 Desktop Refactoring
- [ ] Simplify desktop device classes to pure UI concerns
- [ ] Remove duplicate logic now handled by core
- [ ] Replace `AbstractStreamingDevice` logic with core implementations
- [ ] Migrate channel management to core
- [ ] Migrate device discovery to core
- [ ] Optimize performance and memory usage

### 8.5 Documentation & Examples
- [ ] Complete migration guide for each phase
- [ ] Before/after code examples for desktop migration
- [ ] `examples/` directory with real-world usage
- [ ] Cross-platform application example
- [ ] Performance tuning guide
- [ ] Troubleshooting guide

### Success Criteria
- [ ] `CoreDeviceAdapter` is true drop-in replacement (no wrapper code needed)
- [ ] Desktop integration tests pass with 100% success rate
- [ ] Zero code bloat from adapters (net reduction in desktop code)
- [ ] Desktop-specific features work (buffer clearing, safe shutdown)
- [ ] Message format compatibility (no casting failures)
- [ ] Performance parity or improvement vs legacy desktop code
- [ ] Desktop can migrate incrementally (device by device)
- [ ] Comprehensive documentation for migration path

### Desktop Migration Success Pattern
```csharp
// Before (legacy):
MessageProducer = new Desktop.MessageProducer(stream);
MessageConsumer = new Desktop.MessageConsumer(stream);

// After (CoreDeviceAdapter - should be this simple):
var adapter = CoreDeviceAdapter.CreateTcpAdapter(host, port);
await adapter.ConnectAsync();
MessageProducer = adapter.DesktopMessageProducer;
MessageConsumer = adapter.DesktopMessageConsumer;
// All existing message handling code works unchanged
```

### Desktop Migration Impact
Once complete, desktop can:
- Replace all device communication logic with core
- Reduce `AbstractStreamingDevice` from 1,073 lines to UI bindings only
- Remove duplicate SCPI command generation
- Remove duplicate connection management
- Remove duplicate message handling
- Focus desktop code exclusively on UI/UX concerns

**Deliverable**: Core 1.0.0 - Stable API for device communication

**Related Issues**:
- [#39](https://github.com/daqifi/daqifi-core/issues/39) - CoreDeviceAdapter issues (deferred to this phase)
- Phases 3-7 must be complete before this phase begins

## Implementation Guidelines

### Core Development
- Focus on cross-platform compatibility
- Maintain high test coverage (80%+)
- Use dependency injection for testability
- Follow .NET coding standards and conventions
- Comprehensive XML documentation for public APIs

### Desktop Integration
- Use adapter pattern during transitions
- Preserve existing public APIs until migration complete
- Maintain UI responsiveness with async patterns
- Keep desktop-specific concerns separate from core logic

### Testing Strategy
- Unit tests for all core functionality
- Integration tests for device communication
- Desktop UI tests for user workflows
- Regression testing after each phase

### Version Management
- Core follows semantic versioning
- Desktop updates core dependency after each phase
- Maintain changelog for breaking changes
- Use feature flags for gradual rollout when needed

## Success Metrics
- **Functionality**: All existing desktop features continue to work
- **Performance**: No degradation in device communication performance
- **Code Quality**: Improved separation of concerns and testability
- **Maintenance**: Reduced code duplication between projects
- **Reusability**: Other applications can easily use core for DAQiFi devices

## Risk Mitigation
- **Incremental Rollback**: Each phase can be reverted independently
- **Parallel Development**: Core and desktop can be developed simultaneously
- **Testing Gates**: Comprehensive testing before each phase deployment
- **Documentation**: Clear migration guides for each phase
- **Community**: Engage with any external users early about upcoming changes

## Timeline Considerations
- Each phase represents 2-4 weeks of development
- Allow buffer time for testing and iteration
- Coordinate releases with any external dependencies
- Plan around major desktop application releases

This migration plan ensures a smooth transition while maintaining the desktop application's rich functionality and creating a solid foundation for future DAQiFi applications.