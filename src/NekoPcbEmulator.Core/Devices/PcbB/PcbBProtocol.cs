namespace NekoPcbEmulator.Core.Devices.PcbB;

/// <summary>Command bytes. Host-to-device opcodes are below 0x80, device-to-host at or above.</summary>
public static class PcbBCommand
{
    public const byte LedSet = 0x01;
    public const byte LedClear = 0x02;
    public const byte ClearAll = 0x03;
    public const byte SetAll = 0x04;
    public const byte SetMask = 0x05;
    public const byte SetBatch = 0x06;

    public const byte Ping = 0x10;
    public const byte GetState = 0x11;
    public const byte GetInfo = 0x12;

    public const byte Ack = 0x80;
    public const byte Nak = 0x81;
    public const byte Pong = 0x90;
    public const byte State = 0x91;
    public const byte Info = 0x92;

    public static string Name(byte command) => command switch
    {
        LedSet => "LED_SET",
        LedClear => "LED_CLEAR",
        ClearAll => "CLEAR_ALL",
        SetAll => "SET_ALL",
        SetMask => "SET_MASK",
        SetBatch => "SET_BATCH",
        Ping => "PING",
        GetState => "GET_STATE",
        GetInfo => "GET_INFO",
        Ack => "ACK",
        Nak => "NAK",
        Pong => "PONG",
        State => "STATE",
        Info => "INFO",
        _ => $"0x{command:X2}",
    };
}

/// <summary>Error codes carried by a <see cref="PcbBCommand.Nak"/>.</summary>
public static class PcbBError
{
    public const byte BadLength = 0x01;
    public const byte UnknownCommand = 0x02;
    public const byte BadIndex = 0x03;
    public const byte BadParameter = 0x04;
    public const byte BadChecksum = 0x05;
    public const byte BadFrame = 0x06;

    public static string Name(byte code) => code switch
    {
        BadLength => "BAD_LENGTH",
        UnknownCommand => "UNKNOWN_COMMAND",
        BadIndex => "BAD_INDEX",
        BadParameter => "BAD_PARAMETER",
        BadChecksum => "BAD_CHECKSUM",
        BadFrame => "BAD_FRAME",
        _ => $"0x{code:X2}",
    };
}
