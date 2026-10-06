using System.Text.RegularExpressions;

namespace PocketLedger.Tests;

public sealed class ThemeContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string WebRoot = Path.Combine(RepositoryRoot, "src", "PocketLedger.Web");
    private static readonly string IdentityRoot = Path.Combine(RepositoryRoot, "src", "PocketLedger.Identity");

    [Fact]
    public void ThemeBootstrap_IsSharedAndRestrictsValuesToTheSupportedAllowlist()
    {
        var webScript = Read(WebRoot, "wwwroot", "js", "theme.js");
        var identityScript = Read(IdentityRoot, "wwwroot", "js", "theme.js");

        Assert.Equal(webScript, identityScript);
        Assert.Contains("const themes = [\"horizon\", \"banking\", \"glass\", \"material\"];", webScript);
        Assert.Contains("const selectedTheme = isSupported(theme) ? theme : \"horizon\";", webScript);
        Assert.Contains("if (isSupported(cookieTheme)) return cookieTheme;", webScript);
        Assert.Contains("return \"horizon\";", webScript);
        Assert.DoesNotContain("document.documentElement.dataset.theme = theme", webScript);
    }

    [Fact]
    public void ThemeBootstrap_MigratesOnlyAValidLegacyValueWhenTheCookieIsMissing()
    {
        var script = Read(WebRoot, "wwwroot", "js", "theme.js");

        Assert.Contains("if (!cookieTheme && config.migrateLegacyStorage)", script);
        Assert.Contains("const legacyTheme = localStorage.getItem(legacyStorageKey);", script);
        Assert.Contains("if (isSupported(legacyTheme)) return writeCookie(legacyTheme);", script);
        Assert.Contains("migrateLegacyStorage: true", Read(WebRoot, "Views", "Shared", "_Layout.cshtml"));
        Assert.Contains("migrateLegacyStorage: false", Read(IdentityRoot, "Views", "Shared", "_Layout.cshtml"));
    }

    [Fact]
    public void ThemeBootstrap_WritesTheRequiredPresentationCookieAttributes()
    {
        var script = Read(WebRoot, "wwwroot", "js", "theme.js");

        Assert.Contains("Path=/", script);
        Assert.Contains("SameSite=Lax", script);
        Assert.Contains("Max-Age=31536000", script);
        Assert.Contains("if (config.cookieDomain) attributes.push(`Domain=${config.cookieDomain}`);", script);
        Assert.Contains("if (config.secure) attributes.push(\"Secure\");", script);
        Assert.DoesNotContain("HttpOnly", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Layouts_LoadTheThemeBootstrapBeforeStylesAndIdentitySelectorRequiresAuthentication()
    {
        var webLayout = Read(WebRoot, "Views", "Shared", "_Layout.cshtml");
        var identityLayout = Read(IdentityRoot, "Views", "Shared", "_Layout.cshtml");

        AssertBootstrapBeforeStyles(webLayout);
        AssertBootstrapBeforeStyles(identityLayout);
        Assert.Contains("var showThemeSelector = User.Identity?.IsAuthenticated == true;", identityLayout);
        Assert.Matches(new Regex("@if \\(showThemeSelector\\)[\\s\\S]*id=\"theme-menu-button\"", RegexOptions.CultureInvariant), identityLayout);
    }

    [Fact]
    public void WebAndIdentity_UseTheSameThemeCssDefinitions()
    {
        var webDefinitions = Read(WebRoot, "wwwroot", "css", "site.css").Split("html {", 2, StringSplitOptions.None)[0];
        var identityDefinitions = Read(IdentityRoot, "wwwroot", "css", "site.css").Split("html {", 2, StringSplitOptions.None)[0];

        Assert.Equal(webDefinitions, identityDefinitions);
    }

    private static void AssertBootstrapBeforeStyles(string layout)
    {
        var bootstrap = layout.IndexOf("~/js/theme.js", StringComparison.Ordinal);
        var styles = layout.IndexOf("~/css/site.css", StringComparison.Ordinal);
        Assert.True(bootstrap >= 0 && styles > bootstrap, "The theme bootstrap must run before the theme stylesheet is loaded.");
    }

    private static string Read(string root, params string[] segments) => File.ReadAllText(Path.Combine([root, .. segments]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PocketLedger.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the PocketLedger repository root.");
    }
}
