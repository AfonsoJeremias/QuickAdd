using Playnite.SDK;
using System;

namespace FullscreenKit
{
    internal static class Notifier
    {
        public static void Send(IPlayniteAPI api, string text, NotificationType type)
        {
            if (api == null) return;
            Action add = () => api.Notifications.Add(new NotificationMessage("QuickAdd_" + Guid.NewGuid().ToString("N"), text, type));
            try
            {
                var d = api.MainView.UIDispatcher;
                if (d != null) d.BeginInvoke(add); else add();
            }
            catch { try { add(); } catch { } }
        }
    }
}
