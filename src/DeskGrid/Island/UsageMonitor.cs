using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using DeskGrid.Diagnostics;

namespace DeskGrid.Island;

/// <summary>The user's usage settings: the limit (millions of tokens) within a number of hours, and where the warning starts.</summary>
internal sealed record UsageOptions(double LimitMillions, int Hours, int WarnPercent)
{
    public long LimitTokens => (long)(LimitMillions * 1_000_000);
}

/// <summary>How far usage has come against the limit.</summary>
internal enum UsageLevel
{
    Fine,
    Warn,
    Full,
}

internal static class UsageRules
{
    public static double Percent(long tokens, UsageOptions options) =>
        options.LimitTokens <= 0 ? 0 : 100.0 * tokens / options.LimitTokens;

    public static UsageLevel LevelOf(long tokens, UsageOptions options)
    {
        var percent = Percent(tokens, options);
        return percent >= 100 ? UsageLevel.Full : percent >= options.WarnPercent ? UsageLevel.Warn : UsageLevel.Fine;
    }

    /// <summary>"4.1M" / "820K" / "950": tokens the short way.</summary>
    public static string Short(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M",
        >= 1_000 => $"{tokens / 1_000.0:0.#}K",
        _ => tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Adds up the tokens Claude Code used from its session logs (one JSON object per line). A message is written several
/// times while it streams, so each message counts once (the largest it reported). Input, output and cache-creation tokens
/// count; reading the cache does not.
/// </summary>
internal sealed class UsageCounter(DateTimeOffset since)
{
    private readonly Dictionary<string, long> _byMessage = new(StringComparer.Ordinal);
    private int _anonymous;

    public long Total => _byMessage.Values.Sum();

    public void Add(string line)
    {
        // Most lines are not about usage; the cheap test keeps them from being parsed.
        if (!line.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() != "assistant"
                || !root.TryGetProperty("message", out var message)
                || !message.TryGetProperty("usage", out var usage)
                || !root.TryGetProperty("timestamp", out var stamp)
                || !DateTimeOffset.TryParse(stamp.GetString(), out var time)
                || time < since)
            {
                return;
            }

            var tokens = Read(usage, "input_tokens") + Read(usage, "output_tokens") + Read(usage, "cache_creation_input_tokens");
            var id = message.TryGetProperty("id", out var messageId) ? messageId.GetString() : null;
            var request = root.TryGetProperty("requestId", out var requestId) ? requestId.GetString() : null;
            var key = id is null ? "#" + _anonymous++ : id + "/" + request;
            _byMessage[key] = _byMessage.TryGetValue(key, out var before) ? Math.Max(before, tokens) : tokens;
        }
        catch (JsonException)
        {
            // A line that is cut off (the file is being written): not counted.
        }
    }

    private static long Read(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
}

/// <summary>
/// Watches the AI usage: every minute (only while on) the logs of Claude Code under <c>~/.claude/projects</c> are read on a
/// pool thread, the tokens of the last hours are added up, and <see cref="Alert"/> is raised when usage reaches the
/// warning percentage or the limit. Everything stays on this computer.
/// </summary>
internal sealed class UsageMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly string _root;
    private UsageOptions _options;
    private UsageLevel _level = UsageLevel.Fine;
    private bool _reading;
    private bool _disposed;

    public UsageMonitor(UsageOptions options, string? root = null)
    {
        _options = options;
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        Check();
    }

    /// <summary>The tokens counted at the last reading, or <see langword="null"/> before the first.</summary>
    public long? Tokens { get; private set; }

    public UsageOptions Options => _options;

    /// <summary>Raised on the dispatcher thread when a level is reached that was not before.</summary>
    public event Action<UsageLevel, long>? Alert;

    public void Configure(UsageOptions options)
    {
        _options = options;
        if (Tokens is { } tokens)
        {
            _level = UsageRules.LevelOf(tokens, options); // The new limit's state is not news.
        }

        Check();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    internal static long Count(string root, int hours)
    {
        var since = DateTimeOffset.Now.AddHours(-hours);
        var counter = new UsageCounter(since);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < since.UtcDateTime)
                {
                    continue; // Not touched since: nothing in it is recent.
                }

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    counter.Add(line);
                }
            }
            catch (IOException)
            {
                // A log that is busy or gone: left out of this reading.
            }
        }

        return counter.Total;
    }

    private void Check()
    {
        if (_reading || _disposed)
        {
            return;
        }

        _reading = true;
        var (root, hours) = (_root, Math.Max(1, _options.Hours));
        _ = Task.Run(() => Count(root, hours)).ContinueWith(task =>
        {
            _reading = false;
            if (_disposed)
            {
                return;
            }

            if (task.IsFaulted)
            {
                Log.Error("Reading the AI usage failed", task.Exception!.GetBaseException());
                return;
            }

            Tokens = task.Result;
            var level = UsageRules.LevelOf(task.Result, _options);
            if (level > _level)
            {
                Alert?.Invoke(level, task.Result);
            }

            _level = level;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
