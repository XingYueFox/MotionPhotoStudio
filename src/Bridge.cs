using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using MotionPhotoStudio.Core;

namespace MotionPhotoStudio
{
    internal sealed class Bridge
    {
        private readonly MainForm _form;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly Dictionary<string, string> _thumbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource _cts;
        private bool _running;

        internal Bridge(MainForm form)
        {
            _form = form;
            _form.FilesDropped += paths => PushEvent("files.dropped", new Dictionary<string, object> { { "paths", paths } });
        }

        internal void PushAppInfo()
        {
            var data = new Dictionary<string, object>
            {
                { "version", Program.Version },
                { "exeDir", Paths.ExeDir },
                { "dataDir", Paths.DataDir },
                { "webview", SafeBrowserVersion() },
                { "fixedRuntime", Paths.FixedRuntimeFolder() != null },
                { "maximized", _form.IsMaximized },
                { "page", Program.ScreenshotPage },
                { "forceTheme", Program.ScreenshotTheme },
                { "demoDir", Program.ScreenshotDemoDir },
                { "accent", SystemAccent.Read() }
            };
            PushEvent("app.info", data);
        }

        internal void PushAccent()
        {
            PushEvent("theme.accent", new Dictionary<string, object> { { "hex", SystemAccent.Read() } });
        }

        private string SafeBrowserVersion()
        {
            try { return _form.View.CoreWebView2.Environment.BrowserVersionString; }
            catch { return ""; }
        }

        internal void OnMessage(string json)
        {
            string id = null;
            try
            {
                // 页面 postMessage 传的是 JSON 字符串，WebMessageAsJson 会再包一层引号
                object parsed = _json.DeserializeObject(json);
                if (parsed is string raw) parsed = _json.DeserializeObject(raw);
                var msg = parsed as Dictionary<string, object>;
                if (msg == null) return;
                id = Str(msg, "id");
                string cmd = Str(msg, "cmd");
                var args = msg.ContainsKey("args") ? msg["args"] as Dictionary<string, object> : null;
                object data = Dispatch(cmd, args);
                Respond(id, true, data, null);
            }
            catch (Exception ex)
            {
                Respond(id, false, null, ex.Message);
            }
        }

        private object Dispatch(string cmd, Dictionary<string, object> args)
        {
            switch (cmd)
            {
                case "app.ready":
                    PushAppInfo();
                    return new Dictionary<string, object> { { "version", Program.Version }, { "exeDir", Paths.ExeDir } };

                case "settings.load":
                    return new Dictionary<string, object> { { "raw", LoadSettingsText() } };

                case "settings.save":
                    SaveSettingsText(Str(args, "raw"));
                    return new Dictionary<string, object> { { "saved", true } };

                case "dialog.pickFiles":
                    return PickFiles();

                case "dialog.pickFolder":
                    return PickFolder(Str(args, "title") ?? "选择文件夹");

                case "scan.paths":
                    return Scan(ToStringArray(args, "paths"), Bool(args, "recursive"));

                case "job.start":
                    StartJob(args);
                    return new Dictionary<string, object> { { "started", true } };

                case "job.cancel":
                    if (_cts != null) { try { _cts.Cancel(); } catch { } }
                    return new Dictionary<string, object> { { "canceled", true } };

                case "thumb.get":
                    return new Dictionary<string, object> { { "dataUrl", Thumb(Str(args, "path")) } };

                case "system.accent":
                    return new Dictionary<string, object> { { "hex", SystemAccent.Read() }, { "dark", SystemAccent.IsDarkAppTheme() } };

                case "shell.reveal":
                    Reveal(Str(args, "path"), Bool(args, "select"));
                    return null;

                case "shell.open":
                    OpenPath(Str(args, "path"));
                    return null;

                case "shell.openDataDir":
                    OpenPath(Paths.DataDir);
                    return null;

                case "window.min":
                    _form.WindowState = FormWindowState.Minimized;
                    return null;

                case "window.max":
                    _form.ToggleMaximize();
                    return new Dictionary<string, object> { { "maximized", _form.IsMaximized } };

                case "window.close":
                    _form.Close();
                    return null;

                case "window.drag":
                    _form.StartWindowDrag();
                    return null;

                case "window.resize":
                    _form.StartWindowResize(EdgeHitCode(Str(args, "edge")));
                    return null;

                case "ui.theme":
                    _form.SetDarkFrame(Bool(args, "dark"));
                    _form.SetSurfaceColor(Str(args, "surface"));
                    return null;

                case "update.check":
                    CheckUpdateAsync();
                    return new Dictionary<string, object> { { "checking", true } };

                case "debug.log":
                    try
                    {
                        File.AppendAllText(Path.Combine(Paths.DataDir, "web.log"),
                            DateTime.Now.ToString("HH:mm:ss") + "  " + Str(args, "text") + Environment.NewLine);
                    }
                    catch { }
                    return null;

                default:
                    throw new InvalidOperationException("未知命令：" + cmd);
            }
        }

        // ---------------- 检查更新 ----------------

        private const string UpdateApi = "https://api.github.com/repos/XingYueFox/MotionPhotoStudio/releases/latest";
        private const string UpdatePage = "https://github.com/XingYueFox/MotionPhotoStudio/releases";
        private int _checkingUpdate;

        private void CheckUpdateAsync()
        {
            if (Interlocked.CompareExchange(ref _checkingUpdate, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var request = (HttpWebRequest)WebRequest.Create(UpdateApi);
                    request.UserAgent = "MotionPhotoStudio/" + Program.Version;
                    request.Accept = "application/vnd.github+json";
                    request.Timeout = 12000;
                    request.ReadWriteTimeout = 12000;
                    request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                    string body;
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        body = reader.ReadToEnd();

                    var release = _json.DeserializeObject(body) as Dictionary<string, object>;
                    if (release == null) { PushUpdate(false, null, "无法解析 GitHub 返回的数据"); return; }

                    string tag = Get(release, "tag_name");
                    string latest = NormalizeVersion(tag);
                    string notes = Get(release, "body");
                    if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";

                    var data = new Dictionary<string, object>
                    {
                        { "latest", latest },
                        { "tag", tag },
                        { "name", Get(release, "name") },
                        { "url", Get(release, "html_url") },
                        { "notes", notes },
                        { "publishedAt", Get(release, "published_at") },
                        { "prerelease", Bool(release, "prerelease") },
                        { "hasUpdate", CompareVersions(latest, Program.Version) > 0 },
                        { "page", UpdatePage }
                    };
                    PushUpdate(true, data, null);
                }
                catch (WebException wex)
                {
                    var http = wex.Response as HttpWebResponse;
                    string message;
                    if (http != null && http.StatusCode == HttpStatusCode.NotFound)
                        message = "仓库还没有发布 Release";
                    else if (http != null && (int)http.StatusCode == 403)
                        message = "GitHub 暂时限制了请求，请稍后再试";
                    else if (wex.Status == WebExceptionStatus.Timeout)
                        message = "连接 GitHub 超时，请检查网络";
                    else
                        message = "无法连接 GitHub，请检查网络后重试";
                    PushUpdate(false, null, message);
                }
                catch (Exception ex)
                {
                    PushUpdate(false, null, ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _checkingUpdate, 0);
                }
            });
        }

        private void PushUpdate(bool ok, Dictionary<string, object> payload, string error)
        {
            var data = payload ?? new Dictionary<string, object>();
            data["ok"] = ok;
            data["current"] = Program.Version;
            if (!ok) data["error"] = error ?? "未知错误";
            PushEvent("update.result", data);
        }

        private static string Get(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict != null && dict.TryGetValue(key, out value) && value != null) return Convert.ToString(value);
            return "";
        }

        // "v27.1.0" → "27.1.0"，只保留数字与点
        private static string NormalizeVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            string text = tag.Trim().TrimStart('v', 'V').Trim();
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsDigit(c) || c == '.') sb.Append(c);
                else break;
            }
            return sb.ToString().Trim('.');
        }

        private static int CompareVersions(string left, string right)
        {
            string[] a = (left ?? "").Split('.');
            string[] b = (right ?? "").Split('.');
            int count = Math.Max(a.Length, b.Length);
            for (int i = 0; i < count; i++)
            {
                int na = 0, nb = 0;
                if (i < a.Length) int.TryParse(a[i], out na);
                if (i < b.Length) int.TryParse(b[i], out nb);
                if (na != nb) return na.CompareTo(nb);
            }
            return 0;
        }

        // ---------------- 设置 ----------------

        private string LoadSettingsText()
        {
            try
            {
                return File.Exists(Paths.SettingsFile) ? File.ReadAllText(Paths.SettingsFile, Encoding.UTF8) : "{}";
            }
            catch { return "{}"; }
        }

        private void SaveSettingsText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                File.WriteAllText(Paths.SettingsFile, raw, new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- 对话框 ----------------

        private object PickFiles()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择照片与视频";
                dialog.Filter = "动态照片素材|*.jpg;*.jpeg;*.mov;*.mp4;*.m4v|JPEG 照片|*.jpg;*.jpeg|视频|*.mov;*.mp4;*.m4v|所有文件|*.*";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(_form) != DialogResult.OK) return new Dictionary<string, object> { { "paths", new string[0] } };
                return new Dictionary<string, object> { { "paths", dialog.FileNames } };
            }
        }

        private object PickFolder(string title)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = title;
                dialog.ShowNewFolderButton = true;
                string result = dialog.ShowDialog(_form) == DialogResult.OK ? dialog.SelectedPath : "";
                return new Dictionary<string, object> { { "path", result } };
            }
        }

        // ---------------- 扫描配对 ----------------

        private object Scan(string[] paths, bool recursive)
        {
            var photos = new List<string>();
            var videos = new List<string>();
            var roots = new List<string>();
            int otherFiles = 0;

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    roots.Add(path);
                    IEnumerable<string> files;
                    try
                    {
                        files = Directory.EnumerateFiles(path, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                    }
                    catch { continue; }
                    foreach (string file in files)
                    {
                        Classify(file, photos, videos, ref otherFiles);
                    }
                }
                else if (File.Exists(path))
                {
                    var before = photos.Count + videos.Count;
                    Classify(path, photos, videos, ref otherFiles);
                    if (photos.Count + videos.Count == before) otherFiles++;
                }
            }

            var videoIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string v in videos)
            {
                string key = Path.GetFileNameWithoutExtension(v);
                if (!videoIndex.ContainsKey(key)) videoIndex[key] = v;
            }

            var pairs = new List<object>();
            var usedVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (string photo in photos.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                index++;
                string baseName = Path.GetFileNameWithoutExtension(photo);
                string video = null;
                videoIndex.TryGetValue(baseName, out video);

                bool isJpeg = MotionPhoto.HasJpegExtension(photo);
                bool already = isJpeg && MotionPhoto.LooksLikeMotionPhoto(photo);
                var item = new Dictionary<string, object>
                {
                    { "id", "p" + index },
                    { "photo", photo },
                    { "photoName", Path.GetFileName(photo) },
                    { "photoSize", SafeLength(photo) },
                    { "video", video ?? "" },
                    { "videoName", video == null ? "" : Path.GetFileName(video) },
                    { "videoSize", video == null ? 0L : SafeLength(video) },
                    { "already", already },
                    { "convert", !isJpeg }
                };

                if (video == null)
                {
                    item["status"] = "no-video";
                }
                else
                {
                    usedVideos.Add(video);
                    VideoMeta meta = MotionPhoto.InspectVideo(video);
                    item["codec"] = meta.Codec;
                    item["duration"] = meta.DurationSeconds;
                    item["container"] = meta.IsIsoBmff ? meta.Brand : "";
                    item["status"] = already ? "duplicate" : (meta.IsIsoBmff ? "ready" : "invalid");
                }
                pairs.Add(item);
            }

            int ready = 0, duplicate = 0, missing = 0;
            foreach (Dictionary<string, object> item in pairs)
            {
                switch ((string)item["status"])
                {
                    case "duplicate": duplicate++; break;
                    case "no-video": missing++; break;
                    default: ready++; break;
                }
            }

            var summary = new Dictionary<string, object>
            {
                { "total", pairs.Count },
                { "ready", ready },
                { "duplicate", duplicate },
                { "missing", missing },
                { "unpairedVideos", videos.Count - usedVideos.Count },
                { "otherFiles", otherFiles },
                { "roots", roots }
            };
            return new Dictionary<string, object> { { "pairs", pairs }, { "summary", summary } };
        }

        private static void Classify(string file, List<string> photos, List<string> videos, ref int other)
        {
            if (MotionPhoto.IsSupportedPhoto(file)) photos.Add(file);
            else if (MotionPhoto.HasVideoExtension(file)) videos.Add(file);
            else other++;
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        // ---------------- 任务 ----------------

        private void StartJob(Dictionary<string, object> args)
        {
            if (_running) throw new InvalidOperationException("已有任务在进行中");
            var items = args != null && args.ContainsKey("items") ? args["items"] as object[] : null;
            if (items == null || items.Length == 0) throw new InvalidOperationException("没有待处理的项目");

            string outDir = Str(args, "output");
            if (string.IsNullOrWhiteSpace(outDir)) throw new InvalidOperationException("请先选择输出目录");
            Directory.CreateDirectory(outDir);

            var options = new MuxOptions
            {
                PresentationTimestampUs = (int)Num(args, "timestampUs", 1500000),
                WriteLegacyTags = Bool(args, "legacyTags", true),
                PreserveFileTime = Bool(args, "preserveTime", true)
            };
            string policy = Str(args, "overwrite") ?? "rename";
            bool copyUnmatched = Bool(args, "copyUnmatched", false);
            var roots = ToStringArray(args, "roots");
            var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _running = true;
            PushEvent("job.begin", new Dictionary<string, object> { { "total", items.Length }, { "output", outDir } });

            var snapshot = items.Select(o => o as Dictionary<string, object>).Where(d => d != null).ToArray();

            Task.Run(() =>
            {
                int ok = 0, failed = 0, skipped = 0;
                _form.PostEvent("job.log", new Dictionary<string, object> { { "level", "info" }, { "text", "输出目录：" + outDir } });

                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (token.IsCancellationRequested) break;
                    var item = snapshot[i];
                    string photo = Str(item, "photo");
                    string video = Str(item, "video");
                    string displayName = Path.GetFileName(photo);

                    _form.PostEvent("job.progress", new Dictionary<string, object> { { "index", i }, { "total", snapshot.Length }, { "name", displayName } });

                    try
                    {
                        bool skippedThis;
                        string target = ResolveTarget(outDir, Path.GetFileNameWithoutExtension(photo), policy, out skippedThis);
                        if (target == null)
                        {
                            skipped++;
                            _form.PostEvent("job.item", new Dictionary<string, object>
                            {
                                { "index", i }, { "ok", false }, { "skipped", true }, { "name", displayName }, { "error", "已存在，按设置跳过" }
                            });
                            continue;
                        }

                        MuxOutcome outcome = MotionPhoto.Merge(photo, video, target, options);
                        usedSources.Add(photo);
                        usedSources.Add(video);

                        if (outcome.Ok)
                        {
                            ok++;
                            _form.PostEvent("job.item", new Dictionary<string, object>
                            {
                                { "index", i }, { "ok", true }, { "name", Path.GetFileName(target) },
                                { "output", outcome.Output }, { "size", outcome.Size },
                                { "photoBytes", outcome.PhotoBytes }, { "videoBytes", outcome.VideoBytes },
                                { "codec", outcome.Codec }, { "converted", outcome.Converted }
                            });
                            _form.PostEvent("job.log", new Dictionary<string, object>
                            {
                                { "level", "ok" }, { "text", Path.GetFileName(target) + "  ·  " + FormatSize(outcome.Size) + (string.IsNullOrEmpty(outcome.Codec) ? "" : "  ·  " + outcome.Codec) }
                            });
                        }
                        else
                        {
                            failed++;
                            _form.PostEvent("job.item", new Dictionary<string, object>
                            {
                                { "index", i }, { "ok", false }, { "name", displayName }, { "error", outcome.Error }
                            });
                            _form.PostEvent("job.log", new Dictionary<string, object> { { "level", "error" }, { "text", displayName + " 失败：" + outcome.Error } });
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _form.PostEvent("job.item", new Dictionary<string, object>
                        {
                            { "index", i }, { "ok", false }, { "name", displayName }, { "error", ex.Message }
                        });
                        _form.PostEvent("job.log", new Dictionary<string, object> { { "level", "error" }, { "text", displayName + " 失败：" + ex.Message } });
                    }
                }

                int copied = 0;
                if (copyUnmatched && !token.IsCancellationRequested && roots.Length > 0)
                {
                    _form.PostEvent("job.log", new Dictionary<string, object> { { "level", "info" }, { "text", "正在复制未配对的文件…" } });
                    copied = CopyUnmatched(roots, outDir, usedSources, policy, token, recursive: true);
                }

                _form.PostEvent("job.done", new Dictionary<string, object>
                {
                    { "ok", ok }, { "failed", failed }, { "skipped", skipped }, { "copied", copied },
                    { "canceled", token.IsCancellationRequested }, { "output", outDir }
                });
                _running = false;
            }, token);
        }

        private string ResolveTarget(string outDir, string baseName, string policy, out bool skipped)
        {
            skipped = false;
            string target = Path.Combine(outDir, baseName + ".jpg");
            if (!File.Exists(target)) return target;

            if (policy == "overwrite")
            {
                try { File.Delete(target); } catch { }
                return target;
            }
            if (policy == "skip")
            {
                skipped = true;
                return null;
            }
            for (int i = 2; i < 9999; i++)
            {
                string candidate = Path.Combine(outDir, baseName + " (" + i + ").jpg");
                if (!File.Exists(candidate)) return candidate;
            }
            return target;
        }

        private int CopyUnmatched(string[] roots, string outDir, HashSet<string> used, string policy, CancellationToken token, bool recursive)
        {
            int copied = 0;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                }
                catch { continue; }

                foreach (string file in files)
                {
                    if (token.IsCancellationRequested) return copied;
                    if (used.Contains(file)) continue;
                    if (MotionPhoto.HasVideoExtension(file)) continue;
                    if (MotionPhoto.HasJpegExtension(file) && MotionPhoto.LooksLikeMotionPhoto(file)) continue;

                    string name = Path.GetFileName(file);
                    string target = Path.Combine(outDir, name);
                    if (File.Exists(target))
                    {
                        if (policy == "skip") continue;
                        if (policy == "rename")
                        {
                            string stem = Path.GetFileNameWithoutExtension(name);
                            string ext = Path.GetExtension(name);
                            for (int i = 2; i < 9999; i++)
                            {
                                target = Path.Combine(outDir, stem + " (" + i + ")" + ext);
                                if (!File.Exists(target)) break;
                            }
                        }
                    }
                    try
                    {
                        File.Copy(file, target, true);
                        copied++;
                    }
                    catch { }
                }
            }
            return copied;
        }

        // ---------------- 缩略图 / Shell ----------------

        private string Thumb(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (_thumbs.ContainsKey(path)) return _thumbs[path];
            try
            {
                using (Image source = Image.FromFile(path))
                {
                    int max = 520;
                    double scale = Math.Min(1.0, Math.Min((double)max / source.Width, (double)max / source.Height));
                    int w = Math.Max(1, (int)(source.Width * scale));
                    int h = Math.Max(1, (int)(source.Height * scale));
                    using (var bmp = new Bitmap(w, h))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(source, 0, 0, w, h);
                        }
                        using (var ms = new MemoryStream())
                        {
                            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                            var parameters = new EncoderParameters(1);
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 84L);
                            bmp.Save(ms, codec, parameters);
                            string url = "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                            _thumbs[path] = url;
                            return url;
                        }
                    }
                }
            }
            catch { return ""; }
        }

        private void Reveal(string path, bool select)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (select && File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                else if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch { }
        }

        private void OpenPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { System.Diagnostics.Process.Start(path); } catch { }
        }

        // ---------------- 序列化工具 ----------------

        private void Respond(string id, bool ok, object data, string error)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"id\":");
            WriteValue(sb, id);
            sb.Append(",\"ok\":").Append(ok ? "true" : "false");
            if (ok)
            {
                sb.Append(",\"data\":");
                WriteValue(sb, data);
            }
            else
            {
                sb.Append(",\"error\":");
                WriteValue(sb, error ?? "未知错误");
            }
            sb.Append('}');
            _form.PostJson(sb.ToString());
        }

        private void PushEvent(string name, object data)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"event\":");
            WriteValue(sb, name);
            sb.Append(",\"data\":");
            WriteValue(sb, data);
            sb.Append('}');
            _form.PostJson(sb.ToString());
        }

        internal static string Serialize(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        internal static string JsonString(string value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string) { WriteString(sb, (string)value); return; }
            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }
            if (value is int || value is long || value is short || value is byte) { sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is double || value is float || value is decimal)
            {
                double d = Convert.ToDouble(value);
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("0"); return; }
                sb.Append(d.ToString("0.####", CultureInfo.InvariantCulture));
                return;
            }
            var dict = value as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, entry.Value);
                }
                sb.Append('}');
                return;
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in enumerable)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }

        // ---------------- 参数读取 ----------------

        // 页面上报的边缘方向 -> Win32 命中码
        private static int EdgeHitCode(string edge)
        {
            switch ((edge ?? "").ToLowerInvariant())
            {
                case "left": return 10;         // HTLEFT
                case "right": return 11;        // HTRIGHT
                case "top": return 12;          // HTTOP
                case "topleft": return 13;      // HTTOPLEFT
                case "topright": return 14;     // HTTOPRIGHT
                case "bottom": return 15;       // HTBOTTOM
                case "bottomleft": return 16;   // HTBOTTOMLEFT
                case "bottomright": return 17;  // HTBOTTOMRIGHT
                default: return 0;
            }
        }

        private static string Str(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key)) return null;
            object v = dict[key];
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static long Num(Dictionary<string, object> dict, string key, long fallback)
        {
            if (dict == null || !dict.ContainsKey(key)) return fallback;
            object v = dict[key];
            if (v == null) return fallback;
            double d;
            if (v is double || v is float || v is decimal) return (long)Convert.ToDouble(v);
            if (v is int || v is long) return Convert.ToInt64(v);
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return (long)d;
            return fallback;
        }

        private static bool Bool(Dictionary<string, object> dict, string key, bool fallback = false)
        {
            if (dict == null || !dict.ContainsKey(key)) return fallback;
            object v = dict[key];
            if (v is bool) return (bool)v;
            if (v == null) return fallback;
            bool b;
            if (bool.TryParse(Convert.ToString(v), out b)) return b;
            return fallback;
        }

        private static string[] ToStringArray(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key)) return new string[0];
            var arr = dict[key] as object[];
            if (arr == null) return new string[0];
            var list = new List<string>();
            foreach (object o in arr) if (o != null) list.Add(Convert.ToString(o));
            return list.ToArray();
        }
    }
}
