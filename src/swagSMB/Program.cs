using System;
using System.Threading;
using System.Windows.Forms;
using swagSMB.Models;
using swagSMB.Storage;
using swagSMB.UI;

namespace swagSMB
{
    internal static class Program
    {
        private static SynchronizationContext s_uiSync;

        [STAThread]
        private static void Main()
        {
            Application.ThreadException += ApplicationThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            s_uiSync = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(s_uiSync);

            var store = new AppConfigStore();

            if (TryAutoTrayLaunch(store, out SessionContext autoSession))
            {
                Application.Run(new MainForm(store, autoSession, launchedToTray: true));
                return;
            }

            using (var unlockForm = new UnlockForm(store))
            {
                if (unlockForm.ShowDialog() != DialogResult.OK || unlockForm.SessionContext == null)
                {
                    return;
                }

                Application.Run(new MainForm(store, unlockForm.SessionContext));
            }
        }

        private static bool TryAutoTrayLaunch(AppConfigStore store, out SessionContext session)
        {
            session = null;

            if (!store.ConfigExists())
            {
                return false;
            }

            TrayStartupFlags flags = store.LoadTrayFlags();
            if (flags == null
                || flags.RequireMasterPasswordWhenStartingToTray
                || !flags.StartMinimizedToTray
                || !flags.AutoTrayConsented)
            {
                return false;
            }

            if (!store.TryLoadProtectedMasterPassword(out string masterPassword))
            {
                return false;
            }

            try
            {
                AppConfig config = store.Load(masterPassword);

                if (config?.Server == null
                    || !config.Server.StartMinimizedToTray
                    || config.Server.RequireMasterPasswordWhenStartingToTray
                    || !config.Server.AutoTrayConsented)
                {
                    return false;
                }

                session = new SessionContext
                {
                    MasterPassword = masterPassword,
                    Config = config
                };
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                masterPassword = null;
            }
        }

        private static void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleUnhandledException(e.Exception);
        }

        private static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                HandleUnhandledException(exception);
            }
        }

        private static void HandleUnhandledException(Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("[Unhandled] " + exception);

            void ShowDialog()
            {
                try
                {
                    MessageBox.Show(
                        "swagSMB hit an unexpected error and may need to be restarted.",
                        "swagSMB",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch
                {
                }
            }

            SynchronizationContext sync = s_uiSync;
            if (sync != null && sync != SynchronizationContext.Current)
            {
                try
                {
                    sync.Post(_ => ShowDialog(), null);
                    return;
                }
                catch
                {
                }
            }

            ShowDialog();
        }
    }
}
