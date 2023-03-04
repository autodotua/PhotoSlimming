public class Configs
{
    public string Name { get; set; } = "未命名";
    /// <summary>
    /// 是否在处理前清空目标目录
    /// </summary>
    public bool ClearAllBeforeRunning { get; set; } = false;
    /// <summary>
    /// 需要压缩的文件的后缀名
    /// </summary>
    public string[] CompressExtensions { get; set; } = { "heic", "heif", "jpg", "jpeg" };
    /// <summary>
    /// 直接复制的文件的后缀名
    /// </summary>
    public string[] CopyDirectlyExtensions { get; set; } = { "png", "gpx", "doc", "docx", "ppt", "pptx", "xls", "xlsx", "pdf" };
    /// <summary>
    /// 需要修复修改时间的文件的后缀名
    /// </summary>
    public string[] RepairModifiedTimeExtensions { get; set; } = { "jpg", "jpeg", "heif", "heic" };
    /// <summary>
    /// 源目录
    /// </summary>
    public string SourceDir { get; set; } = @"C:\源\目录";
    /// <summary>
    /// 目标目录
    /// </summary>
    public string DistDir { get; set; } = @"C:\目标\目录";
    /// <summary>
    /// 需要修复文件修改时间的目录
    /// </summary>
    public string RepairTimeDir { get; set; } = @"O:\要修复文件修改时间\的目录";
    /// <summary>
    /// 黑名单（正则）（匹配路径）
    /// </summary>
    public string BlackList { get; set; } = "网";
    /// <summary>
    ///  白名单（正则）（匹配不带后缀名的文件名）
    /// </summary>
    public string WhiteList { get; set; } = ".*";
    /// <summary>
    /// 修复文件修改时间时，最大可接受的Exif和修改时间的时间差（秒）
    /// </summary>
    public double MaxDurationTolerance { get; set; } = 60;
    /// <summary>
    /// 最大长边像素（大于则进行缩放）
    /// </summary>
    public int MaxLongSize { get; set; } = 10000;
    /// <summary>
    /// 最大短边像素（大于则进行缩放）
    /// </summary>
    public int MaxShortSize { get; set; } = 5000;
    /// <summary>
    /// 质量（1-100）
    /// </summary>
    public int Quality { get; set; } = 50;
    /// <summary>
    /// 遇到已经存在的文件是否跳过（而不是覆盖）
    /// </summary>
    public bool SkipIfExist { get; set; } = true;
    /// <summary>
    /// 并行线程数（针对压缩和文件修改时间修复）
    /// </summary>
    public int Thread { get; set; } = 4;
    /// <summary>
    /// 压缩后的文件输出类型/扩展名
    /// </summary>
    public string OutputFormat { get; set; } = "jpg";
    /// <summary>
    /// 最深文件层级，例如设置为2，相对路径为D1/D2/D3/D4/File.ext，则目标相对路径将改为D1/D2/D3-D4-File.ext
    /// </summary>
    public int DeepestLevel { get; set; } = 10000;
}
