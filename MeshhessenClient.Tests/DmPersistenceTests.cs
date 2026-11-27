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
}
