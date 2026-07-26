// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Linq;
using SteamKit2.CDN;
using SteamKit2.Internal;
using Xunit;

namespace DepotDownloader.Tests
{
    public sealed class ChinaCdnDirectoryFacts
    {
        [Fact]
        public void SelectCandidatesAcceptsKnownMainlandSources()
        {
            var servers = new[]
            {
                CreateServer("dl.steam.clngaa.com", 30),
                CreateServer("st.dl.eccdnx.com", 29),
                CreateServer("xz.pphimalayanrt.com", 33),
            };

            var selected = ChinaCdnDirectory.SelectCandidates(servers, appId: 3321460);

            Assert.Equal(3, selected.Count);
            Assert.Equal(new[] { 29, 30, 33 }, selected.Select(candidate => candidate.SourceId).Order());
        }

        [Fact]
        public void SelectCandidatesRejectsOverseasSources()
        {
            var servers = new[]
            {
                CreateServer("cache1-tpe-hnet.steamcontent.com", 564),
                CreateServer("cache1-hkg1.steamcontent.com", 177),
                CreateServer("cache1-tyo3.steamcontent.com", 183),
            };

            var selected = ChinaCdnDirectory.SelectCandidates(servers, appId: 3321460);

            Assert.Empty(selected);
        }

        [Fact]
        public void SelectCandidatesHonorsAllowedAppIds()
        {
            var denied = CreateServer("dl.steam.clngaa.com", 30);
            denied.allowed_app_ids.Add(570);

            var selected = ChinaCdnDirectory.SelectCandidates([denied], appId: 3321460);

            Assert.Empty(selected);
        }

        [Fact]
        public void SelectCandidatesRejectsServersWithNoClientEntries()
        {
            var server = CreateServer("dl.steam.clngaa.com", 30);
            server.num_entries_in_client_list = 0;

            var selected = ChinaCdnDirectory.SelectCandidates([server], appId: 3321460);

            Assert.Empty(selected);
        }

        [Fact]
        public void SelectCandidatesRejectsDifferentHostAndVhost()
        {
            var server = CreateServer("token-host.example", 30);
            server.vhost = "content-host.example";

            var selected = ChinaCdnDirectory.SelectCandidates([server], appId: 3321460);

            Assert.Empty(selected);
        }

        [Fact]
        public void SelectCandidatesUsesAdvertisedHttpsRequirement()
        {
            var server = CreateServer("dl.steam.clngaa.com", 30);
            server.https_support = "mandatory";

            var selected = Assert.Single(
                ChinaCdnDirectory.SelectCandidates([server], appId: 3321460));

            Assert.Equal(Server.ConnectionProtocol.HTTPS, selected.Server.Protocol);
            Assert.Equal(443, selected.Server.Port);
        }

        static CContentServerDirectory_ServerInfo CreateServer(string host, int sourceId)
        {
            return new CContentServerDirectory_ServerInfo
            {
                type = "CDN",
                source_id = sourceId,
                host = host,
                vhost = host,
                https_support = "unavailable",
                weighted_load = 50,
                num_entries_in_client_list = 1,
            };
        }
    }
}
