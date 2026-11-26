using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace SimsLegacy
{
    internal sealed class TreeLayout
    {
        public readonly Dictionary<string, PointF> Positions = new Dictionary<string, PointF>();
        public readonly Dictionary<string, int> Levels = new Dictionary<string, int>();
        public readonly HashSet<string> CompactFormer = new HashSet<string>();
        public readonly List<Sim> Members;
        public readonly List<Link> Links;
        public RectangleF Bounds;
        public int Generations;

        public TreeLayout(Dynasty tree, bool animals)
        {
            Members = tree.Sims.Where(s => s.IsAnimal() == animals).ToList();
            var ids = new HashSet<string>(Members.Select(s => s.Id));
            Links = tree.Links.Where(l => ids.Contains(l.From) && ids.Contains(l.To)).ToList();
            if (Members.Count == 0) { Bounds = new RectangleF(-350, -225, 700, 450); return; }
            var parents = Links.Where(l => l.Kind == "parent").ToList();
            var lateral = Links.Where(l => l.Kind != "parent")
                .OrderBy(l => Family.IsFormer(l.Kind) ? 2 : l.Kind == "sibling" ? 1 : 0).ToList();
            var neighbours = Adjacent(lateral);
            var groups = SiblingGroups(Adjacent(lateral.Where(l => l.Kind == "sibling")));
            // Complex pedigrees can contain siblings whose ancestry sets imply different generations.
            if (!AssignLevels(groups, parents, lateral))
                AssignLevels(ids.ToDictionary(id => id, id => id), parents, lateral);
            Generations = Levels.Values.Max() + 1;
            var childrenOf = parents.ToLookup(l => l.To, l => l.From);
            var allConnections = Adjacent(tree.Links);
            var connectedIds = new HashSet<string>(Links.SelectMany(l => new[] { l.From, l.To }));
            var memberOrder = Members.Select((sim, index) => new { sim.Id, Index = index }).ToDictionary(s => s.Id, s => s.Index);
            foreach (var link in lateral.Where(l => Family.IsFormer(l.Kind) && Levels[l.From] == Levels[l.To]))
            {
                bool fromLeaf = allConnections[link.From].Count() == 1, toLeaf = allConnections[link.To].Count() == 1;
                if (fromLeaf && (!toLeaf || memberOrder[link.From] > memberOrder[link.To])) CompactFormer.Add(link.From);
                else if (toLeaf) CompactFormer.Add(link.To);
            }
            var placed = new HashSet<string>();
            var unconnected = Members.Where(s => !connectedIds.Contains(s.Id)).ToList();
            int linkedGenerations = connectedIds.Count == 0 ? 0 : connectedIds.Max(id => Levels[id]) + 1;
            foreach (var row in Members.Where(s => connectedIds.Contains(s.Id)).GroupBy(s => Levels[s.Id]).OrderBy(group => group.Key))
            {
                var arranged = new List<Sim>();
                var byId = row.ToDictionary(s => s.Id);
                foreach (var sim in row.Where(s => !CompactFormer.Contains(s.Id))
                    .OrderBy(s => childrenOf[s.Id].Select(id => Positions.ContainsKey(id) ? Positions[id].X : 0).DefaultIfEmpty(0).Average()))
                {
                    if (!placed.Add(sim.Id)) continue;
                    var queue = new Queue<string>();
                    queue.Enqueue(sim.Id);
                    while (queue.Count > 0)
                    {
                        string id = queue.Dequeue();
                        arranged.Add(byId[id]);
                        foreach (string other in neighbours[id].Select(link => Family.Other(link, id)))
                            if (byId.ContainsKey(other) && !CompactFormer.Contains(other) && placed.Add(other))
                                queue.Enqueue(other);
                    }
                }
                var slots = new List<Sim>();
                foreach (var sim in arranged)
                {
                    var former = neighbours[sim.Id].Select(link => Family.Other(link, sim.Id))
                        .Where(CompactFormer.Contains).Select(id => byId[id]).ToList();
                    // Keep a current partner beside the main portrait when there is room on its left.
                    bool nextIsPartner = slots.Count == 0 && arranged.Count > 1 &&
                        neighbours[sim.Id].Any(l => Family.IsCouple(l.Kind) && !Family.IsFormer(l.Kind) && Family.Other(l, sim.Id) == arranged[1].Id);
                    if (nextIsPartner) slots.AddRange(former);
                    slots.Add(sim);
                    if (!nextIsPartner) slots.AddRange(former);
                }
                var columns = new List<float> { 0 };
                for (int i = 1; i < slots.Count; i++)
                {
                    string left = slots[i - 1].Id, right = slots[i].Id;
                    bool close = neighbours[left].Any(l => Family.Other(l, left) == right);
                    columns.Add(columns[i - 1] + (close ? 147 : 186) -
                        (CompactFormer.Contains(left) ? 19 : 0) - (CompactFormer.Contains(right) ? 19 : 0));
                }
                var parentAnchors = arranged.SelectMany(s => childrenOf[s.Id]).Distinct().Where(Positions.ContainsKey).Select(id => Positions[id].X).ToArray();
                float center = columns[columns.Count - 1] / 2f - (parentAnchors.Length == 0 ? 0 : parentAnchors.Average());
                for (int i = 0; i < slots.Count; i++)
                {
                    var sim = slots[i];
                    Positions[sim.Id] = new PointF(columns[i] - center + sim.OffsetX,
                        (row.Key - (Generations - 1) / 2f) * 146 + sim.OffsetY);
                }
            }
            // A population report can contain many unrelated Sims. Keep those cards in a compact grid.
            int columnsPerRow = Math.Min(9, Math.Max(4, (int)Math.Ceiling(Math.Sqrt(unconnected.Count))));
            for (int i = 0; i < unconnected.Count; i++)
            {
                var sim = unconnected[i];
                int row = i / columnsPerRow, col = i % columnsPerRow;
                int columns = Math.Min(columnsPerRow, unconnected.Count - row * columnsPerRow);
                Positions[sim.Id] = new PointF((col - (columns - 1) / 2f) * 152 + sim.OffsetX,
                    (linkedGenerations + row - (Generations - 1) / 2f) * 146 + sim.OffsetY);
            }
            Bounds = RectangleF.FromLTRB(Positions.Values.Min(p => p.X) - 78, Positions.Values.Min(p => p.Y) - 94,
                Positions.Values.Max(p => p.X) + 78, Positions.Values.Max(p => p.Y) + 81);
        }

        private static ILookup<string, Link> Adjacent(IEnumerable<Link> links)
        {
            return links.SelectMany(link => new[] { new { Id = link.From, Link = link }, new { Id = link.To, Link = link } })
                .ToLookup(item => item.Id, item => item.Link);
        }

        private Dictionary<string, string> SiblingGroups(ILookup<string, Link> siblings)
        {
            var groups = new Dictionary<string, string>();
            foreach (var sim in Members)
            {
                if (groups.ContainsKey(sim.Id)) continue;
                var queue = new Queue<string>();
                groups.Add(sim.Id, sim.Id);
                queue.Enqueue(sim.Id);
                while (queue.Count > 0)
                {
                    string id = queue.Dequeue();
                    foreach (string other in siblings[id].Select(link => Family.Other(link, id)))
                    {
                        if (groups.ContainsKey(other)) continue;
                        groups.Add(other, sim.Id);
                        queue.Enqueue(other);
                    }
                }
            }
            return groups;
        }

        private bool AssignLevels(Dictionary<string, string> groups, List<Link> parents, List<Link> lateral)
        {
            var depth = groups.Values.Distinct().ToDictionary(id => id, id => 0);
            var incoming = depth.Keys.ToDictionary(id => id, id => 0);
            var children = parents.ToLookup(link => groups[link.From], link => groups[link.To]);
            foreach (var link in parents) incoming[groups[link.To]]++;
            var queue = new Queue<string>(incoming.Where(p => p.Value == 0).Select(p => p.Key));
            var roots = new HashSet<string>(queue);
            var order = new List<string>();
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                order.Add(id);
                foreach (string child in children[id])
                {
                    depth[child] = Math.Max(depth[child], depth[id] + 1);
                    if (--incoming[child] == 0) queue.Enqueue(child);
                }
            }
            if (order.Count != depth.Count) return false;

            var ownDepth = new Dictionary<string, int>(depth);
            foreach (var link in lateral.Where(l => Family.IsCouple(l.Kind)))
            {
                string a = groups[link.From], b = groups[link.To];
                if (roots.Contains(a) && ownDepth[b] > 0 && !IsDescendant(children, a, b))
                    depth[a] = Math.Max(depth[a], ownDepth[b]);
                if (roots.Contains(b) && ownDepth[a] > 0 && !IsDescendant(children, b, a))
                    depth[b] = Math.Max(depth[b], ownDepth[a]);
            }
            foreach (string id in order)
                foreach (string child in children[id]) depth[child] = Math.Max(depth[child], depth[id] + 1);
            foreach (var pair in groups) Levels[pair.Key] = depth[pair.Value];
            return true;
        }

        private static bool IsDescendant(ILookup<string, string> children, string ancestor, string target)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>(children[ancestor]);
            while (stack.Count > 0)
            {
                string id = stack.Pop();
                if (id == target) return true;
                if (seen.Add(id))
                    foreach (string child in children[id]) stack.Push(child);
            }
            return false;
        }
    }

    internal sealed class FamilyCanvas : Control
    {
        private static readonly Font NameFont = Theme.Font(13, FontStyle.Bold);
        private static readonly Font FormerNameFont = Theme.Font(12, FontStyle.Bold);
        private static readonly Font CaptionFont = Theme.Font(11);
        private static readonly Font EmptyFont = Theme.Display(27);
        private readonly ActionButton minus, plus, fit, percentage;
        private readonly ToolTip hints = Ui.Hints();
        private readonly Dictionary<RectangleF, Link> relationBadges = new Dictionary<RectangleF, Link>();
        private readonly Dictionary<RectangleF, string> petBadges = new Dictionary<RectangleF, string>();
        private ILookup<string, Sim> pets;
        private Dictionary<string, Sim> members;
        private Dynasty tree;
        private TreeLayout layout;
        private Sim hit;
        private Point mouseDown;
        private PointF pan, oldPan, initialOffset;
        private bool dragging, movingNode, hasView;
        private bool animals;
        private string lastHint;
        private float zoom = 1;
        public string Selected;
        public bool Preview;
        public Action<string> SelectSim;
        public Action<string> EditSim;
        public Action<string> OpenMember;
        public Action<Point, Sim> ContextSim;
        public Action<Sim, PointF, PointF> MoveSim;

        public int Generations { get { return layout == null ? 0 : layout.Generations; } }

        public FamilyCanvas()
        {
            DoubleBuffered = true; TabStop = true; Cursor = Cursors.Hand;
            AccessibleName = "Семейное древо";
            minus = Tool("minus", "Уменьшить масштаб", () => ZoomAt(.83f, Center));
            plus = Tool("plus", "Увеличить масштаб", () => ZoomAt(1.2f, Center));
            fit = Tool("fit", "Показать всё древо", Fit);
            percentage = Tool(null, "Масштаб, нажмите для сброса", Fit);
            percentage.Font = Theme.Font(12, FontStyle.Bold);
        }

        private Point Center { get { return new Point(ClientSize.Width / 2, ClientSize.Height / 2); } }

        private ActionButton Tool(string glyph, string label, Action action)
        {
            var button = new ActionButton("", glyph) { AccessibleName = label };
            hints.SetToolTip(button, label);
            button.Click += (s, e) => action();
            Controls.Add(button);
            return button;
        }

        public void ShowTree(Dynasty value, bool reset, bool showAnimals)
        {
            tree = value;
            animals = showAnimals;
            AccessibleName = animals ? "Древо животных" : "Семейное древо";
            layout = new TreeLayout(tree, animals);
            members = tree.Sims.ToDictionary(s => s.Id);
            pets = tree.Links.Where(link => link.Kind == "owner").ToLookup(link => link.From, link => members[link.To]);
            relationBadges.Clear();
            petBadges.Clear();
            hints.SetToolTip(this, null);
            lastHint = null;
            BackColor = Theme.CanvasBackground(tree.Background);
            if (reset) Fit(); else Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (fit == null) return;
            int x = Math.Max(10, Width - 187), y = Math.Max(4, Height - 57);
            minus.SetBounds(x, y, 36, 35);
            percentage.SetBounds(x + 40, y, 53, 35);
            plus.SetBounds(x + 97, y, 36, 35);
            fit.SetBounds(x + 141, y, 36, 35);
            if (!hasView && layout != null && Width > 100) Fit();
        }

        public void Fit()
        {
            if (layout == null || Width < 100 || Height < 100) { hasView = false; return; }
            zoom = Math.Max(.025f, Math.Min(1.35f, Math.Min((Width - 90f) / layout.Bounds.Width, (Height - 104f) / layout.Bounds.Height)));
            pan = new PointF(-(layout.Bounds.Left + layout.Bounds.Width / 2) * zoom,
                -(layout.Bounds.Top + layout.Bounds.Height / 2) * zoom - 14);
            hasView = true;
            UpdateZoom();
        }

        public void FocusOn(string id)
        {
            if (layout == null || !layout.Positions.ContainsKey(id)) return;
            var point = layout.Positions[id];
            zoom = Math.Max(zoom, .95f);
            pan = new PointF(-point.X * zoom, -point.Y * zoom);
            UpdateZoom();
        }

        private void ZoomAt(float factor, Point point)
        {
            float next = Math.Max(.025f, Math.Min(3f, zoom * factor));
            var location = World(point);
            pan = new PointF(point.X - Center.X - location.X * next, point.Y - Center.Y - location.Y * next);
            zoom = next;
            UpdateZoom();
        }

        private void UpdateZoom()
        {
            percentage.Text = Math.Round(zoom * 100) + "%";
            Invalidate();
        }

        private PointF World(Point pixel)
        {
            return new PointF((pixel.X - Center.X - pan.X) / zoom, (pixel.Y - Center.Y - pan.Y) / zoom);
        }

        private Sim FindSim(Point pixel)
        {
            if (tree == null || layout == null) return null;
            var p = World(pixel);
            return layout.Members.OrderByDescending(s => s.Id == Selected).FirstOrDefault(s =>
                NodeBox(layout.Positions[s.Id], layout.CompactFormer.Contains(s.Id)).Contains(p));
        }

        private static RectangleF NodeBox(PointF point, bool former)
        {
            return former ? new RectangleF(point.X - 55, point.Y - 34, 110, 74) :
                new RectangleF(point.X - 66, point.Y - 49, 132, 109);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            hit = FindSim(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (hit != null && !Preview && ContextSim != null) ContextSim(PointToScreen(e.Location), hit);
                return;
            }
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle) return;
            mouseDown = e.Location;
            oldPan = pan;
            movingNode = hit != null && !Preview && e.Button == MouseButtons.Left;
            if (movingNode) initialOffset = new PointF(hit.OffsetX, hit.OffsetY);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Capture)
            {
                var position = World(e.Location);
                string animal = petBadges.FirstOrDefault(badge => badge.Key.Contains(position)).Value;
                var relation = relationBadges.FirstOrDefault(badge => badge.Key.Contains(position)).Value;
                string hint = animal != null ? members[animal].Name : relation != null ?
                    Family.Type(relation, relation.From).Label + " · " + members[relation.From].Name + " и " + members[relation.To].Name : null;
                if (hint != lastHint) { hints.SetToolTip(this, hint); lastHint = hint; }
                return;
            }
            float dx = e.X - mouseDown.X, dy = e.Y - mouseDown.Y;
            if (!dragging && Math.Abs(dx) + Math.Abs(dy) < 5) return;
            dragging = true;
            if (movingNode)
            {
                var point = layout.Positions[hit.Id];
                float offsetX = Math.Max(-100000, Math.Min(100000, initialOffset.X + dx / zoom));
                float offsetY = Math.Max(-100000, Math.Min(100000, initialOffset.Y + dy / zoom));
                layout.Positions[hit.Id] = new PointF(point.X + offsetX - hit.OffsetX, point.Y + offsetY - hit.OffsetY);
                hit.OffsetX = offsetX;
                hit.OffsetY = offsetY;
            }
            else pan = new PointF(oldPan.X + dx, oldPan.Y + dy);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Capture) return;
            Capture = false;
            if (dragging && movingNode && MoveSim != null)
            {
                var target = new PointF(hit.OffsetX, hit.OffsetY);
                hit.OffsetX = initialOffset.X;
                hit.OffsetY = initialOffset.Y;
                MoveSim(hit, initialOffset, target);
            }
            if (!dragging && e.Button == MouseButtons.Left && !Preview)
            {
                PointF position = World(e.Location);
                string pet = petBadges.FirstOrDefault(badge => badge.Key.Contains(position)).Value;
                if (pet != null && OpenMember != null) OpenMember(pet);
                else if (SelectSim != null) SelectSim(hit == null ? null : hit.Id);
            }
            dragging = movingNode = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var sim = FindSim(e.Location);
            if (sim != null && !Preview && e.Button == MouseButtons.Left && EditSim != null) EditSim(sim.Id);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomAt(e.Delta > 0 ? 1.15f : .87f, e.Location);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (tree == null || layout == null) return;
            var g = e.Graphics;
            PaintBackground(g, ClientRectangle, BackColor);
            if (layout.Members.Count == 0)
            {
                Theme.Icon(g, animals ? "paw" : "tree", new RectangleF(Center.X - 22, Center.Y - 82, 44, 44), Theme.Green);
                Theme.Text(g, "Древо пока пустое", EmptyFont, Theme.CanvasText(BackColor),
                    new RectangleF(0, Center.Y - 27, Width, 38), StringAlignment.Center);
                Theme.Text(g, animals ? "Добавьте первое животное." : "Добавьте первого сима или импортируйте сохранение.",
                    NameFont, Theme.CanvasText(BackColor, true),
                    new RectangleF(0, Center.Y + 17, Width, 26), StringAlignment.Center);
                return;
            }
            var state = g.Save();
            g.TranslateTransform(Center.X + pan.X, Center.Y + pan.Y);
            g.ScaleTransform(zoom, zoom);
            DrawTree(g, Preview ? null : Selected);
            g.Restore(state);
        }

        private void PaintBackground(Graphics g, Rectangle area, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (area.Width <= 0 || area.Height <= 0) return;
            Color highlight = Theme.IsDark(color) ? ControlPaint.Light(color, .05f) : ControlPaint.Light(color, .26f);
            using (var wash = new LinearGradientBrush(area, highlight, color, 68f))
                g.FillRectangle(wash, area);
            var image = Photos.Read(tree.Backdrop);
            if (image != null)
            {
                Photos.Cover(g, image, area);
                using (var wash = new SolidBrush(Color.FromArgb(Theme.IsDark(color) ? 190 : 125, color))) g.FillRectangle(wash, area);
            }
            if (tree.Grid)
            {
                using (var dot = new SolidBrush(Color.FromArgb(45, Theme.CanvasAccent(tree.Accent))))
                    for (int x = 15; x < area.Width; x += 23)
                        for (int y = 15; y < area.Height; y += 23) g.FillEllipse(dot, x, y, 1.9f, 1.9f);
            }
        }

        private void DrawTree(Graphics g, string selected)
        {
            Color accent = Theme.CanvasAccent(tree.Accent);
            var parentGroups = layout.Links.Where(l => l.Kind == "parent").GroupBy(l => l.To)
                .GroupBy(group => String.Join("|", group.Select(l => l.From).OrderBy(id => id).ToArray()));
            var pairs = new HashSet<string>(layout.Links.Where(l => Family.IsCouple(l.Kind) && !Family.IsFormer(l.Kind))
                .Select(l => String.Join("|", new[] { l.From, l.To }.OrderBy(id => id).ToArray())));
            relationBadges.Clear();
            petBadges.Clear();
            using (var pen = new Pen(Color.FromArgb(157, accent), 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (var group in parentGroups)
                {
                    var parentPoints = group.First().Select(l => layout.Positions[l.From]).ToArray();
                    var childPoints = group.Select(child => layout.Positions[child.Key]).ToArray();
                    float px = parentPoints.Average(p => p.X);
                    float py = parentPoints.Max(p => p.Y) + 58;
                    float cy = childPoints.Min(p => p.Y) - 47;
                    float join = (py + cy) / 2;
                    bool joinedPair = parentPoints.Length == 2 && pairs.Contains(group.Key) &&
                        Math.Abs(parentPoints[0].Y - parentPoints[1].Y) < 20 && Math.Abs(parentPoints[0].X - parentPoints[1].X) < 180;
                    if (joinedPair)
                        g.DrawLine(pen, px, parentPoints.Average(p => p.Y) - 1, px, join);
                    else
                        foreach (var p in parentPoints) g.DrawLine(pen, p.X, p.Y + 59, p.X, join);
                    var all = childPoints.Concat(parentPoints).ToArray();
                    float left = childPoints.Min(p => p.X), right = childPoints.Max(p => p.X);
                    if (joinedPair)
                        g.DrawLine(pen, Math.Min(left, px), join, Math.Max(right, px), join);
                    else
                        g.DrawLine(pen, all.Min(p => p.X), join, all.Max(p => p.X), join);
                    foreach (var c in childPoints) g.DrawLine(pen, c.X, join, c.X, c.Y - 45);
                }
            }
            foreach (var group in layout.Links.Where(l => l.Kind != "parent")
                .GroupBy(l => String.Join("|", new[] { l.From, l.To }.OrderBy(id => id).ToArray())))
            {
                int index = 0;
                foreach (var relation in group.OrderBy(l => !Family.IsCouple(l.Kind)))
                    DrawRelation(g, relation, accent, index++);
            }
            foreach (var sim in layout.Members.OrderBy(s => s.Id == selected))
                DrawNode(g, sim, layout.Positions[sim.Id], accent, sim.Id == selected);
            foreach (var badge in relationBadges)
            {
                var link = badge.Value;
                Color color = Family.IsFormer(link.Kind) ? Theme.Danger : accent;
                Theme.Fill(g, Theme.White, badge.Key, 11);
                Theme.Border(g, Color.FromArgb(150, color), badge.Key, 11);
                Theme.Icon(g, Family.Type(link, link.From).Icon, RectangleF.Inflate(badge.Key, -3, -3), color);
            }
        }

        private void DrawRelation(Graphics g, Link link, Color accent, int index)
        {
            var a = layout.Positions[link.From];
            var b = layout.Positions[link.To];
            Color color = Family.IsFormer(link.Kind) ? Theme.Danger : accent;
            var middle = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 12 + index * 25);
            using (var pen = new Pen(Color.FromArgb(180, color), 1.7f) { LineJoin = LineJoin.Round })
            {
                if (Family.IsFormer(link.Kind) || !Family.IsCouple(link.Kind)) pen.DashStyle = DashStyle.Dash;
                if (Math.Abs(a.Y - b.Y) < 24 && Math.Abs(a.X - b.X) > 188)
                {
                    middle.Y = Math.Min(a.Y, b.Y) - 55 - 16 * (index % 2);
                    g.DrawLines(pen, new[] { a, new PointF(a.X, middle.Y), new PointF(b.X, middle.Y), b });
                }
                else
                {
                    a.Y += middle.Y - (a.Y + b.Y) / 2;
                    b.Y = 2 * middle.Y - a.Y;
                    g.DrawLine(pen, a, b);
                }
            }
            relationBadges[new RectangleF(middle.X - 10.5f, middle.Y - 10.5f, 21, 21)] = link;
        }

        private void DrawNode(Graphics g, Sim sim, PointF point, Color accent, bool selected)
        {
            bool compact = tree.Layout == 2;
            bool circle = tree.Layout == 0;
            bool former = layout.CompactFormer.Contains(sim.Id);
            float size = former ? (compact ? 40 : 48) : (compact ? 54 : 72);
            float top = former ? point.Y - 34 : point.Y - 47 + (compact ? 9 : 0);
            var photo = new RectangleF(point.X - size / 2, top, size, size);
            float radius = circle ? size / 2 + 3 : 15;
            var rim = RectangleF.Inflate(photo, 3, 3);
            if (selected)
                Theme.Fill(g, Color.FromArgb(52, accent), RectangleF.Inflate(rim, 3, 3), radius + 3);
            Theme.Fill(g, Color.FromArgb(27, accent), new RectangleF(rim.X, rim.Y + 3, rim.Width, rim.Height), radius);
            Theme.Fill(g, Theme.White, rim, radius);
            Theme.Border(g, Color.FromArgb(120, accent), rim, radius);
            Photos.Draw(g, sim, photo, circle);
            if (!animals) DrawPets(g, pets[sim.Id].ToArray(), photo, accent, sim.Id);
            if (!String.IsNullOrWhiteSpace(sim.Role) || sim.Deceased || !String.IsNullOrWhiteSpace(sim.LinkedTree))
            {
                var badge = new RectangleF(photo.Right - 14, photo.Top - 5, 20, 20);
                Theme.Fill(g, sim.Deceased ? Theme.White : Theme.SoftGreen, badge, 10);
                if (sim.Deceased) Theme.Text(g, "†", NameFont, Theme.Muted, badge, StringAlignment.Center);
                else Theme.Icon(g, !String.IsNullOrWhiteSpace(sim.LinkedTree) ? "link" : "gem",
                    RectangleF.Inflate(badge, -3, -3), Theme.Green);
            }
            if (tree.Labels)
            {
                float labelWidth = former ? 122 : 142;
                var title = new RectangleF(point.X - labelWidth / 2, top + size + 7, labelWidth, 18);
                var detail = new RectangleF(title.X, title.Bottom, title.Width, 16);
                Theme.Text(g, sim.Name, former ? FormerNameFont : NameFont, Theme.CanvasText(BackColor), title, StringAlignment.Center);
                if (!former)
                    Theme.Text(g, sim.Deceased ? (animals ? "Умерло" : "Умерший сим") :
                        (animals ? sim.Species : String.IsNullOrWhiteSpace(sim.Role) ? sim.Age : sim.Role),
                        CaptionFont, Theme.CanvasText(BackColor, true), detail, StringAlignment.Center);
            }
        }

        private void DrawPets(Graphics g, Sim[] owned, RectangleF photo, Color accent, string owner)
        {
            for (int i = 0; i < Math.Min(2, owned.Length); i++)
            {
                var badge = new RectangleF(photo.Left - 24, photo.Top + i * 28, 26, 26);
                Theme.Fill(g, Theme.White, badge, 13);
                Theme.Border(g, Color.FromArgb(150, accent), badge, 13);
                bool more = i == 1 && owned.Length > 2;
                if (more) Theme.Text(g, "+" + (owned.Length - 1), CaptionFont, accent, badge, StringAlignment.Center);
                else Photos.Draw(g, owned[i], RectangleF.Inflate(badge, -2, -2), true);
                petBadges[badge] = more ? owner : owned[i].Id;
            }
        }

        public void ExportPng(string path)
        {
            var bounds = RectangleF.Inflate(layout.Bounds, 42, 44);
            float scale = Math.Min(1.7f, Math.Min(5600f / bounds.Width, 4600f / bounds.Height));
            int width = Math.Max(320, (int)Math.Ceiling(bounds.Width * scale)), height = Math.Max(180, (int)Math.Ceiling(bounds.Height * scale) + 70);
            using (var bitmap = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bitmap))
            {
                PaintBackground(g, new Rectangle(0, 0, width, height), BackColor);
                Theme.Text(g, animals ? tree.Title + " · Древо животных" : tree.Title, EmptyFont, Theme.CanvasText(BackColor),
                    new RectangleF(12, 12, width - 24, 46), StringAlignment.Center);
                g.TranslateTransform((width - bounds.Width * scale) / 2 - bounds.Left * scale, 70 - bounds.Top * scale);
                g.ScaleTransform(scale, scale);
                DrawTree(g, null);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) hints.Dispose();
            base.Dispose(disposing);
        }
    }
}
