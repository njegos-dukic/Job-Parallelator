using System;
using System.Threading;
using System.IO;

using Windows.Storage;
using Windows.UI.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;

namespace Parallelator
{
    public class BackgroundNotification
    {
        private static readonly SemaphoreSlim notificationSemaphore = new SemaphoreSlim(1);

        public async static Task SendNotification()
        {
            // if (MainPage.GetQueuedFiles().Count > 0 || MainPage.GetTaskedFiles().Count > 0)
            {
                await notificationSemaphore.WaitAsync();
                try
                {
                    var content = new ToastContentBuilder()
                        .AddToastActivationInfo("toast", ToastActivationType.Foreground)
                        .AddText("You have unfinished tasks.")
                        .AddText("Images are waiting for you to drain them from any color and cheer!")
                        .GetToastContent();

                    var notif = new ToastNotification(content.GetXml());

                    ToastNotificationManager.CreateToastNotifier().Show(notif);
                }

                catch { }

                finally
                {
                    notificationSemaphore.Release();
                }
            }
        }
    }
}
