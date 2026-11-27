using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// dm.db round trip as the inline DM mode now uses it: both directions of a conversation are stored under
/// the partner's id (that is what the sidebar and the 24 h restore group by), and a not-yet-decryptable
/// PKI DM keeps its ciphertext until the late-decrypt handler replaces the text. Inline DMs were neither
/// persisted nor restored before, so every reconnect/restart emptied the conversation.
/// </summary>
public class DmPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mh_dm_{Guid.NewGuid():N}");
    private readonly MessageDbManager _mgr;

    public DmPersistenceTests() => _mgr = new MessageDbManager(_dir);

    public void Dispose()
    {
        _mgr.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private const uint Me = 0x75fb6baa;
    private const uint Partner = 0x97ff8680;

    [Fact]
    public void Incoming_and_outgoing_dm_share_the_partner_key()
    {
        _mgr.InsertDmMessage(Partner, new MessageItem { Id = 1, FromId = Partner, ToId = Me, From = "Node-97ff8680", Message = "Nabend!" });
        _mgr.InsertDmMessage(Partner, new MessageItem { Id = 2, FromId = Me, ToId = Partner, From = "Ich", Message = "Moin", IsOwnMessage = true });

        var all = _mgr.LoadDmMessagesSince(0);
        Assert.Equal(2, all.Count);
        Assert.All(all, e => Assert.Equal(Partner, e.PartnerId));

        var incoming = Assert.Single(all, e => e.PacketId == 1);
        Assert.Equal(Partner, incoming.FromId);
        Assert.Equal(Me, incoming.ToId);
        Assert.Equal("Nabend!", incoming.Message);

        var outgoing = Assert.Single(all, e => e.PacketId == 2);
        Assert.Equal(Me, outgoing.FromId);
        Assert.Equal(Partner, outgoing.ToId);

        // Older-history paging for the open conversation
        var older = _mgr.LoadDmConversationBefore(Partner, long.MaxValue, 50);
        Assert.Equal(2, older.Count);
        Assert.Empty(_mgr.LoadDmConversationBefore(0x11111111, long.MaxValue, 50));
    }

    [Fact]
    public void Undecryptable_dm_keeps_its_ciphertext_until_the_late_decrypt_updates_it()
    {
        var cipher = new byte[] { 1, 2, 3, 4 };
        _mgr.InsertDmMessage(Partner, new MessageItem
        {
            Id = 5, FromId = Partner, ToId = Me, From = "ABC", Message = "🔒", IsEncrypted = true, PkiCipher = cipher,
        });

        var stored = Assert.Single(_mgr.LoadDmMessagesSince(0));
        Assert.True(stored.IsEncrypted);
        Assert.Equal(cipher, stored.Cipher);

        _mgr.UpdateDmMessage(5, "Hallo zusammen");

        var updated = Assert.Single(_mgr.LoadDmMessagesSince(0));
        Assert.False(updated.IsEncrypted);
        Assert.Null(updated.Cipher);
        Assert.Equal("Hallo zusammen", updated.Message);
    }

    [Fact]
    public void Hop_count_and_signal_survive_the_round_trip()
    {
        // The bubble's "↪ N" and SNR/RSSI used to exist live only — every reload from the DB dropped them.
        _mgr.InsertDmMessage(Partner, new MessageItem { Id = 7, FromId = Partner, ToId = Me, Message = "direct", HopCount = 0, RxSnr = 7.25f, RxRssi = -81 });
        _mgr.InsertDmMessage(Partner, new MessageItem { Id = 8, FromId = Partner, ToId = Me, Message = "relayed", HopCount = 2 });
        _mgr.InsertChannelMessage(0, "LongFast", new MessageItem { Id = 9, FromId = Partner, ToId = 0xFFFFFFFF, Message = "no hop info" });

        var dms = _mgr.LoadDmMessagesSince(0);
        var direct = Assert.Single(dms, e => e.PacketId == 7);
        Assert.Equal(0, direct.HopCount);
        Assert.Equal(7.25f, direct.RxSnr);
        Assert.Equal(-81, direct.RxRssi);

        var relayed = Assert.Single(dms, e => e.PacketId == 8);
        Assert.Equal(2, relayed.HopCount);
        Assert.Null(relayed.RxSnr);
        Assert.Null(relayed.RxRssi);

        var channel = Assert.Single(_mgr.LoadAllChannelMessagesSince(0));
        Assert.Equal(-1, channel.HopCount);   // unknown stays unknown → no "↪" badge
    }

    [Fact]
    public void Old_db_without_hop_columns_is_migrated_on_open()
    {
        var path = Path.Combine(_dir, "legacy.db");
        using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp INTEGER NOT NULL,
    from_name TEXT NOT NULL DEFAULT '', from_id INTEGER NOT NULL DEFAULT 0, to_id INTEGER NOT NULL DEFAULT 0,
    packet_id INTEGER NOT NULL DEFAULT 0, message TEXT NOT NULL DEFAULT '',
    channel_index INTEGER NOT NULL DEFAULT 0, channel_name TEXT NOT NULL DEFAULT '',
    is_encrypted INTEGER NOT NULL DEFAULT 0, is_via_mqtt INTEGER NOT NULL DEFAULT 0,
    reply_id INTEGER NOT NULL DEFAULT 0, reply_from_name TEXT NOT NULL DEFAULT '', reply_preview TEXT NOT NULL DEFAULT '',
    sender_color TEXT NOT NULL DEFAULT '', sender_note TEXT NOT NULL DEFAULT '', partner_id INTEGER NOT NULL DEFAULT 0);
INSERT INTO messages (timestamp, from_id, to_id, packet_id, message, partner_id) VALUES (1, 1, 2, 3, 'old row', 2);";
            cmd.ExecuteNonQuery();
        }

        using var svc = new MessageDatabaseService(path);   // runs the ADD COLUMN migrations
        var old = Assert.Single(svc.LoadSince(0));
        Assert.Equal("old row", old.Message);
        Assert.Equal(-1, old.HopCount);
        Assert.Null(old.RxSnr);
        Assert.Null(old.Cipher);

        svc.Insert(new MessageItem { Id = 4, FromId = 1, ToId = 2, Message = "new row", HopCount = 1, RxSnr = 3f, RxRssi = -70 }, 0, "", partnerId: 2);
        var rows = svc.LoadSince(0);
        Assert.Equal(2, rows.Count);
        var fresh = Assert.Single(rows, e => e.PacketId == 4);
        Assert.Equal(1, fresh.HopCount);
        Assert.Equal(3f, fresh.RxSnr);
        Assert.Equal(-70, fresh.RxRssi);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
