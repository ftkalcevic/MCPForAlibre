using System;
using System.Windows.Forms;
using AlibreAddOn;

namespace MCPForAlibre.AddOn
{
    // The add-on's menu: MCPForAlibre > Status, which shows the pipe and the log's location.
    sealed class AddOnMenu : IAlibreAddOn
    {
        const int kRoot = 1;
        const int kStatus = 2;

        readonly PipeServer m_server;

        public AddOnMenu(PipeServer server)
        {
            m_server = server;
        }

        public int RootMenuItem => kRoot;
        public bool HasSubMenus(int menuID) => menuID == kRoot;
        public Array SubMenuItems(int menuID) => menuID == kRoot ? new[] { kStatus } : new int[0];
        public string MenuItemText(int menuID) => menuID == kRoot ? "MCPForAlibre" : "Status";
        public bool PopupMenu(int menuID) => false;
        public ADDONMenuStates MenuItemState(int menuID, string sessionIdentifier) => ADDONMenuStates.ADDON_MENU_ENABLED;
        public string MenuItemToolTip(int menuID) => menuID == kStatus ? "Show whether MCP clients can reach Alibre" : "";
        public string MenuIcon(int menuID) => null;
        public bool UseDedicatedRibbonTab() => false;

        public IAlibreAddOnCommand InvokeCommand(int menuID, string sessionIdentifier)
        {
            if (menuID == kStatus)
                MessageBox.Show(m_server.StatusText, "MCPForAlibre", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        public bool HasPersistentDataToSave(string sessionIdentifier) => false;
        public void LoadData(IStream pCustomData, string sessionIdentifier) { }
        public void SaveData(IStream pCustomData, string sessionIdentifier) { }
        public void setIsAddOnLicensed(bool isLicensed) { }
    }
}
