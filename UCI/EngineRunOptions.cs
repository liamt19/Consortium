using System;
using System.Collections.Generic;
using System.Text;

namespace Consortium.UCI;

public class EngineRunOptions
{
    public string Name { get; set; } = null!;
    public string Path { get; set; } = null!;
    public List<string> Opts { get; set; } = new();
}
