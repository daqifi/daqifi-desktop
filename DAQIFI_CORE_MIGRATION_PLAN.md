# DAQiFi Core Migration Plan

## Overview
This document outlines the iterative migration plan to gradually move functionality from `daqifi-desktop` to `daqifi-core`, creating a clean separation between core device communication logic and desktop-specific UI/application concerns.

## Current State
- Desktop app uses `Daqifi.Core 0.2.1`
- Desktop has rich `IDevice`/`IStreamingDevice` interfaces with comprehensive functionality
- Core 0.3.0 provides cleaner, event-driven interfaces but with basic implementations
- Significant overlap in messaging and device communication concepts

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

## Phase 2: Message System Migration (Core 0.4.0)
**Goal**: Migrate to core's message system while preserving desktop functionality

### 2.1 Message Interface Alignment
- Extend core `IOutboundMessage<T>` to support desktop's message patterns
- Create adapters for existing desktop message producers/consumers
- Migrate `ScpiMessageProducer` usage to core implementation

### 2.2 Device Discovery Enhancement
- Move device discovery logic from desktop to core
- Create `IDeviceFinder` interface in core
- Implement WiFi, USB, and Serial device finders in core
- Keep desktop finders as adapters during transition

**Deliverable**: Core 0.4.0 with enhanced messaging and device discovery

## Phase 3: Connection Management (Core 0.5.0)
**Goal**: Move connection lifecycle management to core

### 3.1 Transport Layer Abstraction
- Create transport interfaces in core (`ITransport`, `ITcpTransport`, `IUdpTransport`, `ISerialTransport`)
- Move actual socket/connection management to core
- Desktop devices become wrappers around core transport implementations

### 3.2 Connection State Management
- Enhance core's `ConnectionStatus` enum to match desktop needs
- Migrate connection retry logic to core
- Preserve desktop's `INotifyPropertyChanged` for UI binding

**Deliverable**: Core 0.5.0 with robust connection management

## Phase 4: Device Implementation Migration (Core 0.6.0)
**Goal**: Move core device logic while preserving desktop-specific features

### 4.1 Core Device Implementations
- Enhance core `DaqifiDevice` with actual TCP/UDP communication
- Implement core `DaqifiStreamingDevice` with basic streaming
- Move protocol buffer handling to core

### 4.2 Desktop Device Adapters
- Create `DesktopDeviceAdapter` that wraps core devices
- Preserve desktop-specific features (SD card, network config, firmware management)
- Maintain desktop's rich interface while delegating core functionality

**Deliverable**: Core 0.6.0 with working device implementations

## Phase 5: Channel Management (Core 0.7.0)
**Goal**: Migrate channel configuration and data handling

### 5.1 Channel Abstraction
- Create core channel interfaces (`IChannel`, `IAnalogChannel`, `IDigitalChannel`)
- Move channel configuration logic to core
- Preserve desktop's channel UI models as wrappers

### 5.2 Data Streaming
- Implement core data streaming pipeline
- Move timestamp correlation and data parsing to core
- Keep desktop's data visualization and logging as consumers

**Deliverable**: Core 0.7.0 with channel management and streaming

## Phase 6: Protocol Implementation (Core 0.8.0)
**Goal**: Move protocol-specific communication to core

### 6.1 Protocol Handlers
- Implement SCPI command handling in core
- Move protobuf message serialization/deserialization to core
- Create protocol-agnostic message routing

### 6.2 Device-Specific Logic
- Move device model detection to core
- Implement firmware version handling in core
- Preserve desktop's firmware update UI while using core logic

**Deliverable**: Core 0.8.0 with complete protocol implementation

## Phase 7: Advanced Features (Core 0.9.0)
**Goal**: Migrate remaining shared functionality

### 7.1 Device Configuration
- Move network configuration logic to core
- Implement device settings persistence in core
- Create configuration synchronization between devices and applications

### 7.2 Logging and Diagnostics
- Move device-level logging to core
- Implement core diagnostic interfaces
- Preserve desktop's log visualization while using core logging

**Deliverable**: Core 0.9.0 with advanced device features

## Phase 8: Finalization (Core 1.0.0)
**Goal**: Clean up and stabilize APIs

### 8.1 API Stabilization
- Review and finalize all core interfaces
- Remove deprecated adapter patterns
- Ensure comprehensive documentation

### 8.2 Desktop Refactoring
- Simplify desktop device classes to pure UI concerns
- Remove duplicate logic now handled by core
- Optimize performance and memory usage

**Deliverable**: Core 1.0.0 - Stable API for device communication

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