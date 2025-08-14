using System.Diagnostics;
using CommonHelpers;
using PowerControl.Helpers.AMD;

namespace PowerControl.Options
{
    public static class TDP
    {
        public const string SlowTDP = "SlowTDP";
        public const string FastTDP = "FastTDP";

        public const int DefaultSlowTDP = 15000;
        public const int DefaultFastTDP = 15000;

        public static PersistedOptions UserOptions()
        {
            var options = new PersistedOptions("TDP");
            options.SetOptions(new PersistedOptions.Option[]
            {
                options.ForOption("3W").Set(SlowTDP, 3000).Set(FastTDP, 3000),
                options.ForOption("4W").Set(SlowTDP, 4000).Set(FastTDP, 4000),
                options.ForOption("5W").Set(SlowTDP, 5000).Set(FastTDP, 5000),
                options.ForOption("6W").Set(SlowTDP, 6000).Set(FastTDP, 6000),
                options.ForOption("7W").Set(SlowTDP, 7000).Set(FastTDP, 7000),
                options.ForOption("8W").Set(SlowTDP, 8000).Set(FastTDP, 8000),
                options.ForOption("9W").Set(SlowTDP, 9000).Set(FastTDP, 9000),
                options.ForOption("10W").Set(SlowTDP, 10000).Set(FastTDP, 10000),
                options.ForOption("12W").Set(SlowTDP, 12000).Set(FastTDP, 12000),
                options.ForOption("15W").Set(SlowTDP, 15000).Set(FastTDP, 15000),
            });

            return options;
        }

        public static Menu.MenuItemWithOptions Instance = new Menu.MenuItemWithOptions()
        {
            Name = "TDP",
            PersistentKey = "TDP",
            PersistOnCreate = true,
            OptionsValues = () => { return UserOptions().GetOptions(); },
            ApplyDelay = 1000,
            ApplyValue = (selected) =>
            {
                if (!AntiCheatSettings.Default.AckAntiCheat(
                    Controller.TitleWithVersion,
                    "Changing TDP requires kernel access for a short period.",
                    "Leave the game if it uses anti-cheat protection."))
                    return null;

                var selectedOption = UserOptions().ForOption(selected);
                if (!selectedOption.Exist)
                    return null;

                var slowTDP = selectedOption.Get(SlowTDP, DefaultSlowTDP);
                var fastTDP = selectedOption.Get(FastTDP, DefaultFastTDP);

                string? result = null;
                int mutexAttempts = 0;
                while (result == null && mutexAttempts < 10)
                {
                    result = CommonHelpers.Instance.WithGlobalMutex<string>(200, () =>
                    {
                        VangoghGPU? sd = null;
                        int attempts = 0;
                        while (sd == null && attempts < 10)
                        {
                            sd = VangoghGPU.Open();
                            if (sd == null)
                            {
                                CommonHelpers.Log.TraceLine($"TDP.ApplyValue: GPU NOT FOUND (attempt {attempts + 1})");
                                System.Threading.Thread.Sleep(200);
                                attempts++;
                            }
                        }

                        if (sd == null)
                        {
                            CommonHelpers.Log.TraceLine("TDP.ApplyValue: GPU still not found after attempts, returning selected");
                            return selected;
                        }

                        CommonHelpers.Log.TraceLine($"TDP.ApplyValue: GPU found, applying TDP: slowTDP={slowTDP}, fastTDP={fastTDP}");
                        sd.SlowTDP = (uint)slowTDP;
                        sd.FastTDP = (uint)fastTDP;
                        sd.Dispose();

                        return selected;
                    });

                    if (result == null)
                    {
                        CommonHelpers.Log.TraceLine($"TDP.ApplyValue: Mutex not acquired (attempt {mutexAttempts + 1}), retrying...");
                        System.Threading.Thread.Sleep(200);
                        mutexAttempts++;
                    }
                }
                CommonHelpers.Log.TraceLine($"TDP.ApplyValue: returning {result}");
                return result ?? selected;
            }
        };
    }
}
