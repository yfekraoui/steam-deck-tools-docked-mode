using PowerControl.Helpers;
using System;
using System.Linq;
using Windows.Devices.Radios;

namespace PowerControl.Options
{
    public static class WiFi
    {
        public static Menu.MenuItemWithOptions Instance = new Menu.MenuItemWithOptions()
        {
            Name = "Wi-Fi",
            Options = { "Disabled", "Enabled" },
            CurrentValue = delegate ()
            {
                return IsWiFiEnabled() ? "Enabled" : "Disabled";
            },
            ApplyValue = (selected) =>
            {
                SetWiFiEnabled(selected == "Enabled");
                return IsWiFiEnabled() ? "Enabled" : "Disabled";
            }
        };

        private static bool IsWiFiEnabled()
        {
            if (!RadioHelper.RequestAccess().GetAwaiter().GetResult())
                throw new InvalidOperationException("Accès radios refusé");
            var radios = Radio.GetRadiosAsync().GetAwaiter().GetResult();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            return wifi != null && wifi.State == RadioState.On;
        }

        private static void SetWiFiEnabled(bool enable)
        {
            try
            {
                if (!RadioHelper.RequestAccess().GetAwaiter().GetResult())
                    throw new InvalidOperationException("Accès radios refusé");
                var radios = Radio.GetRadiosAsync().GetAwaiter().GetResult();
                var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                if (wifi != null)
                    RadioHelper.SetState(wifi, enable).GetAwaiter().GetResult();
            }
            catch { }
        }
    }
}

