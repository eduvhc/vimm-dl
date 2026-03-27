namespace Module.Core;

/// <summary>
/// Standard communication bridge between a module and the host application.
/// Each module defines its own event types and the host provides an implementation
/// that routes events to the appropriate transport (SignalR, logging, etc).
/// </summary>
/// <typeparam name="TEvent">The event type this bridge carries.</typeparam>
public interface IModuleBridge<in TEvent>
{
    Task SendAsync(TEvent evt);
}
