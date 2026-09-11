namespace MeshUp.Maui.Models;

/// <summary>
/// A single chat message, either in the "Everyone" broadcast thread or a direct
/// message thread with a specific contact. Presents a WhatsApp-like shape to the UI.
/// </summary>
public class ChatMessage
{
    public required uint From { get; init; }

    public required uint To { get; init; }

    public required string Text { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>True if this message was sent by the local user (this phone/app instance).</summary>
    public bool IsMine { get; init; }

    /// <summary>True if this message went to everyone on the channel rather than a specific contact.</summary>
    public bool IsBroadcast { get; init; }

    /// <summary>Friendly display name of whoever sent this message (e.g. "You" or the contact's name).</summary>
    public string SenderName { get; init; } = "Unknown";

    /// <summary>True when this message was sent by someone other than the local user.</summary>
    public bool IsFromSomeoneElse => !IsMine;

    /// <summary>
    /// The message text with any leading quote block (see <see cref="QuotedSenderName"/>/<see cref="QuotedText"/>)
    /// stripped off, ready to display as the message's own content.
    /// </summary>
    public string DisplayText => ParseQuote().remaining;

    /// <summary>The sender name of the message being replied to, if this message is a reply/quote.</summary>
    public string? QuotedSenderName => ParseQuote().sender;

    /// <summary>The quoted snippet of the message being replied to, if this message is a reply/quote.</summary>
    public string? QuotedText => ParseQuote().text;

    /// <summary>True if this message is a reply that quotes another message.</summary>
    public bool HasQuote => QuotedText is not null;

    // Reply/quote support piggybacks on the plain-text Meshtastic protocol: a reply is sent as a
    // leading "> Sender: quoted text" line followed by a newline and the new message text, so it
    // displays sensibly (and can be parsed back out for nicer rendering) on every device, with no
    // wire-format changes required.
    private static readonly System.Text.RegularExpressions.Regex QuotePrefixRegex =
        new(@"^> (?<sender>[^:\r\n]{1,40}): (?<text>[^\r\n]*)\r?\n", System.Text.RegularExpressions.RegexOptions.Compiled);

    private (string? sender, string? text, string remaining) ParseQuote()
    {
        var match = QuotePrefixRegex.Match(Text);
        if (!match.Success)
        {
            return (null, null, Text);
        }

        return (match.Groups["sender"].Value, match.Groups["text"].Value, Text[match.Length..]);
    }
}

