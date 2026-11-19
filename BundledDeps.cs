using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MiCORE;

/// <summary>
/// Dependências que vêm dentro do MiCORE.exe (pasta Native\ do projeto): o instalador do driver PawnIO
/// (temperatura/consumo da CPU, GPL-2.0). Componentes proprietários (iCUE SDK, drivers seriais) não vão no .exe:
/// são baixados do site oficial do fabricante quando o usuário pede.
/// </summary>
public static class BundledDeps
{
    private const string PawnIOResource = "Native/PawnIO_setup.exe";

    /// <summary>Copia um recurso embutido para um arquivo. Devolve false se o recurso não existir.</summary>
    private static bool Extract(string resource, string destination)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (s == null) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string tmp = destination + ".tmp";
        using (var f = File.Create(tmp)) s.CopyTo(f);
        File.Move(tmp, destination, overwrite: true);
        return true;
    }

    /// <summary>
    /// Instala o PawnIO em modo silencioso se ele ainda não estiver no PC. Precisa de administrador (o MiCORE já
    /// abre assim). Devolve null se o driver está disponível ao final, ou a explicação do que falhou.
    /// </summary>
    public static async Task<string?> EnsurePawnIOAsync()
    {
        if (SensorMonitor.IsPawnIOInstalled) return null;
        if (!SensorMonitor.IsElevated) return "o MiCORE precisa estar aberto como administrador para instalar o driver de sensores";

        string dir = Path.Combine(Path.GetTempPath(), "MiCORE-PawnIO-" + Guid.NewGuid().ToString("N"));
        string setup = Path.Combine(dir, "PawnIO_setup.exe");
        try
        {
            if (!Extract(PawnIOResource, setup)) return "o instalador do PawnIO não veio junto deste MiCORE.exe";
            var psi = new ProcessStartInfo(setup, "-install -silent") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
            using var p = Process.Start(psi);
            if (p == null) return "não foi possível iniciar o instalador do PawnIO";
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(); } catch { } return "o instalador do PawnIO não terminou a tempo"; }
            return SensorMonitor.IsPawnIOInstalled ? null : $"o instalador do PawnIO terminou com o código {p.ExitCode}";
        }
        catch (Exception ex) { return "falha ao instalar o PawnIO: " + ex.Message; }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
