using recontrol_win.Internal;
using System.Text.Json;

namespace recontrol_win.Tools
{
    // ==================== BASE REQUEST / RESPONSE WRAPPERS ====================

    /// <summary>
    /// The base structure for all incoming requests from the server.
    /// </summary>
    internal class BaseRequest
    {
        public string? Id { get; set; }
        public string Command { get; set; } = string.Empty;

        // The payload is held as a raw JsonElement, to be deserialized
        // later by the specific command handler.
        public JsonElement Payload { get; set; }
    }

    /// <summary>
    /// The base class for all responses sent to the server.
    /// </summary>
    internal abstract class BaseResponse
    {
        public string Id { get; set; }
        public string Status { get; set; }

        protected BaseResponse(string id, string status)
        {
            Id = id;
            Status = status;
        }
    }

    /// <summary>
    /// A successful response, containing the result of the operation.
    /// </summary>
    internal class SuccessResponse : BaseResponse
    {
        // The Result can be anything (string, number, list, etc.),
        // so we use 'object?'
        public object? Result { get; set; }

        public SuccessResponse(string id, object? result) : base(id, "success")
        {
            Result = result;
        }
    }

    /// <summary>
    /// An error response, containing a description of what went wrong.
    /// </summary>
    internal class ErrorResponse : BaseResponse
    {
        public string Error { get; set; }

        public ErrorResponse(string id, string error) : base(id, "error")
        {
            Error = error;
        }
    }

    // ==================== COMMAND-SPECIFIC PAYLOADS (DTOs) ====================
    // These classes map to the 'payload' object for each command 'type'.
    // We use default values to match the optional parameters in your services.

    // Keyboard Payloads
    internal class KeyPayload
    {
        public VirtualKey Key { get; set; }
    }

    internal class KeyPressPayload
    {
        public VirtualKey Key { get; set; }
        public int HoldMs { get; set; } = 30;
    }

    // Mouse Payloads
    internal class MouseMovePayload
    {
        public int DeltaX { get; set; } = 0;
        public int DeltaY { get; set; } = 0;
    }

    internal class MouseButtonPayload
    {
        public MouseButton Button { get; set; } = MouseButton.Left;
    }

    internal class MouseScrollPayload
    {
        public int Clicks { get; set; } = 0;
    }

    internal class MouseClickPayload
    {
        public MouseButton Button { get; set; } = MouseButton.Left;
        public int DelayMs { get; set; } = 30;
    }

    internal class MouseDoubleClickPayload
    {
        public int DelayMs { get; set; } = 120;
    }

    // Terminal Payloads
    internal class TerminalCommandPayload
    {
        public string Command { get; set; } = string.Empty;
        public int Timeout { get; set; } = 30000;
    }

    internal class TerminalKillPayload
    {
        public int Pid { get; set; }
        public bool Force { get; set; } = false;
    }

    internal class TerminalStartPayload
    {
        public string FileName { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool RedirectOutput { get; set; } = false;
    }

    internal class TerminalSetCwdPayload
    {
        public string Path { get; set; } = string.Empty;
    }
}
