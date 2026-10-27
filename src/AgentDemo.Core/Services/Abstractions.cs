namespace AgentDemo.Web.Services;

public interface ILocalStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);
}

public enum SoundCue
{
    Click,
    PlaceX,
    PlaceO,
    Win,
    Lose,
    Draw,
    Fade
}

public interface ISoundService
{
    // Muted by default; callers set this from the loaded profile.
    bool Enabled { get; set; }

    // No-op when muted or when audio/interop is unavailable. Never throws.
    Task PlayAsync(SoundCue cue);
}
