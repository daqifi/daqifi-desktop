namespace Daqifi.Desktop.Logger;

/// <summary>
/// A fixed-capacity circular buffer that overwrites the oldest elements when full.
/// Provides O(1) Add and indexed access. Enumerates in insertion order (oldest first).
/// </summary>
public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _buffer = new T[capacity];
    }

    public int Count => _count;
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Adds an item to the buffer. If full, overwrites the oldest item.
    /// </summary>
    public void Add(T item)
    {
        var index = (_head + _count) % _buffer.Length;

        if (_count == _buffer.Length)
        {
            // Buffer is full — overwrite oldest and advance head
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
        }
        else
        {
            _buffer[index] = item;
            _count++;
        }
    }

    /// <summary>
    /// Gets the element at the specified logical index (0 = oldest).
    /// </summary>
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _buffer[(_head + index) % _buffer.Length];
        }
    }

    /// <summary>
    /// Returns a new list containing all elements in insertion order (oldest first).
    /// </summary>
    public List<T> ToList()
    {
        var list = new List<T>(_count);
        for (var i = 0; i < _count; i++)
        {
            list.Add(_buffer[(_head + i) % _buffer.Length]);
        }
        return list;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}
