using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SimsLegacy
{
    internal static class Mccc
    {
        private const RegexOptions Ignore = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex GedLine = new Regex(@"^(\d+)\s+(?:(@[^@]+@)\s+)?([\w_]+)(?:\s+(.*))?$");
        private static readonly Regex DetailName = new Regex(@"^(?:(?:Sim\s+Name|Full\s+Name|Name|Имя(?:\s+сима)?)\s*[:=]\s*|(?:Sim\s+)?Details\s+for(?:\s+Sim)?\s*[:=]?\s+|Sim\s+Details\s*[-:]\s*)(.+)$", Ignore);
        private static readonly Regex Identity = new Regex(@"^(?:Sim\s*(?:ID|Identifier)|ID\s+сима)\s*[:=]\s*(\d+)", Ignore);
        private static readonly Regex Traits = new Regex(@"^(?:(?:Sim|Personality|Equipped|Current)\s+)?Traits(?:\s*\(\d+\))?\s*[:=]\s*(.*)$|^Черты(?:\s+характера)?\s*[:=]\s*(.*)$", Ignore);
        private static readonly Regex TraitToken = new Regex(@"\btrait_[\w_]+", Ignore);
        private static readonly Regex Timestamp = new Regex(@"^\s*\[?\d{1,4}[/.-]\d{1,2}[/.-]\d{1,4}[ T]+\d\d?:\d\d(?::\d\d)?(?:[.,]\d+)?\]?\s*[-:]?\s*");
        private static readonly Regex PopulationHeading = new Regex(@"^(?:(?:\d+\s+)?(?:Married|Single|Pregnant|Ghost|Hidden|Vampire|Alien|Fairy|Immortal|Homeless|Human|Culled|Male|Female|Werewolf|Spellcaster|Mermaid|All|Played|Unplayed)\s+Sims?\b(?:\s+(?:Names?|List))?(?:\s*\(\d+\))?|Sim\s+(?:Careers|Names)|(?:Ghosts|Vampires|Aliens|Fairies|Spellcasters|Mermaids|Werewolves))(?:\s*:\s*(.*))?$", Ignore);
        private static readonly Regex Person = new Regex(@"^[\p{L}\p{M}][\p{L}\p{M} .’'\-]{0,95}$");

        public static string Read(string path)
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024)
                throw new InvalidDataException("Размер файла превышает 32 МБ.");
            try
            {
                using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                    return reader.ReadToEnd();
            }
            catch (DecoderFallbackException) { return File.ReadAllText(path, Encoding.GetEncoding(1251)); }
        }

        public static Dynasty Parse(string source, string fileName)
        {
            return Path.GetExtension(fileName).Equals(".ged", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(fileName).Equals(".gedcom", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(source, @"(?m)^\s*0\s+(HEAD|@[^@]+@\s+INDI)\b")
                ? ParseGed(source) : ParseLog(source, fileName);
        }

        private static Dynasty ParseGed(string source)
        {
            var tree = new Dynasty();
            var people = new Dictionary<string, Sim>();
            var families = new List<List<KeyValuePair<string, string>>>();
            List<KeyValuePair<string, string>> family = null;
            Sim current = null;
            string fact = null;
            string noteField = null;
            foreach (string raw in source.Split('\n'))
            {
                var line = GedLine.Match(raw.Trim().TrimStart('\uFEFF'));
                if (!line.Success) continue;
                int level;
                if (!Int32.TryParse(line.Groups[1].Value, out level)) continue;
                string tag = line.Groups[3].Value.ToUpperInvariant();
                string value = line.Groups[4].Value.Trim();
                if (level == 0)
                {
                    current = null;
                    family = null;
                    string key = line.Groups[2].Value;
                    if (tag == "INDI" && key.Length > 0 && !people.ContainsKey(key))
                    {
                        current = new Sim();
                        people.Add(key, current);
                    }
                    else if (tag == "FAM") families.Add(family = new List<KeyValuePair<string, string>>());
                }
                else if (level == 1 && family != null)
                {
                    if (tag == "HUSB" || tag == "WIFE" || tag == "PART" || tag == "CHIL")
                        family.Add(new KeyValuePair<string, string>(tag, value));
                }
                else if (current != null)
                {
                    if (level == 1)
                    {
                        fact = null;
                        noteField = tag;
                        if (tag == "NAME") current.Name = Regex.Replace(value.Replace("/", " "), @"\s+", " ").Trim();
                        if (tag == "OCCU") current.Career = value;
                        if (tag == "EDUC") current.Education = value;
                        if (tag == "NOTE" && !value.StartsWith("@")) current.Bio = value;
                        if (tag == "DEAT" && value != "N") current.Deceased = true;
                        if (tag == "FACT") fact = value;
                        if (tag == "IDNO" || tag == "_SIM_ID") current.GameId = value;
                    }
                    else if (level == 2)
                    {
                        if (tag == "PLAC" && noteField == "BIRT") current.Birthplace = value;
                        if (tag == "TYPE" && noteField == "FACT" && value.Equals("SIM_ID", StringComparison.OrdinalIgnoreCase))
                            current.GameId = fact;
                        if (tag == "TYPE" && noteField == "FACT" && value.Equals("TRAIT", StringComparison.OrdinalIgnoreCase))
                            current.Traits = Append(current.Traits, fact);
                        if (noteField == "NOTE" && !String.IsNullOrEmpty(current.Bio) && (tag == "CONT" || tag == "CONC"))
                            current.Bio += (tag == "CONT" ? "\n" : "") + value;
                    }
                }
            }
            tree.Sims.AddRange(people.Values.Where(s => !String.IsNullOrWhiteSpace(s.Name)));
            var members = Family.Members(tree.Sims);
            foreach (var fam in families)
            {
                var parents = fam.Where(f => f.Key != "CHIL" && people.ContainsKey(f.Value)).Select(f => people[f.Value]).Distinct().ToList();
                for (int i = 0; i < parents.Count; i++)
                {
                    for (int j = i + 1; j < parents.Count; j++) Add(tree, members, parents[i].Id, parents[j].Id, "partner");
                    foreach (var child in fam.Where(f => f.Key == "CHIL" && people.ContainsKey(f.Value)))
                        Add(tree, members, parents[i].Id, people[child.Value].Id, "parent");
                }
            }
            return tree;
        }

        private static Dynasty ParseLog(string source, string fileName)
        {
            var tree = new Dynasty();
            var fileNameMatch = Regex.Match(Path.GetFileNameWithoutExtension(fileName),
                @"^MCCC_SimDetails_(.+?)_[a-z]{3}\d{1,2}_\d{6}(?:\s*\(\d+\))?$", Ignore);
            Sim current = fileNameMatch.Success && !String.IsNullOrWhiteSpace(source) ? PersonFrom(fileNameMatch.Groups[1].Value.Replace('_', ' ')) : null;
            if (current != null) tree.Sims.Add(current);
            bool readingTraits = false;
            bool population = false;
            string pendingId = null;
            bool populationFile = Path.GetFileName(fileName).StartsWith("MCCC_GetPop", StringComparison.OrdinalIgnoreCase);
            bool detailFile = Path.GetFileName(fileName).StartsWith("MCCC_Sim", StringComparison.OrdinalIgnoreCase);
            foreach (string raw in source.Split('\n'))
            {
                string line = Timestamp.Replace(raw.Trim().TrimStart('\uFEFF'), "").Trim().Trim('|').Trim();
                if (line.Length == 0) { readingTraits = false; continue; }
                var match = DetailName.Match(line);
                if (!match.Success)
                    match = Regex.Match(line, @"^(?:Sim\s*[:=]\s*|(?:Logging|Log)\s+Sim\s+Details\s+for\s+|Sim(?:Info)?\s+(?:Details|Information)\s*[:=-]\s*)(.+)$", Ignore);
                if (match.Success)
                {
                    var sim = PersonFrom(match.Groups[1].Value);
                    if (sim != null)
                    {
                        if (detailFile && current != null && fileNameMatch.Success)
                        {
                            if (String.Equals(sim.Name, current.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                if (current.GameId == null) current.GameId = sim.GameId;
                                if (current.Age == null) current.Age = sim.Age;
                            }
                            continue;
                        }
                        if (sim.GameId == null) sim.GameId = pendingId;
                        pendingId = null;
                        tree.Sims.Add(current = sim);
                        readingTraits = population = false;
                        continue;
                    }
                }
                if (detailFile && current == null)
                {
                    match = Regex.Match(line, @"^(.+?)\s*\((?:Sim\s*ID\s*[:=]\s*)?(\d{5,})\)\s*(?:[:=-].*)?$", Ignore);
                    if (match.Success)
                    {
                        var sim = PersonFrom(match.Groups[1].Value);
                        if (sim != null)
                        {
                            sim.GameId = match.Groups[2].Value;
                            tree.Sims.Add(current = sim);
                            continue;
                        }
                    }
                }
                match = Identity.Match(line);
                if (match.Success)
                {
                    if (current != null && !population) current.GameId = match.Groups[1].Value;
                    else pendingId = match.Groups[1].Value;
                    continue;
                }
                match = PopulationHeading.Match(line.Trim('-', '=', ' ', '[', ']'));
                if (match.Success)
                {
                    population = true;
                    current = null;
                    readingTraits = false;
                    string names = match.Groups[1].Value;
                    foreach (string item in Regex.Split(names, @"[,;](?![^()]*\))"))
                    {
                        var sim = PersonFrom(item);
                        if (sim != null) tree.Sims.Add(sim);
                    }
                    continue;
                }
                bool populationRecord = populationFile && Regex.IsMatch(line,
                    @"^\s*(?:[-*•]|\d+[.)])\s*\p{L}|^\p{L}.*?(?:\(.*\b(?:Female|Male|Adult|Elder|Teen|Child|\d{5,})\b|\|\s*(?:Female|Male)\b)", Ignore);
                if (population || populationRecord)
                {
                    var sim = PersonFrom(line);
                    if (sim != null) { tree.Sims.Add(sim); continue; }
                    if (line.Contains(":") || Regex.IsMatch(line, @"^[-=]{3,}")) population = false;
                }
                if (current == null) continue;
                match = Traits.Match(line.Trim('*', ' ', '=', '-', '[', ']'));
                if (match.Success || Regex.IsMatch(line, @"^[-=*\[\] ]*(?:Sim\s+|Personality\s+)?Traits[-=*\[\] ]*$", Ignore))
                {
                    readingTraits = true;
                    string value = match.Groups.Count > 2 ? match.Groups[1].Value + match.Groups[2].Value : "";
                    current.Traits = Append(current.Traits, CleanTraits(value));
                    continue;
                }
                if (readingTraits)
                {
                    bool indented = raw.Length > 0 && Char.IsWhiteSpace(raw[0]);
                    if (TraitToken.IsMatch(line) || (!line.Contains(":") && (indented || line.StartsWith("-") || line.StartsWith("*") ||
                        Regex.IsMatch(line, @"^\d+[.)]\s+|^\p{L}.*\s+\(\d+\)$")) &&
                        Regex.IsMatch(line, @"\p{L}")))
                    {
                        current.Traits = Append(current.Traits, CleanTraits(line));
                        continue;
                    }
                    readingTraits = false;
                }
                match = Regex.Match(line, @"^(Age(?:\s+Stage)?|Career|Aspiration|Education|Birthplace|Возраст|Карьера|Жизненная цель)\s*[:=]\s*(.+)$", Ignore);
                if (match.Success)
                {
                    string value = match.Groups[2].Value;
                    switch (match.Groups[1].Value.ToLowerInvariant())
                    {
                        case "age": case "age stage": case "возраст": current.Age = TranslateAge(value); break;
                        case "career": case "карьера": current.Career = value; break;
                        case "aspiration": case "жизненная цель": current.Aspiration = value; break;
                        case "education": current.Education = value; break;
                        case "birthplace": current.Birthplace = value; break;
                    }
                }
            }
            var result = new Dynasty();
            Merge(result, tree, new HashSet<string>(tree.Sims.Select(s => s.Id)));
            return result;
        }

        private static Sim PersonFrom(string raw)
        {
            string name = Regex.Replace(raw.Trim(), @"^(?:[-*•]+|\d+[.)])\s*", "").Trim();
            string gameId = null;
            var id = Regex.Match(name, @"(?:\bSim\s*ID\s*[:=]\s*|[([])(\d{5,})(?:[)\]]|\b)", Ignore);
            if (id.Success) gameId = id.Groups[1].Value;
            var age = Regex.Match(name, @"\b(Newborn|Infant|Baby|Toddler|Child|Teen|Young[_ ]Adult|Adult|Elder)\b", Ignore);
            var split = Regex.Split(name, @"\s*[(\[|:]\s*|\s+[-–—]\s+", RegexOptions.None);
            name = split[0].Trim(' ', ',', ';', '"', '*');
            if (!Person.IsMatch(name) || !name.Any(Char.IsLetter) ||
                Regex.IsMatch(name, @"^(?:none|unknown|n/a|no\s+sims|total\b.*|all\s+sims|sim\s+details|summary|households|humans)$", Ignore))
                return null;
            return new Sim { Name = name, GameId = gameId, Age = age.Success && age.Index >= name.Length ? TranslateAge(age.Value) : null };
        }

        private static string TranslateAge(string age)
        {
            switch (age.Trim().Replace("_", " ").ToLowerInvariant())
            {
                case "newborn": case "baby": return "Новорождённый";
                case "infant": return "Младенец";
                case "toddler": return "Малыш";
                case "child": return "Ребёнок";
                case "teen": return "Подросток";
                case "young adult": return "Молодой";
                case "adult": return "Взрослый";
                case "elder": return "Пожилой";
                default: return age;
            }
        }

        private static string CleanTraits(string source)
        {
            string list = source.Trim(' ', '[', ']', '{', '}');
            var names = new List<string>();
            foreach (string item in Regex.Split(list, @"[,;](?![^()]*\))"))
            {
                string clean = item.Trim(' ', '-', '*', '•', '\'', '"');
                var tokens = TraitToken.Matches(clean).Cast<Match>().ToArray();
                if (tokens.Length > 0)
                {
                    names.AddRange(tokens.Select(t => Regex.Replace(t.Value.Substring(6).Replace('_', ' '), "([a-z])([A-Z])", "$1 $2")));
                    continue;
                }
                clean = Regex.Replace(clean, @"^\d+[.): -]+|\s*\(\d+\)$", "").Trim();
                if (clean.Length > 0 && !Regex.IsMatch(clean, @"^(?:none|n/a|нет|\d+)$", Ignore)) names.Add(clean);
            }
            return String.Join(", ", names.ToArray());
        }

        private static string Append(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(second)) return first;
            return String.Join(", ", ((first ?? "") + "," + second).Split(',', ';').Select(s => s.Trim())
                .Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        private static void Add(Dynasty tree, Dictionary<string, bool> members, string from, string to, string kind)
        {
            if (Family.CanLink(tree.Links, from, to, kind, members))
                tree.Links.Add(new Link { From = from, To = to, Kind = kind });
        }

        public static void Merge(Dynasty target, Dynasty imported, HashSet<string> selected)
        {
            var map = new Dictionary<string, string>();
            foreach (var sim in imported.Sims.Where(s => selected.Contains(s.Id)))
            {
                var existing = String.IsNullOrWhiteSpace(sim.GameId) ? null :
                    target.Sims.FirstOrDefault(s => s.IsAnimal() == sim.IsAnimal() && s.GameId == sim.GameId);
                if (existing == null)
                {
                    var same = target.Sims.Where(s => s.IsAnimal() == sim.IsAnimal() &&
                        String.Equals(s.Name, sim.Name, StringComparison.OrdinalIgnoreCase) &&
                        (String.IsNullOrEmpty(s.GameId) || String.IsNullOrEmpty(sim.GameId))).Take(2).ToList();
                    if (same.Count == 1 && (String.IsNullOrEmpty(sim.GameId) ||
                        imported.Sims.Count(s => String.Equals(s.Name, sim.Name, StringComparison.OrdinalIgnoreCase) && !String.IsNullOrEmpty(s.GameId)) == 1))
                        existing = same[0];
                }
                if (existing == null)
                {
                    target.Sims.Add(sim);
                    existing = sim;
                }
                else
                {
                    if (String.IsNullOrEmpty(existing.GameId)) existing.GameId = sim.GameId;
                    if (String.IsNullOrEmpty(existing.Age)) existing.Age = sim.Age;
                    if (String.IsNullOrEmpty(existing.Career)) existing.Career = sim.Career;
                    if (String.IsNullOrEmpty(existing.Aspiration)) existing.Aspiration = sim.Aspiration;
                    if (String.IsNullOrEmpty(existing.Birthplace)) existing.Birthplace = sim.Birthplace;
                    if (String.IsNullOrEmpty(existing.Education)) existing.Education = sim.Education;
                    if (String.IsNullOrEmpty(existing.Bio)) existing.Bio = sim.Bio;
                    existing.Deceased |= sim.Deceased;
                    existing.Traits = Append(existing.Traits, sim.Traits);
                }
                map[sim.Id] = existing.Id;
            }
            var members = Family.Members(target.Sims);
            foreach (var link in imported.Links)
                if (map.ContainsKey(link.From) && map.ContainsKey(link.To))
                    Add(target, members, map[link.From], map[link.To], link.Kind);
        }
    }
}
