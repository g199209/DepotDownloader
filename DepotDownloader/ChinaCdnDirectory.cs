// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using SteamKit2.CDN;
using SteamKit2.Internal;

namespace DepotDownloader
{
    /// <summary>
    /// Discovers mainland China Steam content servers without relying on the
    /// network location of the Steam CM connection.
    /// </summary>
    static class ChinaCdnDirectory
    {
        const uint MaxServersPerProbe = 50;

        // These are stable public IPv4 resolver addresses operated from
        // mainland China. They are used only as geographic hints to Steam's
        // content directory; DepotDownloader never connects to them.
        static readonly (string Name, string Address)[] LocationProbes =
        [
            ("Alibaba Public DNS", "223.5.5.5"),
            ("DNSPod Public DNS", "119.29.29.29"),
            ("Baidu Public DNS", "180.76.76.76"),
            ("114DNS", "114.114.114.114"),
        ];

        // Steam currently assigns these source IDs to its mainland delivery
        // partners. Source IDs are used instead of domain suffixes because
        // hostnames and redirect targets vary by directory probe and ISP.
        static readonly FrozenSet<int> MainlandSourceIds =
            new[] { 29, 30, 33 }.ToFrozenSet();

        public static async Task<IReadOnlyCollection<ContentServerCandidate>> ProbeAsync(
            Steam3Session steamSession,
            uint appId,
            uint cellId)
        {
            Console.WriteLine(
                "China CDN mode: probing Steam's content directory for app {0} (cell {1}).",
                appId,
                cellId);

            var candidates = new Dictionary<string, ContentServerCandidate>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var (name, address) in LocationProbes)
            {
                CContentServerDirectory_GetServersForSteamPipe_Response response;

                try
                {
                    response = await steamSession.GetContentServersForSteamPipe(
                        cellId,
                        MaxServersPerProbe,
                        address);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  {0}: directory probe failed: {1}", name, ex.Message);
                    continue;
                }

                var probeCandidates = SelectCandidates(response.servers, appId);

                Console.WriteLine(
                    "  {0}: {1} verified mainland server(s).",
                    name,
                    probeCandidates.Count);

                foreach (var candidate in probeCandidates)
                {
                    var key = string.Concat(
                        candidate.Server.Host,
                        "\n",
                        candidate.Server.VHost,
                        "\n",
                        candidate.Server.Port);

                    if (!candidates.TryGetValue(key, out var existing)
                        || candidate.WeightedLoad < existing.WeightedLoad)
                    {
                        candidates[key] = candidate;
                    }
                }
            }

            if (candidates.Count == 0)
            {
                throw new ContentDownloaderException(
                    $"Steam returned no eligible mainland China CDN for app {appId}. "
                    + $"Allowed content source IDs: {string.Join(", ", MainlandSourceIds.Order())}. "
                    + "Overseas fallback is disabled.");
            }

            var selected = candidates.Values
                .OrderBy(candidate => candidate.WeightedLoad)
                .ThenBy(candidate => candidate.Server.Host, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Console.WriteLine("Using {0} verified mainland content server(s):", selected.Length);
            foreach (var candidate in selected)
            {
                Console.WriteLine(
                    "  {0} (source {1}, weighted load {2})",
                    candidate.Server.Host,
                    candidate.SourceId,
                    candidate.WeightedLoad);
            }

            return selected;
        }

        internal static IReadOnlyCollection<ContentServerCandidate> SelectCandidates(
            IEnumerable<CContentServerDirectory_ServerInfo> servers,
            uint appId)
        {
            var candidates = new List<ContentServerCandidate>();

            foreach (var serverInfo in servers)
            {
                var isContentServer = serverInfo.type is "CDN" or "SteamCache";
                var isMainlandSource = MainlandSourceIds.Contains(serverInfo.source_id);
                var isEligibleForApp =
                    serverInfo.allowed_app_ids.Count == 0
                    || serverInfo.allowed_app_ids.Contains(appId);
                var isAdvertisedForUse = serverInfo.num_entries_in_client_list > 0;

                if (!isContentServer
                    || !isMainlandSource
                    || !isEligibleForApp
                    || !isAdvertisedForUse)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(serverInfo.host)
                    || string.IsNullOrWhiteSpace(serverInfo.vhost))
                {
                    continue;
                }

                // SteamKit's public Server conversion cannot set Host and VHost
                // independently. Current mainland partners advertise identical
                // values. Refuse an unfamiliar shape instead of silently sending
                // authentication or content requests to the wrong host.
                if (!serverInfo.host.Equals(serverInfo.vhost, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(
                        "  Skipping unsupported mainland endpoint with host '{0}' and vhost '{1}'.",
                        serverInfo.host,
                        serverInfo.vhost);
                    continue;
                }

                var port = serverInfo.https_support == "mandatory" ? 443 : 80;
                Server server = new DnsEndPoint(serverInfo.host, port);

                candidates.Add(new ContentServerCandidate(
                    server,
                    serverInfo.source_id,
                    serverInfo.weighted_load,
                    serverInfo.num_entries_in_client_list));
            }

            return candidates;
        }
    }

    readonly record struct ContentServerCandidate(
        Server Server,
        int SourceId,
        float WeightedLoad,
        int NumEntries);
}
