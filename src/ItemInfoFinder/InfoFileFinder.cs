using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace ItemInfoFinder
{
    public class InfoFileFinder
    {
        public const string DataDir = @"Data";
        public const string DataFileExtension = ".sbc";
        public const string DataFilePattern = "*.sbc";
        public const string LegacyModFilePattern = "*_legacy.bin";
        public const string SeContentDir = @"common\SpaceEngineers\Content";
        public const string SeModsDir = @"workshop\content\244850";
        private readonly string _steamLibraryDirectory;

        public InfoFileFinder(string steamLibraryDirectory)
        {
            _steamLibraryDirectory = steamLibraryDirectory;
        }

        public IEnumerable<InfoFile> EnumerateDataFiles()
        {
            var seDataDir = Path.Combine(_steamLibraryDirectory, SeContentDir, DataDir);
            var buildInDataFiles = Directory.GetFiles(seDataDir, DataFilePattern);
            foreach (var path in buildInDataFiles)
            {
                yield return new InfoFileOnDisc(0L, path);
            }

            var seModsDir = Path.Combine(_steamLibraryDirectory, SeModsDir);
            foreach (var modPath in Directory.GetDirectories(seModsDir))
            {
                var modIdStr = Path.GetFileName(modPath);
                if (Int64.TryParse(modIdStr, out long modId))
                {
                    var legacyArchives = Directory.GetFiles(modPath, LegacyModFilePattern);
                    foreach (var archivePath in legacyArchives)
                    {
                        using (var archive = ZipFile.OpenRead(archivePath))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                var isDataFile = String.Equals(Path.GetDirectoryName(entry.FullName), DataDir, StringComparison.OrdinalIgnoreCase)
                                    && String.Equals(Path.GetExtension(entry.FullName), DataFileExtension, StringComparison.Ordinal);

                                if (isDataFile)
                                    yield return new InfoFileInArchive(modId, entry);
                            }
                        }
                    }

                    var modFilesDir = Path.Combine(modPath, DataDir);
                    if (Directory.Exists(modFilesDir))
                    {
                        var modDataFiles = Directory.GetFiles(modFilesDir, DataFilePattern);
                        foreach (var path in modDataFiles)
                        {
                            yield return new InfoFileOnDisc(modId, path);
                        }
                    }
                }
            }
        }

        public abstract class InfoFile
        {
            protected InfoFile(long modId)
            {
                ModId = modId;
            }

            public long ModId { get; }

            public abstract Stream Open();
        }

        public class InfoFileInArchive : InfoFile
        {
            public InfoFileInArchive(long modId, ZipArchiveEntry entry)
                : base(modId)
            {
                Entry = entry;
            }

            public ZipArchiveEntry Entry { get; }

            public override Stream Open()
            {
                return Entry.Open();
            }

            public override string ToString()
            {
                return String.Format("{0}: [zip] {1}", ModId, Entry.FullName);
            }
        }

        public class InfoFileOnDisc : InfoFile
        {
            public InfoFileOnDisc(long modId, string filePath)
                : base(modId)
            {
                FilePath = filePath;
            }

            public string FilePath { get; }

            public override Stream Open()
            {
                return File.OpenRead(FilePath);
            }

            public override string ToString()
            {
                return String.Format("{0}: [file] {1}", ModId, FilePath);
            }
        }
    }
}
