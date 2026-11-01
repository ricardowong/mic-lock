using MicLock.Core.Sys;

namespace MicLock.Core.Config;

public class AppSettings
{
    public string? SelectedDeviceId { get; set; } = null;
    public string? SelectedDeviceName { get; set; } = null;
    public int TargetVolumePercent { get; set; } = 100;
    public bool IsLockEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;
    public int MaxLogEntries { get; set; } = 100;

    // SoundSwitch Parity & Persistent Mute Overlay
    public bool KeepOverlayWhenMuted { get; set; } = false;
    public double OverlayOpacityWhenMuted { get; set; } = 0.70;

    // Keyboard Remapping Engine
    public bool IsKeyRemapEnabled { get; set; } = true;
    public List<KeyBindingRule> KeyBindings { get; set; } = KeyBindingRule.CreateDefaultRules();

    public void ClampValues()
    {
        if (TargetVolumePercent < 0) TargetVolumePercent = 0;
        if (TargetVolumePercent > 100) TargetVolumePercent = 100;
        if (MaxLogEntries < 10) MaxLogEntries = 10;
        if (MaxLogEntries > 1000) MaxLogEntries = 1000;

        if (OverlayOpacityWhenMuted < 0.10) OverlayOpacityWhenMuted = 0.10;
        if (OverlayOpacityWhenMuted > 1.0) OverlayOpacityWhenMuted = 1.0;

        if (KeyBindings == null || KeyBindings.Count == 0)
        {
            KeyBindings = KeyBindingRule.CreateDefaultRules();
            IsKeyRemapEnabled = true;
        }
        else
        {
            // If active shortcuts are defined, default to active
            if (KeyBindings.Any(b => !b.IsEmpty && b.VirtualKey != 0))
            {
                IsKeyRemapEnabled = true;
            }

            // Ensure all known actions exist in the bindings list
            var existingActions = new HashSet<KeyRemapAction>(KeyBindings.Select(b => b.Action));
            foreach (KeyRemapAction action in Enum.GetValues<KeyRemapAction>())
            {
                if (!existingActions.Contains(action))
                {
                    var defaultRule = KeyBindingRule.CreateDefaultRules().FirstOrDefault(r => r.Action == action)
                        ?? new KeyBindingRule { Action = action, VirtualKey = 0, DisplayText = "None" };
                    KeyBindings.Add(defaultRule);
                }
            }
        }
    }
}
