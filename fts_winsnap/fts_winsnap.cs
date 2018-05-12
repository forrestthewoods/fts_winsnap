using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace fts_winsnap
{
    public partial class fts_winsnap : Form
    {
        // Native imports
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow); enum ShowCmd : int { Hide = 0, Normal = 1, Minimize = 2, Maximize = 3, Restore = 9 }
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint); // screen coords
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hWnd, out WINDOWPLACEMENT lpwndpl);
        [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl); // workspace coords
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags); enum SWP_FLAGS : uint { NO_SIZE = 0x0001, NO_MOVE = 0x0002, NO_REDRAW = 0x0008 }

        // Native structures
        [StructLayout(LayoutKind.Sequential)] struct RECT {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }

            public RECT(int _left, int _top, int _right, int _bottom) { Left = _left; Top = _top; Right = _right; Bottom = _bottom; }
            public RECT(Rectangle r) { Left = r.Left; Top = r.Top; Right = r.Right; Bottom = r.Bottom; }
            public Rectangle AsRectangle() { return new Rectangle(Left, Top, Width, Height); }
            public RECT Extended(RECT other) { return new RECT(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom)); }
        }

        [StructLayout(LayoutKind.Sequential)] struct WINDOWPLACEMENT {
            public int length;
            public int flags;
            public ShowCmd showCmd;
            public System.Drawing.Point ptMinPosition;
            public System.Drawing.Point ptMaxPosition;
            public RECT rcNormalPosition;
        }
        enum ShowWindowCommands : int { Hide = 0, Normal = 1, Minimized = 2, Maximized = 3, }



        // Fields
        List<MonitorLayout> _layouts = new List<MonitorLayout>();
        Settings _settings = null;
        bool _runOnStartup = false;


        // Properties
        IEnumerable<Section> Sections {
            get {
                foreach (var layout in _layouts)
                    foreach (var section in layout.Sections)
                        yield return section;
            }
        }


        // Methods
        public fts_winsnap()
        {
            // Set working dir to exe dir so app don't crash on startup
            var exePath = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            bool autoStarted = Environment.CurrentDirectory != exePath;
            if (autoStarted)
                Environment.CurrentDirectory = exePath;

            // Read settings from disk (if there are any)
            LoadSettings();

            // Check registry for startup option
            var registryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            var v = registryKey.GetValue("fts_winsnap") as string;
            if (v == Application.ExecutablePath)
                _runOnStartup = true;

            // WinForm Component initialization
            InitializeComponent();
            this._notifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(new System.Windows.Forms.MenuItem[]{
                new System.Windows.Forms.MenuItem("Show fts_winsnap", (object sender, System.EventArgs e)=>{ this?.Show(); }),
                new System.Windows.Forms.MenuItem("Close fts_winsnap", (object sender, System.EventArgs e)=>{ this?.Close(); })
            });
            Application.ApplicationExit += new EventHandler(this.OnApplicationExit);

            // Minimize to task bar if app was automatically launched by Windows on startup
            if (autoStarted) {
                this.ShowInTaskbar = false;
                this.WindowState = FormWindowState.Minimized;
            }

            // Register hotkeys
            RegisterHotKey(this.Handle, 0, Constants.WM_MOD_CTRL | Constants.WM_MOD_ALT, (int)Keys.Left);
            RegisterHotKey(this.Handle, 1, Constants.WM_MOD_CTRL | Constants.WM_MOD_ALT, (int)Keys.Right);
            RegisterHotKey(this.Handle, 2, Constants.WM_MOD_CTRL | Constants.WM_MOD_ALT, (int)Keys.Up);
            RegisterHotKey(this.Handle, 3, Constants.WM_MOD_CTRL | Constants.WM_MOD_ALT, (int)Keys.Down);
            RegisterHotKey(this.Handle, 4, Constants.WM_MOD_CTRL | Constants.WM_MOD_SHIFT | Constants.WM_MOD_ALT, (int)Keys.Down);
            RegisterHotKey(this.Handle, 5, Constants.WM_MOD_CTRL | Constants.WM_MOD_SHIFT | Constants.WM_MOD_ALT, (int)Keys.Left);
            RegisterHotKey(this.Handle, 6, Constants.WM_MOD_CTRL | Constants.WM_MOD_SHIFT | Constants.WM_MOD_ALT, (int)Keys.Right);
            RegisterHotKey(this.Handle, 7, Constants.WM_MOD_CTRL | Constants.WM_MOD_SHIFT | Constants.WM_MOD_ALT, (int)Keys.Up);


            // Initialize fonts for UI
            var helveticaFamily = new FontFamily("Helvetica");
            var monospaceFamily = new FontFamily(System.Drawing.Text.GenericFontFamilies.Monospace);

            var pfc = new System.Drawing.Text.PrivateFontCollection();
            pfc.AddFontFile(@"Karla-Regular.ttf");
            var karlaFamily = pfc.Families[0];


            // Initialize per-monitor settings
            while (_settings.monitorSettings.Count < Screen.AllScreens.Count()) {
                var newSettings = new Settings.MonitorSettings();
                _settings.monitorSettings.Add(newSettings);
            }

            // createLabel helper
            Func<string, FontFamily, float, FontStyle, Padding, Rectangle, Label> createLabel =
                (string text, FontFamily fontFamily, float fontScale, FontStyle fontStyle, Padding padding, Rectangle bounds) => {
                    var newLabel = new Label();
                    newLabel.Text = text;
                    newLabel.Font = new Font(fontFamily, newLabel.Font.Size * fontScale, fontStyle);
                    newLabel.AutoSize = true;
                    newLabel.Padding = padding;
                    newLabel.Bounds = bounds;
                    this.Controls.Add(newLabel);
                    return newLabel;
                };

            // Create UI for each monitor
            int idx = 1;
            int y = 0;
            foreach (var monitor in Screen.AllScreens)
            {
                // Create a new layout for this monitor
                var newLayout = new MonitorLayout(monitor);
                _layouts.Add(newLayout);
                
                // Grab settings for this monitor
                var monitorSettings = _settings.monitorSettings[idx - 1];

                // Monitor #
                var titleLabel = createLabel("Monitor " + idx.ToString(), karlaFamily, 2f, FontStyle.Bold, new Padding(4), new Rectangle(0, y, 0, 0));

                // Resolution: 1920x1080
                var resolutionLabel = createLabel("Resolution: " + monitor.Bounds.Width.ToString() + "x" + monitor.Bounds.Height.ToString(),
                    karlaFamily, 1.5f, FontStyle.Regular, new Padding(32, 3, 3, 3), new Rectangle(titleLabel.Bounds.Left, titleLabel.Bounds.Bottom, 0, 0));

                // Resize: <NumberField>
                var resizeLabel = createLabel("Resize: ", karlaFamily, 1.5f, FontStyle.Regular, new Padding(32, 0, 0, 0), new Rectangle(0, resolutionLabel.Bottom, 0, 0));

                var resizeControl = new NumericUpDown();
                resizeControl.Bounds = new Rectangle(resizeLabel.Bounds.Right + 4, resizeLabel.Bounds.Top, 50, 0);
                resizeControl.Minimum = -30;
                resizeControl.Maximum = 30;
                resizeControl.Value = monitorSettings.adjustSize;
                resizeControl.AutoSize = true;
                resizeControl.TabStop = false;
                resizeControl.ValueChanged += (object sender, EventArgs e) => {
                    // Update settings
                    newLayout.adjustSize = (int)resizeControl.Value;
                    monitorSettings.adjustSize = newLayout.adjustSize;
                    SaveSettings();
                };
                
                this.Controls.Add(resizeControl);


                // Layout plus predefined buttons
                var layout = createLabel("Layout: ", karlaFamily, 1.5f, FontStyle.Regular, new Padding(32, 8, 3, 3), new Rectangle(resizeLabel.Bounds.Left, resizeLabel.Bounds.Bottom + 5, 0, 0));

                var customField = new System.Windows.Forms.TextBox();
                Button selectedBtn = null;
                Button customBtn = null;

                // Helper to style button visuals
                Action <Button> styleButton = (Button b) => {
                    b.Padding = new Padding(3);
                    b.AutoSize = true;
                    b.Font = new Font(helveticaFamily, b.Font.Size * 1.2f, FontStyle.Bold);
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.BorderColor = midGray;
                    b.FlatAppearance.MouseOverBackColor = medBlue;
                    b.FlatAppearance.MouseDownBackColor = medBlue;
                    b.FlatAppearance.CheckedBackColor = darkBlue;
                    b.ForeColor = darkGray;
                    b.BackColor = faintGray;
                };

                // Helper to set layous from json (throw exception on bad json!)
                Action<string> UpdateLayouts = (string json) => {
                    var layoutEntries = JsonConvert.DeserializeObject<IList<IList<int>>>(json);
                    List<Rectangle> newRects = new List<Rectangle>(layoutEntries.Count);
                    foreach (var layoutEntry in layoutEntries)
                    {
                        int minX = layoutEntry[0];
                        int minY = layoutEntry[1];
                        int maxX = layoutEntry[2];
                        int maxY = layoutEntry[3];
                        newRects.Add(new Rectangle(minX, minY, maxX - minX, maxY - minY));
                    }
                    newLayout.SetLayouts(newRects);
                };

                // Handler for when user clicks button
                Action<Button, string> clickButton = (Button b, string json) => {

                    // Update layouts
                    try { UpdateLayouts(json); }
                    catch (System.Exception /*ex*/) { return; }

                    // Show/Hide json field if 'Custom' button is clicked
                    if (b == customBtn) {
                        customField.Show();
                    }
                    else {
                        customField.Hide();
                    }

                    // Reset colors on previously selected button
                    if (selectedBtn != null) {
                        selectedBtn.ForeColor = darkGray;
                        selectedBtn.BackColor = faintGray;
                    }

                    // Set colors on nwely selected button
                    b.BackColor = darkBlue;
                    b.ForeColor = Color.White;
                    selectedBtn = b;

                    // Update settings
                    monitorSettings.selectedButton = b.Text;
                    SaveSettings();
                };

                Button initialButton = null;
                string initialJson = null;

                // Helper to make a button
                Func<Rectangle, string, string, Button> makeButton = (Rectangle bounds, string label, string json) => {

                    // Create button
                    var newBtn = new System.Windows.Forms.Button();
                    newBtn.Bounds = bounds;
                    newBtn.Text = label;
                    styleButton(newBtn);

                    Action<object, EventArgs> onClick = (object s, EventArgs e) => { clickButton(newBtn, json); };
                    newBtn.Click += (object s, EventArgs e) => { onClick(s, e); };

                    this.Controls.Add(newBtn);

                    // Check if this is the default selection
                    if (label == monitorSettings.selectedButton) {
                        initialButton = newBtn;
                        initialJson = json;
                    }

                    return newBtn;
                };

                Button btn = null;
                int pad = 3;

                btn = makeButton(new Rectangle(layout.Bounds.Right, layout.Bounds.Top, 0, 0), "1x1", "[[0,0,100,100]]");

                btn = makeButton(new Rectangle(btn.Bounds.Right + pad, btn.Bounds.Top, 0, 0), "2x1", "[[0,0,50,100], [50,0,100,100]]");
                btn = makeButton(new Rectangle(btn.Bounds.Right + pad, btn.Bounds.Top, 0, 0), "1x2", "[[0,0,100,50], [0,50,100,100]]");
                btn = makeButton(new Rectangle(btn.Bounds.Right + pad, btn.Bounds.Top, 0, 0), "2x2", "[[0,0,50,50], [50,0,100,50], [0,50,50,100], [50,50,100,100]]");

                btn = makeButton(new Rectangle(layout.Bounds.Right, btn.Bounds.Bottom + 5, 0, 0), "3x1", "[[0,0,33,100], [33,0,67,100], [67,0,100,100]]");
                btn = makeButton(new Rectangle(btn.Bounds.Right + pad, btn.Bounds.Top, 0, 0), "1x3", "[[0,0,100,33], [0,33,100,67], [0,67,100,100]]");
                btn = makeButton(new Rectangle(btn.Bounds.Right + pad, btn.Bounds.Top, 0, 0), "3x3", "[[0,0,33,33], [33,0,67,33], [67,0,100,33], [0,33,33,67], [33,33,67,67], [67,33,100,67], [0,67,33,100], [33,67,67,100], [67,67,100,100]]");

                customBtn = makeButton(new Rectangle(btn.Bounds.Right + pad, btn.Bounds.Top, 0, 0), "Custom", monitorSettings.customJson);


                // Custom field holds JSON layout data. Can be hand edited by user if 'Custom' button is selected
                customField.Bounds = new Rectangle(layout.Bounds.Right, btn.Bounds.Bottom + 4, this.Width / 2, layout.Bounds.Height);
                customField.TabStop = false;
                customField.Font = new Font(monospaceFamily, customField.Font.Size, FontStyle.Regular);
                customField.Text = monitorSettings.customJson;
                customField.TextChanged += (object sender, EventArgs e) => {
                    try {
                        UpdateLayouts(customField.Text);
                        customField.BackColor = Color.White;

                        monitorSettings.customJson = customField.Text;
                        SaveSettings();
                    }
                    catch (Exception /*ex*/) {
                        customField.BackColor = paleRed;
                    }

                };
                this.Controls.Add(customField);

                // Click initial button for initial state
                clickButton(initialButton, initialJson);

                y = customField.Bounds.Bottom + 15;
                ++idx;
            }


            // 'Controls' section header
            var controls = createLabel("Controls", karlaFamily, 2f, FontStyle.Bold, new Padding(4), new Rectangle(0, y, 0, 0));

            // 'Move: Ctrl  +  Alt +  ArrowKey'
            var moveLabel = createLabel("Move: ", karlaFamily, 1.5f, FontStyle.Regular, new Padding(32, 3, 3, 3), new Rectangle(controls.Bounds.Left, controls.Bounds.Bottom, 0, 0));
            var moveControls = createLabel("Ctrl  +  Alt +  ArrowKey", karlaFamily, 1.5f, FontStyle.Regular, new Padding(3), new Rectangle(110, moveLabel.Bounds.Top, 0, 0));

            // 'Expand: Ctrl  +  Alt  +  Shift  +  ArrowKey'
            var expandLabel = createLabel("Expand: ", karlaFamily, 1.5f, FontStyle.Regular, new Padding(32, 3, 3, 3), new Rectangle(moveLabel.Bounds.Left, moveLabel.Bounds.Bottom, 0, 0));
            var expandText = createLabel("Ctrl  +  Alt  +  Shift  +  ArrowKey", karlaFamily, 1.5f, FontStyle.Regular, new Padding(3), new Rectangle(110, expandLabel.Bounds.Top, 0, 0));


            // 'Options' section header
            var optionsLabel = createLabel("Options", karlaFamily, 2f, FontStyle.Bold, new Padding(4), new Rectangle(0, expandText.Bounds.Bottom + 30, 0, 0));

            var runOnStartupLabel = createLabel("Run on Startup: ", karlaFamily, 1.5f, FontStyle.Regular, new Padding(32, 0, 0, 0), new Rectangle(0, optionsLabel.Bottom, 0, 0));

            var runOnStartup = new System.Windows.Forms.CheckBox();
            runOnStartup.Checked = _runOnStartup;
            runOnStartup.Bounds = new Rectangle(runOnStartupLabel.Bounds.Right + 10, runOnStartupLabel.Bounds.Top + 5, 0, 0);
            runOnStartup.AutoSize = true;
            runOnStartup.CheckedChanged += (object sender, EventArgs e) => {
                // Add or remove register key
                var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (runOnStartup.Checked)
                    rk.SetValue("fts_winsnap", Application.ExecutablePath);
                else
                    rk.DeleteValue("fts_winsnap", false);

                // Update settings
                _runOnStartup = runOnStartup.Checked;
                SaveSettings();
            };
            this.Controls.Add(runOnStartup);
        }

        bool MoveWindow(IntPtr window, Direction moveDir, MoveType moveType)
        {
            if (window == null)
                return false;

            // Get minimized/maximized state
            WINDOWPLACEMENT placement;
            GetWindowPlacement(window, out placement);
            bool isMaximized = placement.showCmd == ShowCmd.Maximize;
            bool isMinimized = placement.showCmd == ShowCmd.Minimize;

            // Get window rect
            RECT windowRect, clientRect;
            GetWindowRect(window, out windowRect);
            GetClientRect(window, out clientRect);

            // Find closest rect to current position
            Section closestSection = null;
            float closestOverlap = -1f;
            RECT testRect = isMinimized ? placement.rcNormalPosition : windowRect;

            foreach (var section in Sections) {
                float overlap = RectangleOverlapRatio(testRect, section.rect);

                if (overlap > closestOverlap
                    || (overlap == closestOverlap && (
                        (moveDir == Direction.Left && section.rect.Left < closestSection.rect.Left)
                        || (moveDir == Direction.Up && section.rect.Top < closestSection.rect.Top)
                        || (moveDir == Direction.Right && section.rect.Right > closestSection.rect.Right)
                        || (moveDir == Direction.Down && section.rect.Bottom > closestSection.rect.Bottom))))
                {
                    closestSection = section;
                    closestOverlap = overlap;
                }
            }

            Section nextSection = null;

            // IF window is not aligned with the closest section THEN fill that section
            bool fillClosest = !isMaximized && !isMinimized && moveType != MoveType.Extend && closestOverlap < .97f;
            if (fillClosest) {
                nextSection = closestSection;
            }
            else {
                // Window will be moving to a new section

                // Check for maximize
                if (moveDir == Direction.Up && !isMaximized && !isMinimized && (closestSection.rect.Top == closestSection.layout.top || RectangleOverlapRatio(testRect.AsRectangle(), closestSection.layout.screen.WorkingArea) > .9f))
                    return ShowWindowAsync(window, (int)ShowCmd.Maximize);

                // Check for minimize
                if (moveDir == Direction.Down && !isMaximized && !isMinimized && closestSection.rect.Bottom == closestSection.layout.bottom)
                    return ShowWindowAsync(window, (int)ShowCmd.Minimize);

                // Check for restore
                if ((moveDir == Direction.Up && isMinimized) || (moveDir == Direction.Down && isMaximized))
                    return ShowWindowAsync(window, (int)ShowCmd.Restore);

                // Cache bounds of closestSection
                var closestBounds = closestSection.rect;
                if (isMaximized) {
                    // Treat maximized as a 0-height rect at top of working area
                    closestBounds = new RECT(closestSection.layout.screen.WorkingArea);
                    closestBounds.Bottom = closestBounds.Top;
                }
                else if (isMinimized) {
                    // Treat minimized as a 0-height rect at bottom of working area
                    closestBounds = new RECT(closestSection.layout.screen.WorkingArea);
                    closestBounds.Top = closestBounds.Bottom;
                }

                // Find best section
                Section bestSection = null;
                int bestOverlap = int.MinValue;
                int bestOffAxisOverlap = int.MinValue;

                foreach (var testSection in Sections) {
                    testRect = testSection.rect;

                    // Skip section where window is currently placed
                    if (testRect.AsRectangle() == closestBounds.AsRectangle())
                        continue;

                    bool isSameScreen = testSection.layout.screen == closestSection.layout.screen;

                    // Moving Up while maximized or Down while minimized means window MUST change screens
                    bool requireDifferentScreen = (isMaximized && moveDir == Direction.Up) || (isMinimized && moveDir == Direction.Down);
                    if (requireDifferentScreen && isSameScreen)
                        continue;
                    
                    // Moving Left/Right while Maximized OR Minimized MUST stay on the same screen
                    bool requireSameScreen = (moveDir == Direction.Left || moveDir == Direction.Right) && (isMaximized || isMinimized);
                    if (requireSameScreen && !isSameScreen)
                        continue;

                    // TestRect must be in direction of moveDir. UNLESS we're in a requireSameScreen special case
                    if (!requireSameScreen) {
                        if (moveDir == Direction.Up && testRect.Bottom > closestBounds.Top)       continue;
                        if (moveDir == Direction.Down && testRect.Top < closestBounds.Top)        continue;
                        if (moveDir == Direction.Right && testRect.Left < closestBounds.Right)    continue;
                        if (moveDir == Direction.Left && testRect.Right > closestBounds.Left)     continue;
                    }

                    int horizOverlap = OverlapAmount(closestBounds.Left, closestBounds.Right, testRect.Left, testRect.Right);
                    int vertOverlap = OverlapAmount(closestBounds.Top, closestBounds.Bottom, testRect.Top, testRect.Bottom);

                    // Determine overlap along primary test axis and off axis.
                    bool vertDir = moveDir == Direction.Up || moveDir == Direction.Down;
                    int axisOverlap = vertDir ? horizOverlap : vertOverlap;
                    int offAxisOverlap = Clamp(vertDir ? vertOverlap : horizOverlap, int.MinValue, 0);

                    // We want the most overlap for our main axis
                    if (axisOverlap < bestOverlap)
                        continue;

                    // Tie breaker for same axisOverlap (happens all the time with grids)
                    if (bestSection != null && axisOverlap == bestOverlap) {
                        var bestRect = bestSection.rect;

                        // Closest for up/down
                        if (moveDir == Direction.Up && bestRect.Bottom > testRect.Bottom) continue;
                        if (moveDir == Direction.Down && bestRect.Top < testRect.Top) continue;

                        if (isMaximized || isMinimized) {
                            // Most left/right for left/right
                            if (moveDir == Direction.Left && bestRect.Left < testRect.Left) continue;
                            if (moveDir == Direction.Right && bestRect.Right > testRect.Right) continue;
                        }
                        else {
                            // Closest for left/right
                            if (moveDir == Direction.Left && bestRect.Left > testRect.Left) continue;
                            if (moveDir == Direction.Right && bestRect.Right < testRect.Right) continue;
                        }
                    }

                    // And we want the closestOverlap on offAxis (which is probably negative)
                    if (offAxisOverlap < bestOffAxisOverlap)
                        continue;

                    bestSection = testSection;
                    bestOverlap = axisOverlap;
                    bestOffAxisOverlap = offAxisOverlap;
                }

                if (bestSection == null)
                    return false;

                nextSection = bestSection;
            }


            // Move to the nextSection
            var nextRect = nextSection.rect;

            // Expact rect to account for border padding
            GetWindowRect(window, out windowRect);
            GetClientRect(window, out clientRect);
            int pad = (windowRect.Width - clientRect.Width) / 2 + nextSection.layout.adjustSize;
            int xPos = nextRect.Left - pad;
            int yPos = nextRect.Top;
            int width = nextRect.Width + 2*pad;
            int height = nextRect.Height + pad;
            RECT screenRect = new RECT(xPos, yPos, xPos + width, yPos + height);

            // Check for Extend operation
            if (moveType == MoveType.Extend && nextSection.layout.screen == closestSection.layout.screen && !isMinimized && !isMaximized)
                screenRect = windowRect.Extended(screenRect);

            // Convert screen coords workspace coords
            var screenBounds = nextSection.layout.screen.Bounds;
            var workspaceBounds = nextSection.layout.screen.WorkingArea;
            RECT workspaceRect = screenRect;


            // Apply workspace offset
            int xOffset = workspaceBounds.Left - screenBounds.Left;
            int yOffset = workspaceBounds.Top - screenBounds.Top;
            workspaceRect.Left -= xOffset;
            workspaceRect.Right -= xOffset;
            workspaceRect.Top -= yOffset;
            workspaceRect.Bottom -= yOffset;

            placement.rcNormalPosition = workspaceRect;
            placement.showCmd = ShowCmd.Normal;
            bool result = SetWindowPlacement(window, ref placement);

            // Windows can't handle moving to a monitor with a different scale factor.
            // So see if SetWindowPlacement failed to the put window where we said to put it
            // If it did fail, then use SetWindowPos to set pos/size
            // Note: We don't use SetWindowPos all the time because SetWindowPos on a maximized window 
            // either pops (ShowCmd.Normal) or we lose the maximize flag which screws up everything :(
            GetWindowRect(window, out windowRect);
            if ((windowRect.Width != screenRect.Width || windowRect.Height != screenRect.Height)) {

                // Recalculate pad and associated values
                GetWindowRect(window, out windowRect);
                GetClientRect(window, out clientRect);
                pad = (windowRect.Width - clientRect.Width) / 2 + nextSection.layout.adjustSize;
                xPos = nextRect.Left - pad;
                yPos = nextRect.Top;
                width = nextRect.Width + 2*pad;
                height = nextRect.Height + pad;
                screenRect = new RECT(xPos, yPos, xPos + width, yPos + height);


                // Force window to normal (in-case it was minimized or maximized)
                ShowWindow(window, (int)ShowCmd.Normal);

                // Move window to correct position. Do not change size. Do not redraw.
                SetWindowPos(window, new IntPtr(), screenRect.Left, screenRect.Top, screenRect.Width, screenRect.Height, (uint)SWP_FLAGS.NO_SIZE | (uint)SWP_FLAGS.NO_REDRAW);

                // Change Window size. Do not move. Redraw.
                SetWindowPos(window, new IntPtr(), screenRect.Left, screenRect.Top, screenRect.Width, screenRect.Height, (uint)SWP_FLAGS.NO_MOVE);
            }

            return true;
        }

        void SaveSettings() {
            // Write JSON settings to ..\users\username\AppData\local\fts\fts_winsnap\settings.json
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\fts\\fts_winsnap\\";
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var filePath = dir + "settings.json";
            var json = JsonConvert.SerializeObject(_settings);
            System.IO.File.WriteAllText(filePath, json);
        }

        void LoadSettings() {
            // Load JSON settings from ..\users\username\AppData\local\fts\fts_winsnap\settings.json
            var filePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\fts\\fts_winsnap\\settings.json";
            if (!System.IO.File.Exists(filePath)) {
                _settings = new Settings();
                _settings.monitorSettings = new List<Settings.MonitorSettings>();
                return;
            }

            var filestream = System.IO.File.Open(filePath, System.IO.FileMode.Open);
            var streamreader = new System.IO.StreamReader(filestream);
            var json = streamreader.ReadToEnd();
            _settings = JsonConvert.DeserializeObject<Settings>(json);
            filestream.Close();
        }
        
        protected override void WndProc(ref Message m)
        {
            // Handle hotkey messages
            if (m.Msg == Constants.WM_HOTKEY_MSG_ID) {
                int iKey = ((int)m.LParam) >> 16;
                int id = (int)m.WParam;
                MoveType moveType = id < 4 ? MoveType.Move : MoveType.Extend;

                MoveWindow(GetForegroundWindow(), DirectionFromKey((Keys)iKey), moveType);
            }
            else
                base.WndProc(ref m);
        }


        // Helper math functions
        static int Clamp(int value, int min, int max) { return value < min ? min : value > max ? max : value; }
        static int OverlapAmount(int min0, int max0, int min1, int max1) { return Math.Min(max0, max1) - Math.Max(min0, min1); }

        static float RectInRectRatio(Rectangle a, Rectangle b) {
            if (!a.IntersectsWith(b))
                return 0f;

            float left = Math.Max(a.Left, b.Left);
            float right = Math.Min(a.Right, b.Right);
            float top = Math.Max(a.Top, b.Top);
            float bot = Math.Min(a.Bottom, b.Bottom);
            float area = (right - left) * (bot - top);
            float aArea = a.Width * a.Height;
            return area / aArea;
        }

        static float RectangleOverlapRatio(Rectangle a, Rectangle b) {
            float AinB = RectInRectRatio(a, b);
            float BinA = RectInRectRatio(b, a);

            return (AinB + BinA) / 2;
        }

        static float RectangleOverlapRatio(RECT a, RECT b) {
            Rectangle _a = new Rectangle(a.Left, a.Top, a.Width, a.Height);
            Rectangle _b = new Rectangle(b.Left, b.Top, b.Width, b.Height);
            return RectangleOverlapRatio(_a, _b);
        }


        // Helper Types
        enum Direction { Left, Right, Up, Down };
        Direction DirectionFromKey(Keys key) {
            switch (key) {
                case Keys.Left:     return Direction.Left;
                case Keys.Right:    return Direction.Right;
                case Keys.Up:       return Direction.Up;
                case Keys.Down:     return Direction.Down;
                default:            return Direction.Left;
            }
        }

        enum MoveType { Move, Extend };

        class Section {
            public MonitorLayout layout { get; private set; }
            public RECT rect { get; private set; }

            public Section(MonitorLayout _layout, RECT _rect) {
                layout = _layout;
                rect = _rect;
            }
        }

        class MonitorLayout
        {
            public Screen screen { get; private set; }
            public int adjustSize { get; set; }
            List<RectFrac> _sections;

            public int top { get { return Sections.Select(s => s.rect.Top).Min(); } }
            public int bottom { get { return Sections.Select(s => s.rect.Bottom).Max(); } }

            public MonitorLayout(Screen screen) {
                this.screen = screen;
                _sections = new List<RectFrac>();
                _sections.Add(new RectFrac(new Rectangle(0, 0, 100, 100)));
            }

            public void SetLayouts(IEnumerable<Rectangle> layouts) {
                _sections.Clear();
                foreach (var rect in layouts) {
                    _sections.Add(new RectFrac(rect));
                }

                if (_sections.Count == 0)
                    _sections.Add(new RectFrac(new Rectangle(0, 0, 100, 100)));
            }

            public IEnumerable<Section> Sections {
                get {
                    var screenArea = screen.WorkingArea;

                    foreach (var rect in _sections) {
                        int x0 = screenArea.Left + (int)(rect.Left * screenArea.Width);
                        int y0 = screenArea.Top + (int)(rect.Top * screenArea.Height);
                        int x1 = screenArea.Left + (int)(rect.Right * screenArea.Width);
                        int y1 = screenArea.Top + (int)(rect.Bottom * screenArea.Height);

                        yield return new Section(this, new RECT(x0, y0, x1, y1));
                    }
                }
            }

            struct RectFrac {
                public float Left { get; private set; }
                public float Top { get; private set; }
                public float Right { get; private set; }
                public float Bottom { get; private set; }

                public RectFrac(Rectangle r) {
                    Left   = (float)Clamp(r.Left) / 100f;
                    Top    = (float)Clamp(r.Top) / 100f;
                    Right  = (float)Clamp(r.Right) / 100f;
                    Bottom = (float)Clamp(r.Bottom) / 100f;
                }
                static int Clamp(int v) { return v < 0 ? 0 : v > 100 ? 100 : v; }
            }
        }

        class Settings {

            public IList<MonitorSettings> monitorSettings { get; set; }

            public class MonitorSettings {
                public int adjustSize               = 0;
                public string selectedButton        = "2x2";
                public string customJson            = "[[0,0,50,50], [50,0,100,50], [0,50,50,100], [50,50,100,100]]";
            }
        }

        // UI Colors
        Color darkBlue = Color.FromArgb(7, 117, 200);
        Color medBlue = Color.FromArgb(33, 150, 240);
        Color faintGray = Color.FromArgb(240, 246, 249);
        Color darkGray = Color.FromArgb(66, 88, 104);
        Color midGray = Color.FromArgb(216, 224, 228);
        Color paleRed = Color.FromArgb(255, 204, 204);

        // WinForm Events
        private void fts_winsnap_Resize(object sender, EventArgs e) {
            if (this.WindowState == FormWindowState.Minimized) {
                this.Hide();
            }
        }

        private void _notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e) {
            this.ShowInTaskbar = true;
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void _notifyIcon_MouseClick(object sender, MouseEventArgs e) {
            this.ShowInTaskbar = true;
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            //Cleanup so that the icon will be removed when the application is closed
            _notifyIcon.Dispose();
        }

        private void fts_winsnap_Click(object sender, EventArgs e)
        {
            this.ActiveControl = null;
        }
    }

    public static class Constants
    {
        // Defined in winuser.h
        public const int WM_HOTKEY_MSG_ID = 0x0312;

        // Via MSDN RegisterHotKey function
        public const int WM_MOD_ALT = 0x1;
        public const int WM_MOD_CTRL = 0x2;
        public const int WM_MOD_SHIFT = 0x4;
        public const int WM_MOD_WIN = 0x8;
    }
}
