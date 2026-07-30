// Official Meshtastic protobufs nest the config sub-types under Config / ModuleConfig
// (e.g. Config.Types.LoRaConfig), whereas our previous hand-rolled protos declared
// them top-level (Meshtastic.Protobufs.LoRaConfig). These global aliases map the
// short names the code already uses onto the official nested types, so the call
// sites stay unchanged. Add an entry here when a further config sub-type is used.

global using DeviceConfig               = Meshtastic.Protobufs.Config.Types.DeviceConfig;
global using PositionConfig             = Meshtastic.Protobufs.Config.Types.PositionConfig;
global using PowerConfig                = Meshtastic.Protobufs.Config.Types.PowerConfig;
global using NetworkConfig              = Meshtastic.Protobufs.Config.Types.NetworkConfig;
global using DisplayConfig              = Meshtastic.Protobufs.Config.Types.DisplayConfig;
global using LoRaConfig                 = Meshtastic.Protobufs.Config.Types.LoRaConfig;
global using BluetoothConfig            = Meshtastic.Protobufs.Config.Types.BluetoothConfig;
global using SecurityConfig             = Meshtastic.Protobufs.Config.Types.SecurityConfig;

// Enums that were top-level in the hand-rolled protos but are nested in the official ones.
global using Role                       = Meshtastic.Protobufs.Config.Types.DeviceConfig.Types.Role;
global using RebroadcastMode            = Meshtastic.Protobufs.Config.Types.DeviceConfig.Types.RebroadcastMode;
global using GpsMode                    = Meshtastic.Protobufs.Config.Types.PositionConfig.Types.GpsMode;
global using Region                     = Meshtastic.Protobufs.Config.Types.LoRaConfig.Types.RegionCode;
global using ModemPreset                = Meshtastic.Protobufs.Config.Types.LoRaConfig.Types.ModemPreset;
global using ChannelRole                = Meshtastic.Protobufs.Channel.Types.Role;
global using MapReportSettings          = Meshtastic.Protobufs.ModuleConfig.Types.MapReportSettings;

global using MQTTConfig                 = Meshtastic.Protobufs.ModuleConfig.Types.MQTTConfig;
global using TelemetryConfig            = Meshtastic.Protobufs.ModuleConfig.Types.TelemetryConfig;
global using SerialConfig               = Meshtastic.Protobufs.ModuleConfig.Types.SerialConfig;
global using ExternalNotificationConfig = Meshtastic.Protobufs.ModuleConfig.Types.ExternalNotificationConfig;
global using StoreForwardConfig         = Meshtastic.Protobufs.ModuleConfig.Types.StoreForwardConfig;
global using RangeTestConfig            = Meshtastic.Protobufs.ModuleConfig.Types.RangeTestConfig;
global using CannedMessageConfig        = Meshtastic.Protobufs.ModuleConfig.Types.CannedMessageConfig;
global using NeighborInfoConfig         = Meshtastic.Protobufs.ModuleConfig.Types.NeighborInfoConfig;
