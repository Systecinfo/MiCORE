using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace MiCORE;

/// <summary>Serviço aberto num aparelho da rede (uma porta TCP que respondeu).</summary>
public sealed record LanService(string Ip, string Name, int Port, string Detail)
{
    public string Text => Detail.Length > 0 ? $"{Name} · {Detail}" : Port is 80 or 443 or 21 or 22 or 445 or 3389 ? Name : $"{Name} :{Port}";
    public bool CanOpen => Name is "HTTP" or "HTTPS" or "FTP" or "PASTAS" or "RDP" or "SSH";
    public string Tip => CanOpen ? $"Abrir {Name} em {Ip}:{Port}" : $"Porta {Port} aberta";
}

/// <summary>Aparelho encontrado na varredura.</summary>
public sealed class LanDevice
{
    public IPAddress Address { get; init; } = IPAddress.None;
    public uint Sort { get; init; }
    public string Ip => Address.ToString();
    public string? DnsName { get; set; }
    public string? NetBiosName { get; set; }
    public string? Workgroup { get; set; }
    public string? Mac { get; set; }
    public string? Vendor { get; set; }
    public long? PingMs { get; set; }
    public bool IsSelf { get; set; }
    public bool IsGateway { get; set; }
    public List<LanService> Services { get; set; } = new();

    public string Title => NetBiosName ?? ShortDns ?? (IsGateway ? "Roteador" : IsSelf ? Environment.MachineName : Ip);
    public string Badge => IsSelf ? "ESTE PC" : IsGateway ? "ROTEADOR" : PingMs is long ms ? $"{ms} ms" : "SEM PING";
    public string Detail => string.Join(" · ", new[] { Ip, Mac, Vendor }.Where(s => !string.IsNullOrEmpty(s)));
    public string Extra
    {
        get
        {
            var parts = new List<string>();
            if (DnsName != null && !string.Equals(DnsName, Title, StringComparison.OrdinalIgnoreCase)) parts.Add("DNS: " + DnsName);
            if (Workgroup != null) parts.Add("Grupo: " + Workgroup);
            if (PingMs == null && !IsSelf) parts.Add("não responde ao ping (achado pela tabela ARP)");
            return string.Join(" · ", parts);
        }
    }
    public Visibility ExtraVisibility => Extra.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MacVisibility => Mac != null && !IsSelf ? Visibility.Visible : Visibility.Collapsed;
    private string? ShortDns => DnsName?.Split('.')[0] is { Length: > 0 } s ? s : null;
}

/// <summary>
/// Varredura da rede local (aba Dispositivos na rede): descobre os aparelhos ligados por ping e pela tabela ARP do
/// Windows (que acha até quem bloqueia ping na mesma sub-rede), identifica nome (DNS reverso e NetBIOS), MAC e
/// fabricante (registro público OUI do IEEE, embutido em Native\oui.txt.gz) e testa algumas portas TCP comuns.
/// Só lê: não tenta login, não envia nada além de ping, ARP, consultas de nome, conexões TCP e um GET / no HTTP.
/// </summary>
public static class LanScanner
{
    /// <summary>Portas testadas em cada aparelho encontrado.</summary>
    public static readonly (int Port, string Name)[] Ports =
    {
        (80, "HTTP"), (443, "HTTPS"), (8080, "HTTP"), (21, "FTP"), (22, "SSH"),
        (445, "PASTAS"), (3389, "RDP"), (5900, "VNC"), (9100, "IMPRESSORA"),
    };

    public const int MaxHosts = 4096;

    // ---------------- Rede deste PC ----------------

    public sealed record LocalNet(IPAddress Address, IPAddress Mask, IPAddress? Gateway, string Adapter, string? Mac);

    /// <summary>Redes IPv4 ativas deste PC; a primeira é a que tem gateway (a que leva à internet).</summary>
    public static List<LocalNet> LocalNetworks()
    {
        var list = new List<LocalNet>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); } catch { continue; }
            var gw = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            string? mac = FormatMac(ni.GetPhysicalAddress().GetAddressBytes());
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue; // APIPA: sem DHCP
                list.Add(new LocalNet(ua.Address, ua.IPv4Mask, gw, ni.Name, mac));
            }
        }
        return list.OrderBy(n => n.Gateway == null).ToList();
    }

    /// <summary>Faixa sugerida para a rede: a sub-rede inteira, ou um /24 em volta do PC se a rede for maior que /22.</summary>
    public static string SuggestedRange(LocalNet n)
    {
        int prefix = PrefixLength(n.Mask);
        if (prefix < 22) prefix = 24;
        uint ip = ToUInt(n.Address), mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        return $"{FromUInt(ip & mask)}/{prefix}";
    }

    // ---------------- Faixa de IPs ----------------

    /// <summary>
    /// Aceita, separados por vírgula: IP único (192.168.0.10), faixa (192.168.0.1-254 ou 192.168.0.1-192.168.1.20)
    /// e CIDR (192.168.0.0/24). Devolve a lista ou null com o erro.
    /// </summary>
    public static List<IPAddress>? ParseRange(string text, out string? error)
    {
        error = null;
        var set = new SortedSet<uint>();
        foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            uint first, last;
            if (part.Contains('/'))
            {
                var p = part.Split('/');
                if (!TryIp(p[0], out uint baseIp) || !int.TryParse(p[1], out int prefix) || prefix < 16 || prefix > 32)
                { error = $"\"{part}\": use um CIDR de /16 a /32 (ex.: 192.168.0.0/24)."; return null; }
                uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
                first = baseIp & mask; last = first | ~mask;
                if (prefix <= 30) { first++; last--; } // sem endereço de rede e de broadcast
            }
            else if (part.Contains('-'))
            {
                var p = part.Split('-', 2);
                if (!TryIp(p[0], out first)) { error = $"\"{part}\" não é uma faixa válida."; return null; }
                if (TryIp(p[1], out last)) { }
                else if (byte.TryParse(p[1], out byte lastOctet)) last = (first & 0xFFFFFF00) | lastOctet;
                else { error = $"\"{part}\" não é uma faixa válida."; return null; }
                if (last < first) (first, last) = (last, first);
            }
            else if (TryIp(part, out first)) last = first;
            else { error = $"\"{part}\" não é um IP, faixa ou CIDR válido."; return null; }

            if ((ulong)last - first + 1 + (ulong)set.Count > MaxHosts) { error = $"Faixa grande demais: o limite é {MaxHosts} endereços por varredura."; return null; }
            for (ulong v = first; v <= last; v++) set.Add((uint)v);
        }
        if (set.Count == 0) { error = "Informe uma faixa de IPs."; return null; }
        return set.Select(FromUInt).ToList();
    }

    // ---------------- Varredura ----------------

    /// <summary>
    /// Varre os endereços. <paramref name="found"/> recebe cada aparelho já com nome, MAC e serviços;
    /// <paramref name="progress"/> recebe (fração 0..1, texto). Os callbacks rodam fora da thread da janela.
    /// </summary>
    public static async Task<int> ScanAsync(IReadOnlyList<IPAddress> targets, Action<LanDevice> found,
                                            Action<double, string> progress, CancellationToken ct)
    {
        var locals = LocalNetworks();
        var selfIps = locals.GroupBy(n => ToUInt(n.Address)).ToDictionary(g => g.Key, g => g.First());
        var gateways = locals.Where(n => n.Gateway != null).Select(n => ToUInt(n.Gateway!)).ToHashSet();
        bool OnLocalSubnet(uint ip) => locals.Any(n => (ip & ToUInt(n.Mask)) == (ToUInt(n.Address) & ToUInt(n.Mask)));

        // 1) Ping em todos (o Windows faz ARP antes de cada ping na mesma sub-rede, o que enche a tabela ARP)
        var ping = new Dictionary<uint, long>();
        int done = 0;
        using (var gate = new SemaphoreSlim(128))
        {
            var tasks = targets.Select(async ip =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    using var p = new Ping();
                    var r = await p.SendPingAsync(ip, 900);
                    if (r.Status == IPStatus.Success) lock (ping) ping[ToUInt(ip)] = r.RoundtripTime;
                }
                catch (PingException) { }
                finally
                {
                    gate.Release();
                    int d = Interlocked.Increment(ref done);
                    if (d % 8 == 0 || d == targets.Count) progress(0.45 * d / targets.Count, $"Procurando aparelhos… {d} de {targets.Count} endereços");
                }
            });
            await Task.WhenAll(tasks);
        }
        ct.ThrowIfCancellationRequested();

        // 2) Tabela ARP: MAC de quem respondeu e aparelhos que bloqueiam ping mas responderam ao ARP
        progress(0.47, "Lendo a tabela ARP do Windows…");
        var targetSet = targets.Select(ToUInt).ToHashSet();
        var arp = ReadArpTable();
        var alive = new HashSet<uint>(ping.Keys);
        foreach (var ip in selfIps.Keys) if (targetSet.Contains(ip)) alive.Add(ip);
        var arpOnly = arp.Keys.Where(ip => targetSet.Contains(ip) && !alive.Contains(ip) && OnLocalSubnet(ip)).ToList();
        var macs = new Dictionary<uint, string>(arp);
        // Confirma com um ARP novo (a tabela pode ter entradas antigas) e busca o MAC de quem respondeu ao ping sem entrada
        var toArp = arpOnly.Concat(alive.Where(ip => !macs.ContainsKey(ip) && !selfIps.ContainsKey(ip) && OnLocalSubnet(ip))).Distinct().ToList();
        using (var gate = new SemaphoreSlim(32))
        {
            await Task.WhenAll(toArp.Select(async ip =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    string? mac = await Task.Run(() => SendArp(ip), ct);
                    lock (macs)
                    {
                        if (mac != null) { macs[ip] = mac; alive.Add(ip); }
                        else if (!ping.ContainsKey(ip)) macs.Remove(ip);
                    }
                }
                finally { gate.Release(); }
            }));
        }
        ct.ThrowIfCancellationRequested();

        // 3) Detalhes de cada aparelho vivo: nomes e portas
        var list = alive.OrderBy(x => x).ToList();
        int detailed = 0;
        using (var gate = new SemaphoreSlim(24))
        {
            await Task.WhenAll(list.Select(async ip =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var addr = FromUInt(ip);
                    var dev = new LanDevice
                    {
                        Address = addr, Sort = ip,
                        PingMs = ping.TryGetValue(ip, out long ms) ? ms : null,
                        IsSelf = selfIps.ContainsKey(ip), IsGateway = gateways.Contains(ip),
                    };
                    lock (macs) dev.Mac = selfIps.TryGetValue(ip, out var self) ? self.Mac : macs.GetValueOrDefault(ip);
                    var dnsTask = ReverseDnsAsync(addr, ct);
                    var nbTask = NetBiosAsync(addr, ct);
                    var portsTask = ScanPortsAsync(addr, ct);
                    await Task.WhenAll(dnsTask, nbTask, portsTask);
                    dev.DnsName = dnsTask.Result;
                    (dev.NetBiosName, dev.Workgroup, string? nbMac) = nbTask.Result;
                    dev.Mac ??= nbMac;
                    dev.Vendor = VendorOf(dev.Mac);
                    dev.Services = portsTask.Result;
                    found(dev);
                }
                finally
                {
                    gate.Release();
                    int d = Interlocked.Increment(ref detailed);
                    progress(0.5 + 0.5 * d / Math.Max(1, list.Count), $"Identificando aparelhos… {d} de {list.Count}");
                }
            }));
        }
        return list.Count;
    }

    // ---------------- Nomes ----------------

    private static async Task<string?> ReverseDnsAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);
            var e = await Dns.GetHostEntryAsync(ip.ToString(), cts.Token);
            string name = e.HostName.TrimEnd('.');
            return name.Length == 0 || IPAddress.TryParse(name, out _) ? null : name;
        }
        catch { return null; }
    }

    /// <summary>
    /// Consulta de status de nome NetBIOS (NBSTAT, RFC 1002 §4.2.17) na porta UDP 137: devolve o nome do computador,
    /// o grupo de trabalho e o MAC informado pelo próprio aparelho (só Windows, Samba e alguns NAS respondem).
    /// </summary>
    private static async Task<(string? Name, string? Group, string? Mac)> NetBiosAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            var q = new byte[50];
            Random.Shared.NextBytes(q.AsSpan(0, 2));       // ID da transação
            q[5] = 1;                                       // 1 pergunta
            q[12] = 0x20;                                   // nome codificado com 32 letras: "*" + 15 zeros
            q[13] = (byte)'C'; q[14] = (byte)'K';           // '*' (0x2A) → 'C','K'
            for (int i = 15; i < 45; i++) q[i] = (byte)'A';// 0x00 → 'A','A'
            q[46] = 0x00; q[47] = 0x21;                     // tipo NBSTAT
            q[48] = 0x00; q[49] = 0x01;                     // classe IN

            using var udp = new UdpClient(AddressFamily.InterNetwork);
            await udp.SendAsync(q, q.Length, new IPEndPoint(ip, 137));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(900);
            var r = (await udp.ReceiveAsync(cts.Token)).Buffer;
            if (r.Length < 57) return (null, null, null);

            int o = 12;
            o += r[o] == 0x20 ? 34 : (r[o] & 0xC0) == 0xC0 ? 2 : 34; // nome repetido ou ponteiro
            o += 2 + 2 + 4 + 2;                                         // tipo, classe, TTL, tamanho
            if (o >= r.Length) return (null, null, null);
            int count = r[o++];
            string? name = null, group = null;
            for (int i = 0; i < count && o + 18 <= r.Length; i++, o += 18)
            {
                string n = Encoding.ASCII.GetString(r, o, 15).Trim();
                byte suffix = r[o + 15];
                bool isGroup = (r[o + 16] & 0x80) != 0;
                if (suffix != 0x00 || n.Length == 0) continue;
                if (isGroup) group ??= n; else name ??= n;
            }
            string? mac = o + 6 <= r.Length ? FormatMac(r.AsSpan(o, 6).ToArray()) : null;
            return (name, group, mac);
        }
        catch { return (null, null, null); }
    }

    // ---------------- Portas ----------------

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        // Roteadores, câmeras e NAS usam certificado próprio: aqui só lemos o título da página, não enviamos dados.
        var h = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true, AllowAutoRedirect = true, MaxAutomaticRedirections = 3 };
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(3) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MiCORE");
        return c;
    }

    private static async Task<List<LanService>> ScanPortsAsync(IPAddress ip, CancellationToken ct)
    {
        var results = await Task.WhenAll(Ports.Select(async p =>
        {
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(800);
                await tcp.ConnectAsync(ip, p.Port, cts.Token);
            }
            catch { return null; }

            string detail = "";
            try
            {
                if (p.Name is "SSH" or "FTP") detail = await BannerAsync(tcp, p.Name, ct);
                tcp.Close();
                if (p.Name is "HTTP" or "HTTPS") detail = await HttpTitleAsync(ip, p.Port, p.Name, ct);
            }
            catch { }
            return new LanService(ip.ToString(), p.Name, p.Port, detail);
        }));
        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    /// <summary>Primeira linha que o serviço manda ao conectar (SSH: "SSH-2.0-OpenSSH_9.6"; FTP: "220 ...").</summary>
    private static async Task<string> BannerAsync(TcpClient tcp, string kind, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(1200);
        var buf = new byte[256];
        int n = await tcp.GetStream().ReadAsync(buf, cts.Token);
        string line = Encoding.ASCII.GetString(buf, 0, n).Split('\n')[0].Trim();
        if (kind == "SSH" && line.StartsWith("SSH-")) line = line.Split('-', 3).ElementAtOrDefault(2) ?? line;
        if (kind == "FTP" && line.StartsWith("220")) line = line[3..].TrimStart(' ', '-');
        return Clip(line, 40);
    }

    private static async Task<string> HttpTitleAsync(IPAddress ip, int port, string scheme, CancellationToken ct)
    {
        string url = $"{scheme.ToLowerInvariant()}://{ip}{(port is 80 or 443 ? "" : ":" + port)}/";
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[65536];
        int total = 0, n;
        while (total < buf.Length && (n = await s.ReadAsync(buf.AsMemory(total), ct)) > 0) total += n;
        string html = Encoding.UTF8.GetString(buf, 0, total);
        var m = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        string title = m.Success ? WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\s+", " ")).Trim() : "";
        if (title.Length == 0 && resp.Headers.Server.Count > 0) title = resp.Headers.Server.ToString();
        return Clip(title, 48);
    }

    // ---------------- Wake-on-LAN ----------------

    /// <summary>Envia o "pacote mágico" (6× FF + 16× o MAC) por broadcast UDP nas portas 9 e 7.</summary>
    public static async Task<string> WakeAsync(string macText)
    {
        string hex = Regex.Replace(macText ?? "", "[^0-9A-Fa-f]", "");
        if (hex.Length != 12) return "MAC inválido: use o formato AA:BB:CC:DD:EE:FF.";
        var mac = Convert.FromHexString(hex);
        var packet = new byte[102];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int r = 0; r < 16; r++) Buffer.BlockCopy(mac, 0, packet, 6 + r * 6, 6);

        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var n in LocalNetworks())
            targets.Add(FromUInt(ToUInt(n.Address) | ~ToUInt(n.Mask)));
        using var udp = new UdpClient { EnableBroadcast = true };
        foreach (var t in targets)
            foreach (int port in new[] { 9, 7 })
                await udp.SendAsync(packet, packet.Length, new IPEndPoint(t, port));
        return $"Pacote enviado para {FormatMac(mac)}. O aparelho precisa ter Wake-on-LAN ligado na BIOS/placa de rede e estar no cabo.";
    }

    // ---------------- Exportar ----------------

    public static string ToCsv(IEnumerable<LanDevice> devices)
    {
        static string C(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("IP;Nome;DNS;Grupo;MAC;Fabricante;Ping (ms);Serviços\r\n");
        foreach (var d in devices.OrderBy(d => d.Sort))
            sb.Append(string.Join(";", C(d.Ip), C(d.NetBiosName ?? d.Title), C(d.DnsName), C(d.Workgroup), C(d.Mac), C(d.Vendor),
                                  C(d.PingMs?.ToString()), C(string.Join(", ", d.Services.Select(s => $"{s.Name}:{s.Port}" + (s.Detail.Length > 0 ? $" ({s.Detail})" : ""))))))
              .Append("\r\n");
        return sb.ToString();
    }

    // ---------------- Fabricante pelo MAC (OUI do IEEE) ----------------

    private static Dictionary<string, string>? _oui;
    private static readonly object OuiLock = new();

    public static string? VendorOf(string? mac)
    {
        if (mac == null) return null;
        string hex = mac.Replace(":", "");
        if (hex.Length < 6) return null;
        // Bit "administrado localmente": celulares e Windows usam MAC aleatório por privacidade
        if ((Convert.ToByte(hex[..2], 16) & 0x02) != 0) return "MAC aleatório (privacidade)";
        lock (OuiLock)
        {
            _oui ??= LoadOui();
            return _oui.GetValueOrDefault(hex[..6].ToUpperInvariant());
        }
    }

    private static Dictionary<string, string> LoadOui()
    {
        var d = new Dictionary<string, string>(45000);
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Native/oui.txt.gz");
            if (s == null) return d;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var rd = new StreamReader(gz, Encoding.UTF8);
            string? line;
            while ((line = rd.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab == 6) d[line[..6]] = line[7..];
            }
        }
        catch { }
        return d;
    }

    // ---------------- ARP (iphlpapi) ----------------

    [DllImport("iphlpapi.dll")] private static extern int GetIpNetTable(IntPtr table, ref int size, bool sort);
    [DllImport("iphlpapi.dll")] private static extern int SendARP(uint destIp, uint srcIp, byte[] mac, ref int macLen);

    /// <summary>Entradas dinâmicas/estáticas da tabela ARP do Windows: IP → MAC.</summary>
    private static Dictionary<uint, string> ReadArpTable()
    {
        var result = new Dictionary<uint, string>();
        int size = 0;
        GetIpNetTable(IntPtr.Zero, ref size, false);
        if (size <= 0) return result;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetIpNetTable(buf, ref size, false) != 0) return result;
            int count = Marshal.ReadInt32(buf);
            for (int i = 0; i < count; i++)
            {
                IntPtr row = buf + 4 + i * 24;               // MIB_IPNETROW: índice, tam. MAC, MAC[8], IP, tipo
                int len = Marshal.ReadInt32(row, 4);
                int type = Marshal.ReadInt32(row, 20);       // 3 = dinâmica, 4 = estática
                if ((type != 3 && type != 4) || len != 6) continue;
                var mac = new byte[6];
                Marshal.Copy(row + 8, mac, 0, 6);
                if (mac.All(b => b == 0) || mac.All(b => b == 0xFF) || (mac[0] & 0x01) != 0) continue; // vazio, broadcast, multicast
                uint ip = ToUInt(new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row, 16))));
                result[ip] = FormatMac(mac)!;
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }

    private static string? SendArp(uint ip)
    {
        var mac = new byte[6];
        int len = 6;
        uint dest = BitConverter.ToUInt32(FromUInt(ip).GetAddressBytes(), 0);
        return SendARP(dest, 0, mac, ref len) == 0 && len == 6 ? FormatMac(mac) : null;
    }

    // ---------------- Utilitários ----------------

    private static string? FormatMac(byte[] b) =>
        b.Length != 6 || b.All(x => x == 0) ? null : string.Join(":", b.Select(x => x.ToString("X2")));

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static bool TryIp(string s, out uint value)
    {
        value = 0;
        if (!IPAddress.TryParse(s.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || s.Count(c => c == '.') != 3) return false;
        value = ToUInt(ip);
        return true;
    }

    private static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static IPAddress FromUInt(uint v) => new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    private static int PrefixLength(IPAddress mask)
    {
        uint m = ToUInt(mask);
        int n = 0;
        while ((m & 0x80000000) != 0) { n++; m <<= 1; }
        return n;
    }
}
