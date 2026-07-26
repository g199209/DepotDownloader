// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SteamKit2.CDN;

namespace DepotDownloader
{
    /// <summary>
    /// CDNClientPool provides a pool of connections to CDN endpoints, requesting CDN tokens as needed
    /// </summary>
    class CDNClientPool
    {
        private readonly Steam3Session steamSession;
        private readonly uint appId;
        public Client CDNClient { get; }
        public Server ProxyServer { get; private set; }

        private readonly List<Server> servers = [];
        private int nextServer;

        public CDNClientPool(Steam3Session steamSession, uint appId)
        {
            this.steamSession = steamSession;
            this.appId = appId;
            CDNClient = new Client(steamSession.steamClient);
        }

        public async Task UpdateServerList()
        {
            this.servers.Clear();
            nextServer = 0;

            IEnumerable<ContentServerCandidate> contentServers;

            if (ContentDownloader.Config.UseChinaCdn)
            {
                ProxyServer = null;
                contentServers = await ChinaCdnDirectory.ProbeAsync(
                    steamSession,
                    appId,
                    (uint)ContentDownloader.Config.CellID);
            }
            else
            {
                var directoryServers = await this.steamSession.steamContent.GetServersForSteamPipe(
                    (uint)ContentDownloader.Config.CellID);

                ProxyServer = directoryServers.FirstOrDefault(server => server.UseAsProxy);

                contentServers = directoryServers
                    .Where(server =>
                    {
                        var isEligibleForApp =
                            server.AllowedAppIds.Length == 0
                            || server.AllowedAppIds.Contains(appId);
                        return isEligibleForApp
                            && (server.Type == "SteamCache" || server.Type == "CDN");
                    })
                    .Select(server => new ContentServerCandidate(
                        server,
                        server.SourceID,
                        server.WeightedLoad,
                        server.NumEntries));
            }

            var weightedCdnServers = contentServers
                .Select(candidate =>
                {
                    AccountSettingsStore.Instance.ContentServerPenalty.TryGetValue(
                        candidate.Server.Host,
                        out var penalty);

                    return (candidate, penalty);
                })
                .OrderBy(pair => pair.penalty)
                .ThenBy(pair => pair.candidate.WeightedLoad);

            foreach (var (candidate, _) in weightedCdnServers)
            {
                for (var i = 0; i < candidate.NumEntries; i++)
                {
                    this.servers.Add(candidate.Server);
                }
            }

            if (this.servers.Count == 0)
            {
                throw new Exception("Failed to retrieve any download servers.");
            }
        }

        public Server GetConnection()
        {
            return servers[nextServer % servers.Count];
        }

        public void ReturnConnection(Server server)
        {
            if (server == null) return;

            // nothing to do, maybe remove from ContentServerPenalty?
        }

        public void ReturnBrokenConnection(Server server)
        {
            if (server == null) return;

            lock (servers)
            {
                if (servers[nextServer % servers.Count] == server)
                {
                    nextServer++;

                    // TODO: Add server to ContentServerPenalty
                }
            }
        }
    }
}
