namespace AgentDemo.Web.Services;

// Lets a native host (MAUI) drive back navigation and pause/resume. Optional: hosts that don't register it are unaffected.
public sealed class AppLifecycle
{
    private IAppLifecycleTarget? _target;

    public void Attach(IAppLifecycleTarget target) => _target = target;

    public void Detach(IAppLifecycleTarget target)
    {
        if (ReferenceEquals(_target, target)) _target = null;
    }

    // True when the app consumed the back press; false means the host may exit.
    public Task<bool> HandleBackAsync() => _target?.HandleBackAsync() ?? Task.FromResult(false);

    public Task PauseAsync() => _target?.PauseAsync() ?? Task.CompletedTask;

    public Task ResumeAsync() => _target?.ResumeAsync() ?? Task.CompletedTask;
}

public interface IAppLifecycleTarget
{
    Task<bool> HandleBackAsync();

    Task PauseAsync();

    Task ResumeAsync();
}
