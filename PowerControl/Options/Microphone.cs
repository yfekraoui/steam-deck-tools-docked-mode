using CommonHelpers;
using PowerControl.Helpers;

namespace PowerControl.Options
{
    public static class Microphone
    {
        public static Menu.MenuItemWithOptions Instance = new Menu.MenuItemWithOptions()
        {
            Name = "Microphone",
            PersistentKey = "Microphone",
            PersistOnCreate = false,
            ApplyDelay = 500,
            Options = { "Disabled", "Enabled" },
            ResetValue = () => { return "Disabled"; },
            CurrentValue = delegate ()
            {
                return Helpers.AudioManager.GetMicrophoneMute() ? "Disabled" : "Enabled";
            },
            ApplyValue = (selected) =>
            {
                Helpers.AudioManager.SetMicrophoneMute(selected.ToString() == "Disabled");
                return Helpers.AudioManager.GetMicrophoneMute() ? "Disabled" : "Enabled";
            }
        };
    }
}
