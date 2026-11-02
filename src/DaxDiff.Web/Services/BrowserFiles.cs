using Microsoft.JSInterop;

namespace DaxDiff.Web.Services;

public sealed record OpenedFile(string Name, string Text);

/// <summary>Project files, downloads and per-browser settings (<c>wwwroot/js/files.js</c>). Nothing goes to a server.</summary>
public sealed class BrowserFiles
{
    private readonly IJSRuntime _js;
    private Task<IJSObjectReference>? _module;

    public BrowserFiles(IJSRuntime js) => _js = js;

    private Task<IJSObjectReference> ModuleAsync() =>
        _module ??= _js.InvokeAsync<IJSObjectReference>("import", "./js/files.js").AsTask();

    public async Task<OpenedFile?> OpenProjectAsync() =>
        await (await ModuleAsync()).InvokeAsync<OpenedFile?>("openProject");

    /// <summary>Returns the saved file name, or null when the user cancelled.</summary>
    public async Task<string?> SaveProjectAsync(string text, string suggestedName, bool saveAs) =>
        await (await ModuleAsync()).InvokeAsync<string?>("saveProject", text, suggestedName, saveAs);

    public async Task ForgetProjectHandleAsync() =>
        await (await ModuleAsync()).InvokeVoidAsync("forgetProjectHandle");

    public async Task SetDirtyAsync(bool dirty) =>
        await (await ModuleAsync()).InvokeVoidAsync("setDirty", dirty);

    public async Task DownloadAsync(string fileName, string contentType, Stream content)
    {
        using var reference = new DotNetStreamReference(content, leaveOpen: true);
        await (await ModuleAsync()).InvokeVoidAsync("downloadStream", fileName, contentType, reference);
    }

    public async Task<string?> GetSettingAsync(string key) =>
        await (await ModuleAsync()).InvokeAsync<string?>("getSetting", key);

    public async Task SetSettingAsync(string key, string? value) =>
        await (await ModuleAsync()).InvokeVoidAsync("setSetting", key, value);
}
