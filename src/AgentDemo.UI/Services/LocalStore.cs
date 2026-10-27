using Microsoft.JSInterop;

namespace AgentDemo.Web.Services;

// Only call after the first interactive render (e.g. from OnAfterRenderAsync); JS interop is unavailable during prerender.
public sealed class JsLocalStore(IJSRuntime js) : ILocalStore, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            var module = await GetModuleAsync();
            return await module.InvokeAsync<string?>("get", key);
        }
        catch (Exception ex) when (IsInteropFailure(ex))
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("set", key, value);
        }
        catch (Exception ex) when (IsInteropFailure(ex))
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception ex) when (IsInteropFailure(ex))
        {
        }
        _module = null;
    }

    private async Task<IJSObjectReference> GetModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/AgentDemo.UI/storage.js");

    internal static bool IsInteropFailure(Exception ex) =>
        ex is JSException or InvalidOperationException or JSDisconnectedException
            or OperationCanceledException;
}
