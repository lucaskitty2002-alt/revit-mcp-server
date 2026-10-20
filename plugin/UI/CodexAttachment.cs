using System;
using System.IO;
using System.Linq;

namespace revit_mcp_plugin.UI
{
    // Keep the selected bytes stable even if the original file changes before Send.
    public sealed class CodexAttachment
    {
        public const int MaxCount = 4;
        public const int MaxFileBytes = 10 * 1024 * 1024;
        public const int MaxTotalBytes = 20 * 1024 * 1024;
        private readonly byte[] _bytes;
        private readonly string _extension;
        public string Name { get; }
        public int Length => _bytes.Length;
        public string Label => Name + " (" + (Length / 1024d).ToString("N0") + " KB)";

        private CodexAttachment(string name, string extension, byte[] bytes)
        { Name = name; _extension = extension; _bytes = bytes; }

        public static CodexAttachment Load(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                throw new InvalidOperationException("Choose a PNG or JPEG drawing. Export PDF/CAD pages as images first.");
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0 || stream.Length > MaxFileBytes)
                    throw new InvalidOperationException("Each drawing must be non-empty and at most 10 MB.");
                bytes = new byte[(int)stream.Length];
                int offset = 0, read;
                while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
                if (offset != bytes.Length) throw new IOException("The drawing changed while it was being read. Choose it again.");
            }
            bool png = bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            bool jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
            if ((extension == ".png" && !png) || (extension != ".png" && !jpeg))
                throw new InvalidOperationException("The file contents do not match a PNG/JPEG image.");
            return new CodexAttachment(Path.GetFileName(path), png ? ".png" : ".jpg", bytes);
        }

        public Stream OpenRead() => new MemoryStream(_bytes, false);

        internal string WriteSnapshot(string directory)
        {
            string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + _extension);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(_bytes, 0, _bytes.Length);
            return path;
        }

        public static void ValidateBatch(CodexAttachment[] attachments)
        {
            if (attachments == null) throw new ArgumentNullException(nameof(attachments));
            if (attachments.Length > MaxCount || attachments.Any(a => a == null))
                throw new InvalidOperationException("Attach up to four drawings per message.");
            if (attachments.Sum(a => (long)a.Length) > MaxTotalBytes)
                throw new InvalidOperationException("The drawings in one message must total at most 20 MB.");
        }
    }
}
