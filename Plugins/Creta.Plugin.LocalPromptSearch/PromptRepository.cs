using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Creta.Plugin.LocalPromptSearch;

internal sealed class PromptRepository
{
    private readonly string _pluginDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private List<PromptTemplate> _prompts = [];

    internal string LoadError { get; private set; } = string.Empty;
    internal string CurrentFilePath { get; private set; } = string.Empty;

    internal PromptRepository(string pluginDirectory)
    {
        _pluginDirectory = pluginDirectory;
    }

    internal IReadOnlyList<PromptTemplate> GetPrompts() => _prompts;

    internal bool CanPersist =>
        !string.IsNullOrWhiteSpace(CurrentFilePath) &&
        !IsSampleFile(CurrentFilePath);

    internal void Load(string configuredPath = "")
    {
        LoadError = string.Empty;
        _prompts = [];
        CurrentFilePath = string.Empty;

        var primaryFilePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_pluginDirectory, "prompts.json")
            : ExpandPath(configuredPath);
        var fallbackFilePath = Path.Combine(_pluginDirectory, "prompts.sample.json");

        var filePath = File.Exists(primaryFilePath) ? primaryFilePath : fallbackFilePath;
        if (!File.Exists(filePath))
        {
            LoadError = "未找到 prompts.json 或 prompts.sample.json。";
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var prompts = JsonSerializer.Deserialize<List<PromptTemplate>>(json, _jsonOptions) ?? [];

            CurrentFilePath = filePath;
            _prompts = prompts
                .Where(prompt => !string.IsNullOrWhiteSpace(prompt.Title))
                .Select(Normalize)
                .ToList();

            if (_prompts.Count == 0)
            {
                LoadError = $"{Path.GetFileName(filePath)} 中没有可用的 Prompt 模板。";
            }
        }
        catch (Exception ex)
        {
            LoadError = $"读取模板失败：{ex.Message}";
        }
    }

    internal PromptTemplate FindByTitle(string title) =>
        _prompts.FirstOrDefault(prompt =>
            string.Equals(prompt.Title, title, StringComparison.CurrentCultureIgnoreCase));

    internal PromptTemplate FindByContent(string content)
    {
        var normalized = (content ?? string.Empty).Trim();
        return _prompts.FirstOrDefault(prompt =>
            string.Equals((prompt.Content ?? string.Empty).Trim(), normalized, StringComparison.CurrentCulture));
    }

    internal PromptTemplate CreatePrompt(PromptSaveInput input, string content)
    {
        var trimmedContent = content.Trim();
        return new PromptTemplate
        {
            Id = CreateId(input.Title),
            Title = input.Title,
            Description = string.Empty,
            Tags = input.Tags.ToList(),
            Keywords = input.Keywords.ToList(),
            Content = trimmedContent,
            Category = string.Empty,
            Favorite = false
        };
    }

    internal void ApplyUpdate(PromptTemplate prompt, PromptSaveInput input, string content)
    {
        prompt.Content = content.Trim();
        if (input.ReplaceTitle)
        {
            prompt.Title = input.Title;
        }

        if (input.Tags.Count > 0)
        {
            prompt.Tags = input.Tags.ToList();
        }

        prompt.Keywords = prompt.Keywords
            .Concat(input.Keywords)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal bool TryAdd(PromptTemplate prompt, string configuredPath, out string error)
    {
        if (!PrepareWritableFile(configuredPath, out error))
        {
            return false;
        }

        _prompts.Add(prompt);
        if (Save())
        {
            error = string.Empty;
            return true;
        }

        _prompts.Remove(prompt);
        error = "写入模板文件失败。";
        return false;
    }

    internal bool TryUpdate(string configuredPath, out string error)
    {
        if (!PrepareWritableFile(configuredPath, out error))
        {
            return false;
        }

        if (Save())
        {
            error = string.Empty;
            return true;
        }

        error = "写入模板文件失败。";
        return false;
    }

    internal bool Save()
    {
        if (!CanPersist)
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(CurrentFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_prompts, _jsonOptions);
            File.WriteAllText(CurrentFilePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal bool PrepareWritableFile(string configuredPath, out string error)
    {
        if (CanPersist)
        {
            error = string.Empty;
            return true;
        }

        var target = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_pluginDirectory, "prompts.json")
            : ExpandPath(configuredPath);

        if (IsSampleFile(target))
        {
            target = Path.Combine(_pluginDirectory, "prompts.json");
        }

        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            CurrentFilePath = target;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"无法准备模板文件：{ex.Message}";
            return false;
        }
    }

    internal string CreateId(string title)
    {
        var slug = CreateSlug(title);
        var candidate = slug;
        var suffix = 2;
        while (_prompts.Any(prompt => string.Equals(prompt.Id, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{slug}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static PromptTemplate Normalize(PromptTemplate prompt)
    {
        prompt.Tags ??= [];
        prompt.Keywords ??= [];
        prompt.Description ??= string.Empty;
        prompt.Content ??= string.Empty;
        prompt.Category ??= string.Empty;
        return prompt;
    }

    private static string CreateSlug(string title)
    {
        var parts = (title ?? string.Empty)
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                var cleaned = part;
                foreach (var invalid in Path.GetInvalidFileNameChars())
                {
                    cleaned = cleaned.Replace(invalid, '-');
                }

                return cleaned.Trim('-');
            })
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var slug = string.Join("-", parts);
        return string.IsNullOrWhiteSpace(slug) ? "prompt" : slug;
    }

    private static bool IsSampleFile(string path) =>
        string.Equals(Path.GetFileName(path), "prompts.sample.json", StringComparison.OrdinalIgnoreCase);

    private static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(expanded);
    }
}
