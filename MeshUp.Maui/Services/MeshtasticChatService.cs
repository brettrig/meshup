using System.Collections.ObjectModel;
using Meshtastic.Protobufs;
using MeshUp.Maui.Models;
using Plugin.BLE.Abstractions.Contracts;
using Contact = MeshUp.Maui.Models.Contact;

namespace MeshUp.Maui.Services;

/// <summary>
/// A friendly, WhatsApp-like abstraction over <see cref="MeshtasticBleService"/> that hides
/// Meshtastic-specific concepts (node numbers, channel indexes, protobuf packet shapes) from
/// the UI. Exposes a single "Everyone" broadcast thread plus a contact list of nodes the radio
/// has seen, along with helpers to send/receive messages for either.
/// </summary>
public class MeshtasticChatService
{
    /// <summary>The channel index used for all sends. Hidden from the UI; always the radio's primary channel.</summary>
    private const uint PrimaryChannelIndex = 0;

    private readonly MeshtasticBleService _bleService;
    private readonly ContactNameLookupService _contactNameLookupService;
    private readonly ChatHistoryStore _historyStore;
    private readonly Dictionary<uint, Contact> _contactsByNodeNum = new();

    private uint? _myNodeNum;
    private Task? _loadHistoryTask;

    public MeshtasticChatService(MeshtasticBleService bleService, ContactNameLookupService contactNameLookupService, ChatHistoryStore historyStore)
    {
        _bleService = bleService;
        _contactNameLookupService = contactNameLookupService;
        _historyStore = historyStore;

        _bleService.DeviceDiscovered += (_, device) => DeviceDiscovered?.Invoke(this, device);
        _bleService.NodeInfoReceived += OnMyNodeInfoReceived;
        _bleService.NodeUpdated += OnNodeUpdated;
        _bleService.TextMessageReceived += OnTextMessageReceived;
        _bleService.ConfigComplete += (_, e) => ConfigComplete?.Invoke(this, e);
    }

    /// <summary>
    /// Restores previously persisted contacts and messages from local storage, so chat history
    /// survives app restarts/redeploys. Should be called once at app startup, before connecting.
    /// Safe to call multiple times (e.g. from a page's OnAppearing firing more than once): the
    /// underlying load only ever runs once per app session, since this service is a singleton
    /// and re-running it would duplicate every contact and message.
    /// </summary>
    public Task LoadHistoryAsync()
    {
        return _loadHistoryTask ??= LoadHistoryCoreAsync();
    }

    private async Task LoadHistoryCoreAsync()
    {
        var contactRecords = await _historyStore.LoadContactsAsync();
        foreach (var record in contactRecords)
        {
            var contact = new Contact(record.NodeNum)
            {
                ShortName = record.ShortName,
                LongName = record.LongName,
                DisplayNameOverride = record.DisplayNameOverride,
                LastMessagePreview = record.LastMessagePreview,
                LastMessageAt = record.LastMessageAt,
                IsFavourite = record.IsFavourite,
            };

            _contactsByNodeNum[record.NodeNum] = contact;
            Contacts.Add(contact);
        }

        var messageRecords = await _historyStore.LoadMessagesAsync();
        foreach (var record in messageRecords)
        {
            var message = new ChatMessage
            {
                From = record.From,
                To = record.To,
                Text = record.Text,
                Timestamp = record.Timestamp,
                IsMine = record.IsMine,
                IsBroadcast = record.IsBroadcast,
                SenderName = record.SenderName,
            };

            if (message.IsBroadcast)
            {
                EveryoneMessages.Add(message);
            }
            else
            {
                var otherNodeNum = message.IsMine ? message.To : message.From;
                GetOrCreateThread(otherNodeNum).Add(message);
            }
        }
    }

    private void PersistContact(Contact contact)
    {
        _ = _historyStore.SaveContactAsync(new ContactRecord
        {
            NodeNum = contact.NodeNum,
            ShortName = contact.ShortName,
            LongName = contact.LongName,
            DisplayNameOverride = contact.DisplayNameOverride,
            LastMessagePreview = contact.LastMessagePreview,
            LastMessageAt = contact.LastMessageAt,
            IsFavourite = contact.IsFavourite,
        });
    }

    private void PersistMessage(ChatMessage message)
    {
        _ = _historyStore.SaveMessageAsync(new ChatMessageRecord
        {
            From = message.From,
            To = message.To,
            Text = message.Text,
            Timestamp = message.Timestamp,
            IsMine = message.IsMine,
            IsBroadcast = message.IsBroadcast,
            SenderName = message.SenderName,
        });
    }

    /// <summary>Everyone/broadcast messages, in the order received.</summary>
    public ObservableCollection<ChatMessage> EveryoneMessages { get; } = new();

    /// <summary>Known contacts (mesh nodes) the radio has told us about, other than ourselves.</summary>
    public ObservableCollection<Contact> Contacts { get; } = new();

    /// <summary>Direct-message threads, keyed by contact node number.</summary>
    public Dictionary<uint, ObservableCollection<ChatMessage>> DirectMessagesByNodeNum { get; } = new();

    public bool IsConnected => _bleService.IsConnected;

    public event EventHandler<IDevice>? DeviceDiscovered;

    /// <summary>Raised whenever a new message is added to the Everyone thread.</summary>
    public event EventHandler<ChatMessage>? EveryoneMessageReceived;

    /// <summary>Raised whenever a new message is added to a contact's direct-message thread.</summary>
    public event EventHandler<(Contact Contact, ChatMessage Message)>? DirectMessageReceived;

    /// <summary>Raised whenever a contact is added or updated (e.g. name learned/changed).</summary>
    public event EventHandler<Contact>? ContactUpdated;

    /// <summary>Raised when the radio finishes streaming its initial config/node database.</summary>
    public event EventHandler? ConfigComplete;

    public Task ScanForDevicesAsync(CancellationToken cancellationToken = default) =>
        _bleService.ScanForDevicesAsync(cancellationToken);

    public Task StopScanAsync() => _bleService.StopScanAsync();

    public Task<bool> ConnectAsync(IDevice device, CancellationToken cancellationToken = default) =>
        _bleService.ConnectAsync(device, cancellationToken);

    /// <summary>
    /// Attempts to reconnect to the last successfully connected radio (remembered across app
    /// restarts). Returns false if there's no remembered device, or if it couldn't be reached.
    /// </summary>
    public Task<bool> TryReconnectToLastDeviceAsync(CancellationToken cancellationToken = default) =>
        _bleService.TryReconnectToLastDeviceAsync(cancellationToken);

    public Task RequestNodeInfoAsync(CancellationToken cancellationToken = default) =>
        _bleService.RequestNodeInfoAsync(cancellationToken);

    public Task DisconnectAsync() => _bleService.DisconnectAsync();

    /// <summary>Sends a plain-text message to everyone on the mesh's primary channel.</summary>
    public async Task SendToEveryoneAsync(string text, ChatMessage? replyTo = null, CancellationToken cancellationToken = default)
    {
        var fullText = BuildTextWithQuote(text, replyTo);
        await _bleService.SendTextMessageAsync(fullText, MeshtasticBleService.BroadcastNodeNum, PrimaryChannelIndex, cancellationToken);

        var message = new ChatMessage
        {
            From = _myNodeNum ?? 0,
            To = MeshtasticBleService.BroadcastNodeNum,
            Text = fullText,
            IsMine = true,
            IsBroadcast = true,
            SenderName = "You"
        };

        EveryoneMessages.Add(message);
        PersistMessage(message);
        EveryoneMessageReceived?.Invoke(this, message);
    }

    /// <summary>Sends a plain-text direct message to a specific contact.</summary>
    public async Task SendToContactAsync(Contact contact, string text, ChatMessage? replyTo = null, CancellationToken cancellationToken = default)
    {
        var fullText = BuildTextWithQuote(text, replyTo);
        await _bleService.SendTextMessageAsync(fullText, contact.NodeNum, PrimaryChannelIndex, cancellationToken);

        var message = new ChatMessage
        {
            From = _myNodeNum ?? 0,
            To = contact.NodeNum,
            Text = fullText,
            IsMine = true,
            IsBroadcast = false,
            SenderName = "You"
        };

        GetOrCreateThread(contact.NodeNum).Add(message);
        PersistMessage(message);
        contact.LastMessagePreview = fullText;
        contact.LastMessageAt = message.Timestamp;
        PersistContact(contact);
        DirectMessageReceived?.Invoke(this, (contact, message));
    }

    /// <summary>
    /// Builds the wire text for a message, prepending a quoted reference to the message being
    /// replied to (if any) as a leading "&gt; Sender: snippet" line so it displays sensibly (and can
    /// be parsed back out, see <see cref="ChatMessage.HasQuote"/>) on every device.
    /// </summary>
    private static string BuildTextWithQuote(string text, ChatMessage? replyTo)
    {
        if (replyTo is null)
        {
            return text;
        }

        var quotedText = replyTo.DisplayText;
        if (quotedText.Length > 60)
        {
            quotedText = quotedText[..60] + "...";
        }

        return $"> {replyTo.SenderName}: {quotedText}\n{text}";
    }

    /// <summary>Gets (creating if needed) the direct-message thread for a contact's node number.</summary>
    public ObservableCollection<ChatMessage> GetOrCreateThread(uint nodeNum)
    {
        if (!DirectMessagesByNodeNum.TryGetValue(nodeNum, out var thread))
        {
            thread = new ObservableCollection<ChatMessage>();
            DirectMessagesByNodeNum[nodeNum] = thread;
        }

        return thread;
    }

    /// <summary>Toggles whether a contact is "liked"/favourited, persisting the change and notifying listeners.</summary>
    public void ToggleFavourite(Contact contact)
    {
        contact.IsFavourite = !contact.IsFavourite;
        PersistContact(contact);
        ContactUpdated?.Invoke(this, contact);
    }

    /// <summary>
    /// Toggles "echo" mode for a contact: while enabled, any message received from them is
    /// immediately sent back unmodified, which is handy for testing mesh range between two
    /// devices. Not persisted; resets to off on app restart.
    /// </summary>
    public void ToggleEcho(Contact contact)
    {
        contact.IsEcho = !contact.IsEcho;
        ContactUpdated?.Invoke(this, contact);
    }

    private void OnMyNodeInfoReceived(object? sender, MyNodeInfo info)
    {
        _myNodeNum = info.MyNodeNum;
    }

    private void OnNodeUpdated(object? sender, NodeInfo info)
    {
        // Don't list ourselves as a contact.
        if (_myNodeNum is not null && info.Num == _myNodeNum)
        {
            return;
        }

        var contact = GetOrCreateContact(info.Num);

        if (info.User is not null)
        {
            contact.ShortName = info.User.ShortName;
            contact.LongName = info.User.LongName;
        }

        PersistContact(contact);
        ContactUpdated?.Invoke(this, contact);

        _ = ApplyContactNameOverrideAsync(contact);
    }

    /// <summary>
    /// Looks up the phone's contacts for one whose email address exactly matches this node's
    /// reported name (short or long name), and if found, overrides the contact's display name
    /// with the phone contact's name so it's used everywhere (sender labels, thread names, etc.).
    /// </summary>
    private async Task ApplyContactNameOverrideAsync(Contact contact)
    {
        foreach (var deviceName in new[] { contact.LongName, contact.ShortName })
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                continue;
            }

            var matchedName = await _contactNameLookupService.TryFindNameByEmailAsync(deviceName);
            if (string.IsNullOrWhiteSpace(matchedName))
            {
                continue;
            }

            if (contact.DisplayNameOverride != matchedName)
            {
                contact.DisplayNameOverride = matchedName;
                PersistContact(contact);
                ContactUpdated?.Invoke(this, contact);
            }

            return;
        }
    }

    private Contact GetOrCreateContact(uint nodeNum)
    {
        if (!_contactsByNodeNum.TryGetValue(nodeNum, out var contact))
        {
            contact = new Contact(nodeNum);
            _contactsByNodeNum[nodeNum] = contact;
            Contacts.Add(contact);
        }

        return contact;
    }

    private void OnTextMessageReceived(object? sender, MeshtasticTextMessage message)
    {
        var isBroadcast = message.To == MeshtasticBleService.BroadcastNodeNum;
        var isMine = _myNodeNum is not null && message.From == _myNodeNum;
        var senderName = isMine ? "You" : GetOrCreateContact(message.From).DisplayName;

        var chatMessage = new ChatMessage
        {
            From = message.From,
            To = message.To,
            Text = message.Text,
            IsMine = isMine,
            IsBroadcast = isBroadcast,
            SenderName = senderName
        };

        if (isBroadcast)
        {
            EveryoneMessages.Add(chatMessage);
            PersistMessage(chatMessage);
            EveryoneMessageReceived?.Invoke(this, chatMessage);
            return;
        }

        // A direct message either to or from a contact; the "other side" of the conversation
        // is whichever of From/To isn't us.
        var otherNodeNum = chatMessage.IsMine ? message.To : message.From;
        var contact = GetOrCreateContact(otherNodeNum);

        GetOrCreateThread(otherNodeNum).Add(chatMessage);
        PersistMessage(chatMessage);
        contact.LastMessagePreview = message.Text;
        contact.LastMessageAt = chatMessage.Timestamp;
        PersistContact(contact);
        DirectMessageReceived?.Invoke(this, (contact, chatMessage));

        // Echo mode: bounce the message straight back to whoever sent it, for range testing
        // between two devices. Only applies to messages we actually received (not our own sends).
        if (!isMine && contact.IsEcho)
        {
            _ = SendToContactAsync(contact, $"Echo: {message.Text}");
        }
    }
}
