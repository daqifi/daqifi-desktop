using System;

namespace Daqifi.Desktop.Bootloader.Exceptions;

public class FirmwareUpdateException : Exception
{
    public FirmwareUpdateException() : base() { }
    
    public FirmwareUpdateException(string message) : base(message) { }
    
    public FirmwareUpdateException(string message, Exception innerException) 
        : base(message, innerException) { }
} 