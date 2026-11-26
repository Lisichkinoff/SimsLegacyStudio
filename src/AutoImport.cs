using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SimsLegacy
{
    internal static class AutoImport
    {
        internal static Tuple<long, DateTime, DateTime> ReadStamp(string path)
        {
            var file = new FileInfo(path);
            return Tuple.Create(file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
        }

        internal sealed class Batch
        {
            internal readonly List<Sim> NewSims = new List<Sim>();
            internal readonly List<Link> NewLinks = new List<Link>();
            internal readonly HashSet<string> Seen;

            internal Batch(IEnumerable<string> seen)
            {
                Seen = new HashSet<string>(seen ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            }
        }

        internal static Batch Prepare(Dynasty target, Dynasty incoming, AutoImportSettings settings)
        {
            var batch = new Batch(settings.SeenGameIds);
            var existing = new Dictionary<string, Sim>(StringComparer.Ordinal);
            foreach (var sim in target.Sims)
                if (!String.IsNullOrEmpty(sim.GameId) && !existing.ContainsKey(sim.GameId))
                    existing.Add(sim.GameId, sim);

            var map = new Dictionary<string, string>();
            foreach (var sim in incoming.Sims)
            {
                if (String.IsNullOrEmpty(sim.GameId)) continue;
                bool firstSeen = batch.Seen.Add(sim.GameId);
                Sim known;
                if (existing.TryGetValue(sim.GameId, out known))
                    map.Add(sim.Id, known.Id);
                else if (firstSeen)
                {
                    existing.Add(sim.GameId, sim);
                    map.Add(sim.Id, sim.Id);
                    batch.NewSims.Add(sim);
                }
            }
            if (batch.NewSims.Count == 0) return batch;

            var newIds = new HashSet<string>(batch.NewSims.Select(sim => sim.Id));
            var members = Family.Members(target.Sims.Concat(batch.NewSims));
            var links = new List<Link>(target.Links);
            foreach (var link in incoming.Links)
            {
                string from, to;
                if (!map.TryGetValue(link.From, out from) || !map.TryGetValue(link.To, out to) ||
                    (!newIds.Contains(from) && !newIds.Contains(to)) ||
                    !Family.CanLink(links, from, to, link.Kind, members)) continue;
                var addition = new Link { From = from, To = to, Kind = link.Kind };
                links.Add(addition);
                batch.NewLinks.Add(addition);
            }
            return batch;
        }
    }
}
