// Gerador de licenças do Otimizador PC PRO (uso exclusivo do vendedor).
//
//   dotnet run -- novo-par                         cria um novo par de chaves e atualiza o app
//   dotnet run -- gerar --titular "Fulano" --dias 30
//   dotnet run -- gerar --titular "Empresa X" --vitalicia
//   dotnet run -- verificar <CHAVE>
//
// A chave privada fica em "chave-privada.pem" nesta pasta. NUNCA a distribua nem a envie ao GitHub:
// quem tiver esse arquivo consegue gerar licenças válidas.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
var epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
string baseDir = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
    ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
    : Directory.GetCurrentDirectory();
string privPath = Path.Combine(baseDir, "chave-privada.pem");
string appKeyFile = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "src", "Otimizador", "Core", "LicensePublicKey.cs"));

if (args.Length == 0) { Help(); return 1; }

switch (args[0])
{
    case "novo-par":
    {
        if (File.Exists(privPath) && !args.Contains("--forcar"))
        {
            Console.Error.WriteLine($"Já existe {privPath}. Use --forcar para substituir (licenças antigas deixarão de valer).");
            return 2;
        }
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(privPath, ec.ExportPkcs8PrivateKeyPem());
        string spki = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
        File.WriteAllText(appKeyFile,
            "namespace Otimizador.Core;\n\n" +
            "/// <summary>Chave PÚBLICA de verificação de licenças (gerada por tools/GeradorLicencas).</summary>\n" +
            "internal static class LicensePublicKey\n{\n" +
            $"    public const string SpkiBase64 = \"{spki}\";\n}}\n");
        Console.WriteLine($"Chave privada: {privPath}");
        Console.WriteLine($"Chave pública gravada em: {appKeyFile}");
        Console.WriteLine("Recompile o app para usar o novo par.");
        return 0;
    }

    case "gerar":
    {
        string holder = Arg("--titular") ?? "";
        bool lifetime = args.Contains("--vitalicia");
        int days = int.TryParse(Arg("--dias"), out var d) ? d : 30;
        uint id = uint.TryParse(Arg("--id"), out var i) ? i : (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        if (!File.Exists(privPath)) { Console.Error.WriteLine("Chave privada não encontrada. Rode 'novo-par' primeiro."); return 2; }

        var nameBytes = Encoding.UTF8.GetBytes(holder);
        if (nameBytes.Length > 40) nameBytes = nameBytes[..40];
        var today = DateTime.UtcNow.Date;
        ushort issued = (ushort)(today - epoch).TotalDays;
        ushort expiry = lifetime ? (ushort)0 : (ushort)((today.AddDays(days) - epoch).TotalDays);

        var payload = new byte[10 + nameBytes.Length];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), issued);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(3), expiry);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5), id);
        payload[9] = (byte)nameBytes.Length;
        nameBytes.CopyTo(payload, 10);

        using var ec = ECDsa.Create();
        ec.ImportFromPem(File.ReadAllText(privPath));
        var sig = ec.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var key = Group(Encode(payload.Concat(sig).ToArray()));

        Console.WriteLine($"Titular : {holder}");
        Console.WriteLine($"ID      : {id}");
        Console.WriteLine($"Validade: {(lifetime ? "vitalícia" : epoch.AddDays(expiry).ToString("dd/MM/yyyy"))}");
        Console.WriteLine();
        Console.WriteLine(key);
        return 0;
    }

    case "verificar":
    {
        if (args.Length < 2) { Help(); return 1; }
        string spkiB64 = File.ReadAllText(appKeyFile).Split('"')[1];
        var raw = Decode(new string(string.Join("", args.Skip(1)).ToUpperInvariant().Where(Alphabet.Contains).ToArray()));
        if (raw.Length < 75) { Console.WriteLine("INVÁLIDA (tamanho)"); return 3; }
        int len = raw[9];
        if (raw.Length != 10 + len + 64) { Console.WriteLine("INVÁLIDA (formato)"); return 3; }
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiB64), out _);
        bool ok = ec.VerifyData(raw.AsSpan(0, 10 + len), raw.AsSpan(10 + len), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        ushort exp = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(3));
        Console.WriteLine(ok ? "VÁLIDA" : "INVÁLIDA (assinatura)");
        Console.WriteLine($"Titular : {Encoding.UTF8.GetString(raw, 10, len)}");
        Console.WriteLine($"ID      : {BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(5))}");
        Console.WriteLine($"Validade: {(exp == 0 ? "vitalícia" : epoch.AddDays(exp).ToString("dd/MM/yyyy"))}");
        return ok ? 0 : 3;
    }

    default:
        Help();
        return 1;
}

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static void Help() => Console.WriteLine(
    "Uso:\n" +
    "  GeradorLicencas novo-par [--forcar]\n" +
    "  GeradorLicencas gerar --titular \"Nome ou e-mail\" [--dias 30 | --vitalicia] [--id 123]\n" +
    "  GeradorLicencas verificar <CHAVE>");

static string Encode(byte[] data)
{
    var sb = new StringBuilder();
    int buffer = 0, bits = 0;
    foreach (var b in data)
    {
        buffer = (buffer << 8) | b; bits += 8;
        while (bits >= 5) { bits -= 5; sb.Append("0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(buffer >> bits) & 31]); }
        buffer &= (1 << bits) - 1;
    }
    if (bits > 0) sb.Append("0123456789ABCDEFGHJKMNPQRSTVWXYZ"[(buffer << (5 - bits)) & 31]);
    return sb.ToString();
}

static byte[] Decode(string s)
{
    var bytes = new List<byte>();
    int buffer = 0, bits = 0;
    foreach (var c in s)
    {
        buffer = (buffer << 5) | "0123456789ABCDEFGHJKMNPQRSTVWXYZ".IndexOf(c); bits += 5;
        if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); buffer &= (1 << bits) - 1; }
    }
    return bytes.ToArray();
}

static string Group(string s) => string.Join("-", Enumerable.Range(0, (s.Length + 4) / 5).Select(i => s.Substring(i * 5, Math.Min(5, s.Length - i * 5))));
