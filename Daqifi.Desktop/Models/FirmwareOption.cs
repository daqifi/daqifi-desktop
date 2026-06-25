using System;

namespace Daqifi.Desktop.Models;

/// <summary>
/// A selectable firmware entry shown in the firmware dropdown — the latest published DAQiFi
/// firmware the user can flash without browsing for a <c>.hex</c> file. In bootloader/recovery
/// mode the device model is not known, so this is typically a single "DAQiFi — &lt;version&gt;" entry.
/// </summary>
public sealed class FirmwareOption : IEquatable<FirmwareOption>
{
    /// <summary>Device model the firmware targets (e.g. "Nq1"), or empty when unknown.</summary>
    public string DeviceModel { get; init; } = string.Empty;

    /// <summary>Firmware version string (e.g. "3.6.1").</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Human-readable label shown in the dropdown.</summary>
    public string Display => string.IsNullOrWhiteSpace(DeviceModel)
        ? Version
        : $"{DeviceModel} — {Version}";

    /// <summary>
    /// Value equality (case-insensitive by <see cref="DeviceModel"/> + <see cref="Version"/>) so the
    /// dropdown's selected option survives a rebuild — <c>Contains()</c> would otherwise
    /// reference-compare freshly constructed instances and reset the selection.
    /// </summary>
    public bool Equals(FirmwareOption? other) =>
        other is not null
        && string.Equals(DeviceModel, other.DeviceModel, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Version, other.Version, StringComparison.OrdinalIgnoreCase);

    /// <summary>Value equality against any object; see <see cref="Equals(FirmwareOption)"/>.</summary>
    public override bool Equals(object? obj) => obj is FirmwareOption other && Equals(other);

    /// <summary>Hash code consistent with <see cref="Equals(FirmwareOption)"/> (case-insensitive).</summary>
    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(DeviceModel),
        StringComparer.OrdinalIgnoreCase.GetHashCode(Version));

    /// <summary>
    /// Mirrors <see cref="Display"/> so a ComboBox's closed selection box shows the friendly label
    /// even when its template falls back to ToString() instead of the item template.
    /// </summary>
    public override string ToString() => Display;
}
