using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Performa.Desktop.Infrastructure;

public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "null view model" };

        var name = data.GetType().FullName!
            .Replace("ViewModels", "Views")
            .Replace("ViewModel", "View");
        var type = Type.GetType(name);

        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "view not found: " + name };
    }

    public bool Match(object? data) => data is ObservableObject;
}
