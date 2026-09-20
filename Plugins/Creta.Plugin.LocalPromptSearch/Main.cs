using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Creta.Plugin.LocalPromptSearch;

public class Main : IPlugin, ISettingProvider, IContextMenu
{
    internal const string SaveKeyword = "c";
    internal const string SearchKeyword = "v";

    internal static PluginInitContext Context { get; private set; } = null!;

    private PromptRepository _repository = null!;
    private Settings _settings = null!;

    public void Init(PluginInitContext context)
    {
        Context = context;
        _settings = context.API.LoadSettingJsonStorage<Settings>();
        _repository = new PromptRepository(context.CurrentPluginMetadata.PluginDirectory);
        _repository.Load(_settings.PromptFilePath);
    }

    public List<Result> Query(Query query)
    {
        return IsSaveQuery(query)
            ? BuildSaveResults(query.Search)
            : BuildSearchResults(query.Search);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not PromptTemplate prompt)
        {
            return
            [
                new Result
                {
                    Title = "清空最近使用",
                    SubTitle = "移除最近使用记录中的所有 Prompt。",
                    Action = _ =>
                    {
                        ClearRecentPrompts();
                        return true;
                    }
                }
            ];
        }

        return
        [
            new Result
            {
                Title = "复制 Prompt 正文",
                SubTitle = "将当前模板正文复制到剪贴板。",
                Action = _ => CopyPromptContent(prompt)
            },
            new Result
            {
                Title = "复制 Prompt 标题",
                SubTitle = "只复制当前模板的标题。",
                Action = _ => CopyPromptTitle(prompt)
            },
            new Result
            {
                Title = prompt.Favorite ? "取消收藏" : "加入收藏",
                SubTitle = prompt.Favorite ? "将该模板从收藏列表移除。" : "将该模板标记为收藏，便于优先显示。",
                Action = _ =>
                {
                    ToggleFavorite(prompt);
                    return true;
                }
            },
            new Result
            {
                Title = "打开模板文件位置",
                SubTitle = $"打开当前模板文件所在目录：{_repository.CurrentFilePath}",
                Action = _ => OpenPromptFileLocation()
            },
            new Result
            {
                Title = "清空最近使用",
                SubTitle = "移除最近使用记录中的所有 Prompt。",
                Action = _ =>
                {
                    ClearRecentPrompts();
                    return true;
                }
            }
        ];
    }

    public Control CreateSettingPanel()
    {
        return new Views.SettingsControl(_settings, ReloadPromptsFromSettings);
    }

    private List<Result> BuildSearchResults(string search)
    {
        if (string.Equals(search?.Trim(), "reload", StringComparison.CurrentCultureIgnoreCase))
        {
            ReloadPromptsFromSettings();

            var subtitle = string.IsNullOrWhiteSpace(_repository.LoadError)
                ? $"已重新加载：{_repository.CurrentFilePath}"
                : _repository.LoadError;

            return
            [
                new Result
                {
                    Title = string.IsNullOrWhiteSpace(_repository.LoadError) ? "已重新加载 Prompt 模板" : "重新加载失败",
                    SubTitle = subtitle,
                    Score = 1200,
                    Action = _ => false
                }
            ];
        }

        if (!string.IsNullOrWhiteSpace(_repository.LoadError))
        {
            return
            [
                new Result
                {
                    Title = "无法加载本地 Prompt 模板",
                    SubTitle = _repository.LoadError,
                    Score = 1000,
                    Action = _ => false
                }
            ];
        }

        var prompts = _repository.GetPrompts();
        if (prompts.Count == 0)
        {
            return
            [
                new Result
                {
                    Title = "未找到可用的 Prompt 模板",
                    SubTitle = "输入 c 文本可直接保存，或检查 prompts.json 是否存在。",
                    Score = 1000,
                    AutoCompleteText = $"{SaveKeyword} ",
                    Action = _ =>
                    {
                        Context.API.ChangeQuery($"{SaveKeyword} ", true);
                        return false;
                    }
                }
            ];
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return BuildEmptySearchResults(prompts);
        }

        var matches = prompts
            .Select(prompt => CreateSearchMatch(prompt, search.Trim()))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Prompt.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(12)
            .ToList();

        if (matches.Count == 0)
        {
            return
            [
                new Result
                {
                    Title = $"没有找到与“{search.Trim()}”相关的 Prompt",
                    SubTitle = "可以换更短的关键词，或输入 c 把这段文字存成新模板。",
                    Score = 900,
                    AutoCompleteText = $"{SaveKeyword} {search.Trim()}",
                    Action = _ =>
                    {
                        Context.API.ChangeQuery($"{SaveKeyword} {search.Trim()}", true);
                        return false;
                    }
                }
            ];
        }

        return
        [
            .. matches.Select(match => CreatePromptResult(
                match.Prompt,
                match.Score,
                "按回车复制 Prompt 到剪贴板。",
                match.TitleHighlightData))
        ];
    }

    private List<Result> BuildSaveResults(string search)
    {
        if (!TryResolveSaveContent(search, out var content, out var replaceTitle, out var sourceError))
        {
            return
            [
                new Result
                {
                    Title = "无法保存 Prompt",
                    SubTitle = sourceError,
                    Score = 1200,
                    Action = _ => false
                }
            ];
        }

        if (!PromptSaveParser.IsContentValid(content, out var contentError))
        {
            return
            [
                new Result
                {
                    Title = "无法保存 Prompt",
                    SubTitle = contentError,
                    Score = 1200,
                    Action = _ => false
                }
            ];
        }

        var input = PromptSaveParser.Parse(search, content, replaceTitle);
        var preview = PromptSaveParser.Preview(content);
        var byTitle = _repository.FindByTitle(input.Title);
        var byContent = _repository.FindByContent(content);
        var results = new List<Result>();

        if (byTitle is not null)
        {
            results.Add(CreateSaveResult(
                $"更新已有模板「{byTitle.Title}」",
                $"标题相同。正文预览：{preview}",
                1100,
                () => SaveExistingPrompt(byTitle, input, content)));
        }

        if (byContent is not null && !ReferenceEquals(byContent, byTitle))
        {
            results.Add(CreateSaveResult(
                $"更新已有模板「{byContent.Title}」",
                $"正文相同。将保留原标题，并写入当前内容。",
                1080,
                () => SaveExistingPrompt(byContent, input, content)));
        }

        var hasDuplicate = byTitle is not null || byContent is not null;
        results.Add(CreateSaveResult(
            hasDuplicate ? $"另存为新模板「{input.Title}」" : $"保存为「{input.Title}」",
            BuildSaveSubtitle(input, preview),
            hasDuplicate ? 1000 : 1100,
            () => SaveNewPrompt(input, content)));

        return results;
    }

    private List<Result> BuildEmptySearchResults(IReadOnlyList<PromptTemplate> prompts)
    {
        var recentIds = _settings.RecentPromptIds;
        var recentPrompts = recentIds
            .Select(id => prompts.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.CurrentCultureIgnoreCase)))
            .Where(prompt => prompt is not null)
            .Cast<PromptTemplate>()
            .ToList();

        var recentIdSet = new HashSet<string>(recentPrompts.Select(p => p.Id), StringComparer.CurrentCultureIgnoreCase);
        var results = new List<Result>();

        if (recentPrompts.Count > 0)
        {
            results.AddRange(recentPrompts
                .Take(5)
                .Select(prompt => CreatePromptResult(prompt, 900, "最近使用，按回车可再次复制。")));
        }

        results.Add(new Result
        {
            Title = "输入 c 文本可直接保存为模板",
            SubTitle = "c 后面的文字就是 Prompt 正文；只输入 c 时则保存剪贴板。v 用于搜索。",
            Score = 850,
            AutoCompleteText = $"{SaveKeyword} ",
            Action = _ =>
            {
                Context.API.ChangeQuery($"{SaveKeyword} ", true);
                return false;
            }
        });

        results.Add(new Result
        {
            Title = "重新加载 Prompt 模板",
            SubTitle = $"当前文件：{_repository.CurrentFilePath}",
            Score = 800,
            AutoCompleteText = $"{SearchKeyword} reload",
            Action = _ =>
            {
                ReloadPromptsFromSettings();
                return false;
            }
        });

        results.AddRange(prompts
            .Where(p => !recentIdSet.Contains(p.Id))
            .OrderByDescending(p => p.Favorite)
            .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .Select(prompt => CreatePromptResult(prompt, prompt.Favorite ? 700 : 600, "输入关键词继续筛选，按回车可复制 Prompt。")));

        return results;
    }

    private SearchMatch CreateSearchMatch(PromptTemplate prompt, string search)
    {
        var normalizedSearch = search.Trim();
        var title = prompt.Title ?? string.Empty;
        var description = prompt.Description ?? string.Empty;
        var keywordsText = string.Join(" ", prompt.Keywords);
        var tagsText = string.Join(" ", prompt.Tags);

        var titleMatch = Context.API.FuzzySearch(normalizedSearch, title);
        var descriptionMatch = Context.API.FuzzySearch(normalizedSearch, description);
        var keywordsMatch = Context.API.FuzzySearch(normalizedSearch, keywordsText);
        var tagsMatch = Context.API.FuzzySearch(normalizedSearch, tagsText);

        var score =
            titleMatch.Score +
            (descriptionMatch.Score / 3) +
            (keywordsMatch.Score / 2) +
            (tagsMatch.Score / 2);

        if (Contains(title, normalizedSearch))
        {
            score += 300;
        }

        if (StartsWith(title, normalizedSearch))
        {
            score += 300;
        }

        if (string.Equals(title, normalizedSearch, StringComparison.CurrentCultureIgnoreCase))
        {
            score += 500;
        }

        if (Contains(keywordsText, normalizedSearch))
        {
            score += 180;
        }

        if (Contains(tagsText, normalizedSearch))
        {
            score += 150;
        }

        if (Contains(description, normalizedSearch))
        {
            score += 80;
        }

        var terms = normalizedSearch.Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var term in terms)
        {
            if (Contains(title, term))
            {
                score += 120;
            }

            if (Contains(keywordsText, term))
            {
                score += 70;
            }

            if (Contains(tagsText, term))
            {
                score += 60;
            }
        }

        if (prompt.Favorite)
        {
            score += 30;
        }

        var recentIndex = _settings.RecentPromptIds.FindIndex(id =>
            string.Equals(id, prompt.Id, StringComparison.CurrentCultureIgnoreCase));
        if (recentIndex >= 0)
        {
            score += 140 - (recentIndex * 10);
        }

        return new SearchMatch(prompt, score, titleMatch.Score > 0 ? titleMatch.MatchData : []);
    }

    private Result CreatePromptResult(PromptTemplate prompt, int score, string suffix, List<int> titleHighlightData = null)
    {
        return new Result
        {
            Title = prompt.Title,
            SubTitle = BuildSubtitle(prompt, suffix),
            Score = score,
            TitleHighlightData = titleHighlightData ?? [],
            AutoCompleteText = $"{SearchKeyword} {prompt.Title}",
            CopyText = prompt.Content,
            ContextData = prompt,
            Action = _ => CopyPromptContent(prompt)
        };
    }

    private static Result CreateSaveResult(string title, string subtitle, int score, Func<bool> action)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            Score = score,
            Action = _ => action()
        };
    }

    private bool SaveNewPrompt(PromptSaveInput input, string content)
    {
        var prompt = _repository.CreatePrompt(input, content);
        if (_repository.TryAdd(prompt, _settings.PromptFilePath, out var error))
        {
            return CompleteSave(prompt, "已保存为新模板");
        }

        Context.API.ShowMsgError("保存 Prompt 失败", error);
        return false;
    }

    private bool SaveExistingPrompt(PromptTemplate prompt, PromptSaveInput input, string content)
    {
        _repository.ApplyUpdate(prompt, input, content);
        if (_repository.TryUpdate(_settings.PromptFilePath, out var error))
        {
            return CompleteSave(prompt, "已更新已有模板");
        }

        Context.API.ShowMsgError("更新 Prompt 失败", error);
        return false;
    }

    private bool CompleteSave(PromptTemplate prompt, string title)
    {
        RegisterRecentPrompt(prompt.Id);
        Context.API.ShowMsg(title, $"“{prompt.Title}”已写入模板库，可用 v 搜索。");
        Context.API.ChangeQuery($"{SearchKeyword} {prompt.Title}", true);
        return false;
    }

    private bool CopyPromptContent(PromptTemplate prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt.Content))
        {
            Context.API.ShowMsgError("该 Prompt 内容为空", $"模板“{prompt.Title}”没有可复制的正文。");
            return false;
        }

        try
        {
            Context.API.CopyToClipboard(prompt.Content, showDefaultNotification: false);
            Context.API.RestorePreviousForegroundWindow(_settings.PasteAfterCopy);
            RegisterRecentPrompt(prompt.Id);
            Context.API.ShowMsg("已复制 Prompt", $"“{prompt.Title}” 已复制到剪贴板。");
            return true;
        }
        catch (Exception ex)
        {
            Context.API.ShowMsgError("复制 Prompt 失败", ex.Message);
            return false;
        }
    }

    private bool CopyPromptTitle(PromptTemplate prompt)
    {
        try
        {
            Context.API.CopyToClipboard(prompt.Title, showDefaultNotification: false);
            Context.API.RestorePreviousForegroundWindow(_settings.PasteAfterCopy);
            Context.API.ShowMsg("已复制标题", $"“{prompt.Title}”标题已复制到剪贴板。");
            return true;
        }
        catch (Exception ex)
        {
            Context.API.ShowMsgError("复制标题失败", ex.Message);
            return false;
        }
    }

    private void ToggleFavorite(PromptTemplate prompt)
    {
        prompt.Favorite = !prompt.Favorite;
        if (_repository.Save())
        {
            Context.API.ShowMsg(
                prompt.Favorite ? "已加入收藏" : "已取消收藏",
                $"“{prompt.Title}”收藏状态已写入模板文件。");
        }
        else
        {
            var reason = _repository.CanPersist
                ? "当前模板文件写入失败，收藏仅在本次运行中生效。"
                : "当前使用的是示例文件，收藏暂未持久化。输入 c 保存时会自动改写到 prompts.json。";
            Context.API.ShowMsg(
                prompt.Favorite ? "已加入收藏" : "已取消收藏",
                reason);
        }

        Context.API.ReQuery();
    }

    private bool OpenPromptFileLocation()
    {
        try
        {
            var pathToOpen = Path.GetDirectoryName(_repository.CurrentFilePath);
            if (string.IsNullOrWhiteSpace(pathToOpen))
            {
                pathToOpen = _repository.CurrentFilePath;
            }

            Context.API.OpenDirectory(pathToOpen);
            return true;
        }
        catch (Exception ex)
        {
            Context.API.ShowMsgError("打开模板文件位置失败", ex.Message);
            return false;
        }
    }

    private void ClearRecentPrompts()
    {
        _settings.RecentPromptIds.Clear();
        Context.API.SaveSettingJsonStorage<Settings>();
        Context.API.ShowMsg("已清空最近使用", "最近使用的 Prompt 记录已移除。");
        Context.API.ReQuery();
    }

    private void RegisterRecentPrompt(string promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId))
        {
            return;
        }

        _settings.RecentPromptIds.RemoveAll(id => string.Equals(id, promptId, StringComparison.CurrentCultureIgnoreCase));
        _settings.RecentPromptIds.Insert(0, promptId);

        const int maxRecentCount = 20;
        if (_settings.RecentPromptIds.Count > maxRecentCount)
        {
            _settings.RecentPromptIds = _settings.RecentPromptIds.Take(maxRecentCount).ToList();
        }

        Context.API.SaveSettingJsonStorage<Settings>();
    }

    private void ReloadPromptsFromSettings()
    {
        _repository.Load(_settings.PromptFilePath);
        Context.API.SaveSettingJsonStorage<Settings>();
    }

    private static string BuildSubtitle(PromptTemplate prompt, string suffix)
    {
        var details = new List<string>();

        if (!string.IsNullOrWhiteSpace(prompt.Description))
        {
            details.Add(prompt.Description);
        }

        if (!string.IsNullOrWhiteSpace(prompt.Category))
        {
            details.Add(prompt.Category);
        }

        if (details.Count == 0)
        {
            return suffix;
        }

        return $"{string.Join(" · ", details)} · {suffix}";
    }

    private static string BuildSaveSubtitle(PromptSaveInput input, string preview)
    {
        var details = new List<string> { $"正文预览：{preview}" };
        if (input.Tags.Count > 0)
        {
            details.Insert(0, $"标签：{string.Join("、", input.Tags)}");
        }

        return string.Join(" · ", details);
    }

    private bool TryResolveSaveContent(string search, out string content, out bool replaceTitle, out string error)
    {
        var split = PromptSaveParser.SplitSearch(search);
        if (!string.IsNullOrWhiteSpace(split.TypedContent))
        {
            content = split.TypedContent;
            replaceTitle = true;
            error = string.Empty;
            return true;
        }

        replaceTitle = false;
        return TryGetClipboardText(out content, out error);
    }

    private static bool TryGetClipboardText(out string text, out string error)
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            var result = dispatcher is not null && !dispatcher.CheckAccess()
                ? dispatcher.Invoke(ReadClipboardOnSta)
                : ReadClipboardOnSta();

            text = result.Text;
            error = result.Error;
            return result.Success;
        }
        catch (Exception ex)
        {
            text = string.Empty;
            error = $"无法读取剪贴板：{ex.Message}";
            return false;
        }
    }

    private static ClipboardReadResult ReadClipboardOnSta()
    {
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new ClipboardReadResult(true, text, string.Empty);
            }
        }

        return new ClipboardReadResult(false, string.Empty, "没有可保存的文本。请在 c 后面输入 Prompt，或先复制后再输入 c。");
    }

    private static bool IsSaveQuery(Query query) =>
        string.Equals(query.ActionKeyword, SaveKeyword, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.CurrentCultureIgnoreCase);

    private static bool StartsWith(string source, string value) =>
        source.StartsWith(value, StringComparison.CurrentCultureIgnoreCase);

    private sealed record SearchMatch(PromptTemplate Prompt, int Score, List<int> TitleHighlightData);

    private sealed record ClipboardReadResult(bool Success, string Text, string Error);
}
