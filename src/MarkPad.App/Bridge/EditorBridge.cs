using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace MarkPad.App.Bridge;

/// <summary>Handler for a request the editor sends to the host (e.g. asset.save).</summary>
public delegate Task<object?> HostRequestHandler(JsonElement? parameters, CancellationToken ct);

/// <summary>
/// JSON-RPC style bridge over WebView2 web messages (ARCHITECTURE.md §4).
/// host→web: <see cref="CallAsync{T}"/> posts a request and awaits the response; requests sent before the
/// editor's <c>ready</c> event are queued. web→host: events raise <see cref="EventReceived"/>, requests are
/// dispatched to handlers registered with <see cref="RegisterHandler"/>.
/// </summary>
public sealed class EditorBridge : IAsyncDisposable
{
    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_loadTimeout = TimeSpan.FromSeconds(60);

    private readonly CoreWebView2 _core;
    private readonly DispatcherQueue _dispatcher;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly Dictionary<string, HostRequestHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Queue<string> _queuedBeforeReady = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _nextId;
    private bool _disposed;

    public EditorBridge(CoreWebView2 core, DispatcherQueue dispatcher, ILogger log)
    {
        _core = core;
        _dispatcher = dispatcher;
        _log = log;
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    public Task WhenReady => _ready.Task;

    public string? EditorVersion { get; private set; }

    /// <summary>Raised on the UI thread for every web→host event (method, payload).</summary>
    public event EventHandler<BridgeEventArgs>? EventReceived;

    public void RegisterHandler(string method, HostRequestHandler handler) => _handlers[method] = handler;

    /// <summary>Resets the ready state, e.g. after the page is re-navigated following a renderer crash.</summary>
    public void ResetReady()
    {
        // TaskCompletionSource cannot be reset; callers create a new bridge instead (see EditorHost.RecreateAsync).
    }

    public async Task<T?> CallAsync<T>(string method, object? parameters = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var r = await CallRawAsync(method, parameters, timeout, ct).ConfigureAwait(true);
        if (r is null || r.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return default;
        return (T?)r.Value.Deserialize(typeof(T), BridgeJsonContext.Default);
    }

    public Task CallAsync(string method, object? parameters = null, TimeSpan? timeout = null, CancellationToken ct = default)
        => CallRawAsync(method, parameters, timeout, ct);

    private async Task<JsonElement?> CallRawAsync(string method, object? parameters, TimeSpan? timeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var json = JsonSerializer.Serialize(new BridgeEnvelope
        {
            T = "req",
            Id = id,
            M = method,
            P = parameters is null ? null : JsonSerializer.SerializeToElement(parameters, parameters.GetType(), BridgeJsonContext.Default),
        }, BridgeJsonContext.Default.BridgeEnvelope);

        var effectiveTimeout = timeout ?? (method == "doc.load" ? s_loadTimeout : s_defaultTimeout);

        if (IsReady)
        {
            _core.PostWebMessageAsJson(json);
        }
        else
        {
            _queuedBeforeReady.Enqueue(json);
            _log.LogDebug("bridge: queued {Method} until ready", method);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effectiveTimeout);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _pending.TryRemove(id, out _);
            throw new BridgeException(BridgeException.Timeout, $"{method} timed out after {effectiveTimeout.TotalSeconds:0}s");
        }
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string json;
        try
        {
            json = args.WebMessageAsJson;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _log.LogWarning(ex, "bridge: non-JSON web message ignored");
            return;
        }

        BridgeEnvelope? msg;
        try
        {
            msg = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeEnvelope);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "bridge: malformed message");
            return;
        }
        if (msg is null) return;

        switch (msg.T)
        {
            case "res":
                HandleResponse(msg);
                break;
            case "evt":
                HandleEvent(msg);
                break;
            case "req":
                _ = HandleRequestAsync(msg);
                break;
            default:
                _log.LogWarning("bridge: unknown envelope type {Type}", msg.T);
                break;
        }
    }

    private void HandleResponse(BridgeEnvelope msg)
    {
        if (msg.Id is not int id || !_pending.TryRemove(id, out var tcs)) return;
        if (msg.Ok == true)
        {
            tcs.TrySetResult(msg.R);
        }
        else
        {
            var e = msg.E ?? new BridgeError("UNKNOWN", "no error payload");
            tcs.TrySetException(new BridgeException(e.Code, e.Msg));
        }
    }

    private void HandleEvent(BridgeEnvelope msg)
    {
        var method = msg.M ?? string.Empty;
        if (method == "ready")
        {
            EditorVersion = msg.P?.TryGetProperty("version", out var v) == true ? v.GetString() : null;
            _log.LogInformation("bridge: editor ready (v{Version})", EditorVersion);
            _ready.TrySetResult();
            while (_queuedBeforeReady.TryDequeue(out var queued))
            {
                _core.PostWebMessageAsJson(queued);
            }
        }
        else if (method == "log" && msg.P is { } p)
        {
            var level = p.TryGetProperty("level", out var l) ? l.GetString() : "info";
            var text = p.TryGetProperty("msg", out var m) ? m.GetString() : "";
            _log.Log(level switch
            {
                "error" => LogLevel.Error,
                "warn" => LogLevel.Warning,
                "debug" => LogLevel.Debug,
                _ => LogLevel.Information,
            }, "editor: {Message}", text);
        }

        EventReceived?.Invoke(this, new BridgeEventArgs(method, msg.P));
    }

    private async Task HandleRequestAsync(BridgeEnvelope msg)
    {
        var id = msg.Id ?? -1;
        var method = msg.M ?? string.Empty;
        BridgeEnvelope response;
        if (!_handlers.TryGetValue(method, out var handler))
        {
            response = new BridgeEnvelope { T = "res", Id = id, Ok = false, E = new BridgeError(BridgeException.UnknownMethod, method) };
        }
        else
        {
            try
            {
                var result = await handler(msg.P, CancellationToken.None).ConfigureAwait(true);
                response = new BridgeEnvelope
                {
                    T = "res",
                    Id = id,
                    Ok = true,
                    R = result is null ? null : JsonSerializer.SerializeToElement(result, result.GetType(), BridgeJsonContext.Default),
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "bridge: host handler {Method} failed", method);
                var code = ex is BridgeException be ? be.Code : "HOST_ERROR";
                response = new BridgeEnvelope { T = "res", Id = id, Ok = false, E = new BridgeError(code, ex.Message) };
            }
        }
        if (_disposed) return;
        _core.PostWebMessageAsJson(JsonSerializer.Serialize(response, BridgeJsonContext.Default.BridgeEnvelope));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _core.WebMessageReceived -= OnWebMessageReceived;
        foreach (var (_, tcs) in _pending)
        {
            tcs.TrySetException(new BridgeException(BridgeException.Disposed, "bridge disposed"));
        }
        _pending.Clear();
        _ready.TrySetException(new BridgeException(BridgeException.Disposed, "bridge disposed"));
        _ = _dispatcher;
        return ValueTask.CompletedTask;
    }
}

public sealed class BridgeEventArgs(string method, JsonElement? payload) : EventArgs
{
    public string Method { get; } = method;
    public JsonElement? Payload { get; } = payload;

    public T? PayloadAs<T>() => Payload is { } p ? (T?)p.Deserialize(typeof(T), BridgeJsonContext.Default) : default;
}
