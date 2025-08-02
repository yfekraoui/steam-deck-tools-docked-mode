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
            CurrentValue = delegate ()
            {
                return IsBluetoothEnabled() ? "Enabled" : "Disabled";
            },
            ApplyValue = (selected) =>
            {
                SetBluetoothEnabled(selected == "Enabled");
                return IsBluetoothEnabled() ? "Enabled" : "Disabled";
            }
        };

        private static bool IsBluetoothEnabled()
        {
            if (!RadioHelper.RequestAccess().GetAwaiter().GetResult())
                throw new InvalidOperationException("Accès radios refusé");
            var radios = Radio.GetRadiosAsync().GetAwaiter().GetResult();
            var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            return bt != null && bt.State == RadioState.On;
        }

        private static void SetBluetoothEnabled(bool enable)
        {
            try
            {
                if (!RadioHelper.RequestAccess().GetAwaiter().GetResult())
                    throw new InvalidOperationException("Accès radios refusé");
                var radios = Radio.GetRadiosAsync().GetAwaiter().GetResult();
                var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                if (bt != null)
                    RadioHelper.SetState(bt, enable).GetAwaiter().GetResult();
            }
            catch { }
        }
    }
}

