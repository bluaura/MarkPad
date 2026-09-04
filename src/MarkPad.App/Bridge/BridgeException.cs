namespace MarkPad.App.Bridge;

/// <summary>Error reported by the editor bundle or by the transport (timeout, not ready). Codes mirror bridge.ts.</summary>
public sealed class BridgeException : Exception
{
    public BridgeException(string code, string message, Exception? inner = null)
        : base($"{code}: {message}", inner)
    {
        Code = code;
    }

    public string Code { get; }

    public const string Timeout = "TIMEOUT";
    public const string NotReady = "NOT_READY";
    public const string Disposed = "DISPOSED";
    public const string UnknownMethod = "UNKNOWN_METHOD";
}
