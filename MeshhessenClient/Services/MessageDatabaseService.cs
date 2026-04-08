using System.IO;
using Microsoft.Data.Sqlite;
using MeshhessenClient.Models;

namespace MeshhessenClient.Services;

/// <summary>
/// SQLite-backed storage for a single messages DB file (one per channel or the shared dm.db).
/// Thread-safe via _lock.
/// </summary>
public class MessageDatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly object _lock = new();
    private bool _disposed;

    public MessageDatabaseService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};";
        InitSchema();
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    private void InitSchema()
    {
        using var con = Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS messages (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp       INTEGER NOT NULL,
    from_name       TEXT    NOT NULL DEFAULT '',
    from_id         INTEGER NOT NULL DEFAULT 0,
    to_id           INTEGER NOT NULL DEFAULT 0,
    packet_id       INTEGER NOT NULL DEFAULT 0,
    message         TEXT    NOT NULL DEFAULT '',
    channel_index   INTEGER NOT NULL DEFAULT 0,
    channel_name    TEXT    NOT NULL DEFAULT '',
    is_encrypted    INTEGER NOT NULL DEFAULT 0,
    is_via_mqtt     INTEGER NOT NULL DEFAULT 0,
    reply_id        INTEGER NOT NULL DEFAULT 0,
    reply_from_name TEXT    NOT NULL DEFAULT '',
    reply_preview   TEXT    NOT NULL DEFAULT '',
    sender_color    TEXT    NOT NULL DEFAULT '',
    sender_note     TEXT    NOT NULL DEFAULT '',
    partner_id      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_msg_timestamp ON messages(timestamp);
CREATE INDEX IF NOT EXISTS idx_msg_partner   ON messages(partner_id, timestamp);
";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var con = new SqliteConnection(_connectionString);
        con.Open();
        return con;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public void Insert(MessageItem msg, int channelIndex, string channelName, uint partnerId = 0)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
INSERT INTO messages
    (timestamp, from_name, from_id, to_id, packet_id, message,
     channel_index, channel_name, is_encrypted, is_via_mqtt,
     reply_id, reply_from_name, reply_preview, sender_color, sender_note, partner_id)
VALUES
    ($ts, $fn, $fi, $ti, $pi, $msg,
     $ci, $cn, $enc, $mqtt,
     $rid, $rfn, $rp, $sc, $sn, $pid)";

            cmd.Parameters.AddWithValue("$ts",   DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$fn",   msg.From ?? string.Empty);
            cmd.Parameters.AddWithValue("$fi",   (long)msg.FromId);
            cmd.Parameters.AddWithValue("$ti",   (long)msg.ToId);
            cmd.Parameters.AddWithValue("$pi",   (long)msg.Id);
            cmd.Parameters.AddWithValue("$msg",  msg.Message ?? string.Empty);
            cmd.Parameters.AddWithValue("$ci",   channelIndex);
            cmd.Parameters.AddWithValue("$cn",   channelName ?? string.Empty);
            cmd.Parameters.AddWithValue("$enc",  msg.IsEncrypted ? 1 : 0);
            cmd.Parameters.AddWithValue("$mqtt", msg.IsViaMqtt ? 1 : 0);
            cmd.Parameters.AddWithValue("$rid",  (long)msg.ReplyId);
            cmd.Parameters.AddWithValue("$rfn",  msg.ReplyFromName ?? string.Empty);
            cmd.Parameters.AddWithValue("$rp",   msg.ReplyPreview ?? string.Empty);
            cmd.Parameters.AddWithValue("$sc",   msg.SenderColorHex ?? string.Empty);
            cmd.Parameters.AddWithValue("$sn",   msg.SenderNote ?? string.Empty);
            cmd.Parameters.AddWithValue("$pid",  (long)partnerId);
            cmd.ExecuteNonQuery();
        }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public List<MessageDbEntry> LoadSince(long sinceUnixSeconds, uint? partnerId = null)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            if (partnerId.HasValue)
            {
                cmd.CommandText = "SELECT * FROM messages WHERE timestamp >= $ts AND partner_id = $pid ORDER BY timestamp ASC";
                cmd.Parameters.AddWithValue("$pid", (long)partnerId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT * FROM messages WHERE timestamp >= $ts ORDER BY timestamp ASC";
            }
            cmd.Parameters.AddWithValue("$ts", sinceUnixSeconds);
            return ReadAll(cmd);
        }
    }

    public List<MessageDbEntry> LoadBefore(long beforeUnixSeconds, int count, uint? partnerId = null)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            if (partnerId.HasValue)
            {
                cmd.CommandText = @"
SELECT * FROM (
    SELECT * FROM messages WHERE timestamp < $ts AND partner_id = $pid
    ORDER BY timestamp DESC LIMIT $lim
) ORDER BY timestamp ASC";
                cmd.Parameters.AddWithValue("$pid", (long)partnerId.Value);
            }
            else
            {
                cmd.CommandText = @"
SELECT * FROM (
    SELECT * FROM messages WHERE timestamp < $ts
    ORDER BY timestamp DESC LIMIT $lim
) ORDER BY timestamp ASC";
            }
            cmd.Parameters.AddWithValue("$ts",  beforeUnixSeconds);
            cmd.Parameters.AddWithValue("$lim", count);
            return ReadAll(cmd);
        }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    public void ClearAll(uint? partnerId = null)
    {
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            if (partnerId.HasValue)
            {
                cmd.CommandText = "DELETE FROM messages WHERE partner_id = $pid";
                cmd.Parameters.AddWithValue("$pid", (long)partnerId.Value);
            }
            else
            {
                cmd.CommandText = "DELETE FROM messages";
            }
            cmd.ExecuteNonQuery();
        }
    }

    public void ClearOlderThan(int days, uint? partnerId = null)
    {
        if (days <= 0) return;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
        lock (_lock)
        {
            using var con = Open();
            using var cmd = con.CreateCommand();
            if (partnerId.HasValue)
            {
                cmd.CommandText = "DELETE FROM messages WHERE timestamp < $ts AND partner_id = $pid";
                cmd.Parameters.AddWithValue("$pid", (long)partnerId.Value);
            }
            else
            {
                cmd.CommandText = "DELETE FROM messages WHERE timestamp < $ts";
            }
            cmd.Parameters.AddWithValue("$ts", cutoff);
            cmd.ExecuteNonQuery();
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private static List<MessageDbEntry> ReadAll(SqliteCommand cmd)
    {
        var list = new List<MessageDbEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MessageDbEntry(
                Id:             r.GetInt64(r.GetOrdinal("id")),
                Timestamp:      r.GetInt64(r.GetOrdinal("timestamp")),
                FromName:       r.GetString(r.GetOrdinal("from_name")),
                FromId:         (uint)r.GetInt64(r.GetOrdinal("from_id")),
                ToId:           (uint)r.GetInt64(r.GetOrdinal("to_id")),
                PacketId:       (uint)r.GetInt64(r.GetOrdinal("packet_id")),
                Message:        r.GetString(r.GetOrdinal("message")),
                ChannelIndex:   r.GetInt32(r.GetOrdinal("channel_index")),
                ChannelName:    r.GetString(r.GetOrdinal("channel_name")),
                IsEncrypted:    r.GetInt64(r.GetOrdinal("is_encrypted")) != 0,
                IsViaMqtt:      r.GetInt64(r.GetOrdinal("is_via_mqtt")) != 0,
                ReplyId:        (uint)r.GetInt64(r.GetOrdinal("reply_id")),
                ReplyFromName:  r.GetString(r.GetOrdinal("reply_from_name")),
                ReplyPreview:   r.GetString(r.GetOrdinal("reply_preview")),
                SenderColorHex: r.GetString(r.GetOrdinal("sender_color")),
                SenderNote:     r.GetString(r.GetOrdinal("sender_note")),
                PartnerId:      (uint)r.GetInt64(r.GetOrdinal("partner_id"))
            ));
        }
        return list;
    }

    public void Dispose() => _disposed = true;
}
