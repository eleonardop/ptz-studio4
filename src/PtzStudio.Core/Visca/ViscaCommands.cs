using PtzStudio.Core.Models;

namespace PtzStudio.Core.Visca;

/// <summary>
/// Montagem dos comandos VISCA padrão (Sony). Os bytes seguem a especificação VISCA publicada;
/// o suporte a cada comando na LJ510-30X-N deve ser confirmado com o teste de conexão e o manual da câmera.
/// </summary>
public static class ViscaCommands
{
    public const int MaxPanSpeed = 0x18;
    public const int MaxTiltSpeed = 0x14;
    public const int MaxZoomSpeed = 8; // velocidades 1..8 -> código VISCA 0..7

    public static byte Header(int address) => (byte)(0x80 | Math.Clamp(address, 1, 7));

    /// <summary>pan: negativo = esquerda, positivo = direita. tilt: positivo = cima, negativo = baixo. 0 = parar o eixo.</summary>
    public static byte[] PanTiltDrive(int address, int pan, int tilt, int maxPan = MaxPanSpeed, int maxTilt = MaxTiltSpeed)
    {
        int ps = Math.Clamp(Math.Abs(pan), 0, Math.Min(maxPan, MaxPanSpeed));
        int ts = Math.Clamp(Math.Abs(tilt), 0, Math.Min(maxTilt, MaxTiltSpeed));
        byte pd = pan < 0 ? (byte)0x01 : pan > 0 ? (byte)0x02 : (byte)0x03;
        byte td = tilt > 0 ? (byte)0x01 : tilt < 0 ? (byte)0x02 : (byte)0x03;
        return new byte[] { Header(address), 0x01, 0x06, 0x01, (byte)Math.Max(1, ps), (byte)Math.Max(1, ts), pd, td, 0xFF };
    }

    public static byte[] PanTiltStop(int address) => PanTiltDrive(address, 0, 0);

    /// <summary>speed: positivo = aproximar (tele), negativo = afastar (wide), 0 = parar. Faixa 1..8 (mapeada para 0..7 do VISCA).</summary>
    public static byte[] Zoom(int address, int speed, int max = MaxZoomSpeed)
    {
        int s = Math.Clamp(Math.Abs(speed), 0, Math.Min(max, MaxZoomSpeed));
        byte code;
        if (speed == 0) code = 0x00;
        else if (speed > 0) code = (byte)(0x20 | Math.Clamp(s - 1, 0, 7));
        else code = (byte)(0x30 | Math.Clamp(s - 1, 0, 7));
        return new byte[] { Header(address), 0x01, 0x04, 0x07, code, 0xFF };
    }

    /// <summary>dir: positivo = foco distante, negativo = foco próximo, 0 = parar.</summary>
    public static byte[] Focus(int address, int dir)
    {
        byte code = dir > 0 ? (byte)0x02 : dir < 0 ? (byte)0x03 : (byte)0x00;
        return new byte[] { Header(address), 0x01, 0x04, 0x08, code, 0xFF };
    }

    public static byte[] AutoFocus(int address, bool on) =>
        new byte[] { Header(address), 0x01, 0x04, 0x38, on ? (byte)0x02 : (byte)0x03, 0xFF };

    public static byte[] PresetSet(int address, int slot) =>
        new byte[] { Header(address), 0x01, 0x04, 0x3F, 0x01, (byte)Math.Clamp(slot, 0, 254), 0xFF };

    public static byte[] PresetRecall(int address, int slot) =>
        new byte[] { Header(address), 0x01, 0x04, 0x3F, 0x02, (byte)Math.Clamp(slot, 0, 254), 0xFF };

    public static byte[] PresetReset(int address, int slot) =>
        new byte[] { Header(address), 0x01, 0x04, 0x3F, 0x00, (byte)Math.Clamp(slot, 0, 254), 0xFF };

    public static byte[] Home(int address) => new byte[] { Header(address), 0x01, 0x06, 0x04, 0xFF };

    /// <summary>Consulta de versão: usada para saber se a câmera responde.</summary>
    public static byte[] VersionInquiry(int address) => new byte[] { Header(address), 0x09, 0x00, 0x02, 0xFF };

    /// <summary>
    /// true se o pacote NÃO movimenta a câmera: apenas PARADAS (Pan/Tilt, Zoom, Foco) e consultas.
    /// A trava de emergência usa isto para descartar qualquer outro comando (movimento, preset, Home, foco automático...).
    /// </summary>
    public static bool IsSafeWhileLocked(byte[]? p)
    {
        if (p == null || p.Length < 5 || p[^1] != 0xFF) return false;
        if (p[1] == 0x09) return true;                                                           // consulta (inquiry)
        if (p[1] != 0x01) return false;
        if (p[2] == 0x06 && p[3] == 0x01) return p.Length >= 9 && p[6] == 0x03 && p[7] == 0x03;  // Pan/Tilt: só "parar"
        if (p[2] == 0x04 && p[3] == 0x07) return p[4] == 0x00;                                   // Zoom: só "parar"
        if (p[2] == 0x04 && p[3] == 0x08) return p[4] == 0x00;                                   // Foco: só "parar"
        return false;                                                                            // presets, Home, foco automático etc.
    }

}

public enum ViscaPayloadType : ushort { Command = 0x0100, Inquiry = 0x0110, Control = 0x0200 }

/// <summary>Cabeçalho de 8 bytes do "VISCA over IP" (usado por câmeras que seguem o encapsulamento da Sony).</summary>
public static class ViscaFraming
{
    public static byte[] Wrap(byte[] payload, uint sequence, ViscaPayloadType type)
    {
        var r = new byte[8 + payload.Length];
        r[0] = (byte)((ushort)type >> 8);
        r[1] = (byte)((ushort)type & 0xFF);
        r[2] = (byte)(payload.Length >> 8);
        r[3] = (byte)(payload.Length & 0xFF);
        r[4] = (byte)(sequence >> 24); r[5] = (byte)(sequence >> 16); r[6] = (byte)(sequence >> 8); r[7] = (byte)sequence;
        Buffer.BlockCopy(payload, 0, r, 8, payload.Length);
        return r;
    }
}

public enum ViscaResponseKind { Desconhecida, Ack, Conclusao, Erro, Inquerito }

public sealed record ViscaResponse(ViscaResponseKind Kind, byte[] Payload)
{
    public static ViscaResponse Parse(byte[] data, ViscaTransportMode mode)
    {
        var p = data;
        if (mode == ViscaTransportMode.ViscaOverIp && data.Length >= 8) p = data[8..];
        var kind = ViscaResponseKind.Desconhecida;
        if (p.Length >= 3 && p[0] >= 0x90 && p[^1] == 0xFF)
        {
            switch (p[1] >> 4)
            {
                case 4: kind = ViscaResponseKind.Ack; break;
                case 5: kind = p.Length > 3 ? ViscaResponseKind.Inquerito : ViscaResponseKind.Conclusao; break;
                case 6: kind = ViscaResponseKind.Erro; break;
            }
        }
        return new ViscaResponse(kind, p);
    }
}
