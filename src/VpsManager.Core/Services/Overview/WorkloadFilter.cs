using VpsManager.Core.Models;

namespace VpsManager.Core.Services.Overview;

/// <summary>
/// Pure filtering for the Overview workload table, kept out of the view model so the
/// matching rules can be unit tested.
/// </summary>
public static class WorkloadFilter
{
    public const string AllSources = "All";

    public static IReadOnlyList<string> AvailableSources(IEnumerable<RuntimeInfo> workloads)
    {
        ArgumentNullException.ThrowIfNull(workloads);

        var sources = new List<string> { AllSources };
        sources.AddRange(workloads
            .Select(workload => workload.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase));

        return sources;
    }

    public static IReadOnlyList<RuntimeInfo> Apply(IEnumerable<RuntimeInfo> workloads, string? search, string? source)
    {
        ArgumentNullException.ThrowIfNull(workloads);

        var term = string.IsNullOrWhiteSpace(search) ? string.Empty : search.Trim();
        var selectedSource = string.IsNullOrWhiteSpace(source) ? AllSources : source;

        return workloads
            .Where(workload => selectedSource.Equals(AllSources, StringComparison.OrdinalIgnoreCase)
                || workload.Source.Equals(selectedSource, StringComparison.OrdinalIgnoreCase))
            .Where(workload => term.Length == 0
                || Contains(workload.Name, term)
                || Contains(workload.Image, term)
                || Contains(workload.Details, term)
                || Contains(workload.Ports, term))
            .ToList();
    }

    private static bool Contains(string value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
