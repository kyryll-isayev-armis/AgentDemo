using Microsoft.JSInterop;

namespace AgentDemo.Web.Services;

public sealed class SoundService(IJSRuntime js) : ISoundService, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public bool Enabled { get; set; }

    public async Task PlayAsync(SoundCue cue)
    {
        if (!Enabled) return;

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/AgentDemo.UI/audio.js");
            var text = cue.ToString();
            await _module.InvokeVoidAsync("play", char.ToLowerInvariant(text[0]) + text[1..]);
        }
        catch (Exception ex) when (JsLocalStore.IsInteropFailure(ex))
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
        catch (Exception ex) when (JsLocalStore.IsInteropFailure(ex))
        {
        }
        _module = null;
    }
}
