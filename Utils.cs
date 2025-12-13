using Consortium.UCI;
using Newtonsoft.Json;

namespace Consortium;

public static class Utils
{
    public static List<EngineRunOptions> ReadConfig()
    {
        string json = File.ReadAllText("config.json");
        return JsonConvert.DeserializeObject<List<EngineRunOptions>>(json) ?? [];
    }
}
