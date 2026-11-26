using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SimsLegacy
{
    public sealed class Sim
    {
        public string Id { get; set; }
        public string GameId { get; set; }
        public string Name { get; set; }
        public string Species { get; set; }
        public string Age { get; set; }
        public string Role { get; set; }
        public string Traits { get; set; }
        public string Career { get; set; }
        public string Aspiration { get; set; }
        public string Birthplace { get; set; }
        public string Education { get; set; }
        public string Bio { get; set; }
        public bool Deceased { get; set; }
        public int Portrait { get; set; }
        public List<string> Photos { get; set; }
        public string LinkedTree { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }

        public Sim()
        {
            Id = Guid.NewGuid().ToString("N");
            Portrait = -1;
            Photos = new List<string>();
        }

        public bool IsAnimal() { return !String.IsNullOrWhiteSpace(Species); }
    }

    public sealed class Link
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Kind { get; set; }
    }

    public sealed class Dynasty
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string About { get; set; }
        public string Author { get; set; }
        public string Background { get; set; }
        public string Accent { get; set; }
        public string Backdrop { get; set; }
        public int Layout { get; set; }
        public bool Grid { get; set; }
        public bool Labels { get; set; }
        public List<Sim> Sims { get; set; }
        public List<Link> Links { get; set; }

        public Dynasty()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "Новая династия";
            Background = "#EAF7FE";
            Accent = "#2B7EAB";
            Grid = true;
            Labels = true;
            Sims = new List<Sim>();
            Links = new List<Link>();
        }

        public static Dynasty Example()
        {
            var tree = new Dynasty {
                Title = "Династия Лэндфорд", Author = "Sims Legacy",
                About = "Семья из Виллоу Крик. Всё началось с небольшого дома, сада и мечты Агаты собрать за одним столом несколько поколений."
            };
            string[] names = { "Агата Лэндфорд", "Томас Лэндфорд", "Эмма Лэндфорд", "Лукас Рид", "Оливия Лэндфорд", "Нора Рид", "Софи Рид", "Ноа Рид" };
            int[] portraits = { 0, 1, 2, 3, 4, 5, 6, 7 };
            string[] ages = { "Пожилая", "Пожилой", "Взрослая", "Взрослый", "Молодая", "Молодая", "Подросток", "Ребёнок" };
            for (int i = 0; i < names.Length; i++)
                tree.Sims.Add(new Sim { Name = names[i], Portrait = portraits[i], Age = ages[i], Birthplace = "Виллоу Крик" });
            tree.Sims[0].Role = "Основательница";
            tree.Sims[2].Role = "Наследница";
            tree.Sims[2].Traits = "Творческая, Семейная, Любит природу";
            tree.Sims[2].Career = "Художница";
            tree.Sims[2].Aspiration = "Выдающийся художник";
            tree.Sims[2].Education = "Бритчестерский университет";
            tree.Sims[2].Bio = "Эмма выросла в Виллоу Крик. Она превратила старую семейную оранжерею в мастерскую и пишет портреты близких.";
            Action<int, int, string> add = (a, b, kind) => tree.Links.Add(new Link { From = tree.Sims[a].Id, To = tree.Sims[b].Id, Kind = kind });
            add(0, 1, "partner"); add(0, 2, "parent"); add(1, 2, "parent");
            add(0, 4, "parent"); add(1, 4, "parent");
            add(2, 3, "partner"); add(4, 5, "partner");
            add(2, 6, "parent"); add(3, 6, "parent"); add(2, 7, "parent"); add(3, 7, "parent");
            return tree;
        }
    }

    public sealed class Library
    {
        public List<Dynasty> Trees { get; set; }
        public string Active { get; set; }
        public bool DarkTheme { get; set; }
        public AutoImportSettings AutoImport { get; set; }
        public Library() { Trees = new List<Dynasty>(); }
    }

    public sealed class AutoImportSettings
    {
        public bool Enabled { get; set; }
        public string FilePath { get; set; }
        public string TreeId { get; set; }
        public ulong HouseholdId { get; set; }
        public string HouseholdName { get; set; }
        public bool IncludeRelatives { get; set; }
        public List<string> SeenGameIds { get; set; }
    }

    internal static class Storage
    {
        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 120 * 1024 * 1024 };
        }

        public static string Json<T>(T value) { return Serializer().Serialize(value); }
        public static T Parse<T>(string value) { return Serializer().Deserialize<T>(value); }

        public static void Write(string path, string json)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static void Validate(Dynasty tree)
        {
            if (tree == null || String.IsNullOrWhiteSpace(tree.Title) || tree.Sims == null || tree.Links == null)
                throw new InvalidDataException("В файле нет данных династии.");
            var ids = new HashSet<string>();
            foreach (var sim in tree.Sims)
            {
                if (sim == null || String.IsNullOrWhiteSpace(sim.Id) || !ids.Add(sim.Id) ||
                    String.IsNullOrWhiteSpace(sim.Name) || Single.IsNaN(sim.OffsetX) || Single.IsNaN(sim.OffsetY) ||
                    Math.Abs(sim.OffsetX) > 100000 || Math.Abs(sim.OffsetY) > 100000)
                    throw new InvalidDataException("В файле найдена некорректная карточка сима.");
                if (sim.Photos == null) sim.Photos = new List<string>();
            }
            var members = Family.Members(tree.Sims);
            var valid = new List<Link>();
            foreach (var link in tree.Links)
                if (link != null && Family.CanLink(valid, link.From, link.To, link.Kind, members))
                    valid.Add(link);
            tree.Links = valid;
            if (tree.Layout < 0 || tree.Layout > 2) tree.Layout = 0;
        }
    }

    internal sealed class RelationType
    {
        public readonly string Kind, Label, Icon;
        public readonly bool Reverse;

        public RelationType(string kind, string label, string icon, bool reverse = false)
        {
            Kind = kind; Label = label; Icon = icon; Reverse = reverse;
        }

        public Link Create(string source, string target)
        {
            return new Link { From = Reverse ? target : source, To = Reverse ? source : target, Kind = Kind };
        }
    }

    internal static class Family
    {
        private static readonly RelationType Parent = new RelationType("parent", "Родитель", "tree", true);
        private static readonly RelationType Child = new RelationType("parent", "Ребёнок", "person");
        private static readonly RelationType Sibling = new RelationType("sibling", "Брат / сестра", "people");
        private static readonly RelationType Owner = new RelationType("owner", "Хозяин", "person", true);
        private static readonly RelationType Pet = new RelationType("owner", "Питомец", "paw");
        private static readonly RelationType[] Social = {
            new RelationType("partner", "Партнёр", "heart"),
            new RelationType("spouse", "Супруг / супруга", "link"),
            new RelationType("engaged", "Помолвлены", "ring"),
            new RelationType("ex", "Бывший партнёр", "heartbreak"),
            new RelationType("divorced", "В разводе", "unlink"),
            new RelationType("widowed", "Вдовство", "memorial"),
            new RelationType("friend", "Друзья", "people"),
            new RelationType("flirt", "Флирт", "spark"),
            new RelationType("other", "Другая связь", "dots")
        };

        public static RelationType[] PickerTypes(bool animal)
        {
            if (animal) return new[] { Parent, Child, Sibling, Owner };
            return new[] { Parent, Child, Sibling }.Concat(Social).Concat(new[] { Pet }).ToArray();
        }

        public static string Other(Link link, string selected) { return link.From == selected ? link.To : link.From; }
        public static bool Touches(Link link, string id) { return link.From == id || link.To == id; }
        public static bool IsFormer(string kind) { return kind == "ex" || kind == "divorced" || kind == "widowed"; }
        public static bool IsCouple(string kind) { return IsFormer(kind) || kind == "partner" || kind == "spouse" || kind == "engaged"; }
        public static bool IsRomantic(string kind) { return IsCouple(kind) || kind == "flirt"; }

        public static RelationType Type(Link link, string selected)
        {
            if (link.Kind == "parent") return link.From == selected ? Child : Parent;
            if (link.Kind == "owner") return link.From == selected ? Pet : Owner;
            if (link.Kind == "sibling") return Sibling;
            return Social.First(t => t.Kind == link.Kind);
        }

        public static Dictionary<string, bool> Members(IEnumerable<Sim> sims)
        {
            return sims.ToDictionary(s => s.Id, s => s.IsAnimal());
        }

        public static bool CanLink(List<Link> links, string from, string to, string kind, Dictionary<string, bool> members)
        {
            bool fromAnimal, toAnimal;
            if (String.IsNullOrEmpty(from) || String.IsNullOrEmpty(to) || from == to ||
                !members.TryGetValue(from, out fromAnimal) || !members.TryGetValue(to, out toAnimal)) return false;
            if (kind == "owner")
            {
                if (fromAnimal || !toAnimal) return false;
            }
            else if (fromAnimal != toAnimal || (kind != "parent" && kind != "sibling" &&
                (fromAnimal || !Social.Any(type => type.Kind == kind)))) return false;

            bool symmetric = kind != "parent" && kind != "owner";
            if (links.Any(l => (l.Kind == kind || (IsRomantic(kind) && IsRomantic(l.Kind))) &&
                ((l.From == from && l.To == to) || (symmetric && l.From == to && l.To == from)))) return false;
            if (kind != "parent") return true;
            var seen = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(to);
            var children = links.Where(l => l.Kind == "parent").ToLookup(l => l.From, l => l.To);
            while (stack.Count > 0)
            {
                string next = stack.Pop();
                if (next == from) return false;
                if (seen.Add(next))
                    foreach (var child in children[next]) stack.Push(child);
            }
            return true;
        }

    }
}
