using System.Net;
using System.Net.Sockets;
using System.Text;
using DeskGrid.Updates;

namespace DeskGrid.Tests;

[Collection("StallTimeout")] // Changes a static: not in parallel with anything else that downloads.
public class UpdateDownloadStallTests
{
    [Fact]
    public async Task DownloadThatStalls_GivesUpInsteadOfHangingForever()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            var stream = client.GetStream();
            _ = await stream.ReadAsync(new byte[4096].AsMemory(), stop.Token); // The request (whatever part arrives first).
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\n0123456789"), stop.Token);
            await Task.Delay(Timeout.Infinite, stop.Token); // ...and then nothing.
        }, TestContext.Current.CancellationToken);

        var before = UpdateInstaller.StallTimeout;
        UpdateInstaller.StallTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            var release = new ReleaseInfo(new Version(9, 9, 9), "v9.9.9", "", $"http://127.0.0.1:{port}/x.exe", new string('0', 64));
            var download = UpdateInstaller.DownloadAsync(release, new Progress<UpdateProgress>(), TestContext.Current.CancellationToken);

            Assert.Same(download, await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
            var (path, error) = await download;
            Assert.Null(path);
            Assert.False(string.IsNullOrEmpty(error));
        }
        finally
        {
            UpdateInstaller.StallTimeout = before;
            await stop.CancelAsync();
            try { await server; } catch (OperationCanceledException) { }
        }
    }
}
