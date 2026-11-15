using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Forms;

namespace SnapLite;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}

internal sealed class TrayAppContext : ApplicationContext
{
    private const int HotkeyId = 0x534E;
    private const uint VkSnapshot = 0x2C;
    private readonly NotifyIcon tray;
    private readonly HotkeyWindow hotkeyWindow;
    private readonly string saveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapLite");

    public TrayAppContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capturar pantalla  (Impr Pant)", null, (_, _) => Capture());
        menu.Items.Add("Abrir carpeta de capturas", null, (_, _) => OpenFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Exit());
        tray = new NotifyIcon { Text = "SnapLite - Capturas rápidas", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Capture();
        hotkeyWindow = new HotkeyWindow(HotkeyId, Capture);
        if (!Native.RegisterHotKey(hotkeyWindow.Handle, HotkeyId, 0, VkSnapshot))
            tray.ShowBalloonTip(5000, "SnapLite", "No se pudo reservar Impr Pant. Comprueba si otra aplicación o Windows está usando esa tecla.", ToolTipIcon.Warning);
        else
            tray.ShowBalloonTip(2500, "SnapLite", "Activo. Pulsa Impr Pant para recortar.", ToolTipIcon.Info);
    }

    private void Capture()
    {
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(full)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            using var overlay = new SelectionForm(full, bounds.Location);
            if (overlay.ShowDialog() != DialogResult.OK || overlay.Selection.Width < 2 || overlay.Selection.Height < 2) return;
            using var crop = new Bitmap(overlay.Selection.Width, overlay.Selection.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(crop)) g.DrawImage(full, new Rectangle(0, 0, crop.Width, crop.Height), overlay.Selection, GraphicsUnit.Pixel);
            using var preview = new PreviewForm(crop, saveFolder);
            preview.ShowDialog();
        }
        catch (Exception ex) { tray.ShowBalloonTip(3500, "SnapLite", "No se pudo realizar la captura: " + ex.Message, ToolTipIcon.Error); }
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(saveFolder);
        Process.Start(new ProcessStartInfo(saveFolder) { UseShellExecute = true });
    }
    private void Exit()
    {
        Native.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
        hotkeyWindow.Dispose(); tray.Visible = false; tray.Dispose(); ExitThread();
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Action callback;
    public HotkeyWindow(int id, Action callback) { this.callback = callback; CreateHandle(new CreateParams { Caption = "SnapLiteHotkeyWindow" }); }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x0312) callback(); base.WndProc(ref m); }
    public void Dispose() => DestroyHandle();
}

internal sealed class SelectionForm : Form
{
    private readonly Bitmap screenshot;
    private Point start, current;
    private bool dragging;
    public Rectangle Selection { get; private set; }
    public SelectionForm(Bitmap screenshot, Point virtualOrigin)
    {
        this.screenshot = screenshot; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(virtualOrigin, screenshot.Size); ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
        Cursor = Cursors.Cross; KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        MouseDown += OnMouseDown; MouseMove += OnMouseMove; MouseUp += OnMouseUp;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(screenshot, 0, 0);
        using (var shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0))) e.Graphics.FillRectangle(shade, ClientRectangle);
        var rect = MakeRect(start, current);
        if (dragging || rect.Width > 0)
        {
            e.Graphics.DrawImage(screenshot, rect, rect, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(167, 139, 250), 2); e.Graphics.DrawRectangle(pen, rect);
            using var fill = new SolidBrush(Color.FromArgb(25, 255, 255, 255)); e.Graphics.FillRectangle(fill, rect);
            e.Graphics.DrawString($"{rect.Width} × {rect.Height}", Font, Brushes.White, rect.Left, Math.Max(0, rect.Top - 24));
        }
        e.Graphics.DrawString("Arrastra para recortar  •  ESC para cancelar", Font, Brushes.White, 16, 16);
    }
    private void OnMouseDown(object? s, MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; start = current = e.Location; dragging = true; Invalidate(); }
    private void OnMouseMove(object? s, MouseEventArgs e) { if (!dragging) return; current = e.Location; Invalidate(); }
    private void OnMouseUp(object? s, MouseEventArgs e)
    {
        if (!dragging) return; current = e.Location; dragging = false; Selection = MakeRect(start, current);
        DialogResult = Selection.Width > 1 && Selection.Height > 1 ? DialogResult.OK : DialogResult.Cancel; Close();
    }
    private static Rectangle MakeRect(Point a, Point b) => Rectangle.FromLTRB(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y), Math.Max(a.X,b.X), Math.Max(a.Y,b.Y));
}

internal sealed class PreviewForm : Form
{
    private readonly Bitmap image;
    private readonly string folder;
    public PreviewForm(Bitmap image, string folder)
    {
        this.image = (Bitmap)image.Clone(); this.folder = folder;
        Text = "SnapLite"; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true; BackColor = Color.FromArgb(35, 35, 42);
        var maxW = 760; var maxH = 480; var scale = Math.Min(1.0, Math.Min((double)maxW / image.Width, (double)maxH / image.Height));
        var preview = new PictureBox { Image = this.image, SizeMode = PictureBoxSizeMode.Zoom, Width = Math.Max(220, (int)(image.Width * scale)), Height = Math.Max(140, (int)(image.Height * scale)), Left = 16, Top = 16, BackColor = Color.FromArgb(48,48,58) };
        var copy = MakeButton("Copiar", 16, (_, _) => { try { Clipboard.SetImage((Image)this.image.Clone()); Close(); } catch (Exception ex) { MessageBox.Show("No se pudo copiar: " + ex.Message, "SnapLite"); } });
        var save = MakeButton("Guardar", 132, (_, _) => SaveImage());
        var cancel = MakeButton("Cancelar", 248, (_, _) => Close());
        Controls.Add(preview); Controls.Add(copy); Controls.Add(save); Controls.Add(cancel);
        ClientSize = new Size(preview.Width + 32, preview.Height + 76); copy.Top = save.Top = cancel.Top = preview.Bottom + 14;
        FormClosed += (_, _) => this.image.Dispose();
        KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
    private Button MakeButton(string text, int x, EventHandler click)
    {
        var b = new Button { Text = text, Left = x, Width = 104, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = text == "Copiar" ? Color.FromArgb(118,84,206) : Color.FromArgb(61,61,72), ForeColor = Color.White };
        b.FlatAppearance.BorderSize = 0; b.Click += click; return b;
    }
    private void SaveImage()
    {
        using var dialog = new SaveFileDialog { Filter = "Imagen PNG|*.png", FileName = $"SnapLite_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png", InitialDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try { image.Save(dialog.FileName, ImageFormat.Png); Close(); }
        catch (Exception ex) { MessageBox.Show("No se pudo guardar: " + ex.Message, "SnapLite"); }
    }
}

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
