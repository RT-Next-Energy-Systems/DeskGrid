// The way Claude Code's usage is read here follows ccusage (https://github.com/ryoppippi/ccusage):
// the data directories, the deduplication of messages written more than once (and of those a sub-agent replays),
// the token total, and the 5-hour session blocks. Ported to C# from its Rust sources, not copied.
// ccusage: MIT License, Copyright (c) 2025 ryoppippi. The full notice is in the README ("Third-party notices").

using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using DeskGrid.Diagnostics;

namespace DeskGrid.Island;

/// <summary>
/// The user's usage settings: the limit (millions of tokens in one session block; 0 = the highest earlier block, as ccusage's
/// "max"), the length of a session block in hours (Claude's is 5), and where the warning starts.
/// </summary>
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

/// <summary>
/// One reading: the tokens of the session block that is running now (0 when none is), when that block ends, and the most
/// tokens any earlier block used (the limit when the user set none).
/// </summary>
internal sealed record UsageReading(long Tokens, DateTimeOffset? Resets, long HighestEarlier, PlanUsage? Plan = null, IReadOnlyList<ToolUsage>? Others = null)
{
    public static readonly UsageReading None = new(0, null, 0);

    /// <summary>The limit to measure against: the user's, or else the highest earlier block.</summary>
    public long LimitFor(UsageOptions options) => options.LimitTokens > 0 ? options.LimitTokens : HighestEarlier;
}

internal static class UsageRules
{
    public static double Percent(long tokens, long limit) => limit <= 0 ? 0 : 100.0 * tokens / limit;

    /// <summary>Claude's own percentage when it reported one, else the tokens against the limit.</summary>
    public static double Percent(UsageReading reading, UsageOptions options) => reading.Plan?.FivePercent ?? Percent(reading.Tokens, reading.LimitFor(options));

    public static UsageLevel LevelOf(UsageReading reading, UsageOptions options)
    {
        var percent = Percent(reading, options);
        return percent >= 100 ? UsageLevel.Full : percent >= options.WarnPercent ? UsageLevel.Warn : UsageLevel.Fine;
    }

    /// <summary>"4.1M" / "820K" / "950": tokens the short way.</summary>
    public static string Short(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M",
        >= 1_000 => $"{tokens / 1_000.0:0.#}K",
        _ => tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>"2h 05m" / "45m": the time left until <paramref name="end"/>.</summary>
    public static string Left(DateTimeOffset end, DateTimeOffset now)
    {
        var left = end - now;
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        // Under an hour the minutes round up, so 59 minutes and a half is "1h 00m", not "60m".
        var minutes = (int)Math.Ceiling(left.TotalMinutes);
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes:00}m" : minutes >= 60 ? "1h 00m" : $"{minutes}m";
    }
}

/// <summary>One counted response of the model: when, how many tokens in all.</summary>
internal readonly record struct UsageEntry(DateTimeOffset Time, long Tokens);

/// <summary>
/// Turns the lines of Claude Code's session logs (one JSON object per line) into counted responses, each once. Claude Code
/// writes a line per content block, all with the response's <c>message.id</c> and <c>requestId</c>; a sub-agent
/// ("sidechain") may replay a parent's message. Of the copies of one response the main-thread one wins, else the one with the
/// most tokens. Tokens are input + output + cache creation + cache reads, as ccusage counts them.
/// </summary>
internal sealed class UsageDeduper
{
    private readonly List<Item> _items = [];
    private readonly Dictionary<string, int> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _replay = new(StringComparer.Ordinal);

    public IEnumerable<UsageEntry> Entries => _items.Select(item => new UsageEntry(item.Time, item.Tokens));

    /// <summary>Reads one log line; <paramref name="fileSession"/> is the session when the line names none (the file's name).</summary>
    public void Add(string line, string fileSession)
    {
        if (Parse(line, fileSession) is { } item)
        {
            Add(item);
        }
    }

    internal void Add(Item item)
    {
        if (item.MessageId is not { } id)
        {
            _items.Add(item);
            return;
        }

        var exact = ExactKey(item);
        if (_exact.TryGetValue(exact, out var index))
        {
            Merge(index, item);
            return;
        }

        var replay = id + "\n" + item.Session;
        if (_replay.TryGetValue(replay, out index)
            && (item.Sidechain || _items[index].Sidechain)
            && ((item.RequestId is null && _items[index].RequestId is null) || item.Time == _items[index].Time))
        {
            Merge(index, item);
            _exact[exact] = index;
            return;
        }

        _items.Add(item);
        _exact[exact] = _items.Count - 1;
        _replay.TryAdd(replay, _items.Count - 1);
    }

    // A request id makes a response unique. Without one, a real Anthropic id ("msg_", "resp_") is unique per session;
    // other ids (placeholders some gateways reuse) are told apart by their time as well.
    private static string ExactKey(Item item) =>
        item.RequestId is { } request ? item.MessageId + "\n" + request
        : item.MessageId!.StartsWith("msg_", StringComparison.Ordinal) || item.MessageId.StartsWith("resp_", StringComparison.Ordinal)
            ? item.MessageId + "\n\n" + item.Session
            : item.MessageId + "\n\n" + item.Session + "\n" + item.Time.UtcTicks;

    private void Merge(int index, Item candidate)
    {
        var existing = _items[index];
        var replace = candidate.Sidechain != existing.Sidechain ? existing.Sidechain : candidate.Tokens > existing.Tokens;
        if (replace)
        {
            _items[index] = candidate;
        }
    }

    internal static Item? Parse(string line, string fileSession) => Parse(System.Text.Encoding.UTF8.GetBytes(line), fileSession);

    internal static Item? Parse(ReadOnlyMemory<byte> line, string fileSession)
    {
        // Most lines are not about usage; the cheap test (on the bytes, before any text is made) keeps them from being parsed.
        if (line.Span.IndexOf("\"usage\""u8) < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("timestamp", out var stamp)
                || !DateTimeOffset.TryParse(stamp.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time))
            {
                return null;
            }

            var creation = Number(usage, "cache_creation_input_tokens");
            if (creation == 0 && usage.TryGetProperty("cache_creation", out var detail) && detail.ValueKind == JsonValueKind.Object)
            {
                creation = Number(detail, "ephemeral_5m_input_tokens") + Number(detail, "ephemeral_1h_input_tokens");
            }

            var tokens = Number(usage, "input_tokens") + Number(usage, "output_tokens") + creation + Number(usage, "cache_read_input_tokens");
            return new Item(
                time, tokens,
                Text(message, "id"), Text(root, "requestId"), Text(root, "sessionId") ?? fileSession,
                root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return null; // A line that is cut off (the file is being written): not counted.
        }
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal sealed record Item(DateTimeOffset Time, long Tokens, string? MessageId, string? RequestId, string Session, bool Sidechain);
}

/// <summary>Claude's session blocks, as ccusage finds them.</summary>
internal static class UsageBlocks
{
    /// <summary>
    /// Puts the responses in blocks: a block starts at the hour (UTC) of its first response and lasts
    /// <paramref name="length"/>; a response more than that after the block's start, or after the response before it,
    /// starts a new block. The block that is running at <paramref name="now"/> (its last response less than a block's
    /// length ago, and its end still ahead) is the one counted.
    /// </summary>
    public static UsageReading Read(IEnumerable<UsageEntry> entries, TimeSpan length, DateTimeOffset now)
    {
        var sorted = entries.OrderBy(entry => entry.Time).ToList();
        if (sorted.Count == 0)
        {
            return UsageReading.None;
        }

        var blocks = new List<(DateTimeOffset Start, DateTimeOffset Last, long Tokens)>();
        var start = FloorToHour(sorted[0].Time);
        var last = sorted[0].Time;
        long tokens = 0;
        foreach (var entry in sorted)
        {
            if (entry.Time - start > length || entry.Time - last > length)
            {
                blocks.Add((start, last, tokens));
                (start, tokens) = (FloorToHour(entry.Time), 0);
            }

            last = entry.Time;
            tokens += entry.Tokens;
        }

        blocks.Add((start, last, tokens));
        var current = blocks[^1];
        var active = now - current.Last < length && now < current.Start + length;
        var earlier = blocks.Take(active ? blocks.Count - 1 : blocks.Count).Select(b => b.Tokens).DefaultIfEmpty(0).Max();
        return active ? new UsageReading(current.Tokens, current.Start + length, earlier) : new UsageReading(0, null, earlier);
    }

    internal static DateTimeOffset FloorToHour(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}

/// <summary>Where Claude Code keeps its session logs, as ccusage looks for them.</summary>
internal static class ClaudeData
{
    /// <summary>
    /// The <c>projects</c> folders to read: those named in <c>CLAUDE_CONFIG_DIR</c> (comma separated; each a Claude
    /// config folder, or its <c>projects</c> folder) if it is set, else <c>$XDG_CONFIG_HOME/claude</c> (default
    /// <c>~/.config/claude</c>) and <c>~/.claude</c>; only those that exist.
    /// </summary>
    public static IReadOnlyList<string> ProjectFolders(Func<string, string?> environment, string home)
    {
        var folders = new List<string>();
        void Consider(string config)
        {
            var projects = Path.GetFileName(config.TrimEnd('\\', '/')).Equals("projects", StringComparison.OrdinalIgnoreCase) && Directory.Exists(config)
                ? config
                : Path.Combine(config, "projects");
            if (Directory.Exists(projects) && !folders.Contains(projects, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(projects);
            }
        }

        if (environment("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured)
        {
            foreach (var raw in configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Consider(raw.StartsWith('~') ? home + raw[1..] : raw);
            }

            return folders;
        }

        Consider(Path.Combine(environment("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".config"), "claude"));
        Consider(Path.Combine(home, ".claude"));
        return folders;
    }
}

/// <summary>
/// Reads the session logs again and again, quickly: each file's counted lines are kept, and only what was appended since
/// the last reading is read (a log that got shorter is read anew). Files not written for longer than the look-back are
/// left out. Used from one thread at a time.
/// </summary>
internal sealed class UsageReader
{
    // Long enough to see the blocks before the current one (for the automatic limit) without reading every log ever.
    private static readonly TimeSpan LookBack = TimeSpan.FromDays(7);

    private readonly Dictionary<string, (long Length, List<UsageDeduper.Item> Items)> _files = new(StringComparer.OrdinalIgnoreCase);

    public UsageReading Read(IReadOnlyList<string> projectFolders, TimeSpan blockLength, DateTimeOffset now)
    {
        var since = now - LookBack;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduper = new UsageDeduper();
        foreach (var folder in projectFolders)
        {
            foreach (var file in SafeFiles(folder))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < since.UtcDateTime - TimeSpan.FromDays(1))
                    {
                        continue; // Logs are appended to: one not written since then holds nothing newer.
                    }

                    seen.Add(file);
                    foreach (var item in ItemsOf(file, info.Length))
                    {
                        if (item.Time >= since)
                        {
                            deduper.Add(item);
                        }
                    }
                }
                catch (IOException)
                {
                    // A log that is busy or gone: left out of this reading.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        foreach (var gone in _files.Keys.Where(path => !seen.Contains(path)).ToList())
        {
            _files.Remove(gone);
        }

        return UsageBlocks.Read(deduper.Entries, blockLength, now);
    }

    private List<UsageDeduper.Item> ItemsOf(string file, long length)
    {
        var known = _files.TryGetValue(file, out var cached) && cached.Length <= length ? cached : (0, new List<UsageDeduper.Item>());
        if (known.Length == length)
        {
            return known.Items;
        }

        // Read in pieces, a line at a time: logs grow to hundreds of megabytes, and reading one whole (as bytes and again as
        // text) took that much memory and more. Only whole lines count: a last line without its line break is still being
        // written, and is read next time.
        var session = Path.GetFileNameWithoutExtension(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        stream.Seek(known.Length, SeekOrigin.Begin);
        var items = known.Items;
        var consumed = known.Length;
        var chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 20);
        var line = new System.Buffers.ArrayBufferWriter<byte>();
        try
        {
            var left = length - known.Length;
            while (left > 0)
            {
                var read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, left));
                if (read <= 0)
                {
                    break;
                }

                left -= read;
                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (chunk[i] != (byte)'\n')
                    {
                        continue;
                    }

                    line.Write(chunk.AsSpan(start, i - start));
                    consumed += line.WrittenCount + 1;
                    if (UsageDeduper.Parse(line.WrittenMemory, session) is { } item)
                    {
                        items.Add(item);
                    }

                    line.Clear();
                    start = i + 1;
                }

                line.Write(chunk.AsSpan(start, read - start));
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
        }

        _files[file] = (consumed, items);
        return items;
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.jsonl", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
        }
        catch (IOException)
        {
            return [];
        }
    }
}

/// <summary>
/// Watches the AI usage: every minute (only while on) Claude Code's logs are read on a pool thread, the current session
/// block is found, and <see cref="Alert"/> is raised when it reaches the warning percentage or the limit. Everything stays
/// on this computer.
/// </summary>
internal sealed class UsageMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly UsageReader _reader = new();
    private readonly IReadOnlyList<string> _folders;
    private UsageOptions _options;
    private UsageLevel _level = UsageLevel.Fine;
    private bool _reading;
    private bool _disposed;

    private readonly string _limitsFile;
    private readonly string? _home; // The user profile when the real computer is read; null in tests.

    public UsageMonitor(UsageOptions options, IReadOnlyList<string>? folders = null, string? limitsFile = null)
    {
        _options = options;
        // With folders given (tests) the real saved limits are not read.
        _home = folders is null ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : null;
        _limitsFile = limitsFile ?? (folders is null ? ClaudeLimits.FilePath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) : "");
        _folders = folders ?? ClaudeData.ProjectFolders(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        Check();
    }

    /// <summary>The last reading, or <see langword="null"/> before the first.</summary>
    public UsageReading? Reading { get; private set; }

    public UsageOptions Options => _options;

    /// <summary>Raised on the dispatcher thread when a level is reached that was not before, with the reading.</summary>
    public event Action<UsageLevel, UsageReading>? Alert;

    public void Configure(UsageOptions options)
    {
        var lengthChanged = options.Hours != _options.Hours;
        _options = options;
        if (Reading is { } reading)
        {
            _level = UsageRules.LevelOf(reading, options); // The new limit's state is not news.
        }

        if (lengthChanged)
        {
            Check();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    private void Check()
    {
        if (_reading || _disposed)
        {
            return;
        }

        _reading = true;
        var length = TimeSpan.FromHours(Math.Max(1, _options.Hours));
        _ = Task.Run(() =>
        {
            var now = DateTimeOffset.UtcNow;
            var reading = _reader.Read(_folders, length, now);

            // What Claude Code reports about the plan wins over what the logs add up to, when DeskGrid is connected to it.
            if (ClaudeLimits.Read(_limitsFile, now) is { } plan)
            {
                reading = reading with { Resets = plan.FiveResets, Plan = plan };
            }

            // The other tools that keep their plan usage on disk (Codex), when this is the real computer and not a test.
            return _home is not null && CodexLimits.Read(_home, now) is { } codex
                ? reading with { Others = [new ToolUsage(new AiTool(AiTools.CodexId, "Codex", true), codex)] }
                : reading;
        }).ContinueWith(task =>
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

            var reading = task.Result;
            var startsNew = Reading is { Resets: { } before } && reading.Resets != before;
            Reading = reading;
            var level = UsageRules.LevelOf(reading, _options);
            if (startsNew)
            {
                _level = UsageLevel.Fine; // A new block: its warnings are news again.
            }

            if (level > _level)
            {
                Alert?.Invoke(level, reading);
            }

            _level = level;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
