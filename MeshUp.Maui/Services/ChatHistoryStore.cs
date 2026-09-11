using SQLite;

namespace MeshUp.Maui.Services;

/// <summary>
/// Persists chat contacts and messages to a local SQLite database in the app's data directory,
/// so history survives app restarts and redeploys (which otherwise wipe the in-memory-only state
/// held by <see cref="MeshtasticChatService"/>).
/// </summary>
public class ChatHistoryStore
{
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _initGate.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "chathistory.db3");
            var connection = new SQLiteAsyncConnection(dbPath);
            await connection.CreateTableAsync<ContactRecord>();
            await connection.CreateTableAsync<ChatMessageRecord>();
            _connection = connection;
            return _connection;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>Loads every persisted contact, in no particular order.</summary>
    public async Task<List<ContactRecord>> LoadContactsAsync()
    {
        try
        {
            var connection = await GetConnectionAsync();
            return await connection.Table<ContactRecord>().ToListAsync();
        }
        catch
        {
            // Corrupt/unavailable local store shouldn't prevent the app from starting fresh.
            return new List<ContactRecord>();
        }
    }

    /// <summary>Loads every persisted message, ordered oldest-to-newest.</summary>
    public async Task<List<ChatMessageRecord>> LoadMessagesAsync()
    {
        try
        {
            var connection = await GetConnectionAsync();
            return await connection.Table<ChatMessageRecord>().OrderBy(m => m.Timestamp).ToListAsync();
        }
        catch
        {
            return new List<ChatMessageRecord>();
        }
    }

    /// <summary>Inserts or updates a contact's persisted state, keyed by node number.</summary>
    public async Task SaveContactAsync(ContactRecord record)
    {
        try
        {
            var connection = await GetConnectionAsync();
            await connection.InsertOrReplaceAsync(record);
        }
        catch
        {
            // Best-effort persistence; don't let storage failures disrupt live chat.
        }
    }

    /// <summary>Appends a new message to the persisted history.</summary>
    public async Task SaveMessageAsync(ChatMessageRecord record)
    {
        try
        {
            var connection = await GetConnectionAsync();
            await connection.InsertAsync(record);
        }
        catch
        {
            // Best-effort persistence; don't let storage failures disrupt live chat.
        }
    }
}

/// <summary>Persisted representation of a <see cref="Models.Contact"/>.</summary>
[Table("Contacts")]
public class ContactRecord
{
    [PrimaryKey]
    public uint NodeNum { get; set; }

    public string? ShortName { get; set; }

    public string? LongName { get; set; }

    public string? DisplayNameOverride { get; set; }

    public string? LastMessagePreview { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }

    public bool IsFavourite { get; set; }
}

/// <summary>Persisted representation of a <see cref="Models.ChatMessage"/>.</summary>
[Table("ChatMessages")]
public class ChatMessageRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public uint From { get; set; }

    public uint To { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public bool IsMine { get; set; }

    public bool IsBroadcast { get; set; }

    public string SenderName { get; set; } = "Unknown";
}
