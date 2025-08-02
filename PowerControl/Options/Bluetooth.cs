using PowerControl.Helpers;
using System;
using System.Linq;
using Windows.Devices.Radios;

namespace PowerControl.Options
{
    public static class Bluetooth
    {
        public static Menu.MenuItemWithOptions Instance = new Menu.MenuItemWithOptions()
        {
            Name = "Bluetooth",
            Options = { "Disabled", "Enabled" },
            CurrentValueAsync = async () =>
            {
                return await IsBluetoothEnabled() ? "Enabled" : "Disabled";
            },
            ApplyValueAsync = async (selected) =>
            {
                await SetBluetoothEnabled(selected == "Enabled");
                return await IsBluetoothEnabled() ? "Enabled" : "Disabled";
            }
        };

        public static async Task<bool> IsBluetoothEnabled()
        {
            if (!await RadioHelper.RequestAccess())
                throw new InvalidOperationException("Accès radios refusé");
            var radios = await Radio.GetRadiosAsync();
            var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            return bt != null && bt.State == RadioState.On;
        }

        public static async Task SetBluetoothEnabled(bool enable)
        {
            try
            {
                if (!await RadioHelper.RequestAccess())
                    throw new InvalidOperationException("Accès radios refusé");
                var radios = await Radio.GetRadiosAsync();
                var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                if (bt != null)
                    await RadioHelper.SetState(bt, enable);
            }
            catch { }
        }
    }
}

