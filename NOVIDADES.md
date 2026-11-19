## Novidades da v0.17.1

**Instalador pequeno · MiCORE-Setup.exe (~150 KB)**
- Baixa a versão mais recente do GitHub (MiCORE.zip, ~80 MB), confere o SHA256 publicado e instala em `C:\Program Files\MiCORE`.
- Cria atalho no Menu Iniciar (e na Área de Trabalho, se marcado) e a entrada em Configurações › Aplicativos para desinstalar.
- Atualização automática opcional: verifica o GitHub ao ligar o PC e uma vez por dia, e só atualiza com o MiCORE fechado.
- A versão portátil continua no **MiCORE.zip** (não precisa instalar).

---

## Novidades da v0.17 (primeira versão MiCORE)

**Nova marca · MiCORE**
- Nome, logo (planeta com anel e um processador no centro) e ícone novos em todo o app.

**Nova página · MiCORE FERRAMENTAS › Limpeza**
- **ANALISAR** mostra quanto cada item libera antes de apagar; **LIMPAR** pede confirmação e mostra o resultado de cada item.
- Windows: temporários do usuário (%TEMP%) e do Windows (Temp), Prefetch, Lixeira, cache do Windows Update, Otimização
  de Entrega, relatórios de erro, despejos de memória, miniaturas e logs do CBS/DISM.
- Jogos: cache de shaders do DirectX, NVIDIA, AMD e Intel.
- Navegadores e programas instalados pelas regras abertas do Winapp2 (Brave, Chrome, Edge, Firefox, Discord, Steam, Spotify...).
- Senhas, cookies, sessões e histórico começam desmarcados; pastas do sistema nunca são apagadas inteiras.

**Nova página · MiCORE SISTEMA › Dispositivos na rede**
- Encontra os aparelhos da sua rede (ping + tabela ARP, inclusive os que bloqueiam ping), com nome, MAC e fabricante.
- Mostra serviços abertos (painel web, pastas, área de trabalho remota, SSH) com botão para abrir; Wake-on-LAN e exportação CSV.

**Menos passos manuais**
- O driver de sensores da CPU (PawnIO) vem dentro do app: na primeira abertura ele pergunta se pode instalar.
- O winget é instalado sozinho quando falta (Windows LTSC ou sem Microsoft Store), em Instalar apps e na Arduino IDE.

**Só código aberto dentro do .exe**
- O SDK da Corsair e os drivers seriais CH340/CP210x/FTDI (proprietários) não vêm mais no executável: o app abre a página
  oficial ou baixa do fabricante quando você pede.

---

## Novidades da v0.16 (versão anterior)

**Nova página · MiCORE FERRAMENTAS › Inicialização**
- Lista os programas que abrem junto com o Windows: chaves Run do registro (usuário, todos os usuários, 32 e 64 bits)
  e pastas Inicializar, com descrição, fabricante e aviso quando o arquivo do programa não existe mais.
- Interruptor para ativar/desativar, igual ao Gerenciador de Tarefas: a entrada continua lá e pode ser religada.
- Botão **REMOVER** com confirmação; o programa continua instalado e tudo pode ser desfeito em **Restaurar removidos**.

**Nova página · MiCORE FERRAMENTAS › Instalar apps**
- Os essenciais depois de formatar o PC: Steam, Google Chrome, Discord, Lightshot, 7-Zip, WinRAR e VLC.
- Instala pelo winget (instalador de pacotes da Microsoft que já vem no Windows): versão oficial mais recente,
  sem janelas, um app de cada vez, com o resultado de cada um na lista.
- Mostra o que já está instalado; se o winget ainda não existir (PC recém-formatado), um botão abre a Microsoft Store
  para atualizar o "Instalador de Aplicativo".

**Nova categoria · MiCORE MAKER** (Arduino, ESP32 e conversores USB-serial)
- **Portas COM**: a limpeza de dispositivos fantasmas e de números COM presos saiu de Otimização e ganhou página própria.
- **Drivers seriais**: diagnóstico das placas com CH340/CH341, CP2102/CP210x, FTDI, Prolific PL2303 e Arduino
  (sem driver, código 10, código 43, FTDI falsificado travado em PID 0000), com a orientação de cada caso.
- **REINSTALAR** remove o dispositivo e faz o Windows detectar de novo com o melhor driver.
- Lista as versões de driver guardadas no Windows para cada chip, com **REMOVER** (backup antes): volta para uma versão
  anterior quando a nova não funciona com clones, ou desinstala de vez. **Restaurar removidos** desfaz.
- Abre a página oficial do driver de cada fabricante.
- **Arduino IDE**: instalar/atualizar pelo winget, fechar programas que prendem a porta COM e limpar o cache de índices
  quando a IDE trava em "Downloading index".

**Novo benchmark · Ray tracing (GPU)**
- Teste em Direct3D 12 com DXR 1.1 que usa os núcleos de ray tracing (RTX 20+, Radeon RX 6000+, Intel Arc): 4 mil objetos,
  reflexos de reflexos, sombras suaves, oclusão de ambiente e iluminação indireta, com a cena refeita a cada frame.
- Feito para comparar com confiança: aquecimento sem medir, 3 passadas iguais, nota pela mediana e variação entre passadas
  (selo de "resultado confiável" abaixo de 3%); tempo de frame medido pelo relógio da própria GPU.
- Presets Normal, Alta e Extrema em 1080p, 1440p e 4K; histórico comparando só resultados do mesmo preset e resolução.
- Referência: RX 9070, Alta 1440p = 878 pts (87,8 FPS), variação 0,5%.

**Overlay personalizável**
- Na aba Overlay, escolha o que aparece: FPS, frametime, 1% low, gráfico; GPU (uso, temperatura, hotspot, clock do núcleo,
  clock da memória, consumo, tensão, ventoinha, limitador NVIDIA); VRAM (usada, %, temperatura); CPU (uso, núcleo mais
  ocupado, temperatura, clock médio, clock máximo, consumo); RAM (GB, %); nome do jogo e hora. Atalhos SÓ FPS, PADRÃO e TUDO.
- Cada componente vira uma linha só com os itens escolhidos; a escolha fica salva e vale na hora.

**MiCORE JOGOS**
- Novo atalho em Extras: **-allow_third_party_software** (CS2), que libera captura de jogo do OBS, ReShade e overlays
  bloqueados pelo modo confiável; o aviso sobre o Fator de Confiança aparece ao passar o mouse.

**Abertura mais rápida**
- A janela aparece em ~0,5 s (antes ~4,6 s). Informações do sistema, sensores e placa de vídeo carregam em paralelo,
  em segundo plano, e cada página é preenchida quando a sua parte fica pronta.
- O aviso "sem sensor da CPU" não aparece mais por engano enquanto o hardware ainda está sendo detectado.

**Correção · Overlay de jogos**
- O overlay agora fica por cima de jogos em janela sem borda que também se marcam como "sempre no topo"
  (ex.: GTA V e Cyberpunk 2077), inclusive depois de Alt+Tab.
- Aparece no monitor onde o jogo está, não só no monitor principal.
