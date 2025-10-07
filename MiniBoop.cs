using System;
using System.Drawing;
using System.Windows.Forms;
using ScintillaNET;
using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;


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
    }



    public class MainForm : Form
    {
        private Scintilla editor;
        private MenuStrip menuStrip;
        private ToolStripMenuItem transformMenu;
        private ToolStripMenuItem viewMenu;
        private ToolStripMenuItem alwaysOnTopItem;

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

            InitializeEditor2();
            InitializeMenu();

            this.Controls.Add(editor);
            this.Controls.Add(menuStrip);
            this.MainMenuStrip = menuStrip;
            this.Load += (s, e) => LoadState();
            this.FormClosing += (s, e) => SaveState();
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
        // private void scintilla_TextChanged(object sender, EventArgs e)
        // {
        //     ApplyHighlighting(scintilla.Text);
        // }
        private void InitializeEditor2()
        {
            editor = new Scintilla
            {
                Dock = DockStyle.Fill
            };
            var patterns = new List<(Regex, int)>
            {
                (new Regex(@"//.*"), BoopStyle.Comment),
                (new Regex(@"""(?:\\.|[^""\\])*"""), BoopStyle.String),
                (new Regex(@"\b(var|let|func|class)\b", RegexOptions.IgnoreCase), BoopStyle.Attribute),
                (new Regex(@"\b(true|false|null|from|to)\b", RegexOptions.IgnoreCase), BoopStyle.Keyword),
                (new Regex(@"\b\d+(\.\d+)?\b"), BoopStyle.Number),
                (new Regex(@"""[^""]+?""(?=\s*:)", RegexOptions.IgnoreCase), BoopStyle.Extra),
            };

            void ApplyHighlighting(string text)
            {
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

            var scintilla_TextChanged = new System.EventHandler((sender, e) =>
            {
                ApplyHighlighting(editor.Text);
            });

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

            editor.SetKeywords(0, "int string void if else return class using namespace public static");

        }

        private void InitializeMenu()
        {
            menuStrip = new MenuStrip();

            // View Menu
            viewMenu = new ToolStripMenuItem("View");
            alwaysOnTopItem = new ToolStripMenuItem("Always on Top") { CheckOnClick = true };
            alwaysOnTopItem.CheckedChanged += (s, e) => this.TopMost = alwaysOnTopItem.Checked;
            viewMenu.DropDownItems.Add(alwaysOnTopItem);

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

            foreach (var path in Directory.GetFiles("Scripts", "*.js"))
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

            menuStrip.Items.Add(transformMenu);
            menuStrip.Items.Add(viewMenu);
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
                WindowWasMaximized = this.WindowState == FormWindowState.Maximized
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

            if (Utils.Debug) Console.WriteLine($"Restored position: Left={this.Left}, Top={this.Top}");
        }

        private string GetStateFilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniBoop");
            return Path.Combine(dir, "boopstate.json");
        }
    }
}