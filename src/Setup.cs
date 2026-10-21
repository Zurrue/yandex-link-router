using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// Per-user installer for the Yandex Music link router.
// No admin rights, no questions: copies the router, registers it as a
// browser candidate and opens Windows "Default apps".
internal static class Setup
{
    private const string AppName = "Yandex Link Router";
    private const string Version = "1.0.0";
    private const string CfgKey = @"Software\YandexLinkRouter";
    private const string ClientKey = @"Software\Clients\StartMenuInternet\YMRouter";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\YandexLinkRouter";

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            bool silent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase));

            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstall(silent);
                return;
            }

            Install(silent);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось завершить установку.\r\n\r\n" + ex.Message,
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void Install(bool silent)
    {
        string dir = InstallDir();
        Directory.CreateDirectory(dir);

        string router = Path.Combine(dir, "ymrouter.exe");
        ExtractPayload(router);

        string self = Process.GetCurrentProcess().MainModule.FileName;
        string uninstaller = Path.Combine(dir, "uninstall.exe");
        if (!string.Equals(self, uninstaller, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, uninstaller, true);
        }

        // Browser detection order:
        // 1. current default browser (if it is not us) - covers reinstall after switching;
        // 2. previously saved browser - reinstall while we are default;
        // 3. any known browser on the machine.
        string browserExe, browserArgs;
        TryUserChoice(out browserExe, out browserArgs);

        if (string.IsNullOrEmpty(browserExe) || !File.Exists(browserExe))
        {
            browserExe = ReadCfg("BrowserExe");
            browserArgs = ReadCfg("BrowserArgs");
        }

        if (string.IsNullOrEmpty(browserExe) || !File.Exists(browserExe) || IsRouter(browserExe))
        {
            FindAnyBrowser(out browserExe, out browserArgs);
        }

        if (string.IsNullOrEmpty(browserExe) || !File.Exists(browserExe))
        {
            throw new Exception(
                "Не найден браузер, в который можно передавать обычные ссылки.\r\n" +
                "Установи Firefox, Chrome, Edge или Яндекс Браузер и запусти установщик снова.");
        }

        WriteCfg(browserExe, browserArgs, dir);
        Register(dir, FindMusicApp());

        Application.EnableVisualStyles();

        string app = FindMusicApp();
        string text =
            "Готово. Маршрутизатор установлен.\r\n\r\n" +
            "Сейчас откроются Параметры. Выбери:\r\n" +
            "Приложения → Приложения по умолчанию → Веб-браузер → «Yandex Link Router».\r\n\r\n" +
            "После этого ссылки на треки и альбомы Яндекс Музыки будут открываться\r\n" +
            "в приложении, все остальные — в браузере.";

        if (app == null)
        {
            text += "\r\n\r\nВнимание: приложение Яндекс Музыки не найдено.\r\n" +
                    "Пока оно не установлено, музыкальные ссылки будут открываться в браузере.";
        }

        if (!silent)
        {
            MessageBox.Show(text, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start("ms-settings:defaultapps");
        }
    }

    private static void Uninstall(bool silent)
    {
        RemoveKey(@"Software\Classes\YMRouter.HTTP");
        RemoveKey(@"Software\Classes\YMRouter.HTTPS");
        RemoveKey(ClientKey);
        RemoveKey(CfgKey);
        RemoveKey(UninstallKey);

        try
        {
            using (RegistryKey ra = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
            {
                if (ra != null && ra.GetValue(AppName) != null) ra.DeleteValue(AppName, false);
            }
        }
        catch
        {
            // not critical
        }

        string dir = InstallDir();
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YandexLinkRouter");

        ScheduleDelete(dir, logDir);

        if (!silent)
        {
            MessageBox.Show(
                "Yandex Link Router удалён.\r\n\r\n" +
                "Если он был браузером по умолчанию, выбери другой браузер\r\n" +
                "в открывшихся Параметрах.",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Process.Start("ms-settings:defaultapps");
        }
    }

    private static void ScheduleDelete(string dir, string logDir)
    {
        try
        {
            string script = Path.Combine(
                Path.GetTempPath(),
                "ymrouter-uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");

            string body =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                (Directory.Exists(dir) ? "rmdir /s /q \"" + dir + "\"\r\n" : "") +
                (Directory.Exists(logDir) ? "rmdir /s /q \"" + logDir + "\"\r\n" : "") +
                "del /f /q \"%~f0\"\r\n";

            File.WriteAllText(script, body, Encoding.ASCII);

            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // the registry is already clean; leftovers can be removed by hand
        }
    }

    private static void Register(string dir, string appExe)
    {
        string exe = Path.Combine(dir, "ymrouter.exe");
        string icon = (appExe ?? exe) + ",0";

        foreach (string prog in new[] { "YMRouter.HTTP", "YMRouter.HTTPS" })
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + prog))
            {
                k.SetValue(null, AppName);
                k.SetValue("URL Protocol", "");
            }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + prog + @"\DefaultIcon"))
            {
                k.SetValue(null, icon);
            }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + prog + @"\shell\open\command"))
            {
                k.SetValue(null, "\"" + exe + "\" \"%1\"");
            }
        }

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ClientKey))
        {
            k.SetValue(null, AppName);
        }

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ClientKey + @"\DefaultIcon"))
        {
            k.SetValue(null, icon);
        }

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ClientKey + @"\shell\open\command"))
        {
            k.SetValue(null, "\"" + exe + "\"");
        }

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ClientKey + @"\InstallInfo"))
        {
            k.SetValue("IconsVisible", 1, RegistryValueKind.DWord);
            k.SetValue("ReinstallCommand", "\"" + exe + "\"");
        }

        using (RegistryKey cap = Registry.CurrentUser.CreateSubKey(ClientKey + @"\Capabilities"))
        {
            cap.SetValue(null, AppName);
            cap.SetValue("ApplicationName", AppName);
            cap.SetValue("ApplicationDescription", "Yandex Music links -> desktop app, everything else -> browser");
            cap.SetValue("ApplicationIcon", icon);

            using (RegistryKey ua = cap.CreateSubKey("URLAssociations"))
            {
                ua.SetValue("http", "YMRouter.HTTP");
                ua.SetValue("https", "YMRouter.HTTPS");
            }
        }

        using (RegistryKey ra = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
        {
            ra.SetValue(AppName, ClientKey + @"\Capabilities");
        }

        int sizeKb = (int)(new FileInfo(exe).Length / 1024) + 1;

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Yandex Link Router");
            k.SetValue("InstallLocation", dir);
            k.SetValue("DisplayIcon", icon);
            k.SetValue("UninstallString", "\"" + Path.Combine(dir, "uninstall.exe") + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + Path.Combine(dir, "uninstall.exe") + "\" /uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
        }
    }

    private static void WriteCfg(string browserExe, string browserArgs, string dir)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(CfgKey))
        {
            k.SetValue("BrowserExe", browserExe);
            k.SetValue("BrowserArgs", string.IsNullOrEmpty(browserArgs) ? "\"%1\"" : browserArgs);
            k.SetValue("InstallPath", dir);
            k.SetValue("Version", Version);
        }
    }

    private static string ReadCfg(string name)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(CfgKey))
            {
                return k == null ? null : k.GetValue(name) as string;
            }
        }
        catch
        {
            return null;
        }
    }

    private static void TryUserChoice(out string exe, out string args)
    {
        exe = null;
        args = null;

        string progId = ReadUserChoice("https") ?? ReadUserChoice("http");
        if (string.IsNullOrEmpty(progId)) return;

        try
        {
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
            {
                string command = key == null ? null : key.GetValue(null) as string;
                string foundExe, foundArgs;
                ParseCommand(command, out foundExe, out foundArgs);
                if (!string.IsNullOrEmpty(foundExe) && File.Exists(foundExe) && !IsRouter(foundExe))
                {
                    exe = foundExe;
                    args = foundArgs;
                }
            }
        }
        catch
        {
            // caller falls back to stored / known browsers
        }
    }

    private static void FindAnyBrowser(out string exe, out string args)
    {
        exe = null;
        args = null;

        // Any browser that installed itself registers a StartMenuInternet client;
        // this covers Opera, Vivaldi, Brave, CentBrowser, LibreWolf and friends.
        if (TryRegisteredClients(out exe, out args)) return;

        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] candidates =
        {
            // Firefox family
            Path.Combine(pf, @"Mozilla Firefox\firefox.exe"),
            Path.Combine(pf86, @"Mozilla Firefox\firefox.exe"),
            Path.Combine(local, @"Mozilla Firefox\firefox.exe"),
            Path.Combine(pf, @"Mozilla Firefox ESR\firefox.exe"),
            Path.Combine(pf, @"Firefox Developer Edition\firefox.exe"),
            Path.Combine(local, @"Programs\Firefox Developer Edition\firefox.exe"),
            Path.Combine(pf, @"Waterfox\Waterfox.exe"),
            Path.Combine(pf, @"LibreWolf\librewolf.exe"),

            // Chromium family
            Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(pf86, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(local, @"Vivaldi\Application\vivaldi.exe"),
            Path.Combine(pf, @"Vivaldi\Application\vivaldi.exe"),
            Path.Combine(local, @"Programs\Opera\opera.exe"),
            Path.Combine(local, @"Programs\Opera GX\opera.exe"),
            Path.Combine(pf, @"Opera\opera.exe"),
            Path.Combine(pf86, @"Opera\opera.exe"),
            Path.Combine(local, @"Programs\Opera\launcher.exe"),
            Path.Combine(local, @"Chromium\Application\chrome.exe"),
            Path.Combine(pf, @"Chromium\Application\chrome.exe"),
            Path.Combine(pf, @"CentBrowser\Application\chrome.exe"),
            Path.Combine(local, @"CentBrowser\Application\chrome.exe"),
            Path.Combine(pf, @"SRWare Iron\iron.exe"),
            Path.Combine(pf86, @"SRWare Iron\iron.exe"),
            Path.Combine(pf, @"Maxthon\Application\Maxthon.exe"),
            Path.Combine(local, @"Maxthon\Application\Maxthon.exe"),

            // Yandex
            Path.Combine(local, @"Yandex\YandexBrowser\Application\browser.exe"),
            Path.Combine(pf, @"Yandex\YandexBrowser\Application\browser.exe"),
            Path.Combine(pf86, @"Yandex\YandexBrowser\Application\browser.exe")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                exe = candidate;
                args = "\"%1\"";
                return;
            }
        }
    }

    private static bool TryRegisteredClients(out string exe, out string args)
    {
        exe = null;
        args = null;

        RegistryKey[] roots = { Registry.CurrentUser, Registry.LocalMachine };
        string[] paths =
        {
            @"Software\Clients\StartMenuInternet",
            @"Software\WOW6432Node\Clients\StartMenuInternet"
        };

        foreach (RegistryKey root in roots)
        {
            foreach (string path in paths)
            {
                try
                {
                    using (RegistryKey clients = root.OpenSubKey(path))
                    {
                        if (clients == null) continue;

                        foreach (string name in clients.GetSubKeyNames())
                        {
                            if (string.Equals(name, "YMRouter", StringComparison.OrdinalIgnoreCase)) continue;

                            using (RegistryKey key = clients.OpenSubKey(name + @"\shell\open\command"))
                            {
                                string command = key == null ? null : key.GetValue(null) as string;
                                string foundExe, foundArgs;
                                ParseCommand(command, out foundExe, out foundArgs);

                                if (!string.IsNullOrEmpty(foundExe) && File.Exists(foundExe) && !IsRouter(foundExe))
                                {
                                    exe = foundExe;
                                    args = foundArgs;
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // keep looking
                }
            }
        }

        return false;
    }

    private static void ParseCommand(string command, out string exe, out string args)
    {
        exe = null;
        args = "";
        if (string.IsNullOrWhiteSpace(command)) return;

        command = command.Trim();

        if (command.StartsWith("\""))
        {
            int end = command.IndexOf('"', 1);
            if (end > 1)
            {
                exe = command.Substring(1, end - 1);
                args = command.Substring(end + 1).Trim();
            }
        }
        else
        {
            int space = command.IndexOf(' ');
            if (space > 0)
            {
                exe = command.Substring(0, space);
                args = command.Substring(space + 1).Trim();
            }
            else
            {
                exe = command;
            }
        }
    }

    private static string ReadUserChoice(string scheme)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + scheme + @"\UserChoice"))
            {
                return key == null ? null : key.GetValue("ProgId") as string;
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRouter(string path)
    {
        return string.Equals(Path.GetFileName(path), "ymrouter.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string InstallDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\YandexLinkRouter");
    }

    private static string FindMusicApp()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] dirs =
        {
            Path.Combine(local, @"Programs\YandexMusic"),
            Path.Combine(pf, "YandexMusic"),
            Path.Combine(pf, @"Yandex\YandexMusic")
        };

        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;

            string found = Directory.GetFiles(dir, "*.exe")
                .Where(f => !Path.GetFileName(f).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();

            if (found != null) return found;
        }

        return null;
    }

    private static void ExtractPayload(string target)
    {
        Assembly asm = Assembly.GetExecutingAssembly();

        using (Stream stream = asm.GetManifestResourceStream("payload.exe"))
        {
            if (stream == null) throw new Exception("В установщике нет файла маршрутизатора (payload.exe).");

            using (FileStream file = new FileStream(target, FileMode.Create, FileAccess.Write))
            {
                stream.CopyTo(file);
            }
        }
    }

    private static void RemoveKey(string path)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
        catch
        {
            // already gone
        }
    }
}
