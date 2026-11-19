using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MiCORE;

/// <summary>Um item da lista de limpeza (uma categoria própria ou uma entrada do Winapp2).</summary>
public sealed class CleanItem : INotifyPropertyChanged
{
    public string Group { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Warning { get; init; }
    public bool Recommended { get; init; }
    /// <summary>Processos que precisam estar fechados para limpar tudo (ex.: chrome).</summary>
    public string[] Processes { get; init; } = Array.Empty<string>();

    internal List<Cleaner.FileRule> Files { get; } = new();
    internal List<Cleaner.RegRule> Registry { get; } = new();
    internal List<Cleaner.ExcludeRule> Excludes { get; } = new();
    internal bool RecycleBin { get; init; }

    private bool _selected;
    private long _bytes;
    private int _files, _regs;
    private string _status = "";

    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public long Bytes { get => _bytes; set { Set(ref _bytes, value); OnChanged(nameof(SizeText)); } }
    public int FileCount { get => _files; set { Set(ref _files, value); OnChanged(nameof(SizeText)); } }
    public int RegCount { get => _regs; set { Set(ref _regs, value); OnChanged(nameof(SizeText)); } }
    public bool Scanned { get; set; }
    public string Status { get => _status; set { Set(ref _status, value); OnChanged(nameof(SizeText)); } }

    public string SizeText => Status.Length > 0 ? Status
        : !Scanned ? ""
        : FileCount == 0 && RegCount == 0 && Bytes == 0 ? "vazio"
        : Cleaner.FormatBytes(Bytes) + (RegCount > 0 ? $" · {RegCount} no registro" : "");
    public string Details => Warning != null ? $"{Description} ⚠ {Warning}".Trim() : Description;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(p);
    }
}

/// <summary>Grupo da página de limpeza (Windows, Jogos, Navegadores...).</summary>
public sealed class CleanGroup : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<CleanItem> Items { get; init; } = new();
    private string _total = "";
    public string Total { get => _total; set { _total = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Total))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Motor da página Limpeza. Junta categorias próprias do MiCORE (temporários, Prefetch, Lixeira, caches do Windows e de
/// shaders) com as regras do Winapp2 (github.com/MoscaDotTo/Winapp2, CC BY-SA 4.0), embutidas em Native\Winapp2.ini.gz.
/// ANALISAR só soma tamanhos; LIMPAR apaga arquivos (e, no Winapp2, chaves do registro do usuário atual).
/// Travas: nunca apaga a raiz de pastas do sistema, não segue junções/links, não mexe em HKLM e pula arquivos em uso.
/// </summary>
public static class Cleaner
{
    internal sealed record FileRule(string Dir, string[] Patterns, bool Recurse, bool RemoveSelf, TimeSpan? MinAge = null, bool RemoveEmptyDirs = false);
    internal sealed record RegRule(RegistryHive Hive, string Key, string? Value);
    internal sealed record ExcludeRule(bool AnyDepth, string Dir, string Pattern);

    // ---------------- Catálogo ----------------

    /// <summary>Monta os grupos: categorias próprias + entradas do Winapp2 detectadas neste PC.</summary>
    public static List<CleanGroup> BuildCatalog()
    {
        var groups = new List<CleanGroup>
        {
            new() { Name = "Windows", Description = "Temporários, caches e relatórios do próprio Windows.", Items = WindowsItems() },
            new() { Name = "Jogos · cache de shaders", Description = "O jogo recria na próxima vez (as primeiras partidas podem engasgar um pouco). Limpe quando um jogo travar ou depois de trocar de driver.", Items = ShaderItems() },
        };
        try
        {
            var w = Winapp2Items();
            foreach (var g in w.GroupBy(i => i.Group).OrderBy(g => GroupOrder(g.Key)).ThenBy(g => g.Key))
                groups.Add(new CleanGroup { Name = g.Key, Items = g.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), Description = GroupDescription(g.Key) });
        }
        catch { /* sem Winapp2: ficam só as categorias próprias */ }
        foreach (var i in groups.SelectMany(g => g.Items)) i.Selected = i.Recommended;
        return groups.Where(g => g.Items.Count > 0).ToList();
    }

    private static List<CleanItem> WindowsItems()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var hour = TimeSpan.FromHours(1);
        var list = new List<CleanItem>();

        CleanItem Item(string name, string desc, bool rec, params FileRule[] rules)
        {
            var i = new CleanItem { Group = "Windows", Name = name, Description = desc, Recommended = rec };
            i.Files.AddRange(rules);
            list.Add(i);
            return i;
        }

        Item("Temporários do usuário (%TEMP%)",
             $"{Path.GetTempPath().TrimEnd('\\')} · arquivos que os programas deixam para trás. Os criados na última hora ficam (instalações em andamento).",
             true, new FileRule(Path.GetTempPath().TrimEnd('\\'), new[] { "*" }, true, false, hour, true));
        Item("Temporários do Windows (Temp)",
             $"{Path.Combine(win, "Temp")} · temporários do sistema e de instaladores. Os criados na última hora ficam.",
             true, new FileRule(Path.Combine(win, "Temp"), new[] { "*" }, true, false, hour, true));
        Item("Prefetch",
             $"{Path.Combine(win, "Prefetch")} · rastros que o Windows usa para abrir programas mais rápido. Ele recria sozinho, mas na primeira vez os programas abrem um pouco mais devagar.",
             false, new FileRule(Path.Combine(win, "Prefetch"), new[] { "*.pf" }, false, false));
        list.Add(new CleanItem { Group = "Windows", Name = "Lixeira", Description = "Esvazia a Lixeira de todos os discos.", Recommended = true, RecycleBin = true });
        Item("Cache do Windows Update",
             "Instaladores de atualizações já baixados (SoftwareDistribution\\Download). O Windows baixa de novo se precisar.",
             true, new FileRule(Path.Combine(win, @"SoftwareDistribution\Download"), new[] { "*" }, true, false, null, true));
        Item("Otimização de Entrega",
             "Pedaços de atualizações guardados para compartilhar com outros PCs.",
             true, new FileRule(Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"), new[] { "*" }, true, false, null, true));
        Item("Relatórios de erro do Windows",
             "Relatórios de travamento de programas (WER) já enviados ou na fila.", true,
             new FileRule(Path.Combine(progData, @"Microsoft\Windows\WER\ReportArchive"), new[] { "*" }, true, false, null, true),
             new FileRule(Path.Combine(progData, @"Microsoft\Windows\WER\ReportQueue"), new[] { "*" }, true, false, null, true),
             new FileRule(Path.Combine(progData, @"Microsoft\Windows\WER\Temp"), new[] { "*" }, true, false, null, true),
             new FileRule(Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive"), new[] { "*" }, true, false, null, true),
             new FileRule(Path.Combine(local, @"Microsoft\Windows\WER\ReportQueue"), new[] { "*" }, true, false, null, true));
        Item("Despejos de memória (tela azul)",
             "Arquivos .dmp de telas azuis e travamentos. Guarde-os se ainda for investigar uma tela azul.", false,
             new FileRule(Path.Combine(win, "Minidump"), new[] { "*.dmp" }, false, false),
             new FileRule(win, new[] { "MEMORY.DMP" }, false, false),
             new FileRule(Path.Combine(local, "CrashDumps"), new[] { "*" }, false, false));
        Item("Cache de miniaturas",
             "Miniaturas de imagens e vídeos do Explorador. São recriadas ao abrir as pastas. Os arquivos em uso pelo Explorador ficam.", false,
             new FileRule(Path.Combine(local, @"Microsoft\Windows\Explorer"), new[] { "thumbcache_*.db", "iconcache_*.db" }, false, false));
        Item("Logs de manutenção do Windows",
             "Registros do CBS e do DISM (Logs\\CBS e Logs\\DISM). Úteis só para diagnosticar falhas de atualização.", false,
             new FileRule(Path.Combine(win, @"Logs\CBS"), new[] { "*.log", "*.cab" }, false, false),
             new FileRule(Path.Combine(win, @"Logs\DISM"), new[] { "*.log" }, false, false));
        return list;
    }

    private static List<CleanItem> ShaderItems()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string low = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow");
        string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var list = new List<CleanItem>();
        void Add(string name, string desc, params string[] dirs)
        {
            var existing = dirs.Where(Directory.Exists).ToList();
            if (existing.Count == 0) return;
            var i = new CleanItem { Group = "Jogos · cache de shaders", Name = name, Description = desc, Recommended = false };
            foreach (var d in existing) i.Files.Add(new FileRule(d, new[] { "*" }, true, false, null, true));
            list.Add(i);
        }
        Add("DirectX (Windows)", "Cache de shaders do DirectX (D3DSCache).", Path.Combine(local, "D3DSCache"));
        Add("NVIDIA", "DXCache, GLCache e NV_Cache do driver NVIDIA.",
            Path.Combine(local, @"NVIDIA\DXCache"), Path.Combine(local, @"NVIDIA\GLCache"),
            Path.Combine(low, @"NVIDIA\PerDriverVersion\DXCache"), Path.Combine(low, @"NVIDIA\PerDriverVersion\GLCache"),
            Path.Combine(progData, @"NVIDIA Corporation\NV_Cache"));
        Add("AMD", "DxCache, DxcCache, VkCache e GLCache do driver AMD.",
            Path.Combine(local, @"AMD\DxCache"), Path.Combine(local, @"AMD\DxcCache"), Path.Combine(local, @"AMD\VkCache"),
            Path.Combine(local, @"AMD\GLCache"), Path.Combine(local, @"AMD\OglCache"));
        Add("Intel", "Cache de shaders do driver de vídeo Intel.", Path.Combine(low, @"Intel\ShaderCache"), Path.Combine(local, @"Intel\ShaderCache"));
        return list;
    }

    private static int GroupOrder(string g) => g switch
    {
        "Navegadores" => 0, "Internet" => 1, "Programas" => 2, "Multimídia" => 3, "Utilitários" => 4, "Jogos" => 5, "Windows · outros" => 6, _ => 7
    };

    private static string GroupDescription(string g) => g switch
    {
        "Navegadores" => "Cache, histórico e rastros dos navegadores. Feche o navegador antes. Senhas, cookies e sessões começam desmarcados.",
        "Windows · outros" => "Rastros e listas de itens recentes do Windows.",
        _ => "Caches e rastros dos programas instalados neste PC."
    };

    // ---------------- Winapp2 ----------------

    private static readonly Regex Sensitive = new(@"cookie|password|senha|session|sess(ã|a)o|history|hist(ó|o)rico|form|autofill|login|credential|bookmark|favorite|settings|preferences|saved|recent|mru|jump ?list|extension|state|database|event|download|song|music|offline|backup", RegexOptions.IgnoreCase);
    private static readonly Regex Safe = new(@"\b(caches?|temp|temporary|logs?|dumps?|crash(es)?|crash ?reports?|thumbnails?)\b", RegexOptions.IgnoreCase);

    /// <summary>Entradas do Winapp2 que repetem as categorias próprias do MiCORE (somariam o mesmo espaço duas vezes).</summary>
    private static readonly Regex DuplicateOfBuiltIn = new(
        @"^(Windows Recycle Bin|Windows Temporary Files|Windows Error Reporting|Windows Prefetch|Prefetch|Windows Delivery Optimization|Delivery Optimization|" +
        @"Microsoft DirectX Shader Cache|NVIDIA Shader Cache|AMD Shader Cache|Intel Shader Cache|Windows Memory Dumps|Memory Dumps|Thumbnail Cache|Windows Thumbnail Cache)$",
        RegexOptions.IgnoreCase);

    private static readonly (string Key, string Process)[] BrowserProcesses =
    {
        ("Chrome", "chrome"), ("Edge", "msedge"), ("Firefox", "firefox"), ("Opera", "opera"), ("Brave", "brave"),
        ("Vivaldi", "vivaldi"), ("Thunderbird", "thunderbird"), ("Waterfox", "waterfox"), ("Chromium", "chrome"),
    };

    private static List<CleanItem> Winapp2Items()
    {
        string text;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Native/Winapp2.ini.gz"))
        {
            if (s == null) return new();
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var rd = new StreamReader(gz, Encoding.UTF8);
            text = rd.ReadToEnd();
        }

        var items = new List<CleanItem>();
        string? name = null;
        var keys = new List<(string K, string V)>();
        void Flush()
        {
            if (name != null && keys.Count > 0)
                try { if (FromWinapp2(name, keys) is CleanItem i) items.Add(i); } catch { }
            keys.Clear();
        }
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { Flush(); name = line[1..^1].Trim(); continue; }
            int eq = line.IndexOf('=');
            if (eq > 0) keys.Add((Regex.Replace(line[..eq], @"\d+$", ""), line[(eq + 1)..].Trim()));
        }
        Flush();
        return items;
    }

    private static CleanItem? FromWinapp2(string rawName, List<(string K, string V)> keys)
    {
        string Get(string k) => keys.FirstOrDefault(x => x.K.Equals(k, StringComparison.OrdinalIgnoreCase)).V ?? "";
        IEnumerable<string> All(string k) => keys.Where(x => x.K.Equals(k, StringComparison.OrdinalIgnoreCase)).Select(x => x.V);

        // Só entra se o programa existe neste PC e a versão do Windows bate
        if (Get("DetectOS") is { Length: > 0 } os && !OsMatches(os)) return null;
        var detects = All("Detect").ToList();
        var detectFiles = All("DetectFile").ToList();
        if (detects.Count + detectFiles.Count > 0 && !detects.Any(RegistryExists) && !detectFiles.Any(PathExists)) return null;

        string name = rawName.EndsWith(" *") ? rawName[..^2] : rawName;
        if (DuplicateOfBuiltIn.IsMatch(name)) return null;
        string group = GroupOf(Get("LangSecRef"), Get("Section"));
        string? warning = Get("Warning") is { Length: > 0 } w ? w : null;
        var item = new CleanItem
        {
            Group = group, Name = name, Warning = warning,
            Description = Get("Section") is { Length: > 0 } sec && group != sec && !sec.StartsWith('.') ? sec : "",
            Recommended = warning == null && group != "Navegadores" ? Safe.IsMatch(name) && !Sensitive.IsMatch(name)
                        : warning == null && Regex.IsMatch(name, @"\bcaches?\b", RegexOptions.IgnoreCase) && !Sensitive.IsMatch(name),
            Processes = BrowserProcesses.Where(b => name.Contains(b.Key, StringComparison.OrdinalIgnoreCase)).Select(b => b.Process).Distinct().ToArray(),
        };

        bool regExcluded = false;
        foreach (var ex in All("ExcludeKey"))
        {
            var p = ex.Split('|');
            if (p[0].Equals("REG", StringComparison.OrdinalIgnoreCase)) { regExcluded = true; continue; }
            if (p.Length < 2) continue;
            bool anyDepth = p[0].Equals("PATH", StringComparison.OrdinalIgnoreCase);
            foreach (var dir in ExpandVars(p[1]))
                item.Excludes.Add(new ExcludeRule(anyDepth, dir.TrimEnd('\\'), p.Length > 2 && p[2].Length > 0 ? p[2] : "*"));
        }
        foreach (var fk in All("FileKey"))
        {
            var p = fk.Split('|');
            if (p.Length < 1 || p[0].Length == 0) continue;
            var patterns = (p.Length > 1 && p[1].Length > 0 ? p[1] : "*").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var flags = p.Skip(2).Select(f => f.Trim().ToUpperInvariant()).ToHashSet();
            bool removeSelf = flags.Contains("REMOVESELF");
            foreach (var dir in ExpandVars(p[0]))
                item.Files.Add(new FileRule(dir.TrimEnd('\\'), patterns, flags.Contains("RECURSE") || removeSelf, removeSelf));
        }
        if (!regExcluded)
            foreach (var rk in All("RegKey"))
            {
                var p = rk.Split('|');
                if (!TrySplitHive(p[0], out var hive, out string sub) || hive != RegistryHive.CurrentUser) continue; // só o usuário atual
                item.Registry.Add(new RegRule(hive, sub, p.Length > 1 && p[1].Length > 0 ? p[1] : null));
            }
        return item.Files.Count + item.Registry.Count > 0 ? item : null;
    }

    private static string GroupOf(string langSecRef, string section)
    {
        if (section.Contains("Browser", StringComparison.OrdinalIgnoreCase)) return "Navegadores";
        if (section.Equals("Games", StringComparison.OrdinalIgnoreCase)) return "Jogos";
        return langSecRef switch
        {
            "3026" or "3027" or "3029" or "3030" or "3032" or "3033" or "3034" or "3035" or "3037" or "3038" or "3039" or "3043" or "3044" => "Navegadores",
            "3021" => "Programas",
            "3022" => "Internet",
            "3023" or "3036" => "Multimídia",
            "3024" => "Utilitários",
            "3025" or "3006" => "Windows · outros",
            _ => section.Length > 0 && !section.StartsWith('.') ? "Programas" : "Programas",
        };
    }

    private static bool OsMatches(string spec)
    {
        var p = spec.Split('|');
        var v = Environment.OSVersion.Version;
        var cur = new Version(v.Major, v.Minor);
        if (p.Length > 0 && Version.TryParse(p[0], out var min) && cur < min) return false;
        if (p.Length > 1 && Version.TryParse(p[1], out var max) && cur > max) return false;
        return true;
    }

    // ---------------- Caminhos ----------------

    private static readonly Dictionary<string, string[]> Vars = BuildVars();

    private static Dictionary<string, string[]> BuildVars()
    {
        string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        string profile = F(Environment.SpecialFolder.UserProfile);
        var pf = new[] { F(Environment.SpecialFolder.ProgramFiles), F(Environment.SpecialFolder.ProgramFilesX86) }.Where(s => s.Length > 0).Distinct().ToArray();
        var cpf = new[] { F(Environment.SpecialFolder.CommonProgramFiles), F(Environment.SpecialFolder.CommonProgramFilesX86) }.Where(s => s.Length > 0).Distinct().ToArray();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["LocalAppData"] = new[] { F(Environment.SpecialFolder.LocalApplicationData) },
            ["AppData"] = new[] { F(Environment.SpecialFolder.ApplicationData) },
            ["LocalLowAppData"] = new[] { Path.Combine(profile, @"AppData\LocalLow") },
            ["ProgramFiles"] = pf,
            ["CommonProgramFiles"] = cpf,
            ["ProgramData"] = new[] { F(Environment.SpecialFolder.CommonApplicationData) },
            ["CommonAppData"] = new[] { F(Environment.SpecialFolder.CommonApplicationData) },
            ["UserProfile"] = new[] { profile },
            ["SystemDrive"] = new[] { Path.GetPathRoot(F(Environment.SpecialFolder.Windows))!.TrimEnd('\\') },
            ["WinDir"] = new[] { F(Environment.SpecialFolder.Windows) },
            ["SystemRoot"] = new[] { F(Environment.SpecialFolder.Windows) },
            ["Public"] = new[] { Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public" },
            ["UserName"] = new[] { Environment.UserName },
            ["Documents"] = new[] { F(Environment.SpecialFolder.MyDocuments) },
            ["Temp"] = new[] { Path.GetTempPath().TrimEnd('\\') },
        };
    }

    /// <summary>Troca %Variáveis% pelos caminhos reais (ProgramFiles vira as duas pastas, 64 e 32 bits).</summary>
    private static IEnumerable<string> ExpandVars(string path)
    {
        var results = new List<string> { path };
        foreach (Match m in Regex.Matches(path, "%([A-Za-z]+)%"))
        {
            if (!Vars.TryGetValue(m.Groups[1].Value, out var values)) return Array.Empty<string>(); // variável desconhecida: ignora a regra
            results = results.SelectMany(r => values.Select(v => r.Replace(m.Value, v, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        return results.Where(r => !r.Contains('%')).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Pastas que casam com um caminho que pode ter * e ? em qualquer parte (ex.: ...\Chrome*\User Data\*).</summary>
    private static IEnumerable<string> GlobDirs(string pattern)
    {
        string root = Path.GetPathRoot(pattern) ?? "";
        if (root.Length == 0) yield break;
        var segments = pattern[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string> { root };
        foreach (var seg in segments)
        {
            var next = new List<string>();
            bool wild = seg.IndexOfAny(new[] { '*', '?' }) >= 0;
            foreach (var dir in current)
            {
                if (!wild)
                {
                    string p = Path.Combine(dir, seg);
                    if (Directory.Exists(p)) next.Add(p);
                    continue;
                }
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(dir, seg, new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                        next.Add(d);
                }
                catch { }
            }
            current = next;
            if (current.Count == 0) yield break;
        }
        foreach (var d in current) yield return d;
    }

    private static bool PathExists(string pattern)
    {
        foreach (var p in ExpandVars(pattern))
        {
            if (p.IndexOfAny(new[] { '*', '?' }) < 0) { if (File.Exists(p) || Directory.Exists(p)) return true; continue; }
            if (GlobDirs(p).Any()) return true;
            // o último pedaço pode ser um arquivo com curinga
            string? dir = Path.GetDirectoryName(p);
            string file = Path.GetFileName(p);
            if (dir != null)
                foreach (var d in GlobDirs(dir))
                    try { if (Directory.EnumerateFiles(d, file).Any()) return true; } catch { }
        }
        return false;
    }

    private static bool TrySplitHive(string full, out RegistryHive hive, out string sub)
    {
        hive = RegistryHive.CurrentUser; sub = "";
        int i = full.IndexOf('\\');
        string h = (i < 0 ? full : full[..i]).ToUpperInvariant();
        sub = i < 0 ? "" : full[(i + 1)..];
        switch (h)
        {
            case "HKCU": case "HKEY_CURRENT_USER": hive = RegistryHive.CurrentUser; return true;
            case "HKLM": case "HKEY_LOCAL_MACHINE": hive = RegistryHive.LocalMachine; return true;
            case "HKU": case "HKEY_USERS": hive = RegistryHive.Users; return true;
            case "HKCR": case "HKEY_CLASSES_ROOT": hive = RegistryHive.ClassesRoot; return true;
            default: return false;
        }
    }

    private static bool RegistryExists(string full)
    {
        if (!TrySplitHive(full, out var hive, out string sub)) return false;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var b = RegistryKey.OpenBaseKey(hive, view);
                using var k = b.OpenSubKey(sub);
                if (k != null) return true;
            }
            catch { }
        }
        return false;
    }

    // ---------------- Travas de segurança ----------------

    private static readonly HashSet<string> Protected = BuildProtected();

    private static HashSet<string> BuildProtected()
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
                                  Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                                  Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                                  Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyPictures,
                                  Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.CommonProgramFiles,
                                  Environment.SpecialFolder.CommonProgramFilesX86 })
        {
            string p = Environment.GetFolderPath(f);
            if (p.Length > 0) s.Add(p.TrimEnd('\\'));
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        s.Add(Path.Combine(profile, @"AppData\LocalLow"));
        s.Add(Path.Combine(profile, "AppData"));
        s.Add(Path.Combine(profile, "Downloads"));
        s.Add(Path.GetDirectoryName(profile)!.TrimEnd('\\')); // C:\Users
        foreach (var d in DriveInfo.GetDrives()) s.Add(d.Name.TrimEnd('\\'));
        s.Add(AppContext.BaseDirectory.TrimEnd('\\'));
        return s;
    }

    /// <summary>Uma regra que apagaria tudo de uma pasta do sistema (ou a própria pasta) é ignorada.</summary>
    private static bool Unsafe(FileRule r, string dir) =>
        Protected.Contains(dir.TrimEnd('\\')) && (r.Recurse || r.RemoveSelf || r.Patterns.Any(p => p == "*" || p == "*.*"));

    // ---------------- Analisar e limpar ----------------

    public sealed record Totals(long Bytes, int Files, int Failed, int Regs);

    /// <summary>Soma o que seria apagado, sem apagar nada.</summary>
    public static Totals Analyze(CleanItem item, CancellationToken ct) => Run(item, delete: false, ct);

    /// <summary>Apaga de verdade. Arquivos em uso ou sem permissão são pulados e contados em Failed.</summary>
    public static Totals Clean(CleanItem item, CancellationToken ct) => Run(item, delete: true, ct);

    private static Totals Run(CleanItem item, bool delete, CancellationToken ct)
    {
        if (item.RecycleBin) return RecycleBin(delete);

        long bytes = 0; int files = 0, failed = 0, regs = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in item.Files)
        {
            foreach (var dir in GlobDirs(rule.Dir).ToList())
            {
                ct.ThrowIfCancellationRequested();
                if (Unsafe(rule, dir)) continue;
                foreach (var f in EnumerateFiles(dir, rule.Recurse))
                {
                    if ((files & 255) == 0) ct.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(f);
                    if (!rule.Patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, name, ignoreCase: true))) continue;
                    if (Excluded(item, f) || !seen.Add(f)) continue;
                    FileInfo fi;
                    try { fi = new FileInfo(f); if (!fi.Exists) continue; } catch { continue; }
                    if (rule.MinAge is TimeSpan age && DateTime.UtcNow - fi.LastWriteTimeUtc < age) continue;
                    long len = fi.Length;
                    if (!delete) { bytes += len; files++; continue; }
                    try
                    {
                        if ((fi.Attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0) fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        bytes += len; files++;
                    }
                    catch { failed++; }
                }
                if (delete && (rule.RemoveSelf || rule.RemoveEmptyDirs)) RemoveEmptyDirs(dir, removeRoot: rule.RemoveSelf);
            }
        }
        foreach (var r in item.Registry)
        {
            try
            {
                using var b = RegistryKey.OpenBaseKey(r.Hive, RegistryView.Default);
                if (r.Value == null)
                {
                    using var k = b.OpenSubKey(r.Key);
                    if (k == null) continue;
                    regs++;
                    k.Close();
                    if (delete) b.DeleteSubKeyTree(r.Key, throwOnMissingSubKey: false);
                }
                else
                {
                    using var k = b.OpenSubKey(r.Key, writable: delete);
                    if (k?.GetValue(r.Value) == null) continue;
                    regs++;
                    if (delete) k.DeleteValue(r.Value, throwOnMissingValue: false);
                }
            }
            catch { failed++; }
        }
        return new Totals(bytes, files, failed, regs);
    }

    private static bool Excluded(CleanItem item, string file)
    {
        if (item.Excludes.Count == 0) return false;
        string dir = Path.GetDirectoryName(file) ?? "";
        string name = Path.GetFileName(file);
        foreach (var ex in item.Excludes)
        {
            bool inDir = ex.AnyDepth
                ? dir.Equals(ex.Dir, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(ex.Dir + "\\", StringComparison.OrdinalIgnoreCase)
                : dir.Equals(ex.Dir, StringComparison.OrdinalIgnoreCase);
            if (inDir && FileSystemName.MatchesSimpleExpression(ex.Pattern, name, ignoreCase: true)) return true;
        }
        return false;
    }

    /// <summary>Arquivos da pasta (e subpastas, se pedido), sem entrar em junções/links simbólicos.</summary>
    private static IEnumerable<string> EnumerateFiles(string dir, bool recurse)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = recurse };
        IEnumerator<string> e;
        try { e = Directory.EnumerateFiles(dir, "*", opts).GetEnumerator(); } catch { yield break; }
        using (e)
            while (true)
            {
                bool ok;
                try { ok = e.MoveNext(); } catch { yield break; }
                if (!ok) yield break;
                yield return e.Current;
            }
    }

    private static void RemoveEmptyDirs(string dir, bool removeRoot)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                RemoveEmptyDirs(sub, removeRoot: true);
            if (removeRoot && !Protected.Contains(dir.TrimEnd('\\')) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch { }
    }

    // ---------------- Lixeira ----------------

    [StructLayout(LayoutKind.Sequential)] // alinhamento natural: 24 bytes no processo de 64 bits
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);

    private static Totals RecycleBin(bool empty)
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        if (SHQueryRecycleBinW(null, ref info) != 0) return new Totals(0, 0, 0, 0);
        if (!empty || info.i64NumItems == 0) return new Totals(info.i64Size, (int)info.i64NumItems, 0, 0);
        const uint NoConfirm = 0x1, NoProgress = 0x2, NoSound = 0x4;
        int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, NoConfirm | NoProgress | NoSound);
        return hr == 0 ? new Totals(info.i64Size, (int)info.i64NumItems, 0, 0) : new Totals(0, 0, (int)info.i64NumItems, 0);
    }

    // ---------------- Utilitários ----------------

    /// <summary>Navegadores/programas abertos entre os processos que os itens marcados pedem para fechar.</summary>
    public static List<string> RunningProcesses(IEnumerable<CleanItem> items)
    {
        var names = items.SelectMany(i => i.Processes).Distinct(StringComparer.OrdinalIgnoreCase);
        var running = new List<string>();
        foreach (var n in names)
        {
            var ps = Process.GetProcessesByName(n);
            if (ps.Length > 0) running.Add(n);
            foreach (var p in ps) p.Dispose();
        }
        return running;
    }

    public static string FormatBytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{b / 1024.0:0} KB",
        _ => $"{b} B",
    };
}
