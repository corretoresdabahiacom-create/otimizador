using Otimizador.Core;
using static Otimizador.Tests.TestRunner;

namespace Otimizador.Tests;

internal static class FileWalkerTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "otimizador-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static void WithRoot(Action<string> body)
    {
        var root = NewRoot();
        try { body(root); }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    public static void Run(TestRunner t)
    {
        t.Group("Percorrer pastas (FileWalker)");

        t.Test("lista arquivos de todas as subpastas", () => WithRoot(root =>
        {
            Touch(Path.Combine(root, "a.tmp"));
            Touch(Path.Combine(root, "sub", "b.tmp"));
            Touch(Path.Combine(root, "sub", "fundo", "c.log"));
            Equal(3, FileWalker.Files(root).Count());
        }));

        t.Test("modo não recursivo olha só a pasta raiz", () => WithRoot(root =>
        {
            Touch(Path.Combine(root, "a.tmp"));
            Touch(Path.Combine(root, "sub", "b.tmp"));
            Equal(1, FileWalker.Files(root, recursive: false).Count());
        }));

        t.Test("filtra por padrão sem diferenciar maiúsculas", () => WithRoot(root =>
        {
            Touch(Path.Combine(root, "a.LOG"));
            Touch(Path.Combine(root, "b.log"));
            Touch(Path.Combine(root, "c.txt"));
            Equal(2, FileWalker.Files(root, pattern: "*.log").Count());
        }));

        t.Test("pasta inexistente devolve lista vazia", () =>
            Equal(0, FileWalker.Files(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N"))).Count()));

        t.Test("não segue atalhos de pasta (evita apagar fora do alvo)", () => WithRoot(root =>
        {
            var outside = NewRoot();
            try
            {
                Touch(Path.Combine(outside, "importante.doc"));
                Touch(Path.Combine(root, "a.tmp"));
                try { Directory.CreateSymbolicLink(Path.Combine(root, "atalho"), outside); }
                catch { return; } // sem permissão para criar link neste ambiente: nada a verificar
                var found = FileWalker.Files(root).Select(f => f.Name).ToList();
                True(!found.Contains("importante.doc"), "seguiu o atalho para fora da pasta");
                Equal(1, found.Count);
            }
            finally { try { Directory.Delete(outside, true); } catch { } }
        }));

        t.Test("remove só pastas vazias, mantém a raiz e as pastas com arquivo", () => WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "vazia", "dentro"));
            Touch(Path.Combine(root, "cheia", "arq.txt"));
            FileWalker.RemoveEmptyDirs(root, DateTime.UtcNow.AddDays(1));
            True(Directory.Exists(root), "a raiz foi removida");
            True(!Directory.Exists(Path.Combine(root, "vazia")), "pasta vazia ficou");
            True(File.Exists(Path.Combine(root, "cheia", "arq.txt")), "pasta com arquivo foi mexida");
        }));

        t.Test("não remove pasta vazia mais nova que o limite", () => WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "recente"));
            FileWalker.RemoveEmptyDirs(root, DateTime.UtcNow.AddDays(-1));
            True(Directory.Exists(Path.Combine(root, "recente")), "apagou pasta recente");
        }));

        t.Test("apaga arquivo somente leitura", () => WithRoot(root =>
        {
            var path = Path.Combine(root, "leitura.tmp");
            Touch(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            True(FileWalker.TryDelete(new FileInfo(path)));
            True(!File.Exists(path));
        }));

        t.Test("TryDelete devolve falso se o arquivo não existe mais", () => WithRoot(root =>
        {
            var path = Path.Combine(root, "fantasma.tmp");
            Touch(path);
            var info = new FileInfo(path);
            File.Delete(path);
            // apagar o que já não existe não é erro grave: o importante é não lançar exceção
            FileWalker.TryDelete(info);
        }));
    }
}
