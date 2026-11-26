using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed class RelationshipPicker : Dialog
    {
        private static readonly Font RowTitle = Theme.Font(14, FontStyle.Bold);
        private static readonly Font RowDetail = Theme.Font(12);
        private readonly Sim source;
        private readonly Dynasty tree;
        private readonly List<Dynasty> library;
        private readonly Dictionary<string, Sim> members;
        private readonly Dictionary<string, bool> animalById;
        private readonly Dictionary<RelationType, ActionButton> tiles = new Dictionary<RelationType, ActionButton>();
        private readonly TextBox search;
        private readonly ListBox candidates, current;
        private readonly ActionButton create, apply, remove;
        private RelationType type;

        public readonly List<Link> Links;
        public readonly List<Sim> NewMembers = new List<Sim>();

        public RelationshipPicker(Sim source, Dynasty tree, List<Dynasty> library)
            : base("Связи", 820, 667)
        {
            this.source = source; this.tree = tree; this.library = library;
            members = tree.Sims.ToDictionary(s => s.Id);
            animalById = Family.Members(tree.Sims);
            Links = tree.Links.ToList();

            var heading = Ui.Label(source.Name, 16, Theme.Ink, false, true);
            heading.Height = 25;
            Add(heading, 7);
            var types = Family.PickerTypes(source.IsAnimal());
            const int leftWidth = 498, rightX = 515, rightWidth = 237;
            int typeRows = (types.Length + 2) / 3;
            int searchY = 25 + typeRows * 37 + 8;
            int height = Math.Max(searchY + 174, ClientSize.Height - 180);
            var content = new Panel { Height = height, BackColor = Theme.Paper };
            Add(content, 0);
            var typeLabel = Ui.Label("Тип связи", 13, Theme.Ink, false, true);
            typeLabel.SetBounds(0, 0, leftWidth, 21);
            content.Controls.Add(typeLabel);
            for (int index = 0; index < types.Length; index++)
            {
                var option = types[index];
                var tile = Ui.Button(option.Label, option.Icon, 162, 33, () => SelectType(option));
                tile.Location = new Point(index % 3 * 168, 25 + index / 3 * 37);
                tile.AlignLeft = true;
                tiles.Add(option, tile);
                content.Controls.Add(tile);
            }
            var searchLabel = Ui.Label("Поиск по имени", 13, Theme.Ink, false, true);
            searchLabel.SetBounds(0, searchY, leftWidth, 21);
            content.Controls.Add(searchLabel);
            search = new TextBox { Font = Theme.Font(15), ForeColor = Theme.Ink, AccessibleName = searchLabel.Text };
            search.SetBounds(0, searchY + 23, leftWidth, 28);
            content.Controls.Add(search);
            create = Ui.Button("", "plus", leftWidth, 34, CreateMember);
            create.SetBounds(0, search.Bottom + 7, leftWidth, 34);
            create.AlignLeft = true;
            content.Controls.Add(create);
            candidates = List("Карточки в династии", DrawCandidate);
            candidates.DisplayMember = "Name";
            candidates.SetBounds(0, create.Bottom + 7, leftWidth, height - create.Bottom - 51);
            content.Controls.Add(candidates);
            apply = Ui.Button("Добавить связь", "plus", leftWidth, 36, Apply, true);
            apply.Location = new Point(0, height - 36);
            content.Controls.Add(apply);

            var existingLabel = Ui.Label("Текущие связи", 13, Theme.Ink, false, true);
            existingLabel.SetBounds(rightX, 0, rightWidth, 21);
            content.Controls.Add(existingLabel);
            current = List("Текущие связи", DrawCurrent);
            current.FormattingEnabled = true;
            current.Format += (s, e) =>
            {
                var link = (Link)e.ListItem;
                e.Value = Family.Type(link, source.Id).Label + " · " + members[Family.Other(link, source.Id)].Name;
            };
            current.SetBounds(rightX, 25, rightWidth, height - 69);
            content.Controls.Add(current);
            remove = Ui.Button("Удалить связь", "close", rightWidth, 36, () =>
            {
                var link = current.SelectedItem as Link;
                if (link == null) return;
                Links.Remove(link);
                RefreshCurrent();
                UpdateActions();
            });
            remove.ForeColor = Theme.Danger;
            remove.Location = new Point(rightX, height - 36);
            content.Controls.Add(remove);

            search.TextChanged += (s, e) => RefreshCandidates();
            candidates.SelectedIndexChanged += (s, e) => UpdateActions();
            candidates.DoubleClick += (s, e) => Apply();
            candidates.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Apply(); e.SuppressKeyPress = true; }
            };
            current.SelectedIndexChanged += (s, e) => UpdateActions();
            ValidateAndSave = () =>
            {
                if (apply.Enabled) throw new InvalidDataException("Сначала добавьте выбранную связь или очистите выбор.");
            };
            SelectType(types[0]);
            RefreshCurrent();
        }

        private static ListBox List(string name, DrawItemEventHandler draw)
        {
            var list = new ListBox {
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 43, IntegralHeight = false,
                BackColor = Theme.White, ForeColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle, AccessibleName = name
            };
            list.DrawItem += draw;
            return list;
        }

        private void SelectType(RelationType option)
        {
            type = option;
            foreach (var tile in tiles)
            {
                bool selected = tile.Key == option;
                tile.Value.Surface = selected ? Theme.SoftGreen : Theme.White;
                tile.Value.Stroke = selected ? Theme.Green : Theme.Line;
                tile.Value.Invalidate();
            }
            create.Text = TargetIsAnimal ? "Создать животное и связать" : "Создать сима и связать";
            RefreshCandidates(candidates.SelectedItem as Sim);
        }

        private bool TargetIsAnimal { get { return type.Kind == "owner" ? !source.IsAnimal() : source.IsAnimal(); } }

        private void RefreshCandidates(Sim selected = null)
        {
            string query = search.Text.Trim();
            var matches = members.Values.Where(s => s.Id != source.Id && s.IsAnimal() == TargetIsAnimal &&
                (s.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (s.Species ?? "").IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)).OrderBy(s => s.Name);
            candidates.BeginUpdate();
            candidates.Items.Clear();
            foreach (var sim in matches) candidates.Items.Add(sim);
            if (selected != null) candidates.SelectedItem = selected;
            candidates.EndUpdate();
            UpdateActions();
        }

        private void RefreshCurrent(Link selected = null)
        {
            current.BeginUpdate();
            current.Items.Clear();
            foreach (var link in Links.Where(l => Family.Touches(l, source.Id))) current.Items.Add(link);
            if (selected != null) current.SelectedItem = selected;
            current.EndUpdate();
            UpdateActions();
        }

        private Link PreviousStatus(Sim target)
        {
            return Family.IsRomantic(type.Kind) ? Links.FirstOrDefault(link =>
                Family.IsRomantic(link.Kind) && Family.Touches(link, source.Id) && Family.Other(link, source.Id) == target.Id) : null;
        }

        private void UpdateActions()
        {
            var target = candidates.SelectedItem as Sim;
            var previous = target == null ? null : PreviousStatus(target);
            apply.Enabled = target != null && (previous == null || previous.Kind != type.Kind);
            apply.Surface = apply.Enabled ? Theme.Lime : Theme.White;
            apply.Stroke = apply.Enabled ? Color.Transparent : Theme.Line;
            apply.Text = previous != null && previous.Kind != type.Kind ? "Обновить статус" : "Добавить связь";
            remove.Enabled = current.SelectedIndex >= 0;
        }

        private void Apply()
        {
            if (apply.Enabled) AddLink((Sim)candidates.SelectedItem);
        }

        private void CreateMember()
        {
            var member = Editors.SimCard(this, null, tree, library, TargetIsAnimal);
            if (member == null) return;
            members.Add(member.Id, member);
            animalById.Add(member.Id, member.IsAnimal());
            NewMembers.Add(member);
            AddLink(member);
            RefreshCandidates();
        }

        private void AddLink(Sim target)
        {
            var link = type.Create(source.Id, target.Id);
            var previous = PreviousStatus(target);
            var otherLinks = previous == null ? Links : Links.Where(l => l != previous).ToList();
            if (!Family.CanLink(otherLinks, link.From, link.To, link.Kind, animalById))
            {
                MessageBox.Show(this, "Такая связь уже есть или она образует цикл родства.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (previous != null) Links[Links.IndexOf(previous)] = link;
            else Links.Add(link);
            candidates.ClearSelected();
            RefreshCurrent(link);
        }

        private void DrawCandidate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= candidates.Items.Count) return;
            var sim = (Sim)candidates.Items[e.Index];
            DrawRow(e, sim, sim.Species ?? sim.Age, null, Theme.TreeAccent);
        }

        private void DrawCurrent(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= current.Items.Count) return;
            var link = (Link)current.Items[e.Index];
            var relation = Family.Type(link, source.Id);
            DrawRow(e, members[Family.Other(link, source.Id)], relation.Label, relation.Icon,
                Family.IsFormer(link.Kind) ? Theme.Danger : Theme.TreeAccent);
        }

        private static void DrawRow(DrawItemEventArgs e, Sim sim, string detail, string glyph, Color color)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = RectangleF.Inflate(e.Bounds, -2, -1);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Theme.Fill(g, selected ? Theme.SoftGreen : Theme.Paper, box, 8);
            var badge = new RectangleF(box.X + 7, box.Y + 5, 31, 31);
            if (glyph == null) Photos.Draw(g, sim, badge, true);
            else
            {
                Theme.Fill(g, Theme.White, badge, 16);
                Theme.Icon(g, glyph, RectangleF.Inflate(badge, -6, -6), color);
            }
            float left = badge.Right + 8;
            bool hasDetail = !String.IsNullOrWhiteSpace(detail);
            Theme.Text(g, sim.Name, RowTitle, Theme.Ink, new RectangleF(left, box.Y + (hasDetail ? 3 : 0),
                box.Right - left - 5, hasDetail ? 19 : box.Height));
            if (hasDetail) Theme.Text(g, detail, RowDetail, glyph == null ? Theme.Muted : color,
                new RectangleF(left, box.Y + 21, box.Right - left - 5, 15));
            if (selected) e.DrawFocusRectangle();
        }
    }
}
