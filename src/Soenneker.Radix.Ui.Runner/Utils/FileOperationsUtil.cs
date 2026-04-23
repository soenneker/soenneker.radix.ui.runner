using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Soenneker.Git.Util.Abstract;
using Soenneker.Playwrights.Crawler.Abstract;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Enums;
using Soenneker.Radix.Ui.Runner.Utils.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Environment;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Path.Abstract;

namespace Soenneker.Radix.Ui.Runner.Utils;

///<inheritdoc cref="IFileOperationsUtil"/>
public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IGitUtil _gitUtil;
    private readonly IPlaywrightCrawler _playwrightCrawler;
    private readonly IPathUtil _pathUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IFileUtil _fileUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IGitUtil gitUtil, IPlaywrightCrawler playwrightCrawler,
        IPathUtil pathUtil, IDirectoryUtil directoryUtil, IFileUtil fileUtil)
    {
        _logger = logger;
        _gitUtil = gitUtil;
        _playwrightCrawler = playwrightCrawler;
        _pathUtil = pathUtil;
        _directoryUtil = directoryUtil;
        _fileUtil = fileUtil;
    }

    public async ValueTask Process(CancellationToken cancellationToken)
    {
        string tempRoot = await _pathUtil.GetUniqueTempDirectory("soenneker-radix-ui-runner", true, cancellationToken);
        string crawledRepositoryDirectory = Path.Combine(tempRoot, "soenneker-radix-ui-crawled");
        string componentsRepositoryDirectory = Path.Combine(tempRoot, "soenneker-radix-ui-components");
        string crawlDirectory = Path.Combine(tempRoot, "crawl");
        string extractedDirectory = Path.Combine(tempRoot, "extracted");

        await CloneRepository(Constants.CrawledRepository, crawledRepositoryDirectory, cancellationToken);
        await CloneRepository(Constants.ComponentsRepository, componentsRepositoryDirectory, cancellationToken);

        await Crawl(crawlDirectory, cancellationToken);
        await ReplaceRepositoryContents(crawledRepositoryDirectory, crawlDirectory, cancellationToken);
        await CommitAndPush(crawledRepositoryDirectory, cancellationToken);
        await ExtractComponentFamilies(crawlDirectory, extractedDirectory, cancellationToken);
        await ReplaceRepositoryContents(componentsRepositoryDirectory, extractedDirectory, cancellationToken);
        await CommitAndPush(componentsRepositoryDirectory, cancellationToken);
    }

    private async ValueTask CloneRepository(string repositoryUrl, string targetDirectory, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cloning {RepositoryUrl} into {TargetDirectory}", repositoryUrl, targetDirectory);
        await _gitUtil.Clone(repositoryUrl, targetDirectory, cancellationToken: cancellationToken);
    }

    private async ValueTask Crawl(string crawlDirectory, CancellationToken cancellationToken)
    {
        PlaywrightCrawlResult result = await _playwrightCrawler.Crawl(new PlaywrightCrawlOptions
        {
            Url = Constants.ComponentsUrl,
            SaveDirectory = crawlDirectory,
            Mode = PlaywrightCrawlMode.Full,
            MaxDepth = 10,
            MaxPages = 500,
            SameHostOnly = true,
            PrettyPrintHtml = true,
            ClearSaveDirectory = true,
            OverwriteExistingFiles = true,
            ContinueOnPageError = true,
            Headless = true,
            UseStealth = true,
            NavigationTimeoutMs = 60_000,
            PostNavigationDelayMs = 2_000
        }, cancellationToken);

        _logger.LogInformation("Crawl complete. PagesVisited: {PagesVisited}, HtmlFilesSaved: {HtmlFilesSaved}", result.PagesVisited, result.HtmlFilesSaved);
    }

    private async ValueTask ExtractComponentFamilies(string crawlDirectory, string extractedDirectory, CancellationToken cancellationToken)
    {
        await _directoryUtil.DeleteIfExists(extractedDirectory, cancellationToken);
        await _directoryUtil.Create(extractedDirectory, cancellationToken: cancellationToken);

        string componentsRoot = Path.Combine(crawlDirectory, "primitives", "docs", "components");

        if (!await _directoryUtil.Exists(componentsRoot, cancellationToken))
            throw new DirectoryNotFoundException($"Expected components crawl directory was not found: {componentsRoot}");

        var parser = new HtmlParser();
        var formatter = new PrettyMarkupFormatter();

        List<string> htmlFiles = await _directoryUtil.GetFilesByExtension(componentsRoot, ".html", true, cancellationToken);

        foreach (string htmlFile in htmlFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string html = await _fileUtil.Read(htmlFile, cancellationToken: cancellationToken);
            IDocument document = await parser.ParseDocumentAsync(html, cancellationToken);
            IHtmlCollection<IElement> previews = document.QuerySelectorAll("div[data-slot=\"preview\"], div[data-code-block-content=\"true\"]");

            if (previews.Length == 0)
                continue;

            string familyName = GetFamilyName(componentsRoot, htmlFile);
            string outputPath = Path.Combine(extractedDirectory, familyName + ".html");

            var builder = new StringBuilder();
            builder.AppendLine("<!DOCTYPE html>");
            builder.AppendLine("<html>");
            builder.AppendLine("  <body>");

            foreach (IElement preview in previews)
            {
                await using var writer = new StringWriter();
                preview.ToHtml(writer, formatter);
                builder.AppendLine(IndentBlock(writer.ToString().Trim(), "    "));
                builder.AppendLine();
            }

            builder.AppendLine("  </body>");
            builder.AppendLine("</html>");

            await _fileUtil.Write(outputPath, builder.ToString().TrimEnd() + Environment.NewLine, cancellationToken: cancellationToken);
            _logger.LogInformation("Saved {Count} component blocks to {Path}", previews.Length, outputPath);
        }
    }

    private async ValueTask ReplaceRepositoryContents(string repositoryDirectory, string sourceDirectory, CancellationToken cancellationToken)
    {
        List<string> directories = await _directoryUtil.GetAllDirectories(repositoryDirectory, cancellationToken);

        foreach (string directory in directories)
        {
            if (Path.GetFileName(directory).Equals(".git", StringComparison.OrdinalIgnoreCase))
                continue;

            await _directoryUtil.Delete(directory, cancellationToken);
        }

        List<string> files = await _directoryUtil.GetFilesByExtension(repositoryDirectory, "", false, cancellationToken);

        foreach (string file in files)
        {
            await _fileUtil.Delete(file, cancellationToken: cancellationToken);
        }

        List<string> sourcePaths = await _directoryUtil.GetFilesByExtension(sourceDirectory, "", true, cancellationToken);

        foreach (string sourcePath in sourcePaths)
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            string destination = Path.Combine(repositoryDirectory, relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destination);

            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                await _directoryUtil.Create(destinationDirectory, cancellationToken: cancellationToken);

            await _fileUtil.DeleteIfExists(destination, cancellationToken: cancellationToken);
            await _fileUtil.Copy(sourcePath, destination, cancellationToken: cancellationToken);
        }
    }

    private async ValueTask CommitAndPush(string repositoryDir, CancellationToken cancellationToken)
    {
        string token = EnvironmentUtil.GetVariableStrict("GH__TOKEN");
        string name = EnvironmentUtil.GetVariableStrict("GIT__NAME");
        string email = EnvironmentUtil.GetVariableStrict("GIT__EMAIL");

        await _gitUtil.CommitAndPush(repositoryDir, Constants.CommitMessage, token, name, email, cancellationToken);
    }

    private static string GetFamilyName(string componentsRoot, string htmlFile)
    {
        string relativePath = Path.GetRelativePath(componentsRoot, htmlFile);
        string withoutExtension = Path.ChangeExtension(relativePath, null) ?? relativePath;
        string normalized = withoutExtension.Replace(Path.DirectorySeparatorChar, '-').Replace(Path.AltDirectorySeparatorChar, '-');

        if (normalized.EndsWith("-index", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^"-index".Length];

        return normalized.Equals("index", StringComparison.OrdinalIgnoreCase) ? "components" : normalized;
    }

    private static string IndentBlock(string content, string indent)
    {
        string[] lines = content.Replace("\r\n", "\n").Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => indent + line));
    }
}
