using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quick.Build;
using SharpCompress.Archives;
using YiQiDong.Core;

var appFolder = QbFolder.GetAppFolder();
if (appFolder == Environment.CurrentDirectory)
    Environment.CurrentDirectory = Path.GetFullPath("../../../../../");

//准备目录变量
var baseFolder = Environment.CurrentDirectory;

if (!Directory.Exists("bin"))
    Directory.CreateDirectory("bin");

var Id = "dotnet";
var Version = "x.x.x";

string getDotNetRuntimeAspNetCoreDownloadUrl(HttpClient client, string dotnetVersion, string arch)
{
    var url = $"https://dotnet.microsoft.com/zh-cn/download/dotnet/thank-you/runtime-aspnetcore-{dotnetVersion}-{arch}-binaries";
    var html = client.GetStringAsync(url).Result;
    var document = new HtmlAgilityPack.HtmlDocument();
    document.LoadHtml(html);
    var aNode = document.DocumentNode.SelectSingleNode("//a[@id='directLink']");
    var href = aNode.GetAttributeValue("href", null);
    return href;
}

var extractionOptions = new SharpCompress.Common.ExtractionOptions()
{
    ExtractFullPath = true,
    Overwrite = true,
    PreserveFileTime = true
};

var displayDownloadProgress = new Action<QbNet.TransferProgress>(t =>
{
    QbConsole.DisplaySameLineInConsole($"[{t.Current * 100 / t.Total}%]进度：{t.Current.ToString("N0")}/{t.Total.ToString("N0")}，速度：{t.Speed.ToString("N0")}，剩余时间：{t.RemainingTime}");
});

HttpClient client = new HttpClient();
var archDict = new Dictionary<string, string>()
{
    ["win-x86"] = "windows-x86",
    ["win-x64"] = "windows-x64",
    ["linux-x64"] = "linux-x64",
    ["linux-arm64"] = "linux-arm64",
    ["linux-arm"] = "linux-arm32",
    ["osx-x64"] = "macos-x64",
    ["osx-arm64"] = "macos-arm64"
};
Console.WriteLine("请选择打包架构(一个都不勾选代表全选)：");
var selectArchs = QbSelect.MultiSelect(archDict.ToArray(), selectedForegroundColor: ConsoleColor.Green);
if (selectArchs == null || selectArchs.Length == 0)
    selectArchs = archDict.Keys.ToArray();

foreach (var arch in selectArchs)
{
    Console.WriteLine($"正在打包[{arch}]...");
    var url = getDotNetRuntimeAspNetCoreDownloadUrl(client, Version, archDict[arch]);
    var tmpFileName = Path.Combine("bin", Path.GetFileName(url));
    var tmpFolder = Path.Combine("bin", $"{Id}-{Version}-{arch}");
    if (Directory.Exists(tmpFolder))
        Directory.Delete(tmpFolder, true);
    Directory.CreateDirectory(tmpFolder);
    var fileExt = Path.GetExtension(url);
    try
    {
        if (!File.Exists(tmpFileName))
        {
            Console.WriteLine($"正在从[{url}]下载...");
            QbNet.DownloadFile(url, tmpFileName, CancellationToken.None, displayDownloadProgress).Wait();
        }
        Console.WriteLine($"文件[{tmpFileName}]下载完成，正在解压...");
        switch (fileExt)
        {
            case ".zip":
                {
                    using (var archive = SharpCompress.Archives.Zip.ZipArchive.Open(tmpFileName))
                    {
                        archive.WriteToDirectory(tmpFolder, extractionOptions);
                    }
                    break;
                }
            case ".gz":
                {
                    var tarFile = Path.GetFileNameWithoutExtension(tmpFileName);
                    if (File.Exists(tarFile))
                        File.Delete(tarFile);
                    using (var archive = SharpCompress.Archives.GZip.GZipArchive.Open(tmpFileName))
                    using (var gzStream = archive.Entries.First().OpenEntryStream())
                    using (var tarFs = new FileStream(tarFile, FileMode.Create))
                        gzStream.CopyTo(tarFs);

                    using (var archive = SharpCompress.Archives.Tar.TarArchive.Open(tarFile))
                        archive.WriteToDirectory(tmpFolder, extractionOptions);
                    File.Delete(tarFile);
                    break;
                }
        }
        Console.WriteLine($"正在打包易启动运行库文件...");
        //生成元信息文件
        var metaObj = new YiQiDong.Core.Protocol.V1.Model.RuntimeInfo()
        {
            Id = $"{Id}-{Version}",
            Name = Id,
            Version = Version,
            BuildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Description = ".NET 是一个免费的跨平台开源开发人员平台，用于生成许多不同类型的应用。使用 .NET，可以使用多种语言、编辑器和库来构建 Web、移动、桌面、游戏和 IoT 等。",
            Platform = new[] { arch },
            Environment = new Dictionary<string, string>()
            {
                ["DOTNET_ROOT"] = "$RUNTIME_DIR"
            },
            Path = new[] { "$RUNTIME_DIR" },
            ExecuteFiles = new[] { "$RUNTIME_DIR/dotnet" },
            TestCommand = new Dictionary<string, string[]>()
            {
                ["信息"] = new[] { "dotnet", "--info" }
            }
        };
        if (arch.StartsWith("win-"))
            metaObj.ExecuteFiles = metaObj.ExecuteFiles.Select(t => t + ".exe").ToArray();
        var metaFile = Path.Combine(tmpFolder, Consts.RUNTIME_META_FILE);
        File.WriteAllText(metaFile, JsonSerializer.Serialize(metaObj, new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        }), Encoding.UTF8);
        //打包
        using (var archive2 = SharpCompress.Archives.Zip.ZipArchive.Create())
        {
            archive2.AddAllFromDirectory(tmpFolder);
            archive2.SaveTo($"bin/{Id}-{Version}-{arch}_{DateTime.Now.ToString("yyyyMMddHHmmss")}.yrt",
                new SharpCompress.Writers.WriterOptions(SharpCompress.Common.CompressionType.LZMA));
        }
    }
    catch
    {
        throw;
    }
    finally
    {
        if (Directory.Exists(tmpFolder))
            Directory.Delete(tmpFolder, true);
        if (File.Exists(tmpFileName))
            File.Delete(tmpFileName);
    }
}

//打开窗口
QbGui.OpenFolder("bin");
