using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Consortium.UCI;

public class EngineConfig
{
    [JsonProperty("sync_by_depth")]
    public bool SyncByDepth { get; set; } = new();

    [JsonProperty("default_opts")]
    public List<string> DefaultOpts { get; set; } = new();

    [JsonProperty("engines")]
    public List<EngineRunOptions> Engines { get; set; } = new();
}

public class EngineRunOptions
{
    public string Name { get; set; } = null!;
    public string Path { get; set; } = null!;
    public List<string> Opts { get; set; } = new();

    [JsonProperty(PropertyName = "remapped_cmds")]
    public List<string> RemappedCmds { get; set; } = new();
}
