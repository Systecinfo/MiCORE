using System.Windows;

namespace MiCORE;

/// <summary>Página Limpeza: temporários, Prefetch, Lixeira, caches do Windows e de shaders, e regras do Winapp2 (Cleaner).</summary>
public partial class MainWindow
{
    private List<CleanGroup>? _cleanGroups;
    private CancellationTokenSource? _cleanCts;
    private bool _cleanBusy, _cleanLoading;

    private async void InitClean()
    {
        if (_cleanGroups != null || _cleanLoading) return;
        _cleanLoading = true;
        CleanStatus.Text = "Procurando programas e navegadores instalados…";
        try
        {
            _cleanGroups = await Task.Run(Cleaner.BuildCatalog);
            CleanGroupsList.ItemsSource = _cleanGroups;
            int n = _cleanGroups.Sum(g => g.Items.Count);
            CleanStatus.Text = $"{n} itens para este PC. Os recomendados já vêm marcados: clique em ANALISAR para ver quanto cada um libera.";
        }
        catch (Exception ex) { CleanStatus.Text = "Não foi possível montar a lista: " + ex.Message; }
        finally { _cleanLoading = false; }
    }

    private IEnumerable<CleanItem> CleanSelected() => _cleanGroups?.SelectMany(g => g.Items).Where(i => i.Selected) ?? Enumerable.Empty<CleanItem>();

    private void BtnCleanRecommended_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanGroups == null || _cleanBusy) return;
        foreach (var i in _cleanGroups.SelectMany(g => g.Items)) i.Selected = i.Recommended;
    }

    private void BtnCleanNone_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanGroups == null || _cleanBusy) return;
        foreach (var i in _cleanGroups.SelectMany(g => g.Items)) i.Selected = false;
    }

    private async void BtnCleanScan_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanBusy) { _cleanCts?.Cancel(); return; }
        if (_cleanGroups == null) return;
        var items = CleanSelected().ToList();
        if (items.Count == 0) { CleanStatus.Text = "Marque pelo menos um item."; return; }

        await RunClean(items, delete: false);
    }

    private async void BtnCleanRun_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanBusy || _cleanGroups == null) return;
        var items = CleanSelected().ToList();
        if (items.Count == 0) { CleanStatus.Text = "Marque pelo menos um item."; return; }

        var running = Cleaner.RunningProcesses(items);
        if (running.Count > 0)
        {
            var r = MessageBox.Show(this,
                "Feche estes programas para limpar tudo (arquivos abertos por eles são pulados):\n\n" +
                string.Join("\n", running.Select(p => "  • " + p)) + "\n\nLimpar mesmo assim?",
                "Limpeza", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        var sensitive = items.Where(i => i.Warning != null || !i.Recommended).Select(i => i.Name).ToList();
        string msg = $"Apagar {items.Count} itens marcados? Não dá para desfazer." +
                     (sensitive.Count > 0 ? "\n\nInclui itens fora dos recomendados:\n" + string.Join("\n", sensitive.Take(12).Select(s => "  • " + s)) +
                                            (sensitive.Count > 12 ? $"\n  … e mais {sensitive.Count - 12}" : "") : "");
        if (MessageBox.Show(this, msg, "Limpeza", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await RunClean(items, delete: true);
    }

    private async Task RunClean(List<CleanItem> items, bool delete)
    {
        _cleanBusy = true;
        _cleanCts = new CancellationTokenSource();
        var ct = _cleanCts.Token;
        BtnCleanScan.Content = "CANCELAR";
        BtnCleanRun.IsEnabled = false;
        CleanProgress.ScaleX = 0;
        foreach (var i in items) i.Status = delete ? "na fila…" : "analisando…";

        long bytes = 0; int files = 0, failed = 0, regs = 0, done = 0;
        try
        {
            await Task.Run(() => Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, item =>
            {
                if (delete) item.Status = "limpando…";
                var t = delete ? Cleaner.Clean(item, ct) : Cleaner.Analyze(item, ct);
                item.Scanned = true;
                item.FileCount = t.Files; item.RegCount = t.Regs; item.Bytes = t.Bytes;
                item.Status = delete
                    ? (t.Bytes > 0 || t.Regs > 0 ? $"liberado {Cleaner.FormatBytes(t.Bytes)}" : "nada para apagar") + (t.Failed > 0 ? $" · {t.Failed} em uso" : "")
                    : "";
                Interlocked.Add(ref bytes, t.Bytes);
                Interlocked.Add(ref files, t.Files);
                Interlocked.Add(ref failed, t.Failed);
                Interlocked.Add(ref regs, t.Regs);
                int d = Interlocked.Increment(ref done);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CleanProgress.ScaleX = (double)d / items.Count;
                    CleanStatus.Text = $"{(delete ? "Limpando" : "Analisando")}… {d} de {items.Count} · {Cleaner.FormatBytes(Interlocked.Read(ref bytes))}";
                }));
            }), ct);

            CleanProgress.ScaleX = 1;
            CleanStatus.Text = delete
                ? $"Pronto: {Cleaner.FormatBytes(bytes)} liberados ({files:N0} arquivos" + (regs > 0 ? $", {regs} entradas do registro" : "") + ")." +
                  (failed > 0 ? $" {failed:N0} arquivos estavam em uso e ficaram." : "")
                : $"Dá para liberar {Cleaner.FormatBytes(bytes)} ({files:N0} arquivos" + (regs > 0 ? $", {regs} entradas do registro" : "") +
                  "). Confira a lista e clique em LIMPAR.";
        }
        catch (OperationCanceledException)
        {
            CleanStatus.Text = delete ? $"Cancelado. {Cleaner.FormatBytes(bytes)} já foram liberados." : "Análise cancelada.";
            CleanProgress.ScaleX = 0;
        }
        catch (Exception ex) { CleanStatus.Text = "Falhou: " + ex.Message; CleanProgress.ScaleX = 0; }
        finally
        {
            foreach (var i in items) if (i.Status is "na fila…" or "analisando…" or "limpando…") i.Status = "";
            UpdateCleanTotals();
            _cleanBusy = false;
            _cleanCts?.Dispose();
            _cleanCts = null;
            BtnCleanScan.Content = "ANALISAR";
            BtnCleanRun.IsEnabled = true;
        }
    }

    private void UpdateCleanTotals()
    {
        if (_cleanGroups == null) return;
        foreach (var g in _cleanGroups)
        {
            var scanned = g.Items.Where(i => i.Scanned).ToList();
            g.Total = scanned.Count == 0 ? "" : Cleaner.FormatBytes(scanned.Sum(i => i.Bytes));
        }
    }
}
