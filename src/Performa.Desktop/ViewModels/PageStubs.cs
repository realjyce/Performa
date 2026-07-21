using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

// Typed placeholders wired into navigation; fleshed out in their own pass.

public sealed class DailyViewModel(PerformaEngine engine) : ObservableObject
{
    public PerformaEngine Engine { get; } = engine;
}

public sealed class AssistantViewModel(PerformaEngine engine) : ObservableObject
{
    public PerformaEngine Engine { get; } = engine;
}
