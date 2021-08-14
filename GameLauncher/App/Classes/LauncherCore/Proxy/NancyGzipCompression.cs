﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GameLauncher.App.Classes.InsiderKit;
using GameLauncher.App.Classes.LauncherCore.Logger;
using Nancy;
using Nancy.Bootstrapper;
using Nancy.Responses;

namespace GameLauncher.App.Classes.LauncherCore.Proxy
{
    public class NancyGzipCompression : IApplicationStartup
    {
        public void Initialize(IPipelines pipelines)
        {
            pipelines.AfterRequest += CheckForCompression;
            pipelines.OnError += OnError;
        }

        private TextResponse OnError(NancyContext context, Exception Error)
        {
            Log.Error("PROXY HANDLER: " + context.Request.Path);
            LogToFileAddons.OpenLog("PROXY HANDLER", null, Error, null, true);

            CommunicationLog.RecordEntry(ServerProxy.Instance.GetServerName(), "PROXY", CommunicationLogEntryType.Error,
                new CommunicationLogLauncherError(Error.Message, context.Request.Path, context.Request.Method));

            return new TextResponse(HttpStatusCode.BadRequest, Error.Message);
        }

        private static void WebCallRejected(string Reason, NancyContext Context)
        {
            string ErrorReason = "(After Request) Web Call Rejected. ";
            switch (Reason)
            {
                case "RequestIsGzipCompatible":
                    ErrorReason += "Request Is Not Gzip Compatible";
                    break;
                case "ResponseIsCompatibleMimeType":
                    ErrorReason += "Response Is Not a Compatible Mime-Type";
                    break;
                case "ResponseIsCompressed":
                    ErrorReason += "Response Is Already Compressed";
                    break;
                case "ContentLengthIsTooSmall":
                    ErrorReason += "Content-Length Is Too Small";
                    break;
                default:
                    ErrorReason += "Unknown Reason";
                    break;
            }

            CommunicationLog.RecordEntry(ServerProxy.Instance.GetServerName(), "PROXY", CommunicationLogEntryType.Rejected,
            new CommunicationLogLauncherError(ErrorReason, Context.Request.Path, Context.Request.Method));
        }

        private static void CheckForCompression(NancyContext Context)
        {
            if (!RequestIsGzipCompatible(Context))
            {
                WebCallRejected("RequestIsGzipCompatible", Context);
            }
            else
            {
                if (!ResponseIsCompatibleMimeType(Context))
                {
                    WebCallRejected("ResponseIsCompatibleMimeType", Context);
                }
                else
                {
                    if (ResponseIsCompressed(Context))
                    {
                        WebCallRejected("RequestIsGzipCompatible", Context);
                    }
                    else
                    {
                        if (ContentLengthIsTooSmall(Context))
                        {
                            WebCallRejected("RequestIsGzipCompatible", Context);
                        }
                        else
                        {
                            CompressResponse(Context);
                        }
                    }
                }
            }
        }

        private static void CompressResponse(NancyContext Context)
        {
            bool Deflate = Context.Request.Headers.AcceptEncoding.Any(x => x.Contains("deflate"));

            Context.Response.Headers["Content-Encoding"] = Deflate ? "deflate" : "gzip";
            Context.Response.Headers["Connection"] = "close";

            var FinalResponse = Context.Response.Contents;

            Context.Response.Contents = responseStream =>
            {
                if (Deflate)
                {
                    using (DeflateStream Compression = new DeflateStream(responseStream, CompressionLevel.Optimal, true))
                    {
                        FinalResponse(Compression);
                    }
                }
                else
                {
                    using (GZipStream Compression = new GZipStream(responseStream, CompressionMode.Compress, true))
                    {
                        FinalResponse(Compression);
                    }
                }
            };

            using (MemoryStream mm = new MemoryStream())
            {
                Context.Response.Contents.Invoke(mm);
                mm.Flush();

                Context.Response.Headers["Content-Length"] = mm.Length.ToString();
            }

            /* Different Solutions With Different OoM Errors */
            /* https://gist.github.com/DavidCarbon/e0b37e7bc58b5e1a46f6dfedc87c966d */
        }

        private static bool ContentLengthIsTooSmall(NancyContext Context)
        {
            if (Context.Response.Headers == null)
            {
                if (EnableInsiderDeveloper.Allowed()) { Log.Debug("Headers is Null for " + Context.Request.Path); }
                return true;
            }
            else
            {
                if (!Context.Response.Headers.TryGetValue("Content-Length", out string ContentLength))
                {
                    using (MemoryStream mm = new MemoryStream())
                    {
                        Context.Response.Contents.Invoke(mm);
                        mm.Flush();
                        ContentLength = mm.Length.ToString();
                    }
                }
                if (EnableInsiderDeveloper.Allowed()) { Log.Debug($"GZip Content-Length of response is {ContentLength} for {Context.Request.Path}"); }

                long length = long.Parse(ContentLength);
                if (length > 0)
                {
                    return false;
                }
                else
                {
                    if (EnableInsiderDeveloper.Allowed()) { Log.Debug($"GZip Content-Length is too small for {Context.Request.Path}"); }
                    return true;
                }
            }
        }

        private static bool ResponseIsCompressed(NancyContext Context)
        {
            bool Status = false;
            if (Context.Response.Headers.Keys != null)
            {
                Status = Context.Response.Headers.Keys.Any(x => x.Contains("Content-Encoding"));
            }

            if (EnableInsiderDeveloper.Allowed() && !Status) { Log.Debug("Is Compressed? For " + Context.Request.Path + " " + Status); }
            return Status;
        }

        private static IList<string> MimeTypes { get; set; } = new List<string>
        {
            "text/plain",
            "text/html",
            "text/xml",
            "text/css",
            "application/json",
            "application/x-javascript",
            "application/atom+xml",
            "application/xml;charset=UTF-8",
            "application/xml"
        };

        private static bool ResponseIsCompatibleMimeType(NancyContext Context)
        {
            bool Status = false;
            if (Context.Response.ContentType != null)
            {
                if (MimeTypes.Any(x => x == Context.Response.ContentType))
                {
                    Status = true;
                }
                else if (MimeTypes.Any(x => Context.Response.ContentType.StartsWith($"{x};")))
                {
                    Status = true;
                }
            }

            if (EnableInsiderDeveloper.Allowed() && !Status) { Log.Debug("Content Type? For " + Context.Request.Path + " " + Status); }
            return Status;
        }

        private static bool RequestIsGzipCompatible(NancyContext Context)
        {
            bool Status = false;
            if (Context.Request.Headers.AcceptEncoding != null)
            {
                if (Context.Request.Headers.AcceptEncoding.Any(x => x.Contains("gzip")))
                {
                    Status = true;
                }
                else if (Context.Request.Headers.AcceptEncoding.Any(x => x.Contains("deflate")))
                {
                    Status = true;
                }
            }

            if (EnableInsiderDeveloper.Allowed() && !Status) { Log.Debug("Gzip Compatible? For " + Context.Request.Path + " " + Status); }
            return Status;
        }
    }
}