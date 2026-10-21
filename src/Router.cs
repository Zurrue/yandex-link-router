using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

// Link router: receives a URL from the shell, sends Yandex Music content links
// to the desktop app and everything else to the user's browser.
internal static class Router
{
    private const string CfgKey = @"Software\YandexLinkRouter";

    private static readonly string[] MusicHosts =
    {
        "music.yandex.ru", "music.yandex.com", "music.yandex.by",
        "music.yandex.kz", "music.yandex.uz"
    };

    private static readonly string[] ContentPrefixes =
    {
        "/album/", "/track/", "/artist/", "/playlist/", "/playlists/", "/users/"
    };

    [STAThread]
    private static void Main(string[] args)
    {
        string url = args.Length > 0 ? args[0] : null;

        if (string.IsNullOrWhiteSpace(url))
        {
            Log("no args -> browser");
            OpenBrowser(null);
            return;
        }

        Uri uri;
        if (Uri.TryCreate(url, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            IsMusicHost(uri.Host) &&
            IsContentPath(uri.AbsolutePath) &&
            IsProtocolRegistered("yandexmusic"))
        {
            string deep = "yandexmusic://" + uri.AbsolutePath.TrimStart('/');
            Log("deeplink -> " + deep);
            ShellStart(deep);
            return;
        }

        Log("browser -> " + url);
        OpenBrowser(url);
    }

    private static bool IsMusicHost(string host)
    {
        return MusicHosts.Any(h => string.Equals(host, h, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsContentPath(string path)
    {
        return ContentPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProtocolRegistered(string scheme)
    {
        try
        {
            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(scheme + @"\shell\open\command"))
                return key != null && key.GetValue(null) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void ShellStart(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("shell error: " + ex.Message);
        }
    }

    private static void OpenBrowser(string url)
    {
        string exe = ReadCfg("BrowserExe");
        string argTemplate = ReadCfg("BrowserArgs");

        if (string.IsNullOrEmpty(exe) || !File.Exists(exe) || IsRouter(exe))
        {
            string detectedArgs;
            DetectBrowser(out exe, out detectedArgs);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log("no browser found");
                return;
            }
            argTemplate = detectedArgs;
        }

        if (string.IsNullOrEmpty(argTemplate)) argTemplate = "\"%1\"";

        // Firefox's shell command contains -osint, which triggers its
        // "set as default browser" check on every launch. Drop it.
        argTemplate = argTemplate.Replace("-osint", "").Trim();
        argTemplate = string.Join(" ", argTemplate.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        string arguments;
        if (url == null)
        {
            arguments = "";
        }
        else if (argTemplate.Contains("%1"))
        {
            arguments = argTemplate.Replace("%1", url);
        }
        else
        {
            arguments = (argTemplate + " \"" + url + "\"").Trim();
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                Arguments = arguments,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Log("browser launch error: " + ex.Message);
        }
    }

    private static void DetectBrowser(out string exe, out string args)
    {
        exe = null;
        args = null;

        string progId = ReadUserChoice("https") ?? ReadUserChoice("http");
        if (!string.IsNullOrEmpty(progId))
        {
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
                        return;
                    }
                }
            }
            catch
            {
                // fall through to registered clients
            }
        }

        // Any browser that installed itself registers a StartMenuInternet client.
        // This covers Opera, Vivaldi, Brave, CentBrowser, LibreWolf and friends
        // without hardcoding every vendor.
        if (TryRegisteredClients(out exe, out args)) return;

        FindCommonBrowser(out exe, out args);
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

    private static void FindCommonBrowser(out string exe, out string args)
    {
        exe = null;
        args = null;

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

    private static bool IsRouter(string path)
    {
        return string.Equals(Path.GetFileName(path), "ymrouter.exe", StringComparison.OrdinalIgnoreCase);
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

    private static string ReadCfg(string name)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CfgKey))
            {
                return key == null ? null : key.GetValue(name) as string;
            }
        }
        catch
        {
            return null;
        }
    }

    private static void Log(string line)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YandexLinkRouter");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "router.log");
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 512 * 1024) info.Delete();

            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
        }
        catch
        {
            // logging must never break link handling
        }
    }
}
