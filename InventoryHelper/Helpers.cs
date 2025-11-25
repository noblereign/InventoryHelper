using FrooxEngine;

namespace InventoryHelper
{
    public static class Helpers
    {
        public static bool IsFolder(this BrowserItem yeah)
        {
            return !string.IsNullOrWhiteSpace(yeah.Button.LabelText);
        }
    }
}