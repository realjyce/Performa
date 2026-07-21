namespace Performa.Desktop.Infrastructure;

/// <summary>
/// A page that wants a nudge when the user navigates to it, so data that
/// depends on a sign-in can load the moment it becomes available.
/// </summary>
public interface IActivatablePage
{
    void OnActivated();
}
