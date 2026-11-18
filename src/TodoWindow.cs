using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Todo list modelled on DeskBox's widget: header with add/more, count tabs,
    /// colour filter row, inline "add" row, checkbox rows, clear-completed footer.
    /// </summary>
    internal sealed class TodoWindow : GlassForm
    {
        private static readonly (TodoFilter Filter, string Label)[] Tabs =
        {
            (TodoFilter.All, "全部"),
            (TodoFilter.Today, "今天"),
            (TodoFilter.Important, "重要"),
            (TodoFilter.Completed, "已完成")
        };

        private readonly AppConfig _config;
        private readonly TodoService _service;
        private readonly Label _title;
        private readonly GlassButton _addButton;
        private readonly GlassButton _moreButton;
        private readonly GlassButton _clearCompleted;

        private TodoFilter _filter = TodoFilter.All;
        private string _colorFilter = "";
        private List<TodoItem> _items = new List<TodoItem>();
        private readonly Dictionary<TodoFilter, int> _counts = new Dictionary<TodoFilter, int>();
        private int _hoverRow = -1;
        private int _scroll;
        private int _rowHeight;
        private int _tabsTop;
        private int _colorsTop;
        private int _listTop;
        private int _listHeight;
        private bool _scrollDragging;
        private bool _suppressClick;
        private int _scrollDragOffset;
        private bool _scrollTrackPressed;
        private bool _desktopMode;
        private bool _topMode;

        public TodoWindow(AppConfig config)
        {
            _config = config;
            _service = new TodoService(config);
            Ui.InitScale();
            CornerRadius = Ui.S(32);
            LiveBackdrop = config.LiveBackdrop;

            Text = "待办";
            Resizable = true;
            MinimumSize = new Size(260, 320);
            ClientSize = ParseSize(_config.TodoWindowSize) ?? new Size(Ui.S(400), Ui.S(860));
            Font = Ui.Font(9f, FontStyle.Regular);
            DragZone = Ui.S(48);   // header only: tabs and dots must stay clickable
            _rowHeight = Ui.S(64);

            _title = new Label
            {
                Text = "待办",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(12.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _addButton = new GlassButton("＋", false) { Ghost = true, Font = Ui.Font(12f, FontStyle.Regular) };
            _addButton.Click += (sender, args) => AddNewItem();

            _moreButton = new GlassButton("⋯", false) { Ghost = true, Font = Ui.Font(12f, FontStyle.Bold) };
            _moreButton.Click += (sender, args) => ShowMoreMenu();

            _clearCompleted = new GlassButton("清除已完成", false) { Ghost = true, Font = Ui.Font(8.6f, FontStyle.Regular) };
            _clearCompleted.Click += (sender, args) =>
            {
                _service.ClearCompleted();
                Reload();
            };

            Controls.AddRange(new Control[] { _title, _addButton, _moreButton, _clearCompleted });

            _desktopMode = config.TodoWidgetOnDesktop;
            LayoutOnce();
            PlaceNearCursor();
            PrepareBackdrop();
            Reload();
            ApplyDesktopMode();
        }

        private static Size? ParseSize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string[] parts = value.Split(',');
            int width;
            int height;
            if (parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) &&
                width > 200 && height > 200)
            {
                int scale = Math.Max(1, Ui.Scale);
                var wanted = new Size(width * scale, height * scale);
                Rectangle working = Screen.FromPoint(Cursor.Position).WorkingArea;
                // Absolute floors: DPI-scaled minimums made the window huge at 150%.
                return new Size(
                    Math.Max(260, Math.Min(wanted.Width, (int)(working.Width * 0.9))),
                    Math.Max(320, Math.Min(wanted.Height, (int)(working.Height * 0.9))));
            }

            return null;
        }

        private void SaveWindowSize()
        {
            // Keep logical units so another monitor with a different DPI shows
            // the same visual size instead of a huge window.
            int scale = Math.Max(1, Ui.Scale);
            _config.TodoWindowSize = (Width / scale) + "," + (Height / scale);
            _config.Save();
        }

        protected override void OnScaleChanged(int previousScale, int newScale)
        {
            base.OnScaleChanged(previousScale, newScale);
            if (previousScale <= 0 || newScale <= 0 || !IsHandleCreated)
            {
                return;
            }

            double ratio = (double)newScale / previousScale;
            ClientSize = new Size(
                Math.Max(Ui.S(300), (int)Math.Round(ClientSize.Width * ratio)),
                Math.Max(Ui.S(400), (int)Math.Round(ClientSize.Height * ratio)));
            LayoutOnce();
            _rowHeight = Ui.S(64);
            Invalidate(true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated)
            {
                LayoutOnce();
                Invalidate(true);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveWindowSize();
            base.OnFormClosed(e);
        }

        /// <summary>
        /// Keeps the window usable on small/high-DPI screens: the minimum is
        /// derived from the monitor, never from the DPI-scaled design size.
        /// </summary>
        private bool IsCompact
        {
            get { return ClientSize.Width < Ui.S(430) || ClientSize.Height < Ui.S(620); }
        }

        private void LayoutOnce()
        {
            bool compact = IsCompact;
            int pad = compact ? Ui.S(12) : Ui.S(18);
            int width = ClientSize.Width;
            int headerTop = compact ? Ui.S(8) : Ui.S(14);
            int headerHeight = compact ? Ui.S(28) : Ui.S(32);

            _rowHeight = compact ? Ui.S(50) : Ui.S(64);
            _title.SetBounds(pad + Ui.S(24), headerTop, Ui.S(140), headerHeight);
            _moreButton.SetBounds(width - pad - Ui.S(30), headerTop, Ui.S(30), headerHeight);
            _addButton.SetBounds(width - pad - Ui.S(30) - Ui.S(34), headerTop, Ui.S(30), headerHeight);

            _tabsTop = compact ? Ui.S(44) : Ui.S(58);
            _colorsTop = _tabsTop + (compact ? Ui.S(38) : Ui.S(46));
            _listTop = _colorsTop + (compact ? Ui.S(32) : Ui.S(40));
            _listHeight = ClientSize.Height - _listTop - Ui.S(42);

            _clearCompleted.SetBounds(width - pad - Ui.S(96), ClientSize.Height - Ui.S(36), Ui.S(96), Ui.S(26));
        }

        private void PlaceNearCursor()
        {
            Point cursor = Cursor.Position;
            Rectangle working = Screen.FromPoint(cursor).WorkingArea;
            int x = Math.Max(working.Left + 8, Math.Min(cursor.X - Width - 40, working.Right - Width - 8));
            int y = Math.Max(working.Top + 8, Math.Min(cursor.Y - Height / 2, working.Bottom - Height - 8));
            Location = new Point(x, y);
        }

        private bool TryRestoreDesktopBounds()
        {
            try
            {
                string bounds = _config.TodoWidgetBounds;
                if (string.IsNullOrWhiteSpace(bounds))
                {
                    return false;
                }

                string[] parts = bounds.Split(',');
                if (parts.Length != 4)
                {
                    return false;
                }

                int x, y, width, height;
                if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y) ||
                    !int.TryParse(parts[2], out width) || !int.TryParse(parts[3], out height))
                {
                    return false;
                }

                Bounds = new Rectangle(x, y, width, height);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SaveDesktopBounds()
        {
            _config.TodoWidgetBounds = string.Format("{0},{1},{2},{3}", Left, Top, Width, Height);
            _config.Save();
        }

        private void CyclePlacement()
        {
            if (!_desktopMode && !_topMode)
            {
                _desktopMode = true;
            }
            else if (_desktopMode)
            {
                _desktopMode = false;
                _topMode = true;
            }
            else
            {
                _topMode = false;
            }

            _config.TodoWidgetOnDesktop = _desktopMode;
            ApplyDesktopMode();
            _config.Save();
        }

        private void ApplyDesktopMode()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            if (_desktopMode)
            {
                TopMost = false;
                ShowInTaskbar = false;
                SaveDesktopBounds();
                PushToDesktop(true);
            }
            else
            {
                TopMost = true;
                ShowInTaskbar = true;
                PushToDesktop(false);
                Activate();
            }
        }

        private void PushToDesktop(bool toBottom)
        {
            try
            {
                SetWindowPos(Handle, toBottom ? HwndBottom : HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
            }
            catch (Exception ex)
            {
                Log.Write("desktop layer failed: " + ex.Message);
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_desktopMode)
            {
                SaveDesktopBounds();
                PushToDesktop(true);
            }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (_desktopMode)
            {
                SaveDesktopBounds();
            }
        }

        private const int HwndTopmost = -1;
        private const int HwndBottom = 1;
        private const uint SwpNomove = 0x0002;
        private const uint SwpNosize = 0x0001;
        private const uint SwpNoactivate = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        public void Reload()
        {
            IEnumerable<TodoItem> all = _service.Load();
            List<TodoItem> filtered = all.Where(item =>
                TodoQuery.Matches(item, _filter, _filter == TodoFilter.Completed) &&
                (string.IsNullOrEmpty(_colorFilter) || string.Equals(item.ColorMarker, _colorFilter, StringComparison.Ordinal))).ToList();
            _items = TodoQuery.Sort(filtered);

            int open = all.Count(item => !item.IsCompleted);
            int today = all.Count(item => !item.IsCompleted && TodoQuery.IsDueToday(item));
            _counts.Clear();
            foreach (TodoFilter filter in Tabs.Select(tab => tab.Filter))
            {
                _counts[filter] = all.Count(item => TodoQuery.Matches(item, filter, filter == TodoFilter.Completed));
            }

            _title.Text = "待办";
            _clearCompleted.Enabled = all.Any(item => item.IsCompleted);
            Invalidate();
        }

        private int CountFor(TodoFilter filter)
        {
            int count;
            return _counts.TryGetValue(filter, out count) ? count : 0;
        }

        private void AddNewItem()
        {
            var item = new TodoItem { Text = "" };
            using (var detail = new TodoDetailForm(_config, item, true))
            {
                if (detail.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(item.Text))
            {
                return;
            }

            _service.Add(item);
            Reload();
        }

        private void OpenItem(TodoItem item)
        {
            TodoItem editable = item.Clone();
            bool deleted = false;
            using (var detail = new TodoDetailForm(_config, editable, false))
            {
                if (detail.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                deleted = detail.Deleted;
            }

            if (deleted)
            {
                _service.Delete(item.Id);
            }
            else
            {
                _service.Update(editable);
            }

            Reload();
        }

        private void ShowMoreMenu()
        {
            var menu = new GlassMenu();
            menu.AddItem("新建待办", () => AddNewItem());
            menu.AddItem("清除已完成", () => { _service.ClearCompleted(); Reload(); });
            menu.AddItem("撤销", () => { if (_service.Undo()) { Reload(); } });
            menu.AddSeparator();
            menu.AddItem("固定在桌面（小组件）", () => CyclePlacement(), _desktopMode);
            menu.AddSeparator();
            menu.AddItem("关闭窗口", () => Close());
            menu.ShowAt(Cursor.Position);
        }

        private Rectangle ScrollTrack()
        {
            int top = _listTop + _rowHeight + Ui.S(4);
            int height = Math.Max(0, _listHeight - _rowHeight - Ui.S(8));
            return new Rectangle(Width - Ui.S(14), top, Ui.S(7), height);
        }

        private int MaxScroll()
        {
            int content = _items.Count * _rowHeight;
            int viewport = Math.Max(1, _listHeight - _rowHeight);
            return Math.Max(0, content - viewport);
        }

        private RectangleF ScrollThumb(Rectangle track)
        {
            int content = _items.Count * _rowHeight;
            int viewport = Math.Max(1, _listHeight - _rowHeight);
            if (content <= viewport)
            {
                return RectangleF.Empty;
            }

            float ratio = (float)viewport / content;
            float thumbHeight = Math.Max(Ui.S(28), track.Height * ratio);
            float maxScroll = MaxScroll();
            float progress = maxScroll <= 0 ? 0 : (float)_scroll / maxScroll;
            float top = track.Top + (track.Height - thumbHeight) * progress;
            return new RectangleF(track.Left, top, track.Width, thumbHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Rectangle track = ScrollTrack();
            Rectangle gutter = new Rectangle(Width - Ui.S(24), track.Top, Ui.S(24), track.Height);
            if (!gutter.Contains(e.Location))
            {
                return;
            }

            RectangleF thumb = ScrollThumb(track);
            if (thumb.Contains(e.Location))
            {
                _scrollDragging = true;
                _scrollDragOffset = (int)(e.Y - thumb.Top);
            }
            else
            {
                // Click on the track: jump so the thumb centres here.
                _suppressClick = true;
                _scrollDragging = true;
                _scrollDragOffset = (int)Math.Max(0, thumb.Height / 2);
                UpdateScrollFromThumb(track, e.Y);
            }

            Capture = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_scrollDragging)
            {
                _scrollDragging = false;
                Capture = false;
                Invalidate();
            }
        }

        private void UpdateScrollFromThumb(Rectangle track, int mouseY)
        {
            RectangleF thumb = ScrollThumb(track);
            float span = Math.Max(1f, track.Height - thumb.Height);
            float position = Math.Max(0f, Math.Min(span, mouseY - track.Top - _scrollDragOffset));
            _scroll = (int)Math.Round(MaxScroll() * (position / span));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int content = _items.Count * _rowHeight + _rowHeight;
            int maxScroll = Math.Max(0, content - _listHeight);
            _scroll = Math.Max(0, Math.Min(maxScroll, _scroll - e.Delta));
            Invalidate();
        }

        private int RowAt(int y)
        {
            if (y < _listTop || y > _listTop + _listHeight)
            {
                return -1;
            }

            int index = (y - _listTop + _scroll) / _rowHeight - 1;
            return index >= 0 && index < _items.Count ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_scrollDragging)
            {
                UpdateScrollFromThumb(ScrollTrack(), e.Y);
                Invalidate();
                return;
            }

            int row = RowAt(e.Y);
            Cursor = row >= 0 || AddRowAt(e.Y) ? Cursors.Hand : Cursors.Default;
            if (row != _hoverRow)
            {
                _hoverRow = row;
                Invalidate();
            }
        }

        private bool AddRowAt(int y)
        {
            return y >= _listTop && y < _listTop + _rowHeight;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverRow = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            if (e.X >= Width - Ui.S(24))
            {
                // Scrollbar gutter never opens a task.
                return;
            }

            if (TabAt(e.Y, e.X) >= 0)
            {
                TodoFilter filter = Tabs[TabAt(e.Y, e.X)].Filter;
                if (filter != _filter)
                {
                    _filter = filter;
                    _scroll = 0;
                    Reload();
                }

                return;
            }

            int colorIndex = ColorAt(e.Y, e.X);
            if (colorIndex >= 0)
            {
                string marker = colorIndex == 0 ? "" : TodoItem.Markers[colorIndex];
                _colorFilter = string.Equals(_colorFilter, marker, StringComparison.Ordinal) ? "" : marker;
                _scroll = 0;
                Reload();
                return;
            }

            if (AddRowAt(e.Y))
            {
                AddNewItem();
                return;
            }

            int row = RowAt(e.Y);
            if (row < 0)
            {
                return;
            }

            TodoItem item = _items[row];
            int top = _listTop + (row + 1) * _rowHeight - _scroll;
            int boxLeft = Ui.S(30);

            if (e.X >= boxLeft - Ui.S(6) && e.X <= boxLeft + Ui.S(26))
            {
                _service.SetCompleted(item.Id, !item.IsCompleted);
                Reload();
                return;
            }

            OpenItem(item);
        }

        private int TabAt(int y, int x)
        {
            if (y < _tabsTop || y > _tabsTop + Ui.S(34))
            {
                return -1;
            }

            int left = Ui.S(18);
            for (int index = 0; index < Tabs.Length; index++)
            {
                int width = TabWidth(Tabs[index].Filter);
                if (x >= left && x <= left + width)
                {
                    return index;
                }

                left += width;
            }

            return -1;
        }

        private string TabLabel(TodoFilter filter)
        {
            string label = Tabs.First(tab => tab.Filter == filter).Label;
            if (IsCompact && label == "已完成")
            {
                label = "完成";
            }

            return label;
        }

        /// <summary>
        /// Picks the largest tab font/padding whose total width still fits the
        /// window, so the tab strip can never run outside a narrow window.
        /// </summary>
        private void MeasureTabs(out float fontSize, out int padding)
        {
            int available = Math.Max(120, ClientSize.Width - Ui.S(16));
            float[] sizes = IsCompact ? new[] { 8.2f, 7.6f, 7.0f } : new[] { 9f, 8.4f, 7.8f, 7.2f };
            int[] paddings = IsCompact ? new[] { Ui.S(16), Ui.S(12), Ui.S(8) } : new[] { Ui.S(22), Ui.S(16), Ui.S(12), Ui.S(8) };

            for (int index = 0; index < sizes.Length; index++)
            {
                float size = sizes[index];
                int pad = paddings[Math.Min(index, paddings.Length - 1)];
                int total = 0;
                foreach (var tab in Tabs)
                {
                    total += TextRenderer.MeasureText(TabLabel(tab.Filter) + " " + CountFor(tab.Filter), Ui.Font(size, FontStyle.Regular)).Width + pad;
                }

                if (total <= available || index == sizes.Length - 1)
                {
                    fontSize = size;
                    padding = pad;
                    return;
                }
            }

            fontSize = 7.2f;
            padding = Ui.S(8);
        }

        private int TabWidth(TodoFilter filter)
        {
            float fontSize;
            int padding;
            MeasureTabs(out fontSize, out padding);
            string label = TabLabel(filter) + " " + CountFor(filter);
            return TextRenderer.MeasureText(label, Ui.Font(fontSize, FontStyle.Regular)).Width + padding;
        }

        private int ColorAt(int y, int x)
        {
            if (y < _colorsTop || y > _colorsTop + Ui.S(22))
            {
                return -1;
            }

            int left = Ui.S(22);
            for (int index = 0; index < TodoItem.Markers.Length; index++)
            {
                var dot = new Rectangle(left, _colorsTop, Ui.S(14), Ui.S(14));
                if (dot.Contains(x, y))
                {
                    return index;
                }

                left += Ui.S(24);
            }

            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            DrawHeaderIcon(g);
            DrawTabs(g);
            DrawColorDots(g);

            using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
            {
                g.DrawLine(pen, 0, _listTop - Ui.S(6), Width, _listTop - Ui.S(6));
            }

            DrawAddRow(g);

            // Clip rows to the scroll area so they never overlap the add row.
            int listClipTop = _listTop + _rowHeight;
            var previousClip = g.Clip;
            g.SetClip(new Rectangle(0, listClipTop, Width, Math.Max(0, _listHeight - _rowHeight)));

            int offset = 0;
            for (int index = 0; index < _items.Count; index++)
            {
                int top = _listTop + (index + 1) * _rowHeight - _scroll;
                offset += _rowHeight;
                if (top + _rowHeight < _listTop || top > _listTop + _listHeight)
                {
                    continue;
                }

                if (top + _rowHeight < listClipTop || top > _listTop + _listHeight)
                {
                    continue;
                }

                DrawRow(g, _items[index], index, top);
            }

            g.SetClip(previousClip, CombineMode.Replace);

            DrawScrollBar(g);
        }

        private void DrawScrollBar(Graphics g)
        {
            Rectangle track = ScrollTrack();
            RectangleF thumb = ScrollThumb(track);
            if (thumb.IsEmpty)
            {
                return;
            }

            Ui.FillRounded(
                g,
                new RectangleF(track.Left, track.Top, track.Width, track.Height),
                Ui.S(3),
                Color.FromArgb(26, 255, 255, 255));
            Ui.FillRounded(
                g,
                thumb,
                Ui.S(3),
                Color.FromArgb(_scrollDragging ? 180 : 120, 255, 255, 255));
        }

        private void DrawHeaderIcon(Graphics g)
        {
            float iconLeft = IsCompact ? Ui.S(12) : Ui.S(18);
            float iconTop = IsCompact ? Ui.S(13) : Ui.S(20);
            var icon = new RectangleF(iconLeft, iconTop, Ui.S(18), Ui.S(18));
            Ui.FillRounded(g, icon, Ui.S(4), Color.FromArgb(235, 86, 142, 247));
            using (var pen = new Pen(Color.White, 1.6f))
            {
                for (int line = 0; line < 3; line++)
                {
                    float y = icon.Top + Ui.S(6) + line * Ui.S(4);
                    g.DrawLine(pen, icon.Left + Ui.S(4), y, icon.Right - Ui.S(4), y);
                }
            }
        }

        private void DrawTabs(Graphics g)
        {
            float tabFont;
            int tabPad;
            MeasureTabs(out tabFont, out tabPad);
            int left = Ui.S(8);
            for (int index = 0; index < Tabs.Length; index++)
            {
                TodoFilter filter = Tabs[index].Filter;
                string label = TabLabel(filter) + " " + CountFor(filter);
                int width = TabWidth(filter);
                bool active = filter == _filter;

                TextRenderer.DrawText(
                    g,
                    label,
                    Ui.Font(tabFont, active ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(left, _tabsTop, width, Ui.S(28)),
                    active ? Ui.Accent : Ui.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                if (active)
                {
                    Ui.FillRounded(g, new RectangleF(left, _tabsTop + Ui.S(28), width, Ui.S(3)), Ui.S(2), Ui.Accent);
                }

                left += width;
            }
        }

        private void DrawColorDots(Graphics g)
        {
            int left = Ui.S(22);
            for (int index = 0; index < TodoItem.Markers.Length; index++)
            {
                string marker = TodoItem.Markers[index];
                Color color = index == 0 ? Color.FromArgb(120, 130, 145) : TodoItem.MarkerColor(marker);
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, left, _colorsTop, Ui.S(14), Ui.S(14));
                }

                bool active = string.Equals(_colorFilter, marker, StringComparison.Ordinal);
                if (active)
                {
                    Ui.StrokeRounded(
                        g,
                        new RectangleF(left - Ui.S(3), _colorsTop - Ui.S(3), Ui.S(20), Ui.S(20)),
                        Ui.S(9),
                        Color.White,
                        1.6f);
                }

                left += Ui.S(24);
            }
        }

        private void DrawAddRow(Graphics g)
        {
            var row = new Rectangle(Ui.S(16), _listTop + Ui.S(2), Width - Ui.S(32), _rowHeight - Ui.S(8));
            DrawGlassCard(g, row, Ui.S(11), AddRowAt(Cursor.Position.Y - Top) ? 12 : 0);

            using (var pen = new Pen(Ui.Text, 1.6f))
            {
                float cx = row.Left + Ui.S(20);
                float cy = row.Top + _rowHeight / 2f;
                g.DrawLine(pen, cx - Ui.S(7), cy, cx + Ui.S(7), cy);
                g.DrawLine(pen, cx, cy - Ui.S(7), cx, cy + Ui.S(7));
            }

            TextRenderer.DrawText(
                g,
                "添加待办",
                Ui.Font(10f, FontStyle.Regular),
                new Rectangle(row.Left + Ui.S(42), row.Top, row.Width - Ui.S(48), _rowHeight),
                Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawRow(Graphics g, TodoItem item, int index, int top)
        {
            var row = new Rectangle(Ui.S(16), top + Ui.S(3), Width - Ui.S(32), _rowHeight - Ui.S(10));
            DrawGlassCard(g, row, Ui.S(11), index == _hoverRow ? 12 : 0);

            int boxSize = Ui.S(20);
            int boxLeft = Ui.S(18 + 12);
            int boxTop = row.Top + (row.Height - boxSize) / 2;
            var box = new RectangleF(boxLeft, boxTop, boxSize, boxSize);

            if (item.IsCompleted)
            {
                Ui.FillRounded(g, box, Ui.S(6), Color.FromArgb(255, 86, 142, 247));
                using (var pen = new Pen(Color.White, 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.X + boxSize * 0.26f, box.Y + boxSize * 0.52f),
                        new PointF(box.X + boxSize * 0.45f, box.Y + boxSize * 0.72f),
                        new PointF(box.X + boxSize * 0.75f, box.Y + boxSize * 0.28f)
                    });
                }
            }
            else
            {
                Ui.StrokeRounded(g, box, Ui.S(6), Color.FromArgb(110, 255, 255, 255), 1.4f);
            }

            Color marker = TodoItem.MarkerColor(item.ColorMarker);
            if (marker != Color.Transparent)
            {
                var stripe = new RectangleF(boxLeft - Ui.S(9), row.Top + Ui.S(10), Ui.S(3), row.Height - Ui.S(20));
                Ui.FillRounded(g, stripe, Ui.S(2), marker);
            }

            int textLeft = boxLeft + boxSize + Ui.S(14);
            int textRight = row.Right - (item.IsImportant ? Ui.S(46) : Ui.S(14));

            if (item.IsImportant)
            {
                // Rounded star, vertically centred on the title line.
                float size = Ui.S(15);
                float centerX = textRight + Ui.S(4) + Ui.S(13);
                float centerY = row.Top + row.Height / 2f;
                DrawRoundedStar(g, centerX, centerY, size, Color.FromArgb(245, 202, 79));
            }

            Ui.TextClipped(
                g,
                item.Text,
                Ui.Font(9.8f, FontStyle.Regular),
                new RectangleF(textLeft, row.Top + Ui.S(9), textRight - textLeft, Ui.S(22)),
                item.IsCompleted ? Ui.TextMuted : Ui.Text,
                StringAlignment.Near,
                true);

            string secondary = SecondaryText(item);
            Ui.TextClipped(
                g,
                secondary,
                Ui.Font(8.6f, FontStyle.Regular),
                new RectangleF(textLeft, row.Top + Ui.S(31), textRight - textLeft, Ui.S(18)),
                item.IsOverdue ? Color.FromArgb(236, 140, 130) : Ui.TextMuted,
                StringAlignment.Near,
                true);
        }

        private static void DrawRoundedStar(Graphics g, float centerX, float centerY, float size, Color color)
        {
            float outer = size / 2f;
            float inner = outer * 0.46f;
            var points = new PointF[10];
            for (int index = 0; index < 10; index++)
            {
                double angle = -Math.PI / 2 + index * Math.PI / 5;
                float radius = index % 2 == 0 ? outer : inner;
                points[index] = new PointF(
                    centerX + (float)(Math.Cos(angle) * radius),
                    centerY + (float)(Math.Sin(angle) * radius));
            }

            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                using (var brush = new SolidBrush(color))
                {
                    g.FillPath(brush, path);
                }

                // Round the tips so it reads friendly rather than sharp.
                using (var pen = new Pen(color, Math.Max(1.2f, size * 0.16f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
        }

        private static string SecondaryText(TodoItem item)
        {
            if (!item.DueDate.HasValue)
            {
                return item.Steps.Count > 0 ? string.Format("{0}/{1} 步", item.Steps.Count(step => step.IsCompleted), item.Steps.Count) : "无截止时间";
            }

            DateTimeOffset due = item.DueDate.Value.ToLocalTime();
            DateTimeOffset now = DateTimeOffset.Now;
            string prefix = due.Date == now.Date ? "今天 "
                : due.Date == now.Date.AddDays(1) ? "明天 "
                : "截止 " + due.ToString("yyyy/M/d", CultureInfo.InvariantCulture) + " ";
            return prefix + due.ToString("HH:mm", CultureInfo.InvariantCulture) +
                   (item.Recurrence == null ? "" : " · 重复" + TodoRecurrenceMode.Display(item.Recurrence.Mode));
        }

        protected override void OnPaletteChanged()
        {
            _title.ForeColor = Ui.Text;
            Invalidate(true);
        }
    }
}
