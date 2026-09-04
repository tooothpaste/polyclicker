// ===========================================================================
//  Updates - the version check behind Settings
// ---------------------------------------------------------------------------
//  One GET to the GitHub releases API, made when Settings opens and on its
//  button. Nothing is sent beyond the request itself and nothing is
//  downloaded - a newer tag turns the button into a link to the releases
//  page. Nothing runs at startup, so the app never pairs its input hooks
//  with a network request the moment it launches.
// ===========================================================================

using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Polyclicker
{
    static class Updates
    {
        public const string ReleasesUrl =
            "https://github.com/tooothpaste/polyclicker/releases/latest";
        const string ApiUrl =
            "https://api.github.com/repos/tooothpaste/polyclicker/releases/latest";

        // One GET to the GitHub releases API on a background thread. done
        // runs on the owner's UI thread with the latest release tag ("1.3")
        // and whether it is newer than this build - or null when the check
        // couldn't be made (offline, rate-limited, blocked, no releases),
        // which is a log line here and a short status in Settings, never a
        // dialog.
        public static void Check(Control owner, Action<string, bool> done)
        {
            var t = new Thread(delegate()
            {
                string tag = null;
                bool newer = false;
                try
                {
                    // github.com requires TLS 1.2, which older .NET 4.x defaults
                    // leave off. 3072 = Tls12; the cast keeps this compiling even
                    // where the enum name is missing from the reference set.
                    try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
                    catch { }

                    var req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                    req.UserAgent = "Polyclicker/" + Current();  // GitHub rejects agentless requests
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = req.ReadWriteTimeout = 10000;
                    string body;
                    using (WebResponse resp = req.GetResponse())
                    using (var r = new StreamReader(resp.GetResponseStream()))
                        body = r.ReadToEnd();

                    string raw = Extract(body, "tag_name");
                    int[] latest = ParseVersion(raw);
                    if (latest == null) Log.Line("update check: no version tag in reply");
                    else
                    {
                        tag = raw.TrimStart('v', 'V');
                        newer = Compare(latest, ParseVersion(Current())) > 0;
                        Log.Line("update check: latest is " + raw + (newer ? " (newer)" : " (up to date)"));
                    }
                }
                catch (Exception ex)
                {
                    Log.Line("update check failed: " + ex.Message);
                }
                try { owner.BeginInvoke((MethodInvoker)delegate { done(tag, newer); }); }
                catch { }               // window torn down before the reply came
            });
            t.IsBackground = true;
            t.Start();
        }

        // The running build's version, trimmed the way release tags are
        // written: "1.1", or "1.1.1" when a build number is in use.
        // build.ps1 stamps every build "dev" or "release"; a dev build says so
        // in its title and wears the muted icon, so the installed release and
        // a build from the tree are never mistaken for each other
        public static readonly bool IsDev = Stamp() == "dev";
        public static readonly string AppName = IsDev ? "Polyclicker dev" : "Polyclicker";

        static string Stamp()
        {
            object[] a = Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            return a.Length > 0 ? ((AssemblyInformationalVersionAttribute)a[0]).InformationalVersion : "";
        }

        public static string Current()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build
                               : v.Major + "." + v.Minor;
        }

        // The next "..."-quoted value after "key": in the body. A whole JSON
        // parser for one string field would be the heaviest code in the exe.
        static string Extract(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            int a = json.IndexOf('"', i);
            if (a < 0) return null;
            int b = json.IndexOf('"', a + 1);
            return b < 0 ? null : json.Substring(a + 1, b - a - 1);
        }

        // "v1.2" or "1.2.3" to [1,2,3]; null when there's no number to read.
        static int[] ParseVersion(string tag)
        {
            if (tag == null) return null;
            string[] parts = tag.TrimStart('v', 'V').Split('.');
            var v = new int[3];
            int got = 0;
            for (int i = 0; i < parts.Length && got < 3; i++)
            {
                int n;
                if (!int.TryParse(parts[i].Trim(), out n)) break;
                v[got++] = n;
            }
            return got > 0 ? v : null;
        }

        static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 3; i++)
                if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return 0;
        }
    }

}
