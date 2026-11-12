using Otimizador.Core;
using static Otimizador.Tests.TestRunner;

namespace Otimizador.Tests;

internal static class VersionTests
{
    public static void Run(TestRunner t)
    {
        t.Group("Versões");

        t.Test("lê formatos comuns de versão", () =>
        {
            True(Versions.TryParse("3.1.0", out var a)); Equal(new Version(3, 1, 0), a);
            True(Versions.TryParse("v3.2.1", out var b)); Equal(new Version(3, 2, 1), b);
            True(Versions.TryParse("V4.0", out var c)); Equal(new Version(4, 0, 0), c);
            True(Versions.TryParse("3.1.0-beta.2", out var d)); Equal(new Version(3, 1, 0), d);
            True(Versions.TryParse("3.1.0.7", out var e)); Equal(new Version(3, 1, 0), e);
        });

        t.Test("rejeita texto que não é versão", () =>
        {
            True(!Versions.TryParse(null, out _));
            True(!Versions.TryParse("", out _));
            True(!Versions.TryParse("   ", out _));
            True(!Versions.TryParse("latest", out _));
            True(!Versions.TryParse("abc.def", out _));
        });

        t.Test("só avisa quando a versão é realmente maior", () =>
        {
            True(Versions.IsNewer("3.1.0", "v3.1.1"));
            True(Versions.IsNewer("3.1.9", "3.2.0"));
            True(Versions.IsNewer("3.9.9", "4.0.0"));
            True(Versions.IsNewer("3.1.0", "3.10.0"));   // 10 > 1, não é comparação de texto
            True(!Versions.IsNewer("3.1.0", "3.1.0"));
            True(!Versions.IsNewer("3.1.1", "3.1.0"));
            True(!Versions.IsNewer("3.1.0", "lixo"));
            True(!Versions.IsNewer("lixo", "3.1.0"));
        });

        t.Test("lê a resposta da última versão do GitHub", () =>
        {
            True(Versions.TryReadRelease(
                """{"tag_name":"v3.2.0","html_url":"https://github.com/o/r/releases/tag/v3.2.0","draft":false,"prerelease":false}""",
                out var tag, out var url));
            Equal("v3.2.0", tag);
            Equal("https://github.com/o/r/releases/tag/v3.2.0", url);
        });

        t.Test("ignora rascunho e pré-lançamento", () =>
        {
            True(!Versions.TryReadRelease("""{"tag_name":"v9.0.0","draft":true}""", out _, out _));
            True(!Versions.TryReadRelease("""{"tag_name":"v9.0.0","prerelease":true}""", out _, out _));
        });

        t.Test("não aceita link fora do GitHub", () =>
        {
            True(Versions.TryReadRelease("""{"tag_name":"v3.2.0","html_url":"https://site-falso.example/baixar.exe"}""", out _, out var url));
            Equal("", url);
            True(Versions.TryReadRelease("""{"tag_name":"v3.2.0","html_url":"file:///c:/windows/system32/calc.exe"}""", out _, out url));
            Equal("", url);
        });

        t.Test("resposta inválida não derruba o app", () =>
        {
            True(!Versions.TryReadRelease("não é json", out _, out _));
            True(!Versions.TryReadRelease("[]", out _, out _));
            True(!Versions.TryReadRelease("{}", out _, out _));
            True(!Versions.TryReadRelease("""{"tag_name":123}""", out _, out _));
            True(!Versions.TryReadRelease("""{"tag_name":"nao-e-versao"}""", out _, out _));
        });
    }
}
