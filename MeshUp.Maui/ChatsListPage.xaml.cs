using System.Collections.ObjectModel;
using MeshUp.Maui.Models;
using MeshUp.Maui.Services;
using Contact = MeshUp.Maui.Models.Contact;

namespace MeshUp.Maui;

/// <summary>
/// The main "home" screen: a WhatsApp-style list of chat threads. Always shows an
/// "Everyone" broadcast thread pinned at the top, plus one row per known contact (mesh node).
/// </summary>
public partial class ChatsListPage : ContentPage
{
    private readonly MeshtasticChatService _chatService;
    private readonly Contact _everyoneContact = new(MeshtasticBleService.BroadcastNodeNum)
    {
        ShortName = "Everyone",
        LastMessagePreview = "Say hello to the mesh!"
    };

    private readonly ObservableCollection<Contact> _threads = new();
    private bool _hasAttemptedAutoReconnect;
    private bool _hasLoadedHistory;

    public ChatsListPage(MeshtasticChatService chatService)
    {
        InitializeComponent();

        _chatService = chatService;
        _threads.Add(_everyoneContact);
        ThreadsList.ItemsSource = _threads;

        foreach (var contact in _chatService.Contacts)
        {
            _threads.Add(contact);
        }

        SortThreads();

        _chatService.ContactUpdated += OnContactUpdated;
        _chatService.EveryoneMessageReceived += OnEveryoneMessageReceived;
        _chatService.DirectMessageReceived += OnDirectMessageReceived;

        UpdateConnectionBanner();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateConnectionBanner();

        if (!_hasLoadedHistory)
        {
            _hasLoadedHistory = true;
            _ = LoadHistoryAsync();
        }

        if (!_hasAttemptedAutoReconnect && !_chatService.IsConnected)
        {
            _hasAttemptedAutoReconnect = true;
            _ = AutoReconnectAsync();
        }
    }

    /// <summary>
    /// Restores persisted contacts/messages from local storage (see
    /// <see cref="MeshtasticChatService.LoadHistoryAsync"/>) and refreshes the thread list to
    /// reflect them. Must run before contacts are otherwise relied upon (e.g. auto-reconnect
    /// updating node info), so it's kicked off first on the initial appearance.
    /// </summary>
    private async Task LoadHistoryAsync()
    {
        try
        {
            await _chatService.LoadHistoryAsync();
        }
        catch
        {
            // Best-effort restore; a failure here shouldn't prevent the app from being usable.
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var contact in _chatService.Contacts)
            {
                if (!_threads.Contains(contact))
                {
                    _threads.Add(contact);
                }
            }

            SortThreads();
        });
    }

    /// <summary>
    /// Silently tries to reconnect to the last radio we were paired with, so the app comes back
    /// up already connected after being closed and reopened. Failures are ignored - the user can
    /// always fall back to the manual "Connect radio" setup flow.
    /// </summary>
    private async Task AutoReconnectAsync()
    {
        try
        {
            var reconnected = await _chatService.TryReconnectToLastDeviceAsync();
            if (reconnected)
            {
                await _chatService.RequestNodeInfoAsync();
            }
        }
        catch
        {
            // Ignored - the connection banner will simply stay visible and the user can reconnect manually.
        }
        finally
        {
            UpdateConnectionBanner();
        }
    }

    private void UpdateConnectionBanner()
    {
        ConnectionBanner.IsVisible = !_chatService.IsConnected;
    }

    private void OnContactUpdated(object? sender, Contact contact)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_threads.Contains(contact))
            {
                _threads.Add(contact);
            }

            SortThreads();
        });
    }

    private void OnEveryoneMessageReceived(object? sender, ChatMessage message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _everyoneContact.LastMessagePreview = message.IsMine ? $"You: {message.Text}" : message.Text;
            _everyoneContact.LastMessageAt = message.Timestamp;
            SortThreads();
        });
    }

    private void OnDirectMessageReceived(object? sender, (Contact Contact, ChatMessage Message) e)
    {
        MainThread.BeginInvokeOnMainThread(SortThreads);
    }

    /// <summary>
    /// Re-orders the chat list so it always reads: "Everyone" first, then favourited contacts
    /// alphabetically, then everyone else by most recently active first.
    /// </summary>
    private void SortThreads()
    {
        var sorted = _threads
            .OrderBy(c => c == _everyoneContact ? 0 : c.IsFavourite ? 1 : 2)
            .ThenBy(c => c.IsFavourite ? c.DisplayName : null, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(c => c.IsFavourite ? null : c.LastMessageAt)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            var current = _threads.IndexOf(sorted[i]);
            if (current != i)
            {
                _threads.Move(current, i);
            }
        }
    }

    private async void OnThreadSelected(object? sender, SelectionChangedEventArgs e)
    {
        ThreadsList.SelectedItem = null;

        if (e.CurrentSelection.FirstOrDefault() is not Contact contact)
        {
            return;
        }

        var isEveryone = contact.NodeNum == MeshtasticBleService.BroadcastNodeNum;
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?nodeNum={contact.NodeNum}&isEveryone={isEveryone}");
    }

    private async void OnSetupClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync($"///{nameof(SetupPage)}");
    }
}
