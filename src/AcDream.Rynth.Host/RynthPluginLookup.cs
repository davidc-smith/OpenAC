namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's plugin-by-name order (PluginManager.ResolvePluginExport): among active
/// plugins, an exact reported name first, then a name that contains the asked one. Here a
/// plugin's host name is its exports namespace ("RynthCore.Plugin.RynthNav"), so the
/// "contains" step also looks at that.
/// </summary>
internal static class RynthPluginLookup
{
    internal static IEnumerable<RynthPlugin> Candidates(IReadOnlyList<RynthPlugin> plugins, string name)
    {
        foreach (RynthPlugin plugin in plugins)
        {
            if (plugin.IsActive && plugin.ReportedName.Equals(name, StringComparison.OrdinalIgnoreCase))
                yield return plugin;
        }

        foreach (RynthPlugin plugin in plugins)
        {
            if (plugin.IsActive
                && !plugin.ReportedName.Equals(name, StringComparison.OrdinalIgnoreCase)
                && (plugin.ReportedName.Contains(name, StringComparison.OrdinalIgnoreCase)
                    || plugin.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                yield return plugin;
        }
    }
}
