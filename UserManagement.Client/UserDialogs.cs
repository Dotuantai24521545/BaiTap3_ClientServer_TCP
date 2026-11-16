using System.Windows.Forms;

namespace QuanLiNguoiDung
{
    // Instance-scoped dialog service: normal application still displays MessageBox.
    // UI verification can record notices without unattended modal dialogs blocking its message loop.
    public interface IUserDialogs
    {
        void Show(IWin32Window owner, string message, string caption);
    }
    public sealed class MessageBoxDialogs : IUserDialogs
    {
        public void Show(IWin32Window owner, string message, string caption)
        { MessageBox.Show(owner, message, caption); }
    }
}
