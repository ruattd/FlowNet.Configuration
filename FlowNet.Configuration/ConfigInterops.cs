using System.Threading.Tasks;

namespace FlowNet.Configuration;

internal static class ConfigInterops
{
    public static Task Initialize() => ConfigCenter.Initialize();
}
