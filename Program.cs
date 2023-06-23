using ImageMagick;
using MetadataExtractor;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Directory = System.IO.Directory;

public class Program
{
    private static readonly string configDir = "configs";
    public Configs Config { get; set; }

    public static void Main(string[] args)
    {
        WL("============照片瘦身============");

        Program p = new Program();

        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
        var configFiles = Directory.EnumerateFiles(configDir, "*.json").ToList();
        List<Configs> configs = new List<Configs>();
        foreach (var file in configFiles)
        {
            try
            {
                configs.Add(JsonSerializer.Deserialize<Configs>(File.ReadAllText(file)));
            }
            catch (Exception ex)
            {
                WL($"加载{Path.GetFileName(file)}失败：{ex.Message}");
            }
        }
        Configs config = null;
        if (configs.Any())
        {
            if (configs.Count == 1)
            {
                config = configs[0];
            }
            else
            {
                WL("请选择配置文件");
                int index = 0;
                foreach (var c in configs)
                {
                    WL($"{++index}：{c.Name}");
                }
                string input;
                do
                {
                    input = Console.ReadLine().Trim();
                }
                while (!int.TryParse(input, out index) && index <= configs.Count && index >= 1);
                config = configs[index - 1];
            }
            WL("使用配置文件：" + config.Name);
            p.Config = config;

#if DEBUG
            WL("DEBUG模式，配置将被重写");
            p.Config.SourceDir = @"O:\旧事重提";
            p.Config.DistDir = @"Z:\mobile\旧事重提\历史\截屏";
            p.Config.DeepestLevel = 2;
            p.Config.Thread = 8;
            p.Config.BlackList = "(历史)";
            p.Config.OutputFormat = "jpg";
#endif
        }
        else
        {
            config = new Configs();
            File.WriteAllText(Path.Combine(configDir, "示例.json"), JsonSerializer.Serialize(config, new JsonSerializerOptions()
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            }));
            Console.WriteLine($"已生成{Path.Combine(configDir, "示例.json")}，修改后运行本程序。");
            Console.ReadKey();
            return;
        }
        try
        {
            p.Start();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(e);
            Console.ReadKey();
        }
    }

    private static readonly object lockObj = new object();

    private readonly ConsoleColor defaultConsoleColor = Console.ForegroundColor;

    private int allFilesCount = 0;

    private List<FileInfo> compressFiles = new List<FileInfo>();

    private long compressFilesLength = 0;

    private List<FileInfo> copyFiles = new List<FileInfo>();

    private long copyFilesLength = 0;

    private List<string> deleteFiles = new List<string>();

    private int excludeFilesCount = 0;

    private int index = 0;

    private Regex rCompress;

    private Regex rCopy;

    private Regex rBlack;

    private Regex rWhite;

    private Regex rRepairTime;

    private ConcurrentDictionary<string,object> sourceFiles = new ConcurrentDictionary<string, object>();

    private static Action<string> W = p => Console.Write(p);

    private static Action<string> WL = p => Console.WriteLine(p);

    public void Start()
    {
        Initialize();
        Console.WriteLine("请输入需要进行的操作：");
        Console.WriteLine("1：复制和压缩指定文件");
        Console.WriteLine("2：删除目标目录中已经不存在对应源文件的文件");
        Console.WriteLine("3：将文件修改时间修复为照片Exif时间");
        int index;
        string input;
        do
        {
            input = Console.ReadLine().Trim();
        }
        while (!int.TryParse(input, out index) && index is <= 3 and >= 1);
        Console.WriteLine();
        switch (index)
        {
            case 1: CopyAndCompress(); break;
            case 2: DeleteFilesThatExistedInDistDirButNotInSrcDir(); break;
            case 3: RepairModifiedTime(); break;
        }
        Console.ReadKey();
    }

    public void CopyAndCompress()
    {
        WL("正在寻找文件");

        CheckFiles();
        WL($"共找到{allFilesCount}个文件，筛选{sourceFiles.Count}个，排除了{excludeFilesCount}个");
        WL($"直接复制{copyFiles.Count}个，大小{copyFilesLength / (1024 * 1024)}MB");
        WL($"需要压缩{compressFiles.Count}个，大小{compressFilesLength / (1024 * 1024)}MB");
        WL($"按回车键继续...");
        while (Console.ReadKey().Key != ConsoleKey.Enter)
        {
        }
        WL("");

        if (Config.ClearAllBeforeRunning)
        {
            if (Directory.Exists(Config.DistDir))
            {
                for (int i = 5; i >= 1; i--)
                {
                    WL($"目标目录{Config.DistDir}已存在，{i}秒后将清空目录");
                    Thread.Sleep(1000);
                }
                Directory.Delete(Config.DistDir, true);
            }
        }
        if (!Directory.Exists(Config.DistDir))
        {
            Directory.CreateDirectory(Config.DistDir);
        }

        WL("开始压缩文件");
        Compress();
        WL("开始复制文件");
        index = 0;
        Copy();
        WL("完成");
    }

    public void Initialize()
    {
        rCopy = new Regex(@$"\.({string.Join('|', Config.CopyDirectlyExtensions)})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        rCompress = new Regex(@$"\.({string.Join('|', Config.CompressExtensions)})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        rRepairTime = new Regex(@$"\.({string.Join('|', Config.RepairModifiedTimeExtensions)})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        rBlack = new Regex(Config.BlackList);
        rWhite = new Regex(string.IsNullOrWhiteSpace(Config.WhiteList) ? ".*" : Config.WhiteList);
    }

    public void RepairModifiedTime()
    {
        WL("正在寻找文件");
        WL($"文件地址      文件时间 => 照片时间");
        var fileCount = 0;
        Dictionary<FileInfo, DateTime> fileExifTimes = new Dictionary<FileInfo, DateTime>();
        Parallel.ForEach(new DirectoryInfo(Config.RepairTimeDir).EnumerateFiles("*", SearchOption.AllDirectories),
            new ParallelOptions() { MaxDegreeOfParallelism = Config.Thread > 0 ? Config.Thread : -1 }, file =>
              {
                  if (rRepairTime.IsMatch(file.Name))
                  {
                      var exifTime = FindExifTime(file.FullName);

                      if (exifTime.HasValue)
                      {
                          var fileTime = file.LastWriteTime;
                          var duration = (exifTime.Value - fileTime).Duration();
                          if (duration.TotalSeconds > Config.MaxDurationTolerance)
                          {
                              fileExifTimes.Add(file, exifTime.Value);
                              if (duration.TotalDays > 365)
                              {
                                  Console.ForegroundColor = ConsoleColor.Red;
                              }
                              else if (duration.TotalDays > 30)
                              {
                                  Console.ForegroundColor = ConsoleColor.DarkYellow;
                              }
                              else if (duration.TotalDays > 1)
                              {
                                  Console.ForegroundColor = ConsoleColor.Yellow;
                              }
                              else
                              {
                                  Console.ForegroundColor = defaultConsoleColor;
                              }
                              WL($"{file.FullName}     {fileTime:yyyy-MM-dd HH:mm} => {exifTime:yyyy-MM-dd HH:mm}");
                          }
                      }

                      lock (lockObj)
                      {
                          if (++fileCount % 1000 == 0)
                          {
                              Console.ForegroundColor = defaultConsoleColor;
                              WL("已检测" + fileCount + "个");
                          }
                      }
                  }
              });
        Console.ForegroundColor = defaultConsoleColor;
        WL($"共找到{fileExifTimes.Count}个文件修改时间与Exif时间不匹配的照片");
        WL($"按回车键开始修改");
        while (Console.ReadKey().Key != ConsoleKey.Enter)
        {
        }
        foreach (var file in fileExifTimes.Keys)
        {
            file.LastWriteTime = fileExifTimes[file];
            WL("已应用：" + file.FullName);
        }
        WL("完成");
    }

    private void CheckFiles()
    {
        foreach (var file in new DirectoryInfo(Config.SourceDir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (++allFilesCount % 100 == 0)
            {
                W($"{allFilesCount}\t");
                if (allFilesCount % 1000 == 0)
                {
                    WL("");
                }
            }

            if (rBlack.IsMatch(file.FullName)
                || !rWhite.IsMatch(Path.GetFileNameWithoutExtension(file.Name)))
            {
                excludeFilesCount++;
                continue;
            }

            if (rCompress.IsMatch(file.Name))
            {
                sourceFiles.TryAdd(Path.GetRelativePath(Config.SourceDir, file.FullName), null);
                compressFiles.Add(file);
                compressFilesLength += file.Length;
            }
            else if (rCopy.IsMatch(file.Name))
            {
                sourceFiles.TryAdd(Path.GetRelativePath(Config.SourceDir, file.FullName),null);
                copyFiles.Add(file);
                copyFilesLength += file.Length;
            }

        }
        WL("");
        WL("");
    }

    private void Compress()
    {
        if (Config.Thread != 1)
        {
            if (Config.Thread <= 0)
            {
                Parallel.ForEach(compressFiles, CompressSingle);
            }
            else
            {
                Parallel.ForEach(compressFiles, new ParallelOptions() { MaxDegreeOfParallelism = Config.Thread }, CompressSingle);
            }
        }
        else
        {
            foreach (var file in compressFiles)
            {
                CompressSingle(file);
            }
        }
    }

    private string GetDistPath(string sourceFileName, string newExtension, out string subPath)
    {
        return GetDistPath(sourceFileName, newExtension, true, out subPath);
    }

    private string GetDistPath(string sourceFileName, string newExtension, bool addToSet, out string subPath)
    {
        char spliiter = sourceFileName.Contains('\\') ? '\\' : '/';
        subPath = Path.IsPathRooted(sourceFileName) ? Path.GetRelativePath(Config.SourceDir, sourceFileName) : sourceFileName;
        string filename = Path.GetFileName(subPath);
        string dir = Path.GetDirectoryName(subPath);
        int level = dir.Count(p => p == spliiter) + 1;
        if (level > Config.DeepestLevel)
        {
            string[] dirParts = dir.Split(spliiter);
            dir = string.Join(spliiter, dirParts[..Config.DeepestLevel]);
            filename = $"{string.Join('-', dirParts[Config.DeepestLevel..])}-{filename}";
            subPath = Path.Combine(dir, filename);
        }
        if (addToSet)
        {
            sourceFiles.TryAdd(subPath,null);
        }
        if (newExtension != null)
        {
            subPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(filename) + "." + newExtension);
        }
        return Path.Combine(Config.DistDir, subPath);

    }

    private void CompressSingle(FileInfo file)
    {
        int thisIndex = 0;
        lock (lockObj)
        {
            thisIndex = index++;
        }
        string distPath = GetDistPath(file.FullName, Config.OutputFormat, out string subPath);
        if (File.Exists(distPath))
        {
            if (Config.SkipIfExist)
            {
                WL($"{index}\t\t已存在 {subPath} 跳过");
                return;
            }
            else
            {
                File.Delete(distPath);
            }
        }
        string dir = Path.GetDirectoryName(distPath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        Console.OutputEncoding = System.Text.Encoding.Unicode;
        try
        {
            using (MagickImage image = new MagickImage(file))
            {
                bool portrait = image.Height > image.Width;
                int width = portrait ? image.Height : image.Width;
                int height = portrait ? image.Width : image.Height;
                if (width > Config.MaxLongSize || height > Config.MaxShortSize)
                {
                    double ratio = width > Config.MaxLongSize ? 1.0 * Config.MaxLongSize / width : 1;
                    ratio = Math.Min(ratio, height > Config.MaxShortSize ? 1.0 * Config.MaxShortSize / height : 1);
                    width = (int)(width * ratio);
                    height = (int)(height * ratio);
                    if (portrait)
                    {
                        (width, height) = (height, width);
                    }
                    image.AdaptiveResize(width, height);
                }
                image.Quality = Config.Quality;
                image.Write(distPath);
            }
            File.SetLastWriteTime(distPath, file.LastWriteTime);

            FileInfo distFile = new FileInfo(distPath);
            if (distFile.Length > file.Length)
            {
                file.CopyTo(distPath, true);
                Console.ForegroundColor = ConsoleColor.Yellow;
                WL($"{thisIndex}\t\t压缩{subPath}后，大小大于原文件");
                Console.ForegroundColor = ConsoleColor.White;
            }

            WL($"{thisIndex}\t\t压缩  {file.Length / 1024d / 1024:0.00}MB => {distFile.Length / 1024d / 1024:0.00}MB    \t{subPath}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            WL($"{thisIndex}\t\t压缩{subPath}失败：{ex.Message}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

    private void Copy()
    {
        foreach (var file in copyFiles)
        {
            index++;

            string distPath = GetDistPath(file.FullName, null, out string subPath);
            if (File.Exists(distPath))
            {
                if (Config.SkipIfExist)
                {
                    WL($"{index}\t\t已存在 {subPath} 跳过");
                    continue;
                }
                else
                {
                    File.Delete(distPath);
                }
            }
            string dir = Path.GetDirectoryName(distPath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            W($"{index}\t\t正在复制 {subPath}...");
            file.CopyTo(distPath);
            WL("完成");
        }
    }

    private void DeleteFilesThatExistedInDistDirButNotInSrcDir()
    {
        WL("正在寻找文件");
        CheckFiles();
        WL($"共找到{sourceFiles.Count}个需要的文件");
        WL("正在寻找不需要的文件");

        int index = 0;

        var desiredDistFiles = copyFiles
            .Select(file => GetDistPath(file.FullName, null, false, out _))
             .Concat(compressFiles
                .Select(file => GetDistPath(file.FullName, Config.OutputFormat, false, out _)))
             .ToHashSet();


        foreach (var file in Directory
            .EnumerateFiles(Config.DistDir, "*", SearchOption.AllDirectories)
             .Where(p => !rBlack.IsMatch(p)))
        {
            //string subPath = Path.GetRelativePath(Config.DistDir, file);
            if (!desiredDistFiles.Contains(file))
            {
                WL(++index + "：" + file);
                deleteFiles.Add(file);
            }
        }
        if (deleteFiles.Count == 0)
        {
            WL($"没有需要删除的文件");
            return;
        }
        WL($"按回车键开始删除");
        while (Console.ReadKey().Key != ConsoleKey.Enter)
        {
        }
        index = 0;
        foreach (var file in deleteFiles)
        {
            File.Delete(file);
            WL(++index + "：已删除 " + file);
        }
        WL($"完成");
    }

    private DateTime? FindExifTime(string file)
    {
        var directories = ImageMetadataReader.ReadMetadata(file);
        MetadataExtractor.Directory? dir = null;
        if ((dir = directories.FirstOrDefault(p => p.Name == "Exif SubIFD")) != null)
        {
            if (dir.TryGetDateTime(36867, out DateTime time1))
            {
                return time1;
            }
            if (dir.TryGetDateTime(36868, out DateTime time2))
            {
                return time2;
            }
        }
        if ((dir = directories.FirstOrDefault(p => p.Name == "Exif IFD0")) != null)
        {
            if (dir.TryGetDateTime(306, out DateTime time))
            {
                return time;
            }
        }



        return null;
    }
}