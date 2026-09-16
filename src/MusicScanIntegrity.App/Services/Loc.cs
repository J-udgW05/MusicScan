using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App.Services;

/// <summary>
/// Localized strings for XAML bindings, refreshed in place when the language
/// changes.
/// </summary>
/// <remarks>
/// Bindings go through the indexer; raising PropertyChanged for
/// <see cref="Binding.IndexerName"/> re-evaluates every one of them at once, so
/// switching languages needs no restart.
/// </remarks>
public sealed class Loc : INotifyPropertyChanged
{
    private Loc()
    {
        AppLanguage.Changed += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    /// <summary>The single instance bindings point at.</summary>
    public static Loc Instance { get; } = new();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>String for a resource key; a missing key shows as "#Key".</summary>
    public string this[string key] => Get(key);

    /// <summary>String for a resource key in the current UI language.</summary>
    public static string Get(string key) =>
        Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? "#" + key;
}

/// <summary>
/// Markup extension for a localized string: <c>Text="{svc:T Settings_Language}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class T : MarkupExtension
{
    /// <summary>Creates the extension for a resource key.</summary>
    public T(string key)
    {
        Key = key;
    }

    /// <summary>Resource key.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        Binding binding = new("[" + Key + "]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
