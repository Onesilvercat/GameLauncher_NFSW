﻿using System;
using System.Windows.Forms;
using GameLauncher.App.Classes.SystemPlatform.Components;
using GameLauncher.App.Classes.Hash;
using GameLauncher.App.Classes.SystemPlatform.Linux;
using GameLauncher.App.Classes.LauncherCore.RPC;
using System.Net;
using GameLauncher.App.Classes.LauncherCore.Global;
using GameLauncher.App.Classes.SystemPlatform.Windows;
using GameLauncher.App.Classes.LauncherCore.Logger;
using GameLauncher.App.Classes.LauncherCore.Support;
using System.IO;
using GameLauncher.App.Classes.LauncherCore.FileReadWrite;

namespace GameLauncher.App.Classes.LauncherCore.Client.Web
{
    class WebCalls
    {
        public static bool Alternative = FileSettingsSave.WebCallMethod == "WebClient";
    }

    class WebHelpers
    {
        private static string Hash = string.Empty;

        public static string Value()
        {
            if (string.IsNullOrWhiteSpace(Hash))
            {
                Hash = SHA.Files(Strings.Encode(Path.Combine(Locations.LauncherFolder, Locations.NameLauncher)));
            }

            return Hash;
        }
    }

    public class WebClientWithTimeout : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            if (DetectLinux.LinuxDetected())
            {
                address = new UriBuilder(address)
                {
                    Scheme = Uri.UriSchemeHttp,
                    Port = address.IsDefaultPort ? -1 : address.Port /* -1 => default port for scheme */
                }.Uri;
            }

            if (!address.AbsolutePath.Contains("auth") ||
                !(address.OriginalString.Contains("section") && address.OriginalString.Contains(".dat")))
            { Log.UrlCall("WEBCLIENTWITHTIMEOUT: Calling URL -> " + address); }

            FunctionStatus.TLS();
            ServicePointManager.FindServicePoint(address).ConnectionLeaseTimeout = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
            request.UserAgent = "SBRW Launcher " + Application.ProductVersion + " (+https://github.com/SoapBoxRaceWorld/GameLauncher_NFSW)";
            request.Headers["X-HWID"] = HardwareID.FingerPrint.Value();
            request.Headers["X-HiddenHWID"] = HardwareID.FingerPrint.ValueAlt();
            request.Headers["X-UserAgent"] = "GameLauncherReborn " + Application.ProductVersion + " WinForms (+https://github.com/SoapBoxRaceWorld/GameLauncher_NFSW)";
            request.Headers["X-GameLauncherHash"] = WebHelpers.Value();
            request.Headers["X-GameLauncherCertificate"] = CertificateStore.LauncherSerial;
            request.Headers["X-DiscordID"] = DiscordLauncherPresense.UserID;
            request.Timeout = 5000;
            request.KeepAlive = false;

            return request;
        }
    }
}
