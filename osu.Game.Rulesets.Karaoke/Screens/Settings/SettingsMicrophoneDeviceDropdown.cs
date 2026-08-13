// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Karaoke.Integration.Uta;

namespace osu.Game.Rulesets.Karaoke.Screens.Settings;

public partial class SettingsMicrophoneDeviceDropdown : SettingsDropdown<string>
{
    protected override OsuDropdown<string> CreateDropdown() => new MicrophoneDeviceDropdownControl();

    [BackgroundDependencyLoader]
    private void load()
    {
        var deviceItems = new List<string> { string.Empty };
        deviceItems.AddRange(UtaMicrophoneDevices.Enumerate().Select(device => device.Name));

        Items = deviceItems.Distinct().ToList();
    }

    private partial class MicrophoneDeviceDropdownControl : DropdownControl
    {
        protected override LocalisableString GenerateItemText(string item)
            => string.IsNullOrEmpty(item) ? CommonStrings.Default : base.GenerateItemText(item);
    }
}
