using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SimsLegacy
{
    internal sealed class SaveHousehold
    {
        public ulong Id;
        public string Name, Description;
        public bool Player, Hidden;
        public readonly HashSet<ulong> Members = new HashSet<ulong>();
        public override string ToString() { return Name + "  ·  " + Members.Count; }
    }

    internal sealed class GameSave
    {
        private struct Connection
        {
            public ulong From, To;
            public bool Partner;
            public Connection(ulong from, ulong to, bool partner)
            {
                From = partner ? Math.Min(from, to) : from;
                To = partner ? Math.Max(from, to) : to;
                Partner = partner;
            }
        }

        public string Title;
        public ulong ActiveHousehold;
        public readonly Dictionary<ulong, Sim> People = new Dictionary<ulong, Sim>();
        public readonly List<SaveHousehold> Households = new List<SaveHousehold>();
        private readonly Dictionary<ulong, ulong> membership = new Dictionary<ulong, ulong>();
        private readonly HashSet<Connection> legacy = new HashSet<Connection>();
        private readonly HashSet<Connection> modern = new HashSet<Connection>();
        private readonly HashSet<ulong> modernChildren = new HashSet<ulong>();

        public static GameSave Open(string path, FileShare share = FileShare.Read)
        {
            var result = new GameSave { Title = Path.GetFileNameWithoutExtension(path) };
            var graphs = new List<SaveFields>();
            var buffers = new List<byte[]>();
            foreach (byte[] bytes in SavePackage.ReadGameData(path, share))
            {
                buffers.Add(bytes);
                var root = new SaveFields(bytes);
                foreach (var field in root.Read().Where(f => f.Wire == 2))
                {
                    switch (field.Number)
                    {
                        case 2: result.ReadSlot(field.Data); break;
                        case 5: result.ReadHousehold(field.Data); break;
                        case 6: result.ReadSim(field.Data); break;
                        case 16: graphs.Add(field.Data); break;
                    }
                }
            }
            if (result.People.Count == 0) throw new InvalidDataException("В сохранении не найдены карточки симов.");
            foreach (var graph in graphs) result.ReadGraph(graph);
            foreach (var bytes in buffers) result.ReadGenealogyRecords(bytes);
            result.legacy.RemoveWhere(link => !link.Partner && result.modernChildren.Contains(link.To));
            result.modern.UnionWith(result.legacy);
            result.modern.RemoveWhere(link => !result.People.ContainsKey(link.From) || !result.People.ContainsKey(link.To));

            var households = result.Households.ToDictionary(h => h.Id);
            foreach (var pair in result.membership)
            {
                if (pair.Value == 0) continue;
                SaveHousehold household;
                if (!households.TryGetValue(pair.Value, out household))
                {
                    household = new SaveHousehold { Id = pair.Value, Name = "Семья " + result.People[pair.Key].Name };
                    households.Add(pair.Value, household);
                    result.Households.Add(household);
                }
                household.Members.Add(pair.Key);
            }
            foreach (var household in result.Households) household.Members.IntersectWith(result.People.Keys);
            result.Households.RemoveAll(h => h.Members.Count == 0);
            return result;
        }

        private void ReadSlot(SaveFields data)
        {
            string name = data.Messages(9).FirstOrDefault().Text(180);
            if (name.Length > 0) Title = name;
            ulong active = data.Number(11);
            if (active != 0) ActiveHousehold = active;
        }

        private void ReadHousehold(SaveFields data)
        {
            ulong id = data.Number(2);
            if (id == 0 || Households.Any(h => h.Id == id)) return;
            string name = data.Messages(3).FirstOrDefault().Text(180);
            var household = new SaveHousehold { Id = id, Name = name.Length > 0 ? name : "Семья без названия",
                Description = data.Messages(18).FirstOrDefault().Text(16000), Hidden = data.Number(26) != 0,
                Player = data.Number(31) != 0 || data.Number(9) != 0 };
            household.Members.UnionWith(data.Messages(11).FirstOrDefault().Ids(1, true));
            Households.Add(household);
        }

        private void ReadSim(SaveFields data)
        {
            ulong id = data.Number(1);
            if (id == 0 || People.ContainsKey(id)) return;
            string name = Name(data, 5, 6);
            if (name.Length == 0) return;
            var attributes = data.Messages(30).FirstOrDefault();
            var traits = attributes.Messages(10).FirstOrDefault().Ids(1, false).Distinct().Where(GameTraits.Names.ContainsKey);
            var death = attributes.Messages(7).FirstOrDefault();
            var career = attributes.Messages(12).FirstOrDefault().Messages(4).FirstOrDefault().Text(180);
            People.Add(id, new Sim { GameId = id.ToString(CultureInfo.InvariantCulture), Name = name,
                Age = Age(data.Number(8), data.Number(7)), Traits = String.Join(", ", traits.Select(t => GameTraits.Names[t]).ToArray()),
                Career = career, Deceased = death.Number(1) != 0 || death.Number(2) != 0 || data.Number(73) != 0 || data.Number(74) != 0 });
            membership[id] = data.Number(4);
            Add(legacy, id, data.Number(15), true);
            Add(legacy, id, data.Number(68), true);
            foreach (ulong partner in data.Messages(72).FirstOrDefault().Ids(1, true)) Add(legacy, id, partner, true);
            var relations = attributes.Messages(14).FirstOrDefault().Messages(1)
                .Select(f => new { Kind = f.Number(1), Id = f.Number(2) }).Where(f => f.Id != 0).ToArray();
            foreach (var f in relations.Where(f => f.Kind == 0 || f.Kind == 1 || f.Kind == 7))
                Add(legacy, f.Id, id, false);
            foreach (var f in relations.Where(f => f.Kind >= 2 && f.Kind <= 5))
            {
                var parent = relations.FirstOrDefault(p => p.Kind == (f.Kind - 2) / 2);
                if (parent != null) Add(legacy, f.Id, parent.Id, false);
            }
            foreach (var f in relations.Where(f => f.Kind >= 9 && f.Kind <= 11)) Add(legacy, id, f.Id, true);
        }

        private void ReadGraph(SaveFields graph)
        {
            var records = graph.Messages(1).ToArray();
            var ids = new Dictionary<ulong, ulong>();
            foreach (var record in records)
            {
                ulong node = record.Number(1);
                if (node == 0) continue;
                ulong sim = record.Number(2);
                ids[node] = sim == 0 ? node : sim;
                var genus = record.Messages(3).FirstOrDefault();
                AddAncestor(ids[node], Name(record, 4, 5), Age(genus.Number(2), genus.Number(1)));
            }
            foreach (var record in records)
            {
                ulong source;
                if (!ids.TryGetValue(record.Number(1), out source)) continue;
                foreach (var edge in record.Messages(6))
                {
                    ulong target;
                    if (ids.TryGetValue(edge.Number(1), out target)) GraphEdge(source, target, edge.Number(2));
                }
            }
        }

        private void ReadGenealogyRecords(byte[] bytes)
        {
            // Recent saves also contain compact genealogy records inside the master resource.
            // Match only complete records whose edge source matches the record ID.
            var candidates = new Dictionary<ulong, Tuple<string, List<Tuple<ulong, ulong>>>>();
            for (int i = 0; i + 8 < bytes.Length; i++)
            {
                if (bytes[i] != 10) continue;
                int end = i;
                SaveFields.Field field, identifier;
                if (!SaveFields.TryRead(bytes, ref end, bytes.Length, out field)) continue;
                var data = field.Data;
                if (data.End - data.Start < 7 || data.End - data.Start > 65536 || bytes[data.Start] != 8) continue;
                int next = data.Start;
                if (!SaveFields.TryRead(bytes, ref next, data.End, out identifier) || identifier.NumberValue == 0 ||
                    next >= data.End || (bytes[next] != 18 && bytes[next] != 26 && bytes[next] != 34)) continue;
                ulong id = identifier.NumberValue;
                string first = "", last = "";
                List<Tuple<ulong, ulong>> edges = null;
                bool valid = true;
                while (next < data.End)
                {
                    if (!SaveFields.TryRead(bytes, ref next, data.End, out field) || field.Wire != 2 || field.Number < 2 || field.Number > 4)
                    { valid = false; break; }
                    if (field.Number == 2) first = field.Data.Text(120);
                    if (field.Number == 3) last = field.Data.Text(120);
                    if (field.Number != 4) continue;
                    ulong target, kind;
                    if (!TryGenealogyEdge(field.Data, id, out target, out kind))
                    { valid = false; break; }
                    if (kind != 1 && kind != 2 && kind != 4) continue;
                    if (edges == null) edges = new List<Tuple<ulong, ulong>>();
                    edges.Add(Tuple.Create(target, kind));
                }
                if (!valid || edges == null) continue;
                string name = (first + " " + last).Trim();
                Sim existing;
                if (People.TryGetValue(id, out existing) && name.Length > 0 && !String.Equals(name, existing.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!candidates.ContainsKey(id)) candidates.Add(id, Tuple.Create(name, edges));
                i = end - 1;
            }
            var adjacent = new Dictionary<ulong, List<ulong>>();
            foreach (var entry in candidates)
                foreach (var edge in entry.Value.Item2)
                {
                    if (!adjacent.ContainsKey(entry.Key)) adjacent[entry.Key] = new List<ulong>();
                    if (!adjacent.ContainsKey(edge.Item1)) adjacent[edge.Item1] = new List<ulong>();
                    adjacent[entry.Key].Add(edge.Item1);
                    adjacent[edge.Item1].Add(entry.Key);
                }
            foreach (ulong id in Reachable(People.Keys.Where(adjacent.ContainsKey), adjacent))
            {
                Tuple<string, List<Tuple<ulong, ulong>>> entry;
                if (!candidates.TryGetValue(id, out entry)) continue;
                AddAncestor(id, entry.Item1, "");
                foreach (var edge in entry.Item2) GraphEdge(id, edge.Item1, edge.Item2);
            }
        }

        private static bool TryGenealogyEdge(SaveFields edge, ulong id, out ulong target, out ulong kind)
        {
            int at = edge.Start;
            SaveFields.Field source, to, relation;
            target = kind = 0;
            if (!SaveFields.TryRead(edge.Bytes, ref at, edge.End, out source) || source.Number != 1 || source.Wire != 0 || source.NumberValue != id ||
                !SaveFields.TryRead(edge.Bytes, ref at, edge.End, out to) || to.Number != 2 || to.Wire != 0 || to.NumberValue == 0 ||
                !SaveFields.TryRead(edge.Bytes, ref at, edge.End, out relation) || relation.Number != 3 || relation.Wire != 0 || at != edge.End)
                return false;
            target = to.NumberValue; kind = relation.NumberValue;
            return true;
        }

        private void GraphEdge(ulong from, ulong to, ulong kind)
        {
            if (from == to || from == 0 || to == 0) return;
            if (kind == 1) { Add(modern, from, to, false); modernChildren.Add(to); }
            else if (kind == 2) { Add(modern, to, from, false); modernChildren.Add(from); }
            else if (kind == 4) Add(modern, from, to, true);
        }

        private void AddAncestor(ulong id, string name, string age)
        {
            if (!People.ContainsKey(id))
                People.Add(id, new Sim { GameId = id.ToString(CultureInfo.InvariantCulture),
                    Name = name.Length == 0 ? "Неизвестный предок" : name, Deceased = true, Age = age });
        }

        private static void Add(HashSet<Connection> links, ulong from, ulong to, bool partner)
        {
            if (from != to && from != 0 && to != 0) links.Add(new Connection(from, to, partner));
        }

        private static string Name(SaveFields data, int first, int last)
        {
            return (data.Messages(first).FirstOrDefault().Text(120) + " " + data.Messages(last).FirstOrDefault().Text(120)).Trim();
        }

        private static string Age(ulong age, ulong gender)
        {
            bool female = gender == 8192;
            switch (age)
            {
                case 1: return female ? "Новорождённая" : "Новорождённый";
                case 128: return "Младенец";
                case 2: return female ? "Малышка" : "Малыш";
                case 4: return "Ребёнок";
                case 8: return "Подросток";
                case 16: return female ? "Молодая" : "Молодой";
                case 32: return female ? "Взрослая" : "Взрослый";
                case 64: return female ? "Пожилая" : "Пожилой";
                default: return "";
            }
        }

        private static HashSet<ulong> Reachable(IEnumerable<ulong> seeds, Dictionary<ulong, List<ulong>> adjacent)
        {
            var seen = new HashSet<ulong>(seeds);
            var queue = new Queue<ulong>(seen);
            while (queue.Count > 0)
            {
                List<ulong> neighbors;
                if (!adjacent.TryGetValue(queue.Dequeue(), out neighbors)) continue;
                foreach (ulong next in neighbors) if (seen.Add(next)) queue.Enqueue(next);
            }
            return seen;
        }

        public SaveHousehold[] OrderedHouseholds()
        {
            return Households.OrderByDescending(h => h.Id == ActiveHousehold)
                .ThenBy(h => h.Hidden).ThenByDescending(h => h.Player).ThenBy(h => h.Name).ToArray();
        }

        public Dynasty CreateTree(SaveHousehold household, bool includeRelatives)
        {
            var ids = new HashSet<ulong>(household == null ? (IEnumerable<ulong>)People.Keys : household.Members);
            if (household != null && includeRelatives)
            {
                var adjacent = People.Keys.ToDictionary(id => id, id => new List<ulong>());
                foreach (var link in modern) { adjacent[link.From].Add(link.To); adjacent[link.To].Add(link.From); }
                ids = Reachable(ids, adjacent);
            }
            var result = new Dynasty { Title = household == null ? Title : household.Name,
                About = household == null ? "" : household.Description };
            result.Sims.AddRange(ids.Select(id => People[id]).OrderBy(s => s.Name));
            foreach (var link in modern.Where(l => ids.Contains(l.From) && ids.Contains(l.To)))
                result.Links.Add(new Link { From = People[link.From].Id, To = People[link.To].Id, Kind = link.Partner ? "partner" : "parent" });
            return result;
        }
    }
}
