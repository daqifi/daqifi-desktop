# DAQiFi Core Migration Plan

## Overview
This document outlines the iterative migration plan to gradually move functionality from `daqifi-desktop` to `daqifi-core`, creating a clean separation between core device communication logic and desktop-specific UI/application concerns.

## Current State (Updated 2025-10-20)

**Desktop (daqifi-desktop)**:
- Production-ready WPF application with 80% test coverage
- Comprehensive device communication (WiFi, Serial, USB)
- 1,073-line `AbstractStreamingDevice` with all device logic
- Channel management, data streaming, SD card, firmware updates
- **Currently uses `Daqifi.Core 0.5.0`** (upgraded from 0.4.1)
- **-270 lines of code** from Phase 4 integration ([PR #286](https://github.com/daqifi/daqifi-desktop/pull/286))

**Core (daqifi-core - main branch)**:
- ✅ **Phase 1-4 Complete**: Foundation, messaging, connection management, and device discovery
- ✅ Clean interfaces (`IDevice`, `IStreamingDevice`, `IMessageProducer`, `IMessageConsumer`)
- ✅ Transport abstractions (TCP, Serial, UDP) with retry logic
- ✅ Device discovery framework (`WiFiDeviceFinder`, `SerialDeviceFinder`, `HidDeviceFinder`)
- ✅ SCPI command producer (45+ commands)
- ✅ Protocol Buffer support
- ❌ **Missing**: Channels, streaming pipeline, advanced features (Phases 5-7)

**Integration Status**:
- ✅ **Phase 4 Successfully Integrated** - Desktop now uses Core device discovery ([PR #286](https://github.com/daqifi/daqifi-desktop/pull/286))
- Eliminated ~400 lines of duplicate device discovery code
- All desktop device finders replaced with Core implementations
- Net reduction of 270 lines of code
- **Next**: Ready to implement Phase 5 in Core

**Phase Progress**:
- ✅ Phase 1: Foundation (Complete - Core 0.3.0)
- ✅ Phase 2: Message System (Complete - Core 0.4.0)
- ✅ Phase 3: Connection Management (Complete - implemented, not yet released)
- ✅ Phase 4: Device Discovery (Complete - Core 0.5.0, Desktop integrated)
- 🔄 Phase 5: Channel Management (In Progress - Critical for next release)
- ⏳ Phase 6: Protocol Implementation (Not started)
- ⏳ Phase 7: Advanced Features (Not started)
- ⏳ Phase 8: Desktop Integration (Partial - Phase 4 complete, 5-7 pending)

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

## Phase 3: Connection Management ✅ (Completed)
**Goal**: Move connection lifecycle management to core

**Status**: Complete - All transport types implemented with retry logic

### 3.1 Transport Layer Abstraction
- [x] Create `IStreamTransport` base interface
- [x] Implement `TcpStreamTransport` for TCP connections
- [x] Implement `SerialStreamTransport` for serial/USB connections
- [x] Create `IUdpTransport` interface for UDP communication
- [x] Implement `UdpTransport` for broadcast/unicast
- [x] Connection retry logic with configurable timeouts
- [x] Thread-safe transport operations

### 3.2 Connection State Management
- [x] `ConnectionStatus` enum (Disconnected, Connecting, Connected)
- [x] Event-driven status change notifications
- [x] Connection retry logic with `ConnectionRetryOptions`
- [x] Connection timeout configuration per transport type
- [x] Thread-safe connection state transitions
- [x] Proper resource cleanup on connection failures

### 3.3 Features Implemented
- [x] Async/await patterns for all transport operations
- [x] `ConnectionRetryOptions` for configurable retry behavior
- [x] Platform-specific serial port handling
- [x] UDP transport for broadcast communication
- [x] Proper disposal and resource management

### Success Criteria
- [x] UDP transport with broadcast discovery support
- [x] Connection retry with configurable attempts and delays
- [x] Platform-specific serial port handling
- [x] All transport types support async/await patterns
- [x] 80%+ test coverage for transport layer
- [x] Cross-platform compatibility (no Windows-specific dependencies)

### Desktop Migration Impact
Desktop can now:
- Replace `DaqifiStreamingDevice.Connect()` with `TcpStreamTransport` from core
- Replace `SerialStreamingDevice.Connect()` with `SerialStreamTransport` from core
- Use core's `ConnectionRetryOptions` for connection retry logic

**Deliverable**: Core with robust connection management ✅

**GitHub Issues**: [Closed #48](https://github.com/daqifi/daqifi-core/issues/48)

## Phase 4: Device Discovery Framework ✅ (Core 0.5.0)
**Goal**: Move device discovery logic from desktop to core

**Status**: Complete - All discovery mechanisms implemented and integrated in desktop

### 4.1 Device Discovery Interfaces
- [x] Create `IDeviceFinder` interface for device discovery
- [x] Create `IDeviceInfo` interface/class for discovered device metadata
- [x] Support for multiple discovery mechanisms (WiFi, Serial, USB HID)
- [x] Async discovery with cancellation token support
- [x] Event-based discovery notifications (`DeviceDiscovered`, `DiscoveryCompleted`)

### 4.2 WiFi Device Discovery
- [x] Implement `WiFiDeviceFinder` using UDP broadcast
- [x] UDP broadcast on port 30303 with "DAQiFi?\r\n" query
- [x] Parse protobuf responses (IP, MAC, port, hostname, serial, firmware)
- [x] Network interface enumeration and selection
- [x] Timeout and retry configuration

### 4.3 Serial Device Discovery
- [x] Implement `SerialDeviceFinder` for USB/Serial enumeration
- [x] Serial port scanning with device info queries
- [x] Quick connection attempts for device detection
- [x] Configurable baud rates and DTR control

### 4.4 USB HID Device Discovery
- [x] Implement `HidDeviceFinder` for bootloader mode devices
- [x] HID device enumeration (VendorId: 0x4D8, ProductId: 0x03C)
- [x] Device mode detection (normal vs bootloader)

### 4.5 Device Info Extraction
- [x] Parse device metadata from protobuf messages
- [x] Extract IP, MAC, TCP port, hostname, serial, firmware version
- [x] Device type detection from part numbers (`Nyquist1`, `Nyquist3`)
- [x] Power status information

### Success Criteria
- [x] `IDeviceFinder` interface with async discovery
- [x] WiFi discovery finds devices on same subnet
- [x] Serial discovery enumerates all available ports
- [x] HID discovery identifies bootloader devices
- [x] Device info parsing extracts all relevant metadata
- [x] Event-based notifications during discovery
- [x] Cancellation token support
- [x] 80%+ test coverage with mock responses

### Desktop Migration Impact
**Desktop has successfully integrated** ([PR #286](https://github.com/daqifi/daqifi-desktop/pull/286)):
- ✅ Replaced `DaqifiDeviceFinder` with `WiFiDeviceFinder` from core
- ✅ Replaced `SerialDeviceFinder` with core implementation
- ✅ Replaced `HidDeviceFinder` with core implementation
- ✅ Removed ~400 lines of device enumeration logic from desktop
- ✅ Net reduction of 270 lines of code

**Deliverable**: Core 0.5.0 with complete device discovery framework ✅

**GitHub Issues**: [Closed #49](https://github.com/daqifi/daqifi-core/issues/49)

## Phase 5: Channel Management & Data Streaming (Core 0.7.0)
**Goal**: Migrate channel configuration and data handling

**Status**: Not started - Critical gap identified

### 5.1 Channel Abstraction
- [ ] Create `IChannel` base interface
- [ ] Create `IAnalogChannel` interface for analog inputs
- [ ] Create `IDigitalChannel` interface for digital I/O
- [ ] Create `IOutputChannel` interface for outputs
- [ ] Implement `AnalogChannel`, `DigitalChannel`, `OutputChannel` classes
- [ ] Channel enable/disable functionality
- [ ] Channel configuration (range, resolution, direction)

### 5.2 Data Sample Handling
- [ ] Create `IDataSample` interface for data points
- [ ] Implement `DataSample` class with timestamp and value
- [ ] Support for multiple data types (int, float, bool)
- [ ] Thread-safe active sample management
- [ ] Sample history/buffering support

### 5.3 Data Scaling & Calibration
- [ ] Calibration parameter support (CalibrationM, CalibrationB)
- [ ] Port range configuration
- [ ] Resolution-based scaling
- [ ] Internal scale factors
- [ ] Scaling formula: `(RawValue / Resolution * PortRange * CalibrationM + CalibrationB) * InternalScaleM`
- [ ] Expression evaluation support (optional)

### 5.4 Channel Configuration Commands
- [ ] ADC channel enable/disable SCPI commands
- [ ] Digital I/O direction configuration
- [ ] Output value setting
- [ ] Range and resolution configuration
- [ ] Channel metadata queries

### 5.5 Streaming Data Pipeline
- [ ] Parse streaming messages with channel data
- [ ] Update channel active samples from stream
- [ ] Timestamp correlation and synchronization
- [ ] Timestamp rollover handling (32-bit overflow)
- [ ] Multi-channel sample grouping
- [ ] Data event notifications

### Success Criteria
- [ ] `IChannel` interface matches desktop channel capabilities
- [ ] Data scaling accuracy within 0.01% of desktop implementation
- [ ] Timestamp correlation handles rollover correctly
- [ ] Thread-safe sample updates for high-frequency streaming
- [ ] Channel configuration commands in SCPI producer
- [ ] Event-driven sample notifications
- [ ] 80%+ test coverage including scaling edge cases

### Desktop Migration Impact
Once complete, desktop can:
- Replace `IChannel` with core implementation
- Replace `AnalogChannel` with core implementation
- Replace `DigitalChannel` with core implementation
- Replace `DataSample` with core implementation
- Remove data scaling logic from `AbstractStreamingDevice`
- Use core channel objects for configuration

**Deliverable**: Core 0.7.0 with channel management and streaming

**GitHub Issues**: [#50](https://github.com/daqifi/daqifi-core/issues/50)

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