using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;

namespace MeshhessenClient.Services;

/// <summary>
/// What the auto-reconnect hands to the fresh protocol service: the node we were connected to and the
/// node list from that session. With it, <see cref="MeshtasticProtocolService.InitializeAsync"/> asks the
/// firmware for config + channels only (<c>want_config_id</c> 69420, "ONLY_CONFIG", firmware ≥ 2.6.8) and
/// skips the node-list download — on a weak Bluetooth path that download was where most drops happened.
/// If <c>my_info</c> then names a different node (device swapped on the same port/address), the service
/// discards the carried list and requests everything.
/// </summary>
public sealed record ReconnectCarryOver(uint NodeNum, IReadOnlyCollection<ModelNodeInfo> Nodes);
