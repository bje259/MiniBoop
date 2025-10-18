using System;
using System.Drawing;
using System.Windows.Forms;
using ScintillaNET;
using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Drawing.Imaging;
using System.Data;


namespace MiniBoop
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
    public static class Utils
    {
        public static bool Debug = false;
        public static string Dump(object obj) =>
            obj == null ? "null" : JsonConvert.SerializeObject(obj, Formatting.Indented);
        public static string DumpPrint(object obj, string prefix = "")
        {
            var json = Dump(obj);
            Console.WriteLine($"{prefix}{json}");
            return json;
        }

    }
    public class ImageLoader
    {
        public static Image LoadImageFromBase64(string base64)
        {
            byte[] imageBytes = Convert.FromBase64String(base64);
            using (var ms = new MemoryStream(imageBytes))
            {
                return Image.FromStream(ms);
            }
        }
    }

    public class BoopState
    {
        public string EditorText { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public int ScrollPosition { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public bool WindowWasMaximized { get; set; }
        public int FontSize { get; set; }
        public int ZoomLevel { get; set; }
    }



    public class MainForm : Form
    {
        private Scintilla editor;
        private MenuStrip menuStrip;
        private ToolStripMenuItem transformMenu;
        private ToolStrip toolStrip;
        private int fontSize;
        private int zoomLevel;

        public class BoopStyle
        {

            private enum BoopStyles
            {
                Default = 0,
                Comment = 1,
                String = 2,
                Attribute = 3,
                Keyword = 4,
                Number = 5,
                Extra = 6
            }

            public const int Default = (int)BoopStyles.Default;
            public const int Comment = (int)BoopStyles.Comment;
            public const int Number = (int)BoopStyles.Number;
            public const int String = (int)BoopStyles.String;
            public const int Attribute = (int)BoopStyles.Attribute;
            public const int Keyword = (int)BoopStyles.Keyword;
            public const int Extra = (int)BoopStyles.Extra;


        }

        public MainForm()
        {
            this.Text = "Mini Boop";
            this.Size = new Size(600, 400);
            this.fontSize = 10;
            
            InitializeEditor2();
            InitializeMenu();
            InitializeContextMenu();

            this.Controls.Add(editor);
            this.Controls.Add(toolStrip);
            this.Controls.Add(menuStrip);
            this.MainMenuStrip = menuStrip;
            this.Load += (s, e) => LoadState();
            this.FormClosing += (s, e) => SaveState();
        }

        Image CharToImage(string c, Font font, Color fore, Color back, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(back);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                // Measure likely size
                SizeF sz = g.MeasureString(c, font);
                float x = (width - sz.Width) / 2;
                float y = (height - sz.Height) / 2;
                using (Brush brush = new SolidBrush(fore))
                {
                    g.DrawString(c, font, brush, x, y);
                }
            }
            return bmp;
        }
        Image ResizeImage(Image img, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, width, height);
            }
            return bmp;
        }

        // Example usage
        // Image scaledImg = ResizeImage(img, 32, 32);
        // label2.Image = scaledImg;

        void ApplyDarkTheme()
        {
            var bg = Color.FromArgb(30, 30, 30);
            var fg = Color.Gainsboro;
            var marginBg = Color.FromArgb(45, 45, 48);
            var caretLineBg = marginBg;

            editor.Styles[Style.Default].BackColor = bg;
            editor.Styles[Style.Default].ForeColor = fg;
            editor.StyleClearAll();

            // Gutter / line number margin
            editor.Styles[Style.LineNumber].BackColor = marginBg;
            editor.Styles[Style.LineNumber].ForeColor = fg;
            editor.Margins[0].BackColor = marginBg;
            editor.Margins[1].BackColor = marginBg; // selection margin
            editor.Margins[2].BackColor = marginBg; // folding margin

            // Caret and selection
            editor.CaretForeColor = Color.White;
            editor.CaretLineVisible = true;
            editor.CaretLineBackColor = caretLineBg;
            editor.SetSelectionBackColor(true, Color.FromArgb(60, 60, 90));

            // Whitespace and guides
            editor.SetWhitespaceBackColor(true, bg);
            editor.SetWhitespaceForeColor(true, Color.DimGray);
            editor.IndentationGuides = IndentView.LookBoth;

            // Brace matching
            editor.Styles[Style.BraceLight].ForeColor = Color.Yellow;
            editor.Styles[Style.BraceLight].BackColor = marginBg;
            editor.Styles[Style.BraceBad].ForeColor = Color.White;
            editor.Styles[Style.BraceBad].BackColor = Color.DarkRed;

            // Indent guides and control chars
            editor.Styles[Style.IndentGuide].ForeColor = Color.DimGray;
            // editor.Styles[Style.ControlChar].ForeColor = Color.DimGray;

            // Call tip
            editor.Styles[Style.CallTip].BackColor = marginBg;
            editor.Styles[Style.CallTip].ForeColor = Color.White;

            // Syntax highlighting
            editor.Styles[BoopStyle.Comment].ForeColor = Color.Green;
            editor.Styles[BoopStyle.String].ForeColor = Color.Orange;
            editor.Styles[BoopStyle.Keyword].ForeColor = Color.DeepSkyBlue;
            editor.Styles[BoopStyle.Number].ForeColor = Color.MediumPurple;
            editor.Styles[BoopStyle.Attribute].ForeColor = Color.Teal;
            editor.Styles[BoopStyle.Extra].ForeColor = Color.DarkRed;


            //folding

            for (int i = 25; i <= 31; i++)
            {
                editor.Markers[i].SetForeColor(Color.Gainsboro);
                editor.Markers[i].SetBackColor(Color.FromArgb(45, 45, 48));
            }
                
        }

        void ApplyLightTheme()
        {
            var bg = Color.White;
            var fg = Color.Black;
            var marginBg = Color.WhiteSmoke;
            var caretLineBg = Color.Gainsboro;

            editor.Styles[Style.Default].BackColor = bg;
            editor.Styles[Style.Default].ForeColor = fg;
            editor.StyleClearAll();

            editor.Styles[Style.LineNumber].BackColor = marginBg;
            editor.Styles[Style.LineNumber].ForeColor = Color.Gray;
            editor.Margins[0].BackColor = marginBg;
            editor.Margins[1].BackColor = marginBg; // selection margin
            editor.Margins[2].BackColor = marginBg; // folding margin

            editor.CaretForeColor = Color.Black;
            editor.CaretLineVisible = true;
            editor.CaretLineBackColor = caretLineBg;
            editor.SetSelectionBackColor(true, Color.LightGray);

            editor.SetWhitespaceBackColor(true, bg);
            editor.SetWhitespaceForeColor(true, Color.LightGray);
            editor.IndentationGuides = IndentView.LookBoth;

            // Brace matching
            editor.Styles[Style.BraceLight].ForeColor = Color.DarkBlue;
            editor.Styles[Style.BraceLight].BackColor = Color.LightYellow;
            editor.Styles[Style.BraceBad].ForeColor = Color.White;
            editor.Styles[Style.BraceBad].BackColor = Color.Red;

            // Indent guides and control chars
            editor.Styles[Style.IndentGuide].ForeColor = Color.LightGray;
            // editor.Styles[Style.ControlChar].ForeColor = Color.Silver;

            // Call tip
            editor.Styles[Style.CallTip].BackColor = Color.Beige;
            editor.Styles[Style.CallTip].ForeColor = Color.Black;

            // Syntax highlighting
            editor.Styles[BoopStyle.Comment].ForeColor = Color.Green;
            editor.Styles[BoopStyle.Keyword].ForeColor = Color.Blue;
            editor.Styles[BoopStyle.String].ForeColor = Color.Brown;
            editor.Styles[BoopStyle.Number].ForeColor = Color.Purple;
            editor.Styles[BoopStyle.Attribute].ForeColor = Color.Teal;
            editor.Styles[BoopStyle.Extra].ForeColor = Color.DarkRed;

            //folding
            for (int i = 25; i <= 31; i++)
            {
                editor.Markers[i].SetForeColor(Color.Gray);
                editor.Markers[i].SetBackColor(Color.WhiteSmoke);
            }


        }
        
        private bool isDarkMode = true;

        private void ToggleTheme()
        {
            if (isDarkMode)
                ApplyLightTheme();
            else
                ApplyDarkTheme();

            isDarkMode = !isDarkMode;
        }

        private void InitializeEditor()
        {
            editor = new Scintilla
            {
                Dock = DockStyle.Fill
            };

            // Setup line numbers
            editor.Margins[0].Width = 20;

            // Set C++/C# style highlighting
            editor.Lexer = Lexer.Cpp;
            editor.Styles[Style.Cpp.Default].ForeColor = Color.Black;
            editor.Styles[Style.Cpp.Comment].ForeColor = Color.Green;
            editor.Styles[Style.Cpp.CommentLine].ForeColor = Color.Green;
            editor.Styles[Style.Cpp.Word].ForeColor = Color.Blue;
            editor.Styles[Style.Cpp.String].ForeColor = Color.Brown;

            editor.SetKeywords(0, "int string void if else return class using namespace public static");

        }


        private void ApplyHighlighting(string text)
        {
            var patterns = new List<(Regex, int)>
            {
                (new Regex(@"//.*"), BoopStyle.Comment),
                (new Regex(@"""(?:\\.|[^""\\])*"""), BoopStyle.String),
                (new Regex(@"\b(var|let|func|class)\b", RegexOptions.IgnoreCase), BoopStyle.Attribute),
                (new Regex(@"\b(true|false|null|from|to)\b", RegexOptions.IgnoreCase), BoopStyle.Keyword),
                (new Regex(@"\b\d+(\.\d+)?\b"), BoopStyle.Number),
                (new Regex(@"""[^""]+?""(?=\s*:)", RegexOptions.IgnoreCase), BoopStyle.Extra),
            };
            editor.StartStyling(0);
            editor.SetStyling(text.Length, BoopStyle.Default); // Clear

            foreach (var (regex, style) in patterns)
            {
                foreach (Match m in regex.Matches(text))
                {
                    editor.StartStyling(m.Index);
                    editor.SetStyling(m.Length, style);
                }
            }
        }
        
        // private void ApplyStyleToSelection(int style)
        // {
        //     int start = editor.SelectionStart;
        //     int end = start + editor.SelectedText.Length;

        //     editor.StartStyling(start);
        //     editor.SetStyling(end - start, style);
        // }

        // private void scintilla_TextChanged(object sender, EventArgs e)
        // {
        //     ApplyHighlighting(scintilla.Text);
        // }
        private void InitializeEditor2(bool reset = false)
        {
            if (!reset)
                editor = new Scintilla
                {
                    Dock = DockStyle.Fill
                };
            editor.Styles[Style.Default].Font = "MesloLGS Nerd Font Mono";
            editor.Styles[Style.Default].Size = this.fontSize == 0 ? 10 : this.fontSize;
            editor.Zoom = this.zoomLevel == 0 ? -2 : this.zoomLevel; // default zoom level
            editor.StartStyling(0);
            editor.SetStyling(editor.Text.Length, BoopStyle.Default); // Clear

            var scintilla_TextChanged = new System.EventHandler((sender, e) =>
            {
                ApplyHighlighting(editor.Text);
            });

            ApplyHighlighting(editor.Text);

            editor.TextChanged += scintilla_TextChanged;
            // Setup line numbers
            editor.Margins[0].Width = 20;

            // Set C++/C# style highlighting
            editor.Lexer = Lexer.Container;
            editor.Styles[BoopStyle.Default].ForeColor = Color.Black;
            editor.Styles[BoopStyle.Comment].ForeColor = Color.Green;
            editor.Styles[BoopStyle.Keyword].ForeColor = Color.Blue;
            editor.Styles[BoopStyle.String].ForeColor = Color.Brown;
            editor.Styles[BoopStyle.Extra].ForeColor = Color.Red;
            editor.Styles[BoopStyle.Number].ForeColor = Color.Purple;
            editor.Styles[BoopStyle.Attribute].ForeColor = Color.DarkCyan;


            if (isDarkMode) ApplyDarkTheme(); else ApplyLightTheme();

            editor.SetKeywords(0, "int string void if else return class using namespace public static");

            // configureFolding();

        }

        private void InitializeMenu()
        {
            menuStrip = new MenuStrip();

            // View Menu
            // viewMenu = new ToolStripMenuItem("View");
            // alwaysOnTopItem = new ToolStripMenuItem("Always on Top") { CheckOnClick = true };
            // alwaysOnTopItem.CheckedChanged += (s, e) => this.TopMost = alwaysOnTopItem.Checked;
            // viewMenu.DropDownItems.Add(alwaysOnTopItem);

            // Transform Menu
            transformMenu = new ToolStripMenuItem("Transform");
            var uppercaseItem = new ToolStripMenuItem("Uppercase");
            uppercaseItem.Click += (s, e) => editor.Text = editor.Text.ToUpper();

            var jsonPrettyItem = new ToolStripMenuItem("Pretty-print JSON");
            jsonPrettyItem.Click += (s, e) =>
            {
                try
                {
                    var parsed = JsonConvert.DeserializeObject(editor.Text);
                    editor.Text = JsonConvert.SerializeObject(parsed, Formatting.Indented);
                }
                catch
                {
                    MessageBox.Show("Invalid JSON", "Error");
                }
            };

            string exeDir = AppContext.BaseDirectory;
            // Console.WriteLine("Exe dir: " + exeDir);
            foreach (var path in Directory.GetFiles(Path.Combine(exeDir, "Scripts"), "*.js"))
            {
                var script = BoopScript.LoadFromFile(path);

                var item = new ToolStripMenuItem(script.Name);
                item.ToolTipText = script.Description;
                item.Click += (s, e) =>
                {
                    try
                    {
                        var result = script.Apply(editor.Text, editor.SelectedText, (msg) => MessageBox.Show(msg, "Boop Script Error"));
                        if (result.selection != "")
                            editor.ReplaceSelection(result.selection);
                        else
                            editor.Text = result.fullText;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Script failed: " + ex.Message, "Error");
                    }
                };

                transformMenu.DropDownItems.Add(item);
            }

            transformMenu.DropDownItems.Add(uppercaseItem);
            transformMenu.DropDownItems.Add(jsonPrettyItem);

            // menuStrip.Items.Add(transformMenu);
            // menuStrip.Items.Add(viewMenu);


            string base64Png = @"
            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
            ";

            toolStrip = new ToolStrip();
            Image img = ImageLoader.LoadImageFromBase64(base64Png);
            // Create a toggleable button
            ToolStripButton darkModeButton = new ToolStripButton();
            darkModeButton.ImageScaling = ToolStripItemImageScaling.SizeToFit;
            darkModeButton.Image = ResizeImage(img, 32, 32);
            darkModeButton.AutoSize = false;
            darkModeButton.Size = new Size(36, 36);
            darkModeButton.CheckOnClick = true; // Enables toggle behavior
            darkModeButton.ToolTipText = "Toggle Dark Mode";
            darkModeButton.Click += (s, e) => ToggleTheme();

            // ➕ Zoom In button
            var zoomInBtn = new ToolStripButton("➕")
            {
                AutoSize = false,
                Size = new Size(36, 36),
                ToolTipText = "Zoom In",
                Font = new Font("Segoe UI", 14, FontStyle.Bold)
            };
            zoomInBtn.Click += (s, e) =>
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    this.zoomLevel = 0;
                    editor.Zoom = 0; // Reset zoom
                }
                else
                {
                    this.zoomLevel++;
                    editor.ZoomIn();
                }
            };
            toolStrip.Items.Add(zoomInBtn);

            // ➖ Zoom Out button
            var zoomOutBtn = new ToolStripButton("➖")
            {
                AutoSize = false,
                Size = new Size(36, 36),
                ToolTipText = "Zoom Out",
                Font = new Font("Segoe UI", 14, FontStyle.Bold)
            };
            zoomOutBtn.Click += (s, e) => {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    this.zoomLevel = 0;
                    editor.Zoom = 0; // Reset zoom
                }
                else
                {
                    this.zoomLevel--;
                    editor.ZoomOut();
                }
            };
            toolStrip.Items.Add(zoomOutBtn);


            toolStrip.ImageScalingSize = new Size(32, 32);

            toolStrip.Items.Add(darkModeButton);
            ToolStripDropDown fontSizeDropdown = new ToolStripDropDown();
            // toolStrip.Items.Add(new ToolStripDropDownButton("Options", null, (s, e) => fontSizeDropdown.Show(Cursor.Position)));
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip.Dock = DockStyle.Top;
            toolStrip.Padding = new Padding(2);
            toolStrip.RenderMode = ToolStripRenderMode.System;
            toolStrip.BackColor = SystemColors.Control;

            ContextMenuStrip menu = new ContextMenuStrip();

            // Font Size submenu
            ToolStripMenuItem fontSizeMenu = new ToolStripMenuItem("Font Size");
            int[] fontSizes = new int[] { 8, 10, 12, 14, 16, 18, 20, 24, 28, 32 };
            foreach (int size in fontSizes)
            {
                ToolStripMenuItem sizeItem = new ToolStripMenuItem(size.ToString());
                sizeItem.Click += (s, ev) => SetEditorFontSize(size);
                fontSizeMenu.DropDownItems.Add(sizeItem);
            }
            menu.Items.Add(fontSizeMenu);

            // Word Wrap toggle
            ToolStripMenuItem wordWrapItem = new ToolStripMenuItem("Word Wrap");
            wordWrapItem.Checked = editor.WrapMode != WrapMode.None;
            wordWrapItem.Click += (s, ev) =>
            {
                if (editor.WrapMode == WrapMode.None)
                    editor.WrapMode = WrapMode.Word;
                else
                    editor.WrapMode = WrapMode.None;
                wordWrapItem.Checked = editor.WrapMode != WrapMode.None;
            };
            menu.Items.Add(wordWrapItem);

            // Always on Top toggle
            ToolStripMenuItem alwaysOnTopItem = new ToolStripMenuItem("Always on Top");
            alwaysOnTopItem.Checked = this.TopMost;
            alwaysOnTopItem.Click += (s, ev) =>           {
                this.TopMost = !this.TopMost;
                alwaysOnTopItem.Checked = this.TopMost;           };
            menu.Items.Add(alwaysOnTopItem);            // Show Line Numbers toggle
            ToolStripMenuItem lineNumbersItem = new ToolStripMenuItem("Show Line Numbers");
            lineNumbersItem.Checked = editor.Margins[0].Width > 0;
            lineNumbersItem.Click += (s, ev) =>
            {
                if (editor.Margins[0].Width > 0)
                    editor.Margins[0].Width = 0;
                else
                    editor.Margins[0].Width = 40; // or any width you prefer
                lineNumbersItem.Checked = editor.Margins[0].Width > 0;
            };
            menu.Items.Add(lineNumbersItem);

            // Show/Hide Toolbar toggle
            ToolStripMenuItem toolbarItem = new ToolStripMenuItem("Show Toolbar");
            toolbarItem.Checked = toolStrip.Visible;
            toolbarItem.Click += (s, ev) =>
            {
                toolStrip.Visible = !toolStrip.Visible;
                toolbarItem.Checked = toolStrip.Visible;
            };
            menu.Items.Add(toolbarItem);

            menuStrip.Items.Add(transformMenu);
            // menuStrip.Items.Add(viewMenu);
            menuStrip.Items.Add(new ToolStripDropDownButton("Options", null, (s, e) => menu.Show(Cursor.Position)));

        }
        
        private void SetEditorFontSize(int size)
        {
            Console.WriteLine($"Setting font size to {size}");
            this.fontSize = size;
            InitializeEditor2(true); // Reapply theme to update font color
        }



        private void SaveState()
        {
            Rectangle restore = this.WindowState == FormWindowState.Normal
                ? this.Bounds
                : this.RestoreBounds;
            var state = new BoopState
            {
                EditorText = CryptoHelper.Encrypt(editor.Text),
                SelectionStart = editor.SelectionStart,
                SelectionLength = editor.SelectionEnd - editor.SelectionStart,
                WindowWidth = restore.Width,
                WindowHeight = restore.Height,
                ScrollPosition = editor.FirstVisibleLine, // Scintilla-specific
                WindowLeft = restore.Left,      // <- screen X
                WindowTop = restore.Top,         // <- screen Y
                WindowWasMaximized = this.WindowState == FormWindowState.Maximized,
                FontSize = this.fontSize,
                ZoomLevel = this.zoomLevel
            };
            if (Utils.Debug) Utils.DumpPrint(state);
            var path = GetStateFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(state, Formatting.Indented));
        }

        private Rectangle InsetRectangle(Rectangle rect, int margin)
        {
            return new Rectangle(
                rect.X + margin,
                rect.Y + margin,
                rect.Width - 2 * margin,
                rect.Height - 2 * margin
            );
        }
        private void LoadState()
        {


            var path = GetStateFilePath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var state = JsonConvert.DeserializeObject<BoopState>(json);

            editor.Text = CryptoHelper.Decrypt(state.EditorText);
            editor.SelectionStart = state.SelectionStart;
            editor.SelectionEnd = state.SelectionStart + state.SelectionLength;
            this.Width = state.WindowWidth;
            this.Height = state.WindowHeight;
            this.fontSize = state.FontSize > 0 ? state.FontSize : this.fontSize;
            this.zoomLevel = state.ZoomLevel;
            if (state.WindowWasMaximized)
                this.WindowState = FormWindowState.Maximized;
            else
            {

                // Get screen bounds that the saved top-left position is on
                var screenBounds = Screen.GetWorkingArea(new Point(state.WindowLeft, state.WindowTop));
                if (Utils.Debug) Utils.DumpPrint(screenBounds, "Screen: \n");

                var safeBounds = InsetRectangle(screenBounds, -16);
                if (Utils.Debug) Utils.DumpPrint(safeBounds, "Safe: \n");
                // Restore window position
                this.StartPosition = FormStartPosition.Manual;


                var savedRect = new Rectangle(
                    state.WindowLeft,
                    state.WindowTop,
                    state.WindowWidth,
                    state.WindowHeight
                );

                if (Utils.Debug) Utils.DumpPrint(savedRect, "Saved: \n");
                if (safeBounds.Contains(savedRect))
                {
                    if (Utils.Debug) Console.WriteLine("Restoring window position");
                    this.Left = state.WindowLeft;
                    this.Top = state.WindowTop;
                }
                else
                {
                    if (Utils.Debug) Console.WriteLine("Saved position was offscreen or invalid");
                    this.StartPosition = FormStartPosition.WindowsDefaultLocation;
                }
            }
            // Scroll caret into view
            editor.ScrollCaret();
            int lineToScroll = Math.Min(state.ScrollPosition, editor.Lines.Count - 1);
            editor.Lines[lineToScroll].Goto();
            InitializeEditor2(true); // Reapply theme to update font size
            if (Utils.Debug) Console.WriteLine($"Restored position: Left={this.Left}, Top={this.Top}");
        }

        private string GetStateFilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniBoop");
            return Path.Combine(dir, "boopstate.json");
        }

        private void configureFolding() //work in progress
        {
            editor.SetProperty("fold", "1");
            editor.SetProperty("fold.compact", "1");

            // Configure a margin to display folding symbols
            editor.Margins[2].Type = MarginType.Symbol;
            editor.Margins[2].Mask = Marker.MaskFolders;
            editor.Margins[2].Sensitive = true;
            editor.Margins[2].Width = 20;


            // Configure folding markers with respective symbols
            editor.Markers[Marker.Folder].Symbol = MarkerSymbol.BoxPlus;
            editor.Markers[Marker.FolderOpen].Symbol = MarkerSymbol.BoxMinus;
            editor.Markers[Marker.FolderEnd].Symbol = MarkerSymbol.BoxPlusConnected;
            editor.Markers[Marker.FolderMidTail].Symbol = MarkerSymbol.TCorner;
            editor.Markers[Marker.FolderOpenMid].Symbol = MarkerSymbol.BoxMinusConnected;
            editor.Markers[Marker.FolderSub].Symbol = MarkerSymbol.VLine;
            editor.Markers[Marker.FolderTail].Symbol = MarkerSymbol.LCorner;

            // Enable automatic folding
            editor.AutomaticFold = AutomaticFold.Show | AutomaticFold.Click | AutomaticFold.Change;
        }

        private void InitializeContextMenu()
        {
            var contextMenu = new ContextMenuStrip();

            contextMenu.Opening += (s, e) =>
            {
                contextMenu.Items.Clear();

                contextMenu.Items.Add("Undo", null, (s1, e1) => editor.Undo())
                    .Enabled = editor.CanUndo;

                contextMenu.Items.Add("Redo", null, (s1, e1) => editor.Redo())
                    .Enabled = editor.CanRedo;

                contextMenu.Items.Add(new ToolStripSeparator());

                contextMenu.Items.Add("Cut", null, (s1, e1) => editor.Cut())
                    .Enabled = editor.SelectedText.Length > 0;

                contextMenu.Items.Add("Copy", null, (s1, e1) => editor.Copy())
                    .Enabled = editor.SelectedText.Length > 0;

                contextMenu.Items.Add("Paste", null, (s1, e1) => editor.Paste())
                    .Enabled = Clipboard.ContainsText();

                contextMenu.Items.Add("Delete", null, (s1, e1) => editor.Clear())
                    .Enabled = editor.SelectedText.Length > 0;

                contextMenu.Items.Add(new ToolStripSeparator());

                contextMenu.Items.Add("Select All", null, (s1, e1) => editor.SelectAll());

                contextMenu.Items.Add(new ToolStripSeparator());

                contextMenu.Items.Add("🌗 Toggle Dark Mode", null, (s1, e1) => ToggleTheme());
            };

            // Attach it
            editor.UsePopup(false);
            editor.ContextMenuStrip = contextMenu;
        }
    }
}