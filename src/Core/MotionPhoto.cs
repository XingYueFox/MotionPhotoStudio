using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace MotionPhotoStudio.Core
{
    public sealed class MuxOptions
    {
        public int PresentationTimestampUs = 1500000;
        public bool WriteLegacyTags = true;
        public bool PreserveFileTime = true;
    }

    public sealed class MuxOutcome
    {
        public bool Ok;
        public string Error;
        public string Output;
        public long Size;
        public long PhotoBytes;
        public long VideoBytes;
        public string Codec;
        public bool Converted;
        public string SourcePhoto;
    }

    public sealed class VideoMeta
    {
        public bool IsIsoBmff;
        public string Brand = "";
        public string Codec = "";
        public double DurationSeconds;
        public long Size;
    }

    /// <summary>
    /// Google Motion Photo 合成器：把 JPEG 与视频拼成一个文件，并重写 JPEG 的 XMP 元数据段。
    /// 不依赖 exiftool / ffmpeg，全部在托管代码里完成。
    /// </summary>
    public static class MotionPhoto
    {
        private const string XmpSignature = "http://ns.adobe.com/xap/1.0/\0";
        private const string XmpExtSignature = "http://ns.adobe.com/xmp/extension/\0";

        public static bool HasJpegExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".jfif";
        }

        /// <summary>可参与合成的照片格式（非 JPEG 会在打包前转码）。</summary>
        public static bool IsSupportedPhoto(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                case ".jfif":
                case ".png":
                case ".bmp":
                case ".tif":
                case ".tiff":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>把 PNG / BMP / TIFF 等转成 JPEG（透明区域铺白底），返回临时文件路径。</summary>
        public static string ConvertToJpeg(string sourcePath, int quality = 92)
        {
            string temp = Path.Combine(Path.GetTempPath(), "mps_" + Guid.NewGuid().ToString("N") + ".jpg");
            using (Image source = Image.FromFile(sourcePath))
            {
                bool hasAlpha = Image.IsAlphaPixelFormat(source.PixelFormat);
                Bitmap canvas = null;
                try
                {
                    Image target = source;
                    if (hasAlpha)
                    {
                        canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
                        using (Graphics g = Graphics.FromImage(canvas))
                        {
                            g.Clear(Color.White);
                            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
                        }
                        target = canvas;
                    }

                    var codec = FindEncoder(ImageFormat.Jpeg) ?? ImageCodecInfo.GetImageEncoders()[0];
                    using (var parameters = new EncoderParameters(1))
                    {
                        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                        target.Save(temp, codec, parameters);
                    }
                }
                finally
                {
                    if (canvas != null) canvas.Dispose();
                }
            }
            return temp;
        }

        private static ImageCodecInfo FindEncoder(ImageFormat format)
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                if (codec.FormatID == format.Guid) return codec;
            return null;
        }

        public static bool HasVideoExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mov" || ext == ".mp4" || ext == ".m4v";
        }

        public static bool IsJpegFile(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    return fs.ReadByte() == 0xFF && fs.ReadByte() == 0xD8;
                }
            }
            catch { return false; }
        }

        public static VideoMeta InspectVideo(string path)
        {
            var meta = new VideoMeta();
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    meta.Size = fs.Length;
                    long length = fs.Length;
                    long pos = 0;
                    var header = new byte[16];
                    while (pos + 8 <= length)
                    {
                        fs.Position = pos;
                        if (fs.Read(header, 0, 8) != 8) break;
                        long size = ReadUInt32BE(header, 0);
                        string type = Encoding.ASCII.GetString(header, 4, 4);
                        int headerSize = 8;
                        if (size == 1)
                        {
                            if (fs.Read(header, 8, 8) != 8) break;
                            size = (long)ReadUInt64BE(header, 8);
                            headerSize = 16;
                        }
                        else if (size == 0)
                        {
                            size = length - pos;
                        }
                        if (size < 8) break;

                        if (type == "ftyp")
                        {
                            meta.IsIsoBmff = true;
                            var brand = new byte[4];
                            if (fs.Read(brand, 0, 4) == 4) meta.Brand = Encoding.ASCII.GetString(brand, 0, 4);
                        }
                        else if (type == "moov")
                        {
                            long payload = size - headerSize;
                            if (payload > 0 && payload < 64L * 1024 * 1024)
                            {
                                var buf = new byte[payload];
                                fs.Position = pos + headerSize;
                                ReadFully(fs, buf, 0, buf.Length);
                                ParseMoov(buf, meta);
                            }
                            break;
                        }
                        pos += size;
                    }
                }
            }
            catch { }
            return meta;
        }

        private static void ParseMoov(byte[] moov, VideoMeta meta)
        {
            meta.Codec = DetectCodec(Encoding.ASCII.GetString(moov, 0, Math.Min(moov.Length, 200000)));
            int mvhd = IndexOf(moov, Encoding.ASCII.GetBytes("mvhd"), 0);
            if (mvhd < 4) return;
            int at = mvhd + 4;
            if (at + 20 > moov.Length) return;
            int version = moov[at];
            if (version == 1)
            {
                if (at + 32 > moov.Length) return;
                uint timescale = ReadUInt32BE(moov, at + 20);
                ulong duration = ReadUInt64BE(moov, at + 24);
                if (timescale > 0) meta.DurationSeconds = duration / (double)timescale;
            }
            else
            {
                uint timescale = ReadUInt32BE(moov, at + 12);
                uint duration = ReadUInt32BE(moov, at + 16);
                if (timescale > 0) meta.DurationSeconds = duration / (double)timescale;
            }
        }

        private static string DetectCodec(string ascii)
        {
            if (ascii.IndexOf("hvc1", StringComparison.Ordinal) >= 0 || ascii.IndexOf("hev1", StringComparison.Ordinal) >= 0) return "HEVC";
            if (ascii.IndexOf("avc1", StringComparison.Ordinal) >= 0) return "H.264";
            if (ascii.IndexOf("av01", StringComparison.Ordinal) >= 0) return "AV1";
            if (ascii.IndexOf("vp09", StringComparison.Ordinal) >= 0) return "VP9";
            return "";
        }

        /// <summary>粗略判断文件是否已经是动态照片（避免重复拼接）。</summary>
        public static bool LooksLikeMotionPhoto(string path)
        {
            try
            {
                long probe = 0;
                using (var fs = File.OpenRead(path))
                {
                    var buf = new byte[512 * 1024];
                    int n = fs.Read(buf, 0, buf.Length);
                    probe = n;
                    if (n <= 0) return false;
                    string head = Encoding.ASCII.GetString(buf, 0, n);
                    return head.IndexOf("MotionPhoto", StringComparison.Ordinal) > 0
                        || head.IndexOf("MicroVideo", StringComparison.Ordinal) > 0
                        || head.IndexOf("GCamera:", StringComparison.Ordinal) > 0;
                }
            }
            catch { return false; }
        }

        public static MuxOutcome Merge(string jpegPath, string videoPath, string outputPath, MuxOptions options)
        {
            var result = new MuxOutcome { SourcePhoto = jpegPath };
            if (options == null) options = new MuxOptions();
            string tempPhoto = null;
            try
            {
                string workingPhoto = jpegPath;
                if (!IsJpegFile(jpegPath))
                {
                    // PNG / BMP / TIFF 等先转码成 JPEG，再进入标准合成流程
                    tempPhoto = ConvertToJpeg(jpegPath);
                    workingPhoto = tempPhoto;
                    result.Converted = true;
                }

                byte[] jpeg = File.ReadAllBytes(workingPhoto);
                if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
                    throw new InvalidDataException("不是有效的 JPEG 图像");

                var videoInfo = new FileInfo(videoPath);
                long videoLength = videoInfo.Length;
                if (videoLength <= 0) throw new InvalidDataException("视频文件为空");

                // 迭代到长度稳定：XMP 里的长度字段是十进制文本，位数变化会改变整段长度
                long imageLengthGuess = jpeg.Length + 2048;
                byte[] rebuiltJpeg = null;
                for (int i = 0; i < 6; i++)
                {
                    byte[] xmp = BuildXmpPacket(imageLengthGuess, videoLength, options.PresentationTimestampUs, options.WriteLegacyTags);
                    rebuiltJpeg = RebuildJpeg(jpeg, xmp);
                    if (rebuiltJpeg.Length == imageLengthGuess) break;
                    imageLengthGuess = rebuiltJpeg.Length;
                }

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    output.Write(rebuiltJpeg, 0, rebuiltJpeg.Length);
                    using (var video = new FileStream(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                    {
                        video.CopyTo(output, 1 << 20);
                    }
                    output.Flush(true);
                }

                if (options.PreserveFileTime)
                {
                    try { File.SetLastWriteTime(outputPath, File.GetLastWriteTime(jpegPath)); } catch { }
                }

                result.Ok = true;
                result.Output = outputPath;
                result.PhotoBytes = rebuiltJpeg.Length;
                result.VideoBytes = videoLength;
                result.Size = new FileInfo(outputPath).Length;
                result.Codec = InspectVideo(videoPath).Codec;
                return result;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                return result;
            }
            finally
            {
                if (tempPhoto != null) { try { File.Delete(tempPhoto); } catch { } }
            }
        }

        /// <summary>构造 Google Motion Photo 所需的 XMP 数据包（同时写新版 Container 与旧版 MicroVideo 标签）。</summary>
        public static byte[] BuildXmpPacket(long imageLength, long videoLength, int timestampUs, bool writeLegacy)
        {
            var sb = new StringBuilder(1400);
            sb.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
            sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"Motion Photo Studio 1.0\">\n");
            sb.Append(" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
            sb.Append("  <rdf:Description rdf:about=\"\"\n");
            sb.Append("    xmlns:Container=\"http://ns.google.com/photos/1.0/container/\"\n");
            sb.Append("    xmlns:Item=\"http://ns.google.com/photos/1.0/container/item/\"\n");
            sb.Append("    xmlns:GCamera=\"http://ns.google.com/photos/1.0/camera/\"\n");
            sb.Append("   GCamera:MotionPhoto=\"1\"\n");
            sb.Append("   GCamera:MotionPhotoVersion=\"1\"\n");
            sb.Append("   GCamera:MotionPhotoPresentationTimestampUs=\"").Append(timestampUs).Append("\"");
            if (writeLegacy)
            {
                sb.Append("\n   GCamera:MicroVideo=\"1\"\n");
                sb.Append("   GCamera:MicroVideoVersion=\"1\"\n");
                sb.Append("   GCamera:MicroVideoOffset=\"").Append(videoLength).Append("\"\n");
                sb.Append("   GCamera:MicroVideoPresentationTimestampUs=\"").Append(timestampUs).Append("\"");
            }
            sb.Append(">\n");
            sb.Append("   <Container:Directory>\n");
            sb.Append("    <rdf:Seq>\n");
            sb.Append("     <rdf:li rdf:parseType=\"Resource\">\n");
            sb.Append("      <Container:Item Item:Semantic=\"Primary\" Item:Mime=\"image/jpeg\" Item:Length=\"").Append(imageLength).Append("\" Item:Padding=\"0\"/>\n");
            sb.Append("     </rdf:li>\n");
            sb.Append("     <rdf:li rdf:parseType=\"Resource\">\n");
            sb.Append("      <Container:Item Item:Semantic=\"MotionPhoto\" Item:Mime=\"video/mp4\" Item:Length=\"").Append(videoLength).Append("\" Item:Padding=\"0\"/>\n");
            sb.Append("     </rdf:li>\n");
            sb.Append("    </rdf:Seq>\n");
            sb.Append("   </Container:Directory>\n");
            sb.Append("  </rdf:Description>\n");
            sb.Append(" </rdf:RDF>\n");
            sb.Append("</x:xmpmeta>\n");
            sb.Append("<?xpacket end=\"w\"?>");
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        /// <summary>把 XMP 作为 APP1 段写进 JPEG，并移除原有的 XMP 段。</summary>
        public static byte[] RebuildJpeg(byte[] jpeg, byte[] xmp)
        {
            byte[] signature = Encoding.ASCII.GetBytes(XmpSignature);
            byte[] extSignature = Encoding.ASCII.GetBytes(XmpExtSignature);

            int app1Length = 2 + signature.Length + xmp.Length;
            if (app1Length > 0xFFFF) throw new InvalidDataException("XMP 数据过长，无法写入 JPEG");

            using (var output = new MemoryStream(jpeg.Length + app1Length + 64))
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xD8);

                int pos = 2;
                // 保留 JFIF APP0 在最前面
                if (pos + 4 <= jpeg.Length && jpeg[pos] == 0xFF && jpeg[pos + 1] == 0xE0)
                {
                    int app0Len = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
                    if (pos + app0Len + 2 <= jpeg.Length)
                    {
                        output.Write(jpeg, pos, app0Len + 2);
                        pos += app0Len + 2;
                    }
                }

                // 新的 XMP APP1
                output.WriteByte(0xFF);
                output.WriteByte(0xE1);
                output.WriteByte((byte)(app1Length >> 8));
                output.WriteByte((byte)(app1Length & 0xFF));
                output.Write(signature, 0, signature.Length);
                output.Write(xmp, 0, xmp.Length);

                bool restCopied = false;
                while (pos + 4 <= jpeg.Length)
                {
                    if (jpeg[pos] != 0xFF) break;
                    byte marker = jpeg[pos + 1];

                    if (marker == 0xDA) // SOS，后面是扫描数据，整段照抄
                    {
                        output.Write(jpeg, pos, jpeg.Length - pos);
                        pos = jpeg.Length;
                        restCopied = true;
                        break;
                    }
                    if (marker == 0xD9) break; // EOI
                    if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                    {
                        output.Write(jpeg, pos, 2);
                        pos += 2;
                        continue;
                    }
                    if (marker == 0x00) { pos++; continue; }

                    int segLength = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
                    int segEnd = pos + 2 + segLength;
                    if (segEnd > jpeg.Length || segLength < 2) break;

                    bool drop = false;
                    if (marker == 0xE1 && segLength > signature.Length)
                    {
                        if (StartsWith(jpeg, pos + 4, signature)) drop = true;
                        else if (StartsWith(jpeg, pos + 4, extSignature)) drop = true;
                    }
                    if (!drop) output.Write(jpeg, pos, segEnd - pos);
                    pos = segEnd;
                }

                if (!restCopied && pos < jpeg.Length) output.Write(jpeg, pos, jpeg.Length - pos);

                // 保证以 EOI 结尾
                byte[] built = output.ToArray();
                if (built.Length < 2 || built[built.Length - 2] != 0xFF || built[built.Length - 1] != 0xD9)
                {
                    output.WriteByte(0xFF);
                    output.WriteByte(0xD9);
                    built = output.ToArray();
                }
                return built;
            }
        }

        private static bool StartsWith(byte[] data, int offset, byte[] pattern)
        {
            if (offset < 0 || offset + pattern.Length > data.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
                if (data[offset + i] != pattern[i]) return false;
            return true;
        }

        private static int IndexOf(byte[] data, byte[] pattern, int start)
        {
            for (int i = start; i <= data.Length - pattern.Length; i++)
            {
                bool hit = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j]) { hit = false; break; }
                }
                if (hit) return i;
            }
            return -1;
        }

        private static void ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, offset + read, count - read);
                if (n <= 0) break;
                read += n;
            }
        }

        private static uint ReadUInt32BE(byte[] b, int o)
        {
            return ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
        }

        private static ulong ReadUInt64BE(byte[] b, int o)
        {
            return ((ulong)ReadUInt32BE(b, o) << 32) | ReadUInt32BE(b, o + 4);
        }
    }
}
