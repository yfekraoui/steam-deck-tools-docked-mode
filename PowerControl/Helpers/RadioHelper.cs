using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace PowerControl.Helpers
{
    public static class RadioHelper
    {
        public static async Task<bool> RequestAccess()
        {
            return (await Radio.RequestAccessAsync()) == RadioAccessStatus.Allowed;
        }

        public static async Task SetState(Radio radio, bool enable)
        {
            await radio.SetStateAsync(enable ? RadioState.On : RadioState.Off);
        }
    }
}
