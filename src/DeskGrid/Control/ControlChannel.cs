using System.IO;
using System.IO.Pipes;
using System.Text;
using DeskGrid.Diagnostics;

namespace DeskGrid.Control;

/// <summary>
/// The control pipe: a per-user named pipe on which scripts and AI agents send the running DeskGrid one
/// <see cref="ControlRequest"/> per connection and read back one <see cref="ControlResponse"/>. Only processes of the
/// same user can connect (<see cref="PipeOptions.CurrentUserOnly"/>).
/// </summary>
internal sealed class ControlChannel : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    // UTF-8 without a byte order mark: a BOM in front of the first line would break the JSON.
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// Listens on <paramref name="pipeName"/>. <paramref name="handle"/> is called on a thread-pool thread, one request
    /// at a time; it is up to it to get onto the UI thread.
    /// </summary>
    public ControlChannel(Func<ControlRequest, Task<ControlResponse>> handle, string? pipeName = null) =>
        _ = ListenAsync(pipeName ?? DefaultPipeName, handle, _stop.Token);

    public static string DefaultPipeName { get; } = $"DeskGrid.Control.{Environment.UserName}";

    /// <summary>
    /// Sends <paramref name="request"/> to the DeskGrid listening on <paramref name="pipeName"/> and waits for its answer.
    /// Throws <see cref="TimeoutException"/> when nothing listens there (DeskGrid isn't running).
    /// </summary>
    public static ControlResponse Send(ControlRequest request, TimeSpan connectTimeout, TimeSpan replyTimeout, string? pipeName = null)
    {
        using var client = new NamedPipeClientStream(".", pipeName ?? DefaultPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        client.Connect(connectTimeout);
        using var writer = new StreamWriter(client, Utf8, leaveOpen: true) { AutoFlush = true };
        writer.WriteLine(request.Format());
        using var reader = new StreamReader(client, Utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var read = reader.ReadLineAsync();
        if (!read.Wait(replyTimeout))
        {
            throw new TimeoutException("DeskGrid did not answer in time.");
        }

        return read.Result is { } line && ControlResponse.TryParse(line) is { } response
            ? response
            : throw new IOException("DeskGrid sent back something that is not a response.");
    }

    public void Dispose() => _stop.Cancel();

    private static async Task ListenAsync(string pipeName, Func<ControlRequest, Task<ControlResponse>> handle, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop);

                // A client that connects and then says nothing must not block everyone after it.
                using var patience = CancellationTokenSource.CreateLinkedTokenSource(stop);
                patience.CancelAfter(RequestTimeout);
                using var reader = new StreamReader(server, Utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                if (await reader.ReadLineAsync(patience.Token) is not { Length: > 0 } line)
                {
                    continue;
                }

                var response = ControlRequest.TryParse(line) is { } request
                    ? await HandleAsync(handle, request)
                    : ControlResponse.Failure("Not a request. Send one line of JSON: {\"command\":\"…\",\"args\":[…]}.");
                await using var writer = new StreamWriter(server, Utf8, leaveOpen: true);
                await writer.WriteLineAsync(response.Format().AsMemory(), stop);
                await writer.FlushAsync(stop);

                // Closing before the client has read the answer could take the answer with it.
                server.WaitForPipeDrain();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                Log.Info("A control request timed out.");
            }
            catch (Exception exception)
            {
                // A client that went away mid-answer, or a name clash: log, then keep listening. Nobody awaits this
                // loop, so anything that ended it would silently stop control requests from working.
                Log.Error("Serving a control request failed", exception);
                try
                {
                    await Task.Delay(1000, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static async Task<ControlResponse> HandleAsync(Func<ControlRequest, Task<ControlResponse>> handle, ControlRequest request)
    {
        try
        {
            return await handle(request);
        }
        catch (Exception exception)
        {
            Log.Error($"The control command \"{request.Command}\" failed", exception);
            return ControlResponse.Failure($"The command failed: {exception.Message}");
        }
    }
}
