using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Otimizador.Core;

internal enum LicenseState { Missing, Invalid, Valid, ExpiringSoon, Grace, Expired }

internal sealed record LicenseStatus(LicenseState State, string Holder, uint Id, DateTime? ExpiresAt, string Message)
{
    public bool AllowsUse => State is LicenseState.Valid or LicenseState.ExpiringSoon or LicenseState.Grace;
    public bool Lifetime => ExpiresAt is null;
    /// <summary>Último dia de validade (a licença vale até o fim deste dia, horário local).</summary>
    public DateTime? LastValidDay => ExpiresAt?.Date.AddDays(-1);
}

/// <summary>
/// Licenças assinadas digitalmente (ECDSA P-256). O app só contém a chave PÚBLICA:
/// é impossível gerar uma chave válida a partir do executável. As chaves são emitidas
/// com a ferramenta "tools/GeradorLicencas", que guarda a chave privada.
///
/// Formato (Base32 Crockford, em grupos de 5):
///   [versão 1][emissão u16][validade u16][id u32][len u8][titular UTF-8][assinatura 64]
///   Datas = dias desde 01/01/2024. Validade 0 = vitalícia.
/// </summary>
internal static class LicenseService
{
    public const int NotifyDaysBefore = 3;
    public const int GraceDays = 5;
    private static readonly DateTime Epoch = new(2024, 1, 1); // datas locais (só o dia importa)
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static LicenseStatus Check(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new(LicenseState.Missing, "", 0, null, "Nenhuma licença ativada.");

        if (!TryDecode(key, out var holder, out var id, out var expires))
            return new(LicenseState.Invalid, "", 0, null, "Chave de licença inválida.");

        if (expires is null)
            return new(LicenseState.Valid, holder, id, null, "Licença vitalícia ativa.");

        var now = TrustedNow();
        var days = (expires.Value - now).TotalDays;
        if (days > NotifyDaysBefore)
            return new(LicenseState.Valid, holder, id, expires, $"Assinatura ativa até {expires.Value.AddDays(-1):dd/MM/yyyy}.");
        if (days > 0)
            return new(LicenseState.ExpiringSoon, holder, id, expires,
                $"Sua assinatura vence em {Math.Ceiling(days)} dia(s). Renove para não perder o acesso.");
        if (days > -GraceDays)
            return new(LicenseState.Grace, holder, id, expires,
                $"Assinatura vencida. Você tem mais {Math.Ceiling(GraceDays + days)} dia(s) de carência para renovar.");
        return new(LicenseState.Expired, holder, id, expires, "Assinatura vencida. Renove para continuar usando.");
    }

    public static LicenseStatus Current() => Check(Settings.Current.LicenseKey);

    /// <summary>Usa a maior data já vista, para que atrasar o relógio não estenda a validade.</summary>
    private static DateTime TrustedNow()
    {
        var utc = DateTime.UtcNow;
        var s = Settings.Current;
        if (utc > s.LicenseMaxSeenUtc.AddMinutes(10))
        {
            s.LicenseMaxSeenUtc = utc;
            Settings.Save();
        }
        var maxSeen = DateTime.SpecifyKind(s.LicenseMaxSeenUtc, DateTimeKind.Utc).ToLocalTime();
        return DateTime.Now > maxSeen ? DateTime.Now : maxSeen;
    }

    public static string Normalize(string key) =>
        new string(key.ToUpperInvariant().Where(c => Alphabet.Contains(c) || c is 'O' or 'I' or 'L').ToArray())
            .Replace('O', '0').Replace('I', '1').Replace('L', '1');

    public static bool TryDecode(string key, out string holder, out uint id, out DateTime? expires)
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
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(LicensePublicKey.SpkiBase64), out _);
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

    public static string Mask(string? key)
    {
        var k = Normalize(key ?? "");
        return k.Length <= 10 ? "—" : $"{k[..5]}-•••••-{k[^5..]}";
    }
}
