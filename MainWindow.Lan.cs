using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace MiCORE;

/// <summary>Página Dispositivos na rede: varredura da rede local (LanScanner), abrir serviços e Wake-on-LAN.</summary>
public partial class MainWindow
{
    private readonly ObservableCollection<LanDevice> _lanDevices = new();
    private CancellationTokenSource? _lanCts;
    private bool _lanRunning, _lanInit;

    private void InitLan()
    {
        if (_lanInit) return;
        _lanInit = true;
        LanList.ItemsSource = _lanDevices;
        var net = LanScanner.LocalNetworks().FirstOrDefault();
        if (net == null)
        {
            LanStatus.Text = "Nenhuma rede IPv4 ativa encontrada. Conecte o PC ao cabo ou Wi-Fi e abra esta página de novo.";
            _lanInit = false;
            return;
        }
        LanRange.Text = LanScanner.SuggestedRange(net);
        LanStatus.Text = $"Rede {net.Adapter}: este PC é {net.Address}" + (net.Gateway != null ? $", roteador {net.Gateway}" : "") +
                         ". Clique em VARRER.";
    }

    private void BtnLanMine_Click(object sender, RoutedEventArgs e)
    {
        var net = LanScanner.LocalNetworks().FirstOrDefault();
        if (net != null) LanRange.Text = LanScanner.SuggestedRange(net);
    }

    private async void BtnLanScan_Click(object sender, RoutedEventArgs e)
    {
        if (_lanRunning) { _lanCts?.Cancel(); return; }
        var targets = LanScanner.ParseRange(LanRange.Text, out string? error);
        if (targets == null) { LanStatus.Text = error; return; }

        _lanRunning = true;
        _lanCts = new CancellationTokenSource();
        BtnLanScan.Content = "CANCELAR";
        BtnLanExport.IsEnabled = false;
        _lanDevices.Clear();
        LanSummary.Text = "APARELHOS ENCONTRADOS";
        LanProgress.ScaleX = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            int count = await Task.Run(() => LanScanner.ScanAsync(targets,
                dev => Dispatcher.BeginInvoke(new Action(() => InsertLanDevice(dev))),
                (frac, text) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_lanRunning) return;
                    LanStatus.Text = text;
                    LanProgress.ScaleX = Math.Clamp(frac, 0, 1);
                })),
                _lanCts.Token));
            await Dispatcher.InvokeAsync(() => { }); // espera os últimos aparelhos entrarem na lista
            int services = _lanDevices.Sum(d => d.Services.Count);
            LanStatus.Text = $"Concluído em {sw.Elapsed.TotalSeconds:0} s: {count} aparelhos em {targets.Count} endereços, " +
                             $"{services} serviços abertos.";
            LanProgress.ScaleX = 1;
        }
        catch (OperationCanceledException) { LanStatus.Text = "Varredura cancelada."; LanProgress.ScaleX = 0; }
        catch (Exception ex) { LanStatus.Text = "Falhou: " + ex.Message; LanProgress.ScaleX = 0; }
        finally
        {
            _lanRunning = false;
            _lanCts?.Dispose();
            _lanCts = null;
            BtnLanScan.Content = "VARRER";
            BtnLanExport.IsEnabled = _lanDevices.Count > 0;
            LanSummary.Text = _lanDevices.Count > 0 ? $"APARELHOS ENCONTRADOS · {_lanDevices.Count}" : "APARELHOS ENCONTRADOS";
        }
    }

    /// <summary>Mantém a lista em ordem de IP enquanto os aparelhos chegam.</summary>
    private void InsertLanDevice(LanDevice dev)
    {
        int i = 0;
        while (i < _lanDevices.Count && _lanDevices[i].Sort < dev.Sort) i++;
        _lanDevices.Insert(i, dev);
        LanSummary.Text = $"APARELHOS ENCONTRADOS · {_lanDevices.Count}";
    }

    private void LanService_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not LanService s || !s.CanOpen) return;
        try
        {
            switch (s.Name)
            {
                case "HTTP": Open($"http://{s.Ip}{(s.Port == 80 ? "" : ":" + s.Port)}/"); break;
                case "HTTPS": Open($"https://{s.Ip}{(s.Port == 443 ? "" : ":" + s.Port)}/"); break;
                case "FTP": Open($"ftp://{s.Ip}/"); break;
                case "PASTAS": Process.Start("explorer.exe", $@"\\{s.Ip}"); break;
                case "RDP": Process.Start(new ProcessStartInfo("mstsc.exe", $"/v:{s.Ip}") { UseShellExecute = true }); break;
                case "SSH":
                    string ssh = Path.Combine(Environment.SystemDirectory, @"OpenSSH\ssh.exe");
                    if (!File.Exists(ssh)) { LanStatus.Text = "O cliente SSH do Windows não está instalado (Configurações › Aplicativos › Recursos opcionais › Cliente OpenSSH)."; return; }
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{ssh}\" {s.Ip}\"") { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex) { LanStatus.Text = $"Não foi possível abrir {s.Name}: {ex.Message}"; }

        static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void LanUseForWake_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not LanDevice d || d.Mac == null) return;
        LanWakeMac.Text = d.Mac;
        LanWakeResult.Text = $"MAC de {d.Title} copiado. Quando o aparelho estiver desligado, clique em LIGAR.";
    }

    private async void BtnLanWake_Click(object sender, RoutedEventArgs e)
    {
        try { LanWakeResult.Text = await LanScanner.WakeAsync(LanWakeMac.Text); }
        catch (Exception ex) { LanWakeResult.Text = "Falhou: " + ex.Message; }
    }

    private async void BtnLanExport_Click(object sender, RoutedEventArgs e)
    {
        if (_lanDevices.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar aparelhos da rede",
            Filter = "Planilha CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"MiCORE-rede-{DateTime.Now:yyyyMMdd-HHmm}",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(dlg.FileName, LanScanner.ToCsv(_lanDevices), new UTF8Encoding(true));
            LanStatus.Text = $"Lista salva em {dlg.FileName}";
        }
        catch (Exception ex) { LanStatus.Text = "Não foi possível salvar: " + ex.Message; }
    }
}
