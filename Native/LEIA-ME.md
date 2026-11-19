# Dependências embutidas no MiCORE.exe

Os arquivos desta pasta entram no executável como recursos (`MiCORE.csproj`) e são extraídos por
`BundledDeps.cs` apenas quando faltam no PC. Assim o app não depende de internet, winget ou instaladores externos.

**Estes arquivos não fazem parte da licença MIT do MiCORE:** pertencem aos respectivos autores e são
redistribuídos sem modificação.

| Arquivo | O que é | Versão | Origem oficial | Licença |
|---|---|---|---|---|
| `PawnIO_setup.exe` | Instalador do driver PawnIO (temperatura e consumo da CPU, usado pelo LibreHardwareMonitor). Roda com `-install -silent`. Assinado por namazso. | 2.2.0 | [github.com/namazso/PawnIO.Setup/releases](https://github.com/namazso/PawnIO.Setup/releases) | GPL-2.0 · código-fonte em [github.com/namazso/PawnIO](https://github.com/namazso/PawnIO) |
| `iCUESDK.x64_2019.dll` | iCUE SDK da Corsair (pasta `redist/x64` do pacote oficial). Assinado por Corsair Memory, Inc. | 4.0.84 | [github.com/CorsairOfficial/cue-sdk/releases](https://github.com/CorsairOfficial/cue-sdk/releases) | Redistribuível da Corsair (pasta `redist` do SDK) |

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
