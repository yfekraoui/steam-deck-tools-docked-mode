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
            CurrentValueAsync = async () =>
            {
                return await IsWiFiEnabled() ? "Enabled" : "Disabled";
            },
            ApplyValueAsync = async (selected) =>
            {
                await SetWiFiEnabled(selected == "Enabled");
                return await IsWiFiEnabled() ? "Enabled" : "Disabled";
            }
        };

        public static async Task<bool> IsWiFiEnabled()
        {
            if (!await RadioHelper.RequestAccess())
                throw new InvalidOperationException("Accès radios refusé");
            var radios = await Radio.GetRadiosAsync();
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            return wifi != null && wifi.State == RadioState.On;
        }

        public static async Task SetWiFiEnabled(bool enable)
        {
            try
            {
                if (!await RadioHelper.RequestAccess())
                    throw new InvalidOperationException("Accès radios refusé");
                var radios = await Radio.GetRadiosAsync();
                var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                if (wifi != null)
                    await RadioHelper.SetState(wifi, enable);
            }
            catch { }
        }
    }
}

