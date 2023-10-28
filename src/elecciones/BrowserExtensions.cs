using System.Diagnostics;
using Microsoft.Playwright;

namespace MenosRelato;

static class BrowserExtensions
{
    static readonly PageWaitForSelectorOptions NoPageWaitForSelector = new() { Timeout = 0 };

    public static async Task<IElementHandle?> FocusAsync(this IPage page, string selector)
    {
        await page.WaitForSelectorAsync(selector, NoPageWaitForSelector);
        var element = await page.QuerySelectorAsync(selector);
        if (element is not null)
            await element.FocusAsync();
        
        return element;
    }

    public static async Task Save(this IPage page, bool open = true)
    {
        var targetDir = Path.Combine(Constants.DefaultCacheDir, "html", DateTime.Today.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(targetDir);

        var body = await page.InnerHTMLAsync("html");
        var html = Path.GetFileName(new Uri(page.Url).GetComponents(UriComponents.Path, UriFormat.Unescaped)) + ".html";
        if (html == ".html")
            html = "index.html";

        await File.WriteAllTextAsync(Path.Combine(targetDir, html),
        $"""
        <!DOCTYPE html>
        <html>
            <!-- Url: {page.Url} 
            {body}
        </html>
        """);

        if (open) 
            Process.Start(new ProcessStartInfo("code", Path.Combine(targetDir, html)) { UseShellExecute = true });
    }
}
