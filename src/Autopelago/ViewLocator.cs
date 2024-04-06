using Autopelago.ViewModels;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Autopelago;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        string name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        if (Type.GetType(name) is not { } type)
        {
            return new TextBlock
            {
                Text = $"Not Found: {name}",
            };
        }

        Control control = (Control)Activator.CreateInstance(type)!;
        control.DataContext = param;
        return control;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
