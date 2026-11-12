using Otimizador.Core;
using Otimizador.Tests;

var t = new TestRunner();
VersionTests.Run(t);
LicenseTests.Run(t);
FileWalkerTests.Run(t);
return t.Finish();

namespace Otimizador.Tests
{
    /// <summary>Executor mínimo: cada teste é uma função; uma falha não interrompe as demais.</summary>
    internal sealed class TestRunner
    {
        private int _ok;
        private readonly List<string> _fail = new();
        private string _group = "";

        public void Group(string name) { _group = name; Console.WriteLine($"\n{name}"); }

        public void Test(string name, Action body)
        {
            try { body(); _ok++; Console.WriteLine($"  ok   {name}"); }
            catch (Exception ex)
            {
                var msg = $"{_group} / {name}: {ex.Message}";
                _fail.Add(msg);
                Console.WriteLine($"  FALHOU {name}: {ex.Message}");
            }
        }

        public static void True(bool cond, string what = "esperava verdadeiro")
        {
            if (!cond) throw new Exception(what);
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"esperado '{expected}', obtido '{actual}'");
        }

        public int Finish()
        {
            Console.WriteLine($"\n{_ok} passaram, {_fail.Count} falharam.");
            foreach (var f in _fail) Console.WriteLine("  - " + f);
            return _fail.Count == 0 ? 0 : 1;
        }
    }
}
