using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MiCORE;

/// <summary>
/// Instala o winget (Gerenciador de Pacotes do Windows) quando ele não existe no PC, como no Windows 10 LTSC
/// ou em PC recém-formatado sem Microsoft Store. Baixa a última versão oficial da Microsoft
/// (github.com/microsoft/winget-cli/releases) e instala com Add-AppxPackage, que só aceita pacotes assinados.
/// </summary>
public static class WingetSetup
{
    private const string Repo = "microsoft/winget-cli";
    private const string BundleName = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
    private const string DepsName = "DesktopAppInstaller_Dependencies.zip";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MiCORE");
        return c;
    }

    /// <summary>
    /// Garante o winget no PC. Devolve null se ele está disponível ao final, ou a explicação do que falhou.
    /// <paramref name="progress"/> recebe frases curtas para mostrar na tela.
    /// </summary>
    public static async Task<string?> EnsureAsync(Action<string> progress, CancellationToken ct = default)
    {
        if (AppInstaller.WingetAvailable) return null;
        if (!SensorMonitor.IsElevated) return "o MiCORE precisa estar aberto como administrador para instalar o winget";

        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64", Architecture.X86 => "x86", _ => "x64"
        };
        string dir = Path.Combine(Path.GetTempPath(), "MiCORE-winget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 1) Última release oficial
            progress("Procurando a versão mais recente do winget…");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
            string? bundleUrl = null, depsUrl = null, licUrl = null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                string url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (!url.StartsWith("https://github.com/microsoft/winget-cli/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals(BundleName, StringComparison.OrdinalIgnoreCase)) bundleUrl = url;
                else if (name.Equals(DepsName, StringComparison.OrdinalIgnoreCase)) depsUrl = url;
                else if (name.EndsWith("_License1.xml", StringComparison.OrdinalIgnoreCase)) licUrl = url;
            }
            if (bundleUrl == null || depsUrl == null) return "não encontrei os arquivos do winget na release oficial da Microsoft";

            // 2) Downloads
            string bundle = Path.Combine(dir, BundleName);
            string deps = Path.Combine(dir, DepsName);
            string? lic = licUrl != null ? Path.Combine(dir, "License1.xml") : null;
            await DownloadAsync(bundleUrl, bundle, "Baixando o winget", progress, ct);
            await DownloadAsync(depsUrl, deps, "Baixando as dependências do winget", progress, ct);
            if (lic != null) await DownloadAsync(licUrl!, lic, "Baixando a licença do winget", progress, ct);

            // 3) Só as dependências da arquitetura deste PC
            string depDir = Path.Combine(dir, "deps");
            Directory.CreateDirectory(depDir);
            var depFiles = new List<string>();
            using (var zip = ZipFile.OpenRead(deps))
                foreach (var e in zip.Entries)
                    if (e.FullName.StartsWith(arch + "/", StringComparison.OrdinalIgnoreCase)
                        && (e.Name.EndsWith(".appx", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)))
                    {
                        string target = Path.Combine(depDir, e.Name);
                        e.ExtractToFile(target, true);
                        depFiles.Add(target);
                    }
            if (depFiles.Count == 0) return $"o pacote de dependências não tem a versão para {arch}";

            // 4) Instalação: para este usuário (vale na hora) e, se der, para todos os usuários do PC
            progress("Instalando o winget… (pode levar um ou dois minutos)");
            string script = Path.Combine(dir, "instalar.ps1");
            await File.WriteAllTextAsync(script, """
                param([string]$Bundle, [string]$DepDir, [string]$License)
                $ErrorActionPreference = 'Stop'
                $deps = @(Get-ChildItem -LiteralPath $DepDir -File | ForEach-Object { $_.FullName })
                foreach ($d in $deps) {
                    try { Add-AppxPackage -Path $d -ForceApplicationShutdown -ErrorAction Stop }
                    catch { if ($_.Exception.HResult -ne -2147009274) { Write-Warning $_ } } # versão maior já instalada: tudo bem
                }
                Add-AppxPackage -Path $Bundle -ForceApplicationShutdown
                if ($License) {
                    try { Add-AppxProvisionedPackage -Online -PackagePath $Bundle -DependencyPackagePath $deps -LicensePath $License | Out-Null } catch { }
                }
                """, ct);
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                                        "-Bundle", bundle, "-DepDir", depDir, "-License", lic ?? "" })
                psi.ArgumentList.Add(arg);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("não foi possível iniciar o PowerShell");
            var errTask = p.StandardError.ReadToEndAsync(ct);
            _ = p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            string err = (await errTask).Trim();

            // 5) O atalho winget.exe aparece alguns segundos depois do registro do pacote
            for (int i = 0; i < 30 && !AppInstaller.WingetAvailable; i++) await Task.Delay(500, ct);
            if (AppInstaller.WingetAvailable) { progress("winget instalado."); return null; }
            return p.ExitCode != 0 && err.Length > 0
                ? "o Windows recusou a instalação do winget: " + FirstLine(err)
                : "o winget foi instalado, mas ainda não apareceu. Saia da conta do Windows e entre de novo, e tente outra vez";
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) { return "sem acesso à internet para baixar o winget (" + ex.Message + ")"; }
        catch (Exception ex) { return "falha ao instalar o winget: " + ex.Message; }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static async Task DownloadAsync(string url, string file, string label, Action<string> progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(file);
        var buf = new byte[1 << 16];
        long read = 0; int n; var last = DateTime.MinValue;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if ((DateTime.Now - last).TotalMilliseconds > 400)
            {
                last = DateTime.Now;
                progress(total is long t && t > 0
                    ? $"{label}… {read / 1048576.0:0} de {t / 1048576.0:0} MB"
                    : $"{label}… {read / 1048576.0:0} MB");
            }
        }
    }

    private static string FirstLine(string s)
    {
        int i = s.IndexOfAny(new[] { '\r', '\n' });
        return i < 0 ? s : s[..i];
    }
}
