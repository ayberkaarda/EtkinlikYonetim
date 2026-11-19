using Microsoft.Win32;

namespace EtkinlikYonetim.UI.Theming;

/// <summary>Raises <see cref="Changed"/> when Windows changes its user preferences (contrast, app mode).</summary>
internal interface IPreferenceSource
{
    event EventHandler? Changed;
}

/// <summary>The real source: the Windows user preference notification, which arrives on the system events thread.</summary>
internal sealed class SystemPreferenceSource : IPreferenceSource
{
    private EventHandler? _changed;

    public event EventHandler? Changed
    {
        add
        {
            if (_changed is null)
            {
                SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
            }

            _changed += value;
        }
        remove
        {
            _changed -= value;
            if (_changed is null)
            {
                SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            }
        }
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => _changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Follows the Windows contrast and app mode settings while the application runs: a notification chooses the
/// palette again on the UI thread, and the open screens refresh themselves. Disposing it ends the subscription.
/// </summary>
internal sealed class ThemeWatcher : IDisposable
{
    private readonly IPreferenceSource _source;
    private readonly SynchronizationContext? _ui;
    private readonly Func<bool> _highContrast;
    private readonly Func<bool> _prefersDark;

    public ThemeWatcher(IPreferenceSource source, SynchronizationContext? ui, Func<bool> highContrast, Func<bool> prefersDark)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ui = ui;
        _highContrast = highContrast ?? throw new ArgumentNullException(nameof(highContrast));
        _prefersDark = prefersDark ?? throw new ArgumentNullException(nameof(prefersDark));
        _source.Changed += OnChanged;
    }

    public void Dispose() => _source.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_ui is null)
        {
            Reapply();
        }
        else
        {
            _ui.Post(_ => Reapply(), null);
        }
    }

    private void Reapply() => Theme.Reapply(_highContrast(), _prefersDark());
}
