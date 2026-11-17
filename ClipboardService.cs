using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Ostrovok.Services;

public enum ClipKind { Text, Image, Files }

public class ClipItem
{
    public ClipKind Kind;
    public string Text = "";
    public BitmapSource Image;
    public string[] Files;
    public DateTime Time = DateTime.Now;
    public string Preview => Kind switch
    {
        ClipKind.Text => Text.Length > 120 ? Text[..120] + "…" : Text,
        ClipKind.Image => "🖼 Изображение",
        _ => "📁 " + string.Join(", ", Files.Select(System.IO.Path.GetFileName))
    };
}

/// <summary>История буфера: только в памяти, до 30 записей. Пароли из менеджеров не сохраняются.</summary>
public class ClipboardService
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    public List<ClipItem> Items { get; } = new();
    public event Action Changed;
    public bool Paused;
    IntPtr _hwnd;

    public void Attach(HwndSource src)
    {
        _hwnd = src.Handle;
        AddClipboardFormatListener(_hwnd);
        src.AddHook(Hook);
    }

    public void Detach() { if (_hwnd != IntPtr.Zero) RemoveClipboardFormatListener(_hwnd); }

    IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) OnClip();
        return IntPtr.Zero;
    }

    async void OnClip()
    {
        if (Paused) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // менеджеры паролей помечают данные этим форматом
                if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return;
                ClipItem item = null;
                if (Clipboard.ContainsFileDropList())
                {
                    var f = Clipboard.GetFileDropList().Cast<string>().ToArray();
                    item = new ClipItem { Kind = ClipKind.Files, Files = f };
                }
                else if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null) { img.Freeze(); item = new ClipItem { Kind = ClipKind.Image, Image = img }; }
                }
                else if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        if (Items.Count > 0 && Items[0].Kind == ClipKind.Text && Items[0].Text == t) return;
                        item = new ClipItem { Kind = ClipKind.Text, Text = t };
                    }
                }
                if (item == null) return;
                Items.Insert(0, item);
                while (Items.Count > 30) Items.RemoveAt(Items.Count - 1);
                Changed?.Invoke();
                return;
            }
            catch (COMException) { await Task.Delay(80); } // буфер занят другим процессом
            catch (Exception ex) { AppSettings.Log("clip: " + ex.Message); return; }
        }
    }

    public void Put(ClipItem it)
    {
        try
        {
            Paused = true;
            switch (it.Kind)
            {
                case ClipKind.Text: Clipboard.SetText(it.Text); break;
                case ClipKind.Image: Clipboard.SetImage(it.Image); break;
                case ClipKind.Files:
                    var c = new System.Collections.Specialized.StringCollection();
                    c.AddRange(it.Files); Clipboard.SetFileDropList(c); break;
            }
        }
        catch (Exception ex) { AppSettings.Log("clip put: " + ex.Message); }
        finally { Task.Delay(300).ContinueWith(_ => Paused = false); }
    }

    public void Clear() { Items.Clear(); Changed?.Invoke(); }
}
