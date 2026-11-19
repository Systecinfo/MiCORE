// MiCORE Setup - instalador pequeno (bootstrapper), atualizador automático e desinstalador.
// Baixa a versão mais recente do MiCORE (MiCORE.zip) das Releases do GitHub, confere o SHA256 publicado e instala.
// Compila com o csc.exe do .NET Framework 4 (já vem no Windows 10/11), sem dependências. Ver build.ps1.
//
// Modos:
//   (sem argumentos)                 janela do instalador; pede administrador uma vez
//   --finish <zip> <tag> [opções]    parte elevada: instala, cria atalhos, entrada em Aplicativos e a tarefa de atualização
//                                    opções: --no-autoupdate  --no-desktop  --launch
//   --update                         atualização silenciosa (roda como SYSTEM pela tarefa agendada "MiCORE Update")
//   --uninstall [--quiet]            remove o MiCORE (chamado por Configurações > Aplicativos)
//   --check <arquivo>                teste sem instalar: busca a release, baixa, confere o SHA256 e grava um relatório
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("MiCORE Setup")]
[assembly: System.Reflection.AssemblyProduct("MiCORE")]
[assembly: System.Reflection.AssemblyDescription("Instalador e atualizador do MiCORE")]
[assembly: System.Reflection.AssemblyCompany("Systecinfo")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 MiCORE - licença MIT")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.0.0")]

namespace MiCoreSetup
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        static bool Has(string[] args, string flag) { return Array.IndexOf(args, flag) >= 0; }

        [STAThread]
        static int Main(string[] args)
        {
            if (Has(args, "--update")) return Updater.RunUpdate();
            if (Has(args, "--uninstall")) { Prepare(); return Uninstaller.Run(args, Has(args, "--quiet")); }
            if (args.Length >= 2 && args[0] == "--check") return Checker.Run(args[1]);
            if (args.Length >= 3 && args[0] == "--finish")
                return Installer.Finish(args[1], args[2], !Has(args, "--no-autoupdate"), !Has(args, "--no-desktop"), Has(args, "--launch"));
            Prepare();
            Application.Run(new SetupForm());
            return 0;
        }

        static void Prepare()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
    }

    class Release
    {
        public string Tag, ZipName, ZipUrl, SumsUrl, SetupName, SetupUrl;
    }

    static class Core
    {
        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), BuildConfig.AppName); } }
        public static string AppExe { get { return Path.Combine(InstallDir, BuildConfig.AppName + ".exe"); } }
        public static string SetupExe { get { return Path.Combine(InstallDir, BuildConfig.AppName + "-Setup.exe"); } }
        public static string TagFile { get { return Path.Combine(InstallDir, "installed-tag.txt"); } }
        public static string LogFile { get { return Path.Combine(InstallDir, "setup.log"); } }
        public static string TaskName { get { return BuildConfig.AppName + " Update"; } }
        public static string UninstallKey { get { return @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + BuildConfig.AppName; } }
        public static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), BuildConfig.AppName + ".lnk"); } }
        public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), BuildConfig.AppName + ".lnk"); } }
        public static string PolicyUrl { get { return "https://github.com/" + BuildConfig.Repo + "/blob/main/CODE_SIGNING.md"; } }

        public static WebClient NewClient()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = BuildConfig.AppName + "-Setup";
            return wc;
        }

        public static Release GetLatest()
        {
            string json;
            using (var wc = NewClient()) json = wc.DownloadString("https://api.github.com/repos/" + BuildConfig.Repo + "/releases/latest");
            var release = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            var r = new Release();
            r.Tag = release.ContainsKey("tag_name") ? (string)release["tag_name"] : "?";
            string prefix = "https://github.com/" + BuildConfig.Repo + "/releases/download/";
            foreach (Dictionary<string, object> asset in (IEnumerable)release["assets"])
            {
                string name = (string)asset["name"];
                string url = (string)asset["browser_download_url"];
                if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue; // só arquivos deste repositório
                string lower = name.ToLowerInvariant();
                if (lower == BuildConfig.ZipName.ToLowerInvariant()) { r.ZipName = name; r.ZipUrl = url; }
                else if (lower == "sha256sums.txt") r.SumsUrl = url;
                else if (lower == (BuildConfig.AppName + "-Setup.exe").ToLowerInvariant()) { r.SetupName = name; r.SetupUrl = url; }
            }
            return r;
        }

        public static string DownloadString(string url) { using (var wc = NewClient()) return wc.DownloadString(url); }

        public static string Download(string url, string name, string dir)
        {
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, name);
            using (var wc = NewClient()) wc.DownloadFile(url, file);
            return file;
        }

        public static string FindHash(string sums, string fileName)
        {
            foreach (string line in sums.Split('\n'))
            {
                string[] parts = line.Trim().Split(new[] { ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].Equals(fileName, StringComparison.OrdinalIgnoreCase)) return parts[0];
            }
            return null;
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        public static bool HashMatches(string file, string name, string sums)
        {
            string expected = FindHash(sums, name);
            return expected != null && expected.Equals(Sha256(file), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInstalled() { return File.Exists(AppExe); }

        public static string InstalledTag()
        {
            try { return File.Exists(TagFile) ? File.ReadAllText(TagFile).Trim() : ""; } catch { return ""; }
        }

        public static bool IsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void Log(string text)
        {
            try
            {
                if (!Directory.Exists(InstallDir)) return;
                var fi = new FileInfo(LogFile);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>Processos do MiCORE abertos a partir da pasta de instalação.</summary>
        public static List<Process> RunningApp()
        {
            var list = new List<Process>();
            foreach (var p in Process.GetProcessesByName(BuildConfig.AppName))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(p.MainModule.FileName), Path.GetFullPath(AppExe), StringComparison.OrdinalIgnoreCase)) { list.Add(p); continue; }
                }
                catch { }
                p.Dispose();
            }
            return list;
        }

        public static void CloseApp()
        {
            foreach (var p in RunningApp())
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(8000)) { p.Kill(); p.WaitForExit(5000); }
                }
                catch { }
                p.Dispose();
            }
        }

        /// <summary>Extrai o zip para a pasta, sobrescrevendo (sem deixar sair da pasta).</summary>
        public static void ExtractZip(string zip, string dir)
        {
            Directory.CreateDirectory(dir);
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var e in archive.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue; // pasta
                    string dest = Path.GetFullPath(Path.Combine(dir, e.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("Caminho inválido no pacote: " + e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    string tmp = dest + ".new";
                    e.ExtractToFile(tmp, true);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                }
        }

        public static int Schtasks(string args)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
        }
    }

    static class Installer
    {
        public static int Finish(string zip, string tag, bool autoUpdate, bool desktop, bool launch)
        {
            try
            {
                Core.CloseApp();
                Core.ExtractZip(zip, Core.InstallDir);
                Core.Log("Instalação: " + tag);

                string self = Application.ExecutablePath;
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Core.SetupExe), StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, Core.SetupExe, true);
                File.WriteAllText(Core.TagFile, tag);

                Shortcut(Core.StartMenuLink);
                if (desktop) Shortcut(Core.DesktopLink);
                else if (File.Exists(Core.DesktopLink)) File.Delete(Core.DesktopLink);
                WriteUninstallEntry(tag);

                int result = 0;
                try
                {
                    if (autoUpdate) { RegisterTask(); Core.Log("Atualização automática ativada."); }
                    else { Core.Schtasks("/Delete /TN \"" + Core.TaskName + "\" /F"); Core.Log("Sem atualização automática."); }
                }
                catch (Exception ex) { Core.Log("Não foi possível criar a tarefa de atualização: " + ex.Message); result = 3; }

                if (launch)
                    try { Process.Start(new ProcessStartInfo(Core.AppExe) { UseShellExecute = true, WorkingDirectory = Core.InstallDir }); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                Core.Log("Erro na instalação: " + ex);
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), BuildConfig.AppName + "-Setup-erro.txt"), ex.ToString()); } catch { }
                return 2;
            }
        }

        static void Shortcut(string path)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                dynamic sh = shell;
                dynamic lnk = sh.CreateShortcut(path);
                lnk.TargetPath = Core.AppExe;
                lnk.WorkingDirectory = Core.InstallDir;
                lnk.IconLocation = Core.AppExe + ",0";
                lnk.Description = "MiCORE - diagnóstico, benchmark, limpeza e otimização do PC";
                lnk.Save();
                Marshal.FinalReleaseComObject(lnk);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        public static void WriteUninstallEntry(string tag)
        {
            using (var k = Registry.LocalMachine.CreateSubKey(Core.UninstallKey))
            {
                string version = tag.TrimStart('v', 'V');
                long kb = 0;
                try { foreach (var f in new DirectoryInfo(Core.InstallDir).GetFiles()) kb += f.Length / 1024; } catch { }
                k.SetValue("DisplayName", BuildConfig.AppName);
                k.SetValue("DisplayVersion", version);
                k.SetValue("Publisher", "Systecinfo");
                k.SetValue("DisplayIcon", Core.AppExe + ",0");
                k.SetValue("InstallLocation", Core.InstallDir);
                k.SetValue("UninstallString", "\"" + Core.SetupExe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + Core.SetupExe + "\" --uninstall --quiet");
                k.SetValue("URLInfoAbout", "https://github.com/" + BuildConfig.Repo);
                k.SetValue("HelpLink", Core.PolicyUrl);
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        // Tarefa agendada: verifica 5 min depois de ligar o PC e todo dia às 03:00, como SYSTEM.
        static void RegisterTask()
        {
            string xml =
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Instala automaticamente novas versões do " + BuildConfig.AppName + " publicadas no GitHub (github.com/" + BuildConfig.Repo + ").</Description></RegistrationInfo>\r\n" +
"  <Triggers>\r\n" +
"    <BootTrigger><Enabled>true</Enabled><Delay>PT5M</Delay></BootTrigger>\r\n" +
"    <CalendarTrigger><StartBoundary>2026-01-01T03:00:00</StartBoundary><Enabled>true</Enabled><RandomDelay>PT30M</RandomDelay><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>\r\n" +
"  </Triggers>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
"    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>\r\n" +
"    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>\r\n" +
"    <Enabled>true</Enabled>\r\n" +
"  </Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>\"" + Core.SetupExe + "\"</Command><Arguments>--update</Arguments></Exec></Actions>\r\n" +
"</Task>\r\n";
            string xmlFile = Path.Combine(Path.GetTempPath(), BuildConfig.AppName + "-task.xml");
            File.WriteAllText(xmlFile, xml, Encoding.Unicode);
            int code = Core.Schtasks("/Create /TN \"" + Core.TaskName + "\" /XML \"" + xmlFile + "\" /F");
            try { File.Delete(xmlFile); } catch { }
            if (code != 0) throw new Exception("schtasks retornou " + code);
        }
    }

    static class Updater
    {
        public static int RunUpdate()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Global\" + BuildConfig.AppName + "-Updater", out created))
            {
                if (!created) return 0;
                try { return DoUpdate(); }
                catch (Exception ex) { Core.Log("Erro na atualização: " + ex.Message); return 1; }
            }
        }

        static int DoUpdate()
        {
            if (!Core.IsInstalled())
            {
                // O MiCORE foi removido sem o desinstalador: apaga a tarefa.
                Core.Schtasks("/Delete /TN \"" + Core.TaskName + "\" /F");
                return 0;
            }
            Release r = Core.GetLatest();
            string current = Core.InstalledTag();
            if (r.Tag == current) return 0;
            if (r.ZipUrl == null || r.SumsUrl == null) { Core.Log("A release " + r.Tag + " está incompleta; ignorando."); return 1; }
            if (Core.RunningApp().Count > 0) { Core.Log("Nova versão " + r.Tag + ", mas o MiCORE está aberto; fica para a próxima verificação."); return 0; }

            Core.Log("Atualizando de " + (current == "" ? "?" : current) + " para " + r.Tag);
            string tmp = Path.Combine(Path.GetTempPath(), BuildConfig.AppName + "-update");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            string sums = Core.DownloadString(r.SumsUrl);
            string zip = Core.Download(r.ZipUrl, r.ZipName, tmp);
            if (!Core.HashMatches(zip, r.ZipName, sums)) { Core.Log("SHA256 não confere; atualização cancelada."); return 1; }
            if (Core.RunningApp().Count > 0) { Core.Log("O MiCORE foi aberto durante o download; fica para a próxima verificação."); return 0; }

            Core.ExtractZip(zip, Core.InstallDir);
            File.WriteAllText(Core.TagFile, r.Tag);
            try { Installer.WriteUninstallEntry(r.Tag); } catch { }
            Core.Log("Atualizado para " + r.Tag);
            UpdateSelf(r, sums, tmp);
            try { Directory.Delete(tmp, true); } catch { }
            return 0;
        }

        // Troca o próprio Setup pela versão publicada junto com a release (se mudou e o hash confere).
        static void UpdateSelf(Release r, string sums, string tmp)
        {
            try
            {
                if (r.SetupUrl == null) return;
                string file = Core.Download(r.SetupUrl, r.SetupName, tmp);
                if (!Core.HashMatches(file, r.SetupName, sums)) { Core.Log("SHA256 do Setup novo não confere; mantendo o atual."); return; }
                if (Core.Sha256(file) == Core.Sha256(Core.SetupExe)) return;
                string old = Core.SetupExe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(Core.SetupExe, old); // um exe em execução pode ser renomeado
                File.Copy(file, Core.SetupExe, true);
                Core.Log("Setup atualizado.");
            }
            catch (Exception ex) { Core.Log("Não foi possível atualizar o Setup: " + ex.Message); }
        }
    }

    static class Uninstaller
    {
        public static int Run(string[] args, bool quiet)
        {
            if (!Core.IsAdmin())
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", args)) { UseShellExecute = true, Verb = "runas" };
                    using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
                }
                catch { return 1; }
            }
            if (!quiet && MessageBox.Show("Remover o " + BuildConfig.AppName + " deste computador?\n\n" +
                    "Suas configurações e históricos em %AppData%\\" + BuildConfig.AppName + " ficam guardados.\n" +
                    "O driver PawnIO, se instalado, sai em Configurações > Aplicativos > PawnIO.",
                    "Desinstalar o " + BuildConfig.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;

            Core.CloseApp();
            Core.Schtasks("/Delete /TN \"" + Core.TaskName + "\" /F");
            foreach (var link in new[] { Core.StartMenuLink, Core.DesktopLink })
                try { if (File.Exists(link)) File.Delete(link); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(Core.UninstallKey, false); } catch { }

            string self = Path.GetFullPath(Application.ExecutablePath);
            bool selfInside = self.StartsWith(Path.GetFullPath(Core.InstallDir) + "\\", StringComparison.OrdinalIgnoreCase);
            try
            {
                foreach (var f in Directory.GetFiles(Core.InstallDir, "*", SearchOption.AllDirectories))
                    if (!string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
                if (!selfInside) Directory.Delete(Core.InstallDir, true);
            }
            catch { }
            // O próprio Setup está rodando de dentro da pasta: um cmd apaga a pasta alguns segundos depois.
            if (selfInside)
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 >nul & rd /s /q \"" + Core.InstallDir + "\"")
                { UseShellExecute = false, CreateNoWindow = true });
            if (!quiet) MessageBox.Show(BuildConfig.AppName + " removido.", "Desinstalar o " + BuildConfig.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }

    static class Checker
    {
        // Teste completo do caminho de download sem instalar nada (usado no build e para diagnóstico).
        public static int Run(string report)
        {
            var sb = new StringBuilder();
            try
            {
                Release r = Core.GetLatest();
                sb.AppendLine("Release: " + r.Tag);
                sb.AppendLine("Zip: " + (r.ZipUrl ?? "(faltando)"));
                sb.AppendLine("SHA256SUMS: " + (r.SumsUrl ?? "(faltando)"));
                sb.AppendLine("Setup: " + (r.SetupUrl ?? "(não publicado)"));
                if (r.ZipUrl == null || r.SumsUrl == null) { File.WriteAllText(report, sb.ToString()); return 1; }
                string tmp = Path.Combine(Path.GetTempPath(), BuildConfig.AppName + "-check");
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                string zip = Core.Download(r.ZipUrl, r.ZipName, tmp);
                bool ok = Core.HashMatches(zip, r.ZipName, Core.DownloadString(r.SumsUrl));
                sb.AppendLine("Baixado: " + new FileInfo(zip).Length / 1048576 + " MB · SHA256 confere: " + ok);
                using (var a = ZipFile.OpenRead(zip))
                    foreach (var e in a.Entries) sb.AppendLine("  no zip: " + e.FullName + " (" + e.Length / 1048576 + " MB)");
                Directory.Delete(tmp, true);
                File.WriteAllText(report, sb.ToString());
                return ok ? 0 : 1;
            }
            catch (Exception ex) { sb.AppendLine("Erro: " + ex); File.WriteAllText(report, sb.ToString()); return 1; }
        }
    }

    class SetupForm : Form
    {
        readonly Label status = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly Button cancel = new Button();
        readonly Button install = new Button();
        readonly Label info = new Label();
        readonly CheckBox desktop = new CheckBox();
        readonly CheckBox autoUpdate = new CheckBox();
        readonly CheckBox launch = new CheckBox();
        readonly LinkLabel policy = new LinkLabel();
        volatile bool cancelled;
        bool optDesktop, optAuto, optLaunch;
        WebClient client;

        public SetupForm()
        {
            string installed = Core.InstalledTag();
            Text = "Instalador do " + BuildConfig.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(500, 370);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label();
            title.Text = BuildConfig.AppName + (installed.Length > 0 ? "  ·  instalado: " + installed : "");
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.SetBounds(16, 10, 470, 30);

            // O que será instalado e o que o programa transmite (exigido pela política de assinatura).
            info.Text =
                "Este instalador baixa a versão mais recente do " + BuildConfig.AppName + " do GitHub\n" +
                "(github.com/" + BuildConfig.Repo + "), confere o SHA256 e instala:\n" +
                "  • o " + BuildConfig.AppName + " em C:\\Program Files\\" + BuildConfig.AppName + " (download de ~80 MB);\n" +
                "  • atalho no Menu Iniciar e entrada em Configurações > Aplicativos.\n\n" +
                "O " + BuildConfig.AppName + " roda como administrador para ler os sensores. Ele não envia\n" +
                "dados de uso: só acessa a internet quando você usa um recurso que precisa\n" +
                "dela (verificar drivers, teste de rede, instalar apps).\n" +
                "Para remover: Configurações > Aplicativos > " + BuildConfig.AppName + ".";
            info.SetBounds(16, 46, 470, 160);

            desktop.Text = "Criar atalho na Área de Trabalho";
            desktop.Checked = true;
            desktop.SetBounds(16, 212, 470, 22);
            autoUpdate.Text = "Atualizar automaticamente (verifica o GitHub uma vez por dia)";
            autoUpdate.Checked = true;
            autoUpdate.SetBounds(16, 236, 470, 22);
            launch.Text = "Abrir o " + BuildConfig.AppName + " ao terminar";
            launch.Checked = true;
            launch.SetBounds(16, 260, 470, 22);

            policy.Text = "Política de privacidade e de assinatura de código";
            policy.SetBounds(16, 290, 470, 20);
            policy.LinkClicked += delegate { try { Process.Start(Core.PolicyUrl); } catch { } };

            status.SetBounds(16, 50, 470, 20);
            bar.SetBounds(16, 76, 468, 18);
            bar.Style = ProgressBarStyle.Marquee;
            status.Visible = bar.Visible = false;

            install.Text = installed.Length > 0 ? "Atualizar" : "Instalar";
            install.SetBounds(312, 328, 82, 28);
            install.Click += delegate { StartInstall(); };

            cancel.Text = "Cancelar";
            cancel.SetBounds(402, 328, 82, 28);
            cancel.Click += delegate { cancelled = true; if (client != null) client.CancelAsync(); Close(); };

            Controls.AddRange(new Control[] { title, info, desktop, autoUpdate, launch, policy, status, bar, install, cancel });
            AcceptButton = install;
        }

        void StartInstall()
        {
            optDesktop = desktop.Checked; optAuto = autoUpdate.Checked; optLaunch = launch.Checked;
            info.Visible = desktop.Visible = autoUpdate.Visible = launch.Visible = policy.Visible = install.Visible = false;
            status.Text = "Procurando a versão mais recente...";
            status.Visible = bar.Visible = true;
            ClientSize = new Size(500, 140);
            cancel.SetBounds(402, 104, 82, 28);
            new Thread(Run) { IsBackground = true }.Start();
        }

        void SetStatus(string text) { BeginInvoke((MethodInvoker)delegate { status.Text = text; }); }

        void Fail(string text)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            });
        }

        void Run()
        {
            string zip = null;
            try
            {
                Release r = Core.GetLatest();
                if (r.ZipUrl == null || r.SumsUrl == null) { Fail("A release " + r.Tag + " não tem o pacote " + BuildConfig.ZipName + " ou o SHA256SUMS.txt."); return; }

                string dir = Path.Combine(Path.GetTempPath(), BuildConfig.AppName + "-Setup");
                Directory.CreateDirectory(dir);
                zip = Path.Combine(dir, r.ZipName);

                SetStatus("Baixando " + BuildConfig.AppName + " " + r.Tag + "...");
                Download(r.ZipUrl, zip);
                if (cancelled) return;

                SetStatus("Verificando a integridade do arquivo...");
                if (!Core.HashMatches(zip, r.ZipName, Core.DownloadString(r.SumsUrl)))
                {
                    File.Delete(zip);
                    Fail("O arquivo baixado não confere com o SHA256 publicado. Instalação cancelada.");
                    return;
                }

                SetStatus("Instalando (aceite a permissão de administrador)...");
                string args = "--finish \"" + zip + "\" \"" + r.Tag + "\"" +
                              (optAuto ? "" : " --no-autoupdate") + (optDesktop ? "" : " --no-desktop") + (optLaunch ? " --launch" : "");
                Process p;
                try { p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = true, Verb = "runas" }); }
                catch (System.ComponentModel.Win32Exception)
                {
                    Fail("A instalação precisa da permissão de administrador. Rode o instalador de novo e aceite o pedido.");
                    return;
                }
                p.WaitForExit();
                if (cancelled) return;

                if (p.ExitCode == 2 || !Core.IsInstalled())
                {
                    Fail("A instalação não foi concluída. Detalhes em %TEMP%\\" + BuildConfig.AppName + "-Setup-erro.txt");
                    return;
                }
                if (p.ExitCode == 3)
                    BeginInvoke((MethodInvoker)delegate
                    {
                        MessageBox.Show(this, BuildConfig.AppName + " instalado, mas não foi possível ativar a atualização automática.\nDetalhes em " + Core.LogFile,
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                BeginInvoke((MethodInvoker)Close);
            }
            catch (Exception ex)
            {
                if (!cancelled) Fail("Não foi possível instalar o " + BuildConfig.AppName + ":\n\n" + ex.Message);
            }
            finally
            {
                try { if (zip != null && File.Exists(zip) && Core.IsInstalled()) File.Delete(zip); } catch { }
            }
        }

        void Download(string url, string file)
        {
            var done = new ManualResetEvent(false);
            Exception error = null;
            client = Core.NewClient();
            client.DownloadProgressChanged += (s, e) => BeginInvoke((MethodInvoker)delegate
            {
                if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                bar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                status.Text = string.Format("Baixando... {0:0.0} de {1:0.0} MB", e.BytesReceived / 1048576.0, e.TotalBytesToReceive / 1048576.0);
            });
            client.DownloadFileCompleted += (s, e) => { error = e.Cancelled ? null : e.Error; done.Set(); };
            client.DownloadFileAsync(new Uri(url), file);
            done.WaitOne();
            client.Dispose();
            client = null;
            if (error != null) throw error;
        }
    }
}
