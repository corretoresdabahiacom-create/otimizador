using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Otimizador.Core;
using static Otimizador.Tests.TestRunner;

namespace Otimizador.Tests;

internal static class LicenseTests
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Emite uma chave no mesmo formato da ferramenta GeradorLicencas (a chave privada de teste é descartável).</summary>
    private static string Issue(ECDsa signer, string holder, ushort expiryDay, uint id)
    {
        var name = Encoding.UTF8.GetBytes(holder);
        var payload = new byte[10 + name.Length];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(3), expiryDay);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5), id);
        payload[9] = (byte)name.Length;
        name.CopyTo(payload, 10);
        var sig = signer.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Encode(payload.Concat(sig).ToArray());
    }

    private static string Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5) { bits -= 5; sb.Append(Alphabet[(buffer >> bits) & 31]); }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static string Group(string k) =>
        string.Join("-", Enumerable.Range(0, (k.Length + 4) / 5).Select(i => k.Substring(i * 5, Math.Min(5, k.Length - i * 5))));

    private static string Spki(ECDsa e) => Convert.ToBase64String(e.ExportSubjectPublicKeyInfo());

    public static void Run(TestRunner t)
    {
        t.Group("Licença");
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = Spki(signer);

        t.Test("aceita chave vitalícia válida", () =>
        {
            var key = Issue(signer, "Cliente Teste", 0, 1234);
            True(LicenseCodec.TryDecode(key, pub, out var holder, out var id, out var exp));
            Equal("Cliente Teste", holder);
            Equal(1234u, id);
            True(exp is null, "vitalícia não deve ter validade");
        });

        t.Test("lê a data de validade (vale até o fim do dia)", () =>
        {
            var key = Issue(signer, "Mensal", 100, 7);
            True(LicenseCodec.TryDecode(key, pub, out _, out _, out var exp));
            Equal(LicenseCodec.Epoch.AddDays(101), exp!.Value);
        });

        t.Test("aceita acentos no titular", () =>
        {
            var key = Issue(signer, "João da Conceição", 0, 1);
            True(LicenseCodec.TryDecode(key, pub, out var holder, out _, out _));
            Equal("João da Conceição", holder);
        });

        t.Test("aceita hífens, espaços e minúsculas ao digitar", () =>
        {
            var key = Issue(signer, "Teste", 0, 9);
            True(LicenseCodec.TryDecode(Group(key).ToLowerInvariant(), pub, out _, out _, out _));
            True(LicenseCodec.TryDecode("  " + Group(key).Replace("-", " ") + "\n", pub, out _, out _, out _));
        });

        t.Test("corrige letras confundíveis (O, I, L)", () =>
        {
            Equal("0011", LicenseCodec.Normalize("OoIl"));
            Equal("AB12", LicenseCodec.Normalize("a-b 1 2"));
        });

        t.Test("rejeita chave adulterada", () =>
        {
            var key = Issue(signer, "Teste", 0, 5);
            for (int pos = 0; pos < key.Length; pos += 7)
            {
                var chars = key.ToCharArray();
                chars[pos] = chars[pos] == 'A' ? 'B' : 'A';
                True(!LicenseCodec.TryDecode(new string(chars), pub, out _, out _, out _), $"alteração na posição {pos} passou");
            }
        });

        t.Test("rejeita chave assinada por outra chave privada", () =>
        {
            var forged = Issue(other, "Pirata", 0, 666);
            True(!LicenseCodec.TryDecode(forged, pub, out _, out _, out _));
        });

        t.Test("não permite estender a validade trocando só a data", () =>
        {
            var name = Encoding.UTF8.GetBytes("Teste");
            var good = Issue(signer, "Teste", 100, 3);
            // reconstrói a chave com validade maior reaproveitando a assinatura antiga
            var raw = Decode(good);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(3), 60000);
            True(!LicenseCodec.TryDecode(Encode(raw), pub, out _, out _, out _));
        });

        t.Test("rejeita lixo, vazio e chave truncada", () =>
        {
            True(!LicenseCodec.TryDecode("", pub, out _, out _, out _));
            True(!LicenseCodec.TryDecode("TESTE-2026-OK", pub, out _, out _, out _));
            True(!LicenseCodec.TryDecode("FREE-2026-TRIAL", pub, out _, out _, out _));
            True(!LicenseCodec.TryDecode("%%%%", pub, out _, out _, out _));
            var key = Issue(signer, "Teste", 0, 2);
            True(!LicenseCodec.TryDecode(key[..(key.Length - 10)], pub, out _, out _, out _));
            True(!LicenseCodec.TryDecode(key + "00000", pub, out _, out _, out _));
        });

        t.Test("chave pública inválida não derruba o app", () =>
        {
            var key = Issue(signer, "Teste", 0, 2);
            True(!LicenseCodec.TryDecode(key, "isso-nao-e-base64!", out _, out _, out _));
            True(!LicenseCodec.TryDecode(key, "", out _, out _, out _));
        });
    }

    private static byte[] Decode(string s)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); buffer &= (1 << bits) - 1; }
        }
        return bytes.ToArray();
    }
}
