using System;
using AlibreAddOn;
using AlibreX;

// Alibre looks for this exact namespace and class in a managed add-on
// (the same shape as Alibre Script's AlibreScriptAddOn.dll).
namespace AlibreAddOnAssembly
{
    public static class AlibreAddOn
    {
        static MCPForAlibre.AddOn.AddOnMenu s_menu;
        static MCPForAlibre.AddOn.PipeServer s_server;

        public static void AddOnLoad(IntPtr hwnd, IAutomationHook pAutomationHook, IntPtr unused)
        {
            MCPForAlibre.AddOn.Log.Write("load", "AddOnLoad");
            try
            {
                var root = (IADRoot)pAutomationHook.Root;
                var ui = new MCPForAlibre.AddOn.UiThread();
                var commands = new MCPForAlibre.AddOn.Commands(root);
                s_server = new MCPForAlibre.AddOn.PipeServer(ui, commands);
                s_server.Start();
                s_menu = new MCPForAlibre.AddOn.AddOnMenu(s_server);
            }
            catch (Exception ex)
            {
                MCPForAlibre.AddOn.Log.Write("load", "failed: " + ex);
            }
        }

        public static void AddOnInvoke(IntPtr hwnd, IntPtr pAutomationHook, string sessionName,
                                       bool isLicensed, int reserved1, int reserved2)
        {
        }

        public static void AddOnUnload(IntPtr hwnd, bool forceUnload, ref bool cancel, int reserved1, int reserved2)
        {
            MCPForAlibre.AddOn.Log.Write("load", "AddOnUnload");
            s_server?.Stop();
            s_server = null;
        }

        public static IAlibreAddOn GetAddOnInterface()
        {
            return s_menu;
        }
    }
}
