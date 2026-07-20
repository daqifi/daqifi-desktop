using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Daqifi.Desktop.Helpers;

/// <summary>
/// A strongly-typed reference-identity equality comparer. Unlike the framework's
/// <see cref="ReferenceEqualityComparer"/> (which is typed as <c>IEqualityComparer&lt;object?&gt;</c> and,
/// because <see cref="IEqualityComparer{T}"/> is invariant, cannot be handed to a
/// <c>HashSet&lt;TSpecific&gt;</c>), this generic comparer plugs directly into typed collections.
/// Use it for bookkeeping sets that track object <em>instances</em> (e.g. subscription/claim tracking)
/// so entries stay reachable even when a stored object mutates fields that participate in its value hash.
/// </summary>
/// <typeparam name="T">The reference type being compared by identity.</typeparam>
public sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
{
    /// <summary>The shared, stateless instance.</summary>
    public static readonly ReferenceComparer<T> Instance = new();

    private ReferenceComparer() { }

    /// <inheritdoc />
    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    /// <inheritdoc />
    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
