using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MareSynchronos.MareConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, IDalamudPluginInterface pi,
    NotesConfigService notesConfig) : IHostedService
{
    public void Migrate()
    {
        var oldUri = MareSynchronos.WebAPI.ApiController.LoporritServiceUriOld;
        var newUri = MareSynchronos.WebAPI.ApiController.LoporritServiceUri;

        if (notesConfig.Current.ServerNotes.TryGetValue(oldUri, out var old))
        {
            logger.LogDebug("Migrating server notes {old} => {new}", oldUri, newUri);
            notesConfig.Current.ServerNotes.TryAdd(newUri, new());
            var merged = notesConfig.Current.ServerNotes.GetValueOrDefault(newUri, new());
            foreach (var (k, v) in old.GidServerComments)
                merged.GidServerComments.TryAdd(k, v);
            foreach (var (k, v) in old.UidServerComments)
                merged.UidServerComments.TryAdd(k, v);
            notesConfig.Current.ServerNotes.Remove(oldUri);
            notesConfig.Save();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Migrate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static void SaveConfig(IMareConfiguration config, string path)
    {
        File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
    }

    private string ConfigurationPath(string configName) => Path.Combine(pi.ConfigDirectory.FullName, configName);
}
