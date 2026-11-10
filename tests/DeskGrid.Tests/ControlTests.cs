using System.IO;
using System.Text.Json.Nodes;
using DeskGrid.Config;
using DeskGrid.Control;
using DeskGrid.Folders;
using DeskGrid.Island;
using DeskGrid.Threading;

namespace DeskGrid.Tests;

public sealed class ControlMessageTests
{
    [Fact]
    public void Request_RoundTrips()
    {
        var request = new ControlRequest("add", ["Work", @"C:\Tools\a b.exe", "中文"]);

        var parsed = ControlRequest.TryParse(request.Format());

        Assert.NotNull(parsed);
        Assert.Equal("add", parsed.Command);
        Assert.Equal(request.Args, parsed.Args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"command\":\"\"}")]
    [InlineData("{\"command\":3}")]
    [InlineData("{\"command\":\"list\",\"args\":\"Work\"}")]
    [InlineData("{\"command\":\"list\",\"args\":[1]}")]
    public void Request_RejectsWhatIsNotOne(string line) => Assert.Null(ControlRequest.TryParse(line));

    [Fact]
    public void Request_WithoutArgs_HasNone()
    {
        var parsed = ControlRequest.TryParse("{\"command\":\" Status \"}");

        Assert.NotNull(parsed);
        Assert.Equal("status", parsed.Command);
        Assert.Empty(parsed.Args);
    }

    [Fact]
    public void Response_IsAscii_AndRoundTrips()
    {
        var response = ControlResponse.Success(new JsonObject { ["name"] = "工作" });

        var line = response.Format();
        var parsed = ControlResponse.TryParse(line);

        Assert.All(line, c => Assert.True(c < 128));
        Assert.NotNull(parsed);
        Assert.True(parsed.Ok);
        Assert.Equal("工作", parsed.Result!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        var parsed = ControlResponse.TryParse(ControlResponse.Failure("nope").Format());

        Assert.NotNull(parsed);
        Assert.False(parsed.Ok);
        Assert.Equal("nope", parsed.Error);
    }
}

public sealed class ControlCliTests
{
    [Theory]
    [InlineData("--ctl")]
    [InlineData("--ctl", "help")]
    [InlineData("--ctl", "--help")]
    public void Help_IsNoRequest(params string[] args) => Assert.Null(ControlCli.ToRequest(args));

    [Fact]
    public void IsRequest_OnlyWithTheSwitchFirst()
    {
        Assert.True(ControlCli.IsRequest(["--CTL", "list"]));
        Assert.False(ControlCli.IsRequest(["--open", "--ctl"]));
        Assert.False(ControlCli.IsRequest([]));
    }

    [Fact]
    public void Add_MakesRelativePathsFull_ButNotTheFolderName()
    {
        var request = ControlCli.ToRequest(["--ctl", "ADD", "Work", "notes.txt", @"C:\x.exe"]);

        Assert.NotNull(request);
        Assert.Equal("add", request.Command);
        Assert.Equal(["Work", Path.GetFullPath("notes.txt"), @"C:\x.exe"], request.Args);
    }

    [Fact]
    public void HelpText_ListsEveryCommand()
    {
        var help = ControlCli.HelpText();

        Assert.All(ControlCommands.Usage, u => Assert.Contains(u.Usage, help));
    }

    [Fact]
    public void Run_WithNothingListening_SaysNotRunning()
    {
        var output = new StringWriter();

        var code = ControlCli.Run(["--ctl", "status"], output, pipeName: $"DeskGrid.Tests.{Guid.NewGuid():N}");

        Assert.Equal(ControlCli.NotRunning, code);
        Assert.False(ControlResponse.TryParse(output.ToString().Trim())!.Ok);
    }

    [Fact]
    public async Task Run_SendsTheRequest_AndPrintsTheAnswer()
    {
        var pipe = $"DeskGrid.Tests.{Guid.NewGuid():N}";
        ControlRequest? received = null;
        using var channel = new ControlChannel(request =>
        {
            received = request;
            return Task.FromResult(ControlResponse.Success(new JsonObject { ["echo"] = request.Args[0] }));
        }, pipe);
        var output = new StringWriter();

        var code = await Task.Run(() => ControlCli.Run(["--ctl", "list", "工作"], output, pipe));

        Assert.Equal(ControlCli.Done, code);
        Assert.Equal(new ControlRequest("list", ["工作"]).Format(), received!.Format());
        Assert.Equal("工作", ControlResponse.TryParse(output.ToString().Trim())!.Result!["echo"]!.GetValue<string>());
    }

    [Fact]
    public async Task Run_ReportsAFailedCommand_AndServesTheNextOne()
    {
        var pipe = $"DeskGrid.Tests.{Guid.NewGuid():N}";
        using var channel = new ControlChannel(request => request.Command == "boom"
            ? throw new InvalidOperationException("kaput")
            : Task.FromResult(ControlResponse.Success()), pipe);

        var failed = new StringWriter();
        var failedCode = await Task.Run(() => ControlCli.Run(["--ctl", "boom"], failed, pipe));
        var next = await Task.Run(() => ControlCli.Run(["--ctl", "status"], new StringWriter(), pipe));

        Assert.Equal(ControlCli.Failed, failedCode);
        Assert.Contains("kaput", ControlResponse.TryParse(failed.ToString().Trim())!.Error);
        Assert.Equal(ControlCli.Done, next);
    }
}

public sealed class ControlCommandsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly DispatcherThread _sta = new("Control STA");
    private readonly FakeHost _host;
    private readonly ControlCommands _commands;

    public ControlCommandsTests()
    {
        _host = new FakeHost(_temp.Combine("root"));
        Directory.CreateDirectory(_host.Config.RootPath);
        _commands = new ControlCommands(_host, [_temp.Combine("desktop")]);
    }

    public void Dispose()
    {
        _sta.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void TurnedOff_RefusesEverything()
    {
        _host.Config.AllowControl = false;

        var response = Run("status");

        Assert.False(response.Ok);
        Assert.Contains("turned off", response.Error);
    }

    [Fact]
    public void New_CreatesTheFolder_AndListShowsIt()
    {
        var created = Run("new", "Work");
        var list = Run("list");

        Assert.True(created.Ok);
        Assert.True(Directory.Exists(Path.Combine(_host.Config.RootPath, "Work")));
        Assert.Equal("Work", list.Result!.AsArray().Single()!["name"]!.GetValue<string>());
    }

    [Fact]
    public void New_RefusesABadName()
    {
        Run("new", "Work");

        Assert.False(Run("new", "work").Ok);
        Assert.False(Run("new", "a/b").Ok);
    }

    [Fact]
    public async Task Add_MakesShortcuts_AndReportsWhatWasSkipped()
    {
        Run("new", "Tools");
        var program = _temp.CreateFile(@"apps\tool.exe");
        var missing = _temp.Combine("nothing.txt");

        var response = await _sta.InvokeAsync(() => Run("add", "Tools", program, missing));

        Assert.True(response.Ok, response.Error);
        Assert.Equal("tool.lnk", response.Result!["added"]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal(missing, response.Result["skipped"]!.AsArray().Single()!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(_host.Config.RootPath, "Tools", "tool.lnk")));
        Assert.True(File.Exists(program)); // A program is never moved.
    }

    [Fact]
    public void Add_RefusesARelativePath()
    {
        Run("new", "Tools");

        Assert.False(Run("add", "Tools", "tool.exe").Ok);
    }

    [Fact]
    public void Move_ReordersTheItems()
    {
        Run("new", "Docs");
        _temp.CreateFile(@"root\Docs\a.txt");
        _temp.CreateFile(@"root\Docs\b.txt");
        _temp.CreateFile(@"root\Docs\c.txt");
        _host.Rescan();

        var response = Run("move", "Docs", "C.TXT", "0");

        Assert.True(response.Ok, response.Error);
        Assert.Equal(["c.txt", "a.txt", "b.txt"], response.Result!["items"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void Move_NeedsAnItemAndAPosition()
    {
        Run("new", "Docs");
        _temp.CreateFile(@"root\Docs\a.txt");
        _host.Rescan();

        Assert.False(Run("move", "Docs", "zzz.txt", "0").Ok);
        Assert.False(Run("move", "Docs", "a.txt", "-1").Ok);
        Assert.False(Run("move", "Docs", "a.txt", "first").Ok);
    }

    [Fact]
    public void Rename_GoesThroughTheHost()
    {
        Run("new", "Old");

        var response = Run("rename", "old", "New");

        Assert.True(response.Ok, response.Error);
        Assert.Equal(("Old", "New"), _host.Renamed);
    }

    [Fact]
    public void Open_And_Close_GoThroughTheHost()
    {
        Run("new", "Work");

        Assert.True(Run("open", "WORK").Ok);
        Assert.Equal("Work", _host.OpenPanel);
        Assert.True(Run("open").Ok);
        Assert.Equal(ControlCommands.AllFolders, _host.OpenPanel);
        Assert.True(Run("close").Ok);
        Assert.Null(_host.OpenPanel);
    }

    [Fact]
    public void UnknownFolder_UnknownCommand_WrongArguments_AreErrors()
    {
        Assert.Contains("no folder", Run("open", "Nope").Error);
        Assert.Contains("Unknown command", Run("delete", "x").Error);
        Assert.Contains("Usage: new <name>", Run("new").Error);
    }

    [Fact]
    public void WhileStarting_FolderCommandsWait()
    {
        _host.Ready = false;

        Assert.Contains("still starting", Run("list").Error);
        Assert.True(Run("status").Ok);
    }

    [Fact]
    public void IslandShow_PassesTheTextAndTime_AndHideClearsIt()
    {
        _host.Config.IslandEnabled = true;

        var shown = Run("island", "show", "Build done", "7.5");
        var plain = Run("island", "show", "Next");

        Assert.True(shown.Ok, shown.Error);
        Assert.Equal(7.5, shown.Result!["seconds"]!.GetValue<double>());
        Assert.True(plain.Ok);
        Assert.Equal([("Build done", TimeSpan.FromSeconds(7.5)), ("Next", IslandActivity.DefaultDuration)], _host.IslandLines);
        Assert.True(Run("island", "hide").Ok);
        Assert.Empty(_host.IslandLines);
    }

    [Fact]
    public void IslandShow_WhileTheIslandIsOff_SaysHowToTurnItOn()
    {
        var response = Run("island", "show", "Hello");

        Assert.False(response.Ok);
        Assert.Contains("Settings", response.Error);
        Assert.Empty(_host.IslandLines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IslandShow_RefusesEmptyText(string text)
    {
        _host.Config.IslandEnabled = true;

        Assert.False(Run("island", "show", text).Ok);
    }

    [Fact]
    public void IslandShow_RefusesTextThatIsTooLong_AndBadSeconds()
    {
        _host.Config.IslandEnabled = true;

        Assert.False(Run("island", "show", new string('x', IslandActivity.MaxTextLength + 1)).Ok);
        Assert.False(Run("island", "show", "Hi", "0").Ok);
        Assert.False(Run("island", "show", "Hi", "61").Ok);
        Assert.False(Run("island", "show", "Hi", "soon").Ok);
        Assert.Empty(_host.IslandLines);
    }

    [Fact]
    public void Island_WrongArguments_ListsBothUsages()
    {
        var error = Run("island", "flash").Error;

        Assert.Contains("island show <text> [seconds]", error);
        Assert.Contains("island hide", error);
    }

    private ControlResponse Run(string command, params string[] args) => _commands.Execute(new ControlRequest(command, args));

    /// <summary>Folders read straight from disk, items in name order unless moved.</summary>
    private sealed class FakeHost(string root) : IControlHost
    {
        private readonly Dictionary<string, List<string>> _order = new(StringComparer.OrdinalIgnoreCase);
        private List<WidgetFolder> _widgets = [];

        public AppConfig Config { get; } = new() { RootPath = root, AllowControl = true };

        public bool Ready { get; set; } = true;

        public IReadOnlyList<WidgetFolder>? Widgets => Ready ? _widgets : null;

        public string? OpenPanel { get; private set; }

        public (string Old, string New)? Renamed { get; private set; }

        public void Rescan() => _widgets =
        [
            .. Directory.GetDirectories(root).Order().Select(dir =>
            {
                var name = Path.GetFileName(dir);
                var files = Directory.GetFiles(dir).Order().ToList();
                if (_order.TryGetValue(name, out var order))
                {
                    files = [.. files.OrderBy(f => order.FindIndex(n => string.Equals(n, Path.GetFileName(f), StringComparison.OrdinalIgnoreCase)))];
                }

                return new WidgetFolder(name, dir, [.. files.Select(f => new FolderItem(Path.GetFileName(f), f, default))]);
            }),
        ];

        public string? RenameFolder(string oldName, string newName)
        {
            Renamed = (oldName, newName);
            Directory.Move(Path.Combine(root, oldName), Path.Combine(root, newName));
            return null;
        }

        public void MoveItem(string folderName, string itemName, int index)
        {
            var order = _widgets.Single(w => w.Name == folderName).Items.Select(i => i.Name).ToList();
            WidgetCatalog.MoveInOrder(order, itemName, index);
            _order[folderName] = order;
            Rescan();
        }

        public void OpenFolder(WidgetFolder folder) => OpenPanel = folder.Name;

        public void OpenList() => OpenPanel = ControlCommands.AllFolders;

        public void ClosePanel() => OpenPanel = null;

        public List<(string Text, TimeSpan Duration)> IslandLines { get; } = [];

        public void ShowOnIsland(string text, TimeSpan duration, IslandLook? look = null) => IslandLines.Add((text, duration));

        public void HideIsland() => IslandLines.Clear();

        public List<string> IslandActions { get; } = [];

        public void IslandAction(string action) => IslandActions.Add(action);
    }
}
