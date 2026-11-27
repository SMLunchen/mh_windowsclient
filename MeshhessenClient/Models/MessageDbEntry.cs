namespace MeshhessenClient.Models;

/// <summary>Row representation of a persisted message from the SQLite messages DB.</summary>
public record MessageDbEntry(
    long   Id,
    long   Timestamp,       // Unix seconds (UTC)
    string FromName,
    uint   FromId,
    uint   ToId,
    uint   PacketId,
    string Message,
    int    ChannelIndex,
    string ChannelName,
    bool   IsEncrypted,
    bool   IsViaMqtt,
    uint   ReplyId,
    string ReplyFromName,
    string ReplyPreview,
    string SenderColorHex,
    string SenderNote,
    uint   PartnerId,       // 0 for channel messages; chat-partner node ID for DMs
    byte[]? Cipher = null,  // PKI ciphertext for an undecryptable DM (null otherwise)
    int    HopCount = -1,   // relay hops of the received packet; -1 = unknown (old rows, own messages)
    float? RxSnr = null,    // our radio's reading — only stored for direct (0-hop, non-MQTT) reception
    int?   RxRssi = null
);
