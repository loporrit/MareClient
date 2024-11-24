using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.MareConfiguration.Configurations;

[Serializable]
public class SyncshellConfig : IMareConfiguration
{
    public Dictionary<string, ServerShellStorage> ServerShellStorage { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}