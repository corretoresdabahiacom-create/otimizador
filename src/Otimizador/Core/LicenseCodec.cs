using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Otimizador.Core;

/// <summary>
/// Leitura e verificação de chaves de licença (sem dependência de interface, registro ou configuração).
/// Formato (Base32 Crockford): [versão 1][emissão u16][validade u16][id u32][len u8][titular UTF-8][assinatura 64].
/// Datas = dias desde 01/01/2024. Validade 0 = vitalícia.
/// </summary>
internal static class LicenseCodec
{
    public static readonly DateTime Epoch = new(2024, 1, 1); // datas locais (só o dia importa)
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Normalize(string key) =>
        new string(key.ToUpperInvariant().Where(c => Alphabet.Contains(c) || c is 'O' or 'I' or 'L').ToArray())
            .Replace('O', '0').Replace('I', '1').Replace('L', '1');

    public static bool TryDecode(string key, string publicKeySpkiBase64,
        out string holder, out uint id, out DateTime? expires)
    {
        holder = ""; id = 0; expires = null;
        try
        {
            var raw = Base32Decode(Normalize(key));
            if (raw == null || raw.Length < 1 + 2 + 2 + 4 + 1 + 64 || raw[0] != 1) return false;

            int nameLen = raw[9];
            int payloadLen = 10 + nameLen;
            if (raw.Length != payloadLen + 64) return false;

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
            if (!ecdsa.VerifyData(raw.AsSpan(0, payloadLen), raw.AsSpan(payloadLen, 64),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return false;

            ushort exp = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(3));
            id = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(5));
            holder = Encoding.UTF8.GetString(raw, 10, nameLen);
            expires = exp == 0 ? null : Epoch.AddDays(exp + 1); // vale até o fim do dia
            return true;
        }
        catch { return false; }
    }

    private static byte[]? Base32Decode(string s)
    {
        var bytes = new List<byte>(s.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            int v = Alphabet.IndexOf(c);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }
        return bytes.ToArray();
    }
}
