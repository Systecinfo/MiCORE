# Dependências embutidas no MiCORE.exe

Os arquivos desta pasta entram no executável como recursos (`MiCORE.csproj`) e são usados só quando
faltam no PC. **Nada proprietário vai aqui:** componentes de fabricantes (iCUE SDK da Corsair, drivers seriais
CH340/CP210x/FTDI) são baixados do site oficial quando o usuário pede, para o MiCORE seguir só com código aberto.

**Estes arquivos não fazem parte da licença MIT do MiCORE:** pertencem aos respectivos autores e são
redistribuídos sem modificação.

| Arquivo | O que é | Versão | Origem oficial | Licença |
|---|---|---|---|---|
| `PawnIO_setup.exe` | Instalador do driver PawnIO (temperatura e consumo da CPU, usado pelo LibreHardwareMonitor). Roda com `-install -silent` depois que o usuário confirma. Assinado por namazso. | 2.2.0 | [github.com/namazso/PawnIO.Setup/releases](https://github.com/namazso/PawnIO.Setup/releases) | GPL-2.0 · código-fonte em [github.com/namazso/PawnIO](https://github.com/namazso/PawnIO) |

## Atualizar

1. Baixe a versão nova da origem oficial e confira a assinatura digital (Propriedades › Assinaturas Digitais).
2. Substitua o arquivo nesta pasta, mantendo o mesmo nome.
3. Atualize a versão na tabela acima.

## Dados

| Arquivo | O que é | Origem oficial |
|---|---|---|
| `oui.txt.gz` | Tabela de fabricantes por MAC (prefixo OUI de 6 dígitos → nome da empresa), usada em Dispositivos na rede. Gerada do `oui.csv` público, só com prefixo e nome, compactada em gzip. | Registro MA-L do IEEE: [standards-oui.ieee.org/oui/oui.csv](https://standards-oui.ieee.org/oui/oui.csv) |

Para atualizar a tabela: baixe o `oui.csv`, gere um arquivo de texto UTF-8 com uma linha `PREFIXO<TAB>Fabricante` por
registro (prefixo em hexadecimal maiúsculo, sem separadores) e compacte com gzip como `oui.txt.gz`.
| `Winapp2.ini.gz` | Regras de limpeza de programas e navegadores (formato Winapp2), usadas na página Limpeza. Arquivo original, só compactado com gzip. Versão 260915, 4.068 entradas. | [github.com/MoscaDotTo/Winapp2](https://github.com/MoscaDotTo/Winapp2) |

O **Winapp2.ini** é © projeto Winapp2 e é licenciado sob **CC BY-SA 4.0**
([texto da licença](https://github.com/MoscaDotTo/Winapp2/blob/master/License.md)). Ele é redistribuído aqui sem
modificações; se você alterar o arquivo, a versão alterada também precisa ser distribuída sob CC BY-SA 4.0.
Essa licença vale só para o arquivo de regras, não para o código do MiCORE (MIT).
