using System;
using System.Windows.Forms;

namespace MCPForAlibre.AddOn
{
    // Runs work on Alibre's UI thread, where its COM objects live.
    // Created in AddOnLoad, which Alibre calls on that thread.
    sealed class UiThread
    {
        readonly Control m_control;

        public UiThread()
        {
            m_control = new Control();
            m_control.CreateControl();
            IntPtr unused = m_control.Handle; // forces the window to exist on this thread
        }

        // Runs func on the UI thread and waits up to timeout for it.
        // If Alibre is busy (a modal dialog, a long regenerate) the call stays queued and runs later.
        public object Run(Func<object> func, TimeSpan timeout)
        {
            object result = null;
            Exception error = null;
            IAsyncResult pending = m_control.BeginInvoke((MethodInvoker)(() =>
            {
                try { result = func(); }
                catch (Exception ex) { error = ex; }
            }));
            if (!pending.AsyncWaitHandle.WaitOne(timeout))
                throw new TimeoutException("Alibre didn't answer within " + timeout.TotalSeconds +
                                           " s. It may be showing a dialog or busy; the request will still run when it's free.");
            m_control.EndInvoke(pending);
            if (error != null)
                throw error;
            return result;
        }
    }
}
