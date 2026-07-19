// ReSharper disable all

namespace NecroLens.util;

/**
 * Server IPC Zone Type Codes used to identify relevant ZoneDown network packets.
 * These change with every game patch and must be re-verified when they stop matching.
 */
internal enum ServerZoneIpcType : ushort
{
    ActorControlSelf = 0x00B2,
    SystemLogMessage = 0x0382,
}
