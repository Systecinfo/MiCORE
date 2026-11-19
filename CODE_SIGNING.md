# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

MiCORE is an open-source (MIT) hardware diagnostics, benchmark, cleanup and tuning tool for Windows.

Two files are signed, both built from the source code in this repository by GitHub Actions
([`.github/workflows/publicar.yml`](.github/workflows/publicar.yml)) and published in [Releases](../../releases).
Every signing request is approved manually.

- **`MiCORE.exe`**: the application, a self-contained single-file .NET 8 program (shipped inside `MiCORE.zip`).
- **`MiCORE-Setup.exe`**: the small online installer, updater and uninstaller written for this project
  (source: [`installer/MiCORESetup.cs`](installer/MiCORESetup.cs), build script: [`installer/build.ps1`](installer/build.ps1)).

What `MiCORE-Setup.exe` does:

1. Downloads `MiCORE.zip` from this repository's latest GitHub Release and verifies its SHA-256 against the published
   `SHA256SUMS.txt`.
2. Shows what will be installed and asks for confirmation (one UAC prompt).
3. Installs MiCORE into `C:\Program Files\MiCORE`, creates Start menu (and optionally desktop) shortcuts and an entry
   in *Settings > Apps* for uninstalling.
4. Optionally (checkbox, enabled by default) registers a scheduled task "MiCORE Update" that checks this repository's
   GitHub Releases at boot and once a day, and installs a new version only when MiCORE is not open and the SHA-256 matches.

Uninstall: *Settings > Apps > MiCORE* removes the program, shortcuts and the scheduled task (user settings in
`%AppData%\MiCORE` are kept). Auto-update can also be disabled with `schtasks /Change /TN "MiCORE Update" /Disable`.

Third-party components inside `MiCORE.exe` are all open source and included unmodified:

| Component | License |
|---|---|
| .NET runtime and WPF (Microsoft) | MIT |
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (sensors) | MPL-2.0 |
| [TraceEvent](https://github.com/microsoft/perfview), [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows), System.Management | MIT |
| [PawnIO](https://github.com/namazso/PawnIO) installer `PawnIO_setup.exe` (CPU temperature driver, signed by its author) | GPL-2.0 |
| [Winapp2](https://github.com/MoscaDotTo/Winapp2) cleanup rules (data file) | CC BY-SA 4.0 |
| IEEE OUI registry (MAC vendor names, public data) | public registry data |

No proprietary component is included. Vendor software that is proprietary (Corsair iCUE SDK, USB-serial drivers
from WCH, Silicon Labs and FTDI, GPU drivers) is never bundled: MiCORE only opens the vendor's official page or
downloads it from the vendor's official source after the user asks for it.

## What the program does to the system

MiCORE runs as administrator because reading CPU sensors and changing Windows settings require it. It changes the
system only when the user asks, always with a confirmation dialog, and most changes can be undone in the app:

- **CPU sensor driver (PawnIO):** asks once on first start before installing; can be removed in *Settings > Apps > PawnIO*.
- **Optimization:** Windows gaming tweaks are saved before the first change and can be reverted with one button.
- **Cleanup:** shows what will be deleted (ANALYZE) before deleting temporary files, caches and browser traces;
  sensitive items (passwords, cookies, history) start unchecked. System folders are never deleted.
- **Startup programs:** disable/remove with a backup and "restore removed".
- **Drivers and apps:** installs only official packages (NVIDIA/AMD/Windows Update; apps through Microsoft winget) after confirmation.
- **GPU tuning (NVIDIA, NVML):** power limit and clock lock, with a restore button; reset on reboot.
- **Network devices page:** lists devices on the user's own local network (ping, ARP, DNS/NetBIOS names) and checks
  a few common ports (web, FTP, SSH, file sharing, remote desktop, VNC, printer) so the user can open those services.
  It does not test for vulnerabilities, does not try to log in and only runs when the user clicks SCAN.

Uninstall: if installed with `MiCORE-Setup.exe`, use *Settings > Apps > MiCORE*. The portable version (`MiCORE.zip`)
needs no uninstall: delete `MiCORE.exe` and the folder `%AppData%\MiCORE`. Optional: remove PawnIO in *Settings > Apps*.

## Team roles

- Committers and reviewers: [Systecinfo](https://github.com/Systecinfo)
- Approvers: [Systecinfo](https://github.com/Systecinfo)

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it. There is no telemetry and no account. Settings are stored only on this
computer (`%AppData%\MiCORE`). Network access happens only when the user starts one of these features:

- **Driver check** (MiCORE DRIVER): sends the graphics card model to NVIDIA's or AMD's official driver lookup service.
- **Network test:** downloads and uploads test data to `speed.cloudflare.com` and pings 1.1.1.1 and 8.8.8.8.
- **FPS estimate with HowManyFPS:** only if the user enters their own API key; sends the game and hardware query to `howmanyfps.com`.
- **Install apps / Arduino IDE:** uses Microsoft winget; if winget is missing it is downloaded from Microsoft's official GitHub releases.
- **Corsair iCUE SDK:** downloaded from Corsair's official GitHub releases only when the user clicks the download button.
- **Network devices:** talks only to devices on the user's local network.
- **MiCORE-Setup / auto-update:** contacts `api.github.com` and `github.com` only to check for and download releases of
  this repository. Auto-update can be turned off during installation or later as described above.

Policies of those services apply to the data they receive ([GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
NVIDIA, AMD, Cloudflare, HowManyFPS).

---

# Política de assinatura de código (português)

Assinatura de código gratuita fornecida pelo [SignPath.io](https://about.signpath.io), certificado da
[SignPath Foundation](https://signpath.org).

- **Arquivos assinados:** o `MiCORE.exe` (o programa) e o `MiCORE-Setup.exe` (instalador, atualizador e desinstalador,
  código em `installer/`), compilados pelo GitHub Actions a partir deste repositório. Cada assinatura é aprovada manualmente.
- **Instalador:** baixa o `MiCORE.zip` da release mais recente, confere o SHA256, instala em Program Files com atalhos e
  entrada em Aplicativos e, se marcado, atualiza sozinho uma vez por dia (tarefa "MiCORE Update", desligável). Os componentes de terceiros dentro dele são todos de código aberto (tabela acima); nada proprietário
  vai no executável.
- **Mudanças no sistema:** só quando o usuário pede, com confirmação; a maioria pode ser desfeita no próprio app. O driver
  PawnIO pergunta antes de instalar, na primeira abertura.
- **Equipe:** desenvolvimento, revisão e aprovação: [Systecinfo](https://github.com/Systecinfo), com autenticação de dois
  fatores no GitHub e no SignPath.
- **Privacidade:** sem telemetria e sem conta. O MiCORE só acessa a internet quando o usuário usa um recurso que precisa dela
  (verificar drivers, teste de rede, instalar apps, SDK da Corsair, API do HowManyFPS com a chave do próprio usuário).
- **Desinstalar:** instalado pelo Setup, em Configurações › Aplicativos › MiCORE. Versão portátil: apague o `MiCORE.exe` e a
  pasta `%AppData%\MiCORE`. O PawnIO, se instalado, sai em Configurações › Aplicativos.
