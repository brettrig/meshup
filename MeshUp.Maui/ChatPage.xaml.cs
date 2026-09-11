using System.Collections.ObjectModel;
using System.Collections.Specialized;
using MeshUp.Maui.Models;
using MeshUp.Maui.Services;
using Contact = MeshUp.Maui.Models.Contact;

namespace MeshUp.Maui;

/// <summary>
/// A single chat thread: either the "Everyone" broadcast thread, or a direct-message
/// thread with a specific contact. Shows a WhatsApp-style bubble list plus a send bar.
/// </summary>
[QueryProperty(nameof(NodeNum), "nodeNum")]
[QueryProperty(nameof(IsEveryone), "isEveryone")]
public partial class ChatPage : ContentPage
{
    private readonly MeshtasticChatService _chatService;
    private readonly Services.AppForegroundState _foregroundState;
    private Contact? _contact;
    private INotifyCollectionChanged? _observableMessages;
    private ChatMessage? _replyTarget;

    public ChatPage(MeshtasticChatService chatService, Services.AppForegroundState foregroundState)
    {
        InitializeComponent();
        _chatService = chatService;
        _foregroundState = foregroundState;
    }

    public string NodeNum { get; set; } = "0";

    public string IsEveryone { get; set; } = "false";

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ClearReply();

        var nodeNum = uint.Parse(NodeNum);
        var isEveryone = bool.Parse(IsEveryone);

        if (isEveryone)
        {
            Title = "Everyone";
            SetMessagesSource(_chatService.EveryoneMessages);

            if (ToolbarItems.Contains(FavouriteToolbarItem))
            {
                ToolbarItems.Remove(FavouriteToolbarItem);
            }

            if (ToolbarItems.Contains(EchoToolbarItem))
            {
                ToolbarItems.Remove(EchoToolbarItem);
            }
        }
        else
        {
            _contact = _chatService.Contacts.FirstOrDefault(c => c.NodeNum == nodeNum);
            Title = _contact?.DisplayName ?? $"Node {nodeNum}";
            SetMessagesSource(_chatService.GetOrCreateThread(nodeNum));

            if (_contact is not null && !ToolbarItems.Contains(FavouriteToolbarItem))
            {
                ToolbarItems.Add(FavouriteToolbarItem);
            }

            if (_contact is not null && !ToolbarItems.Contains(EchoToolbarItem))
            {
                ToolbarItems.Add(EchoToolbarItem);
            }

            UpdateFavouriteToolbarText();
            UpdateEchoToolbarText();
        }

        _chatService.ContactUpdated += OnContactUpdated;

        _foregroundState.SetVisibleThread(isEveryone, nodeNum);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _chatService.ContactUpdated -= OnContactUpdated;

        _foregroundState.ClearVisibleThread();

        if (_observableMessages is not null)
        {
            _observableMessages.CollectionChanged -= OnMessagesCollectionChanged;
            _observableMessages = null;
        }
    }

    private void OnContactUpdated(object? sender, Contact contact)
    {
        if (_contact is null || contact.NodeNum != _contact.NodeNum)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Title = contact.DisplayName;
            UpdateFavouriteToolbarText();
            UpdateEchoToolbarText();
        });
    }

    private void SetMessagesSource(System.Collections.IEnumerable messages)
    {
        if (_observableMessages is not null)
        {
            _observableMessages.CollectionChanged -= OnMessagesCollectionChanged;
        }

        MessagesList.ItemsSource = messages;

        if (messages is INotifyCollectionChanged observable)
        {
            _observableMessages = observable;
            _observableMessages.CollectionChanged += OnMessagesCollectionChanged;
        }

        ScrollToLastMessage();
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToLastMessage();

    private void ScrollToLastMessage()
    {
        // Defer to the next UI cycle: this can be triggered synchronously from inside an
        // ObservableCollection.Add() call (e.g. while sending a message), and the CollectionView
        // may not have realized the new item yet, which makes ScrollTo throw ("Invalid target
        // position"). Dispatching also keeps any such failure from bubbling back into the caller
        // (e.g. the send-message flow) and surfacing as a false "message not sent" error.
        Dispatcher.Dispatch(() =>
        {
            try
            {
                if (MessagesList.ItemsSource is System.Collections.IEnumerable items)
                {
                    object? last = null;
                    foreach (var item in items)
                    {
                        last = item;
                    }

                    if (last is not null)
                    {
                        MessagesList.ScrollTo(last, position: ScrollToPosition.End, animate: true);
                    }
                }
            }
            catch
            {
                // Best-effort only; failing to auto-scroll should never break sending/receiving messages.
            }
        });
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var text = MessageEntry.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            SendButton.IsEnabled = false;

            if (bool.Parse(IsEveryone))
            {
                await _chatService.SendToEveryoneAsync(text, _replyTarget);
            }
            else if (_contact is not null)
            {
                await _chatService.SendToContactAsync(_contact, text, _replyTarget);
            }

            MessageEntry.Text = string.Empty;
            ClearReply();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Message not sent", ex.Message, "OK");
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private void OnReplyTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ChatMessage message)
        {
            return;
        }

        _replyTarget = message;

        ReplyPreviewSender.Text = message.SenderName;
        ReplyPreviewText.Text = message.DisplayText;
        ReplyPreviewBar.IsVisible = true;
    }

    private void OnCancelReplyTapped(object? sender, TappedEventArgs e) => ClearReply();

    private void OnFavouriteClicked(object? sender, EventArgs e)
    {
        if (_contact is null)
        {
            return;
        }

        _chatService.ToggleFavourite(_contact);
        UpdateFavouriteToolbarText();
    }

    private void UpdateFavouriteToolbarText()
    {
        FavouriteToolbarItem.Text = _contact?.IsFavourite == true ? "Unlike" : "Like";
    }

    private void OnEchoClicked(object? sender, EventArgs e)
    {
        if (_contact is null)
        {
            return;
        }

        _chatService.ToggleEcho(_contact);
        UpdateEchoToolbarText();
    }

    private void UpdateEchoToolbarText()
    {
        EchoToolbarItem.Text = _contact?.IsEcho == true ? "Echo: On" : "Echo: Off";
    }

    private void ClearReply()
    {
        _replyTarget = null;
        ReplyPreviewBar.IsVisible = false;
    }
}
