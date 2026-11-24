# NekoPcbEmulator

[![CI](https://github.com/Sakamoto0110/NekoPcbEmulator/actions/workflows/ci.yml/badge.svg)](https://github.com/Sakamoto0110/NekoPcbEmulator/actions/workflows/ci.yml)

Download the Windows x64 packages from [GitHub Releases](https://github.com/Sakamoto0110/NekoPcbEmulator/releases/latest).
The self-contained package includes the runtime; the framework-dependent package requires
the .NET 10 Desktop Runtime. Both contain the emulator, reference client, and protocol documents.

Emulador visual de PCBs com porta RX/TX emulada. Cada placa "ligada" abre uma porta e uma
janela mostrando a placa e seus periféricos.

É um app standalone, sem dependência de nenhuma testing suite: qualquer coisa que precise de
PCBs virtuais pode conectar na porta emulada — uma testing suite para um módulo RX/TX, outro app
de controle, um script. O `TestClient` incluído é só uma referência de como falar cada protocolo.

- **PCB-A** — protocolo raw ASCII. 6 LEDs RGBA em duas fileiras, com auto-desligamento opcional,
  LCD de caracteres 20x4 com 5 slots de memória, e um painel de LED RGBA de 360x120 com
  POINT / LINE / RECT.
- **PCB-B** — protocolo binário com framing. Grid 5x5 endereçável na ponta de um cabo flat,
  com cor e tempo de aceso por LED.

Solução .NET 10 / VS 2026 (`NekoPcbEmulator.slnx`).

---

## Rodando

```bash
dotnet run --project src/NekoPcbEmulator.App
```

O launcher abre com um card por placa. **POWER ON** abre a porta e a janela da placa; fechar a
janela desliga a placa e libera a porta.

Pra subir tudo já ligado (útil em CI ou script de teste):

```bash
dotnet run --project src/NekoPcbEmulator.App -- --power all
```

| Flag | Efeito |
|------|--------|
| `--power a` \| `b` \| `all` | liga as placas no startup |
| `--port-a <n>` / `--port-b <n>` | portas TCP (default 5001 / 5002) |
| `--pipe` | usa named pipe em vez de TCP |
| `--serial` / `--com0com` | use serial ports; validate each port when its board powers on |
| `--com-a <name>` / `--com-b <name>` | emulator-side serial port names (defaults COM5 / COM4) |

Dentro de uma janela de placa, **F5** reseta aquela placa.

## Enviando comandos pela própria janela

Passar o mouse sobre um periférico abre o menu dos comandos que ele aceita; escolher um abre
um diálogo com as propriedades, um preview do que vai ser enviado, e os botões **Send to
board** / **Cancel**.

| Periférico | Comandos |
|---|---|
| PCB-A · cada `LIGHT[i]` | `LIGHT` (cor + estado + auto-off opcional) |
| PCB-A · LCD | `TEXT`, `SHOW`, `CLR`, `SAVE`, `LOAD` |
| PCB-A · controlador U1 | `SYS PING`, `SYS ID`, `SYS STAT`, `SYS RESET` |
| PCB-B · cada LED da grid | `LED_SET` (cor + tempo), `LED_CLEAR` |
| PCB-B · controlador U1 | `SET_ALL`, `SET_MASK`, `CLEAR_ALL`, `PING`, `GET_STATE`, `GET_INFO` |

O painel de pixels não tem menu de propósito: comandos de desenho não cabem bem num
formulário e rendem muito mais scriptados.

O comando **não** chama a API do dispositivo direto — ele é montado como mensagem de
protocolo e injetado por uma conexão de loopback, então passa pelo mesmo parser, validação e
log que uma mensagem vinda do socket. Um índice inválido digitado no diálogo vira um `ERR` ou
`NAK` no log de tráfego, exatamente como viria da rede. O preview mostra os bytes reais
porque é gerado pelo mesmo encoder que produz o que sai na porta.

## Cliente de teste

Conecta na placa, roda uma sequência scriptada ou abre um prompt interativo. Serve tanto de
smoke test quanto de referência de como falar cada protocolo.

```bash
dotnet run --project src/NekoPcbEmulator.TestClient -- a --demo
dotnet run --project src/NekoPcbEmulator.TestClient -- b --demo
dotnet run --project src/NekoPcbEmulator.TestClient -- a           # prompt ASCII
dotnet run --project src/NekoPcbEmulator.TestClient -- b           # prompt binário
dotnet run --project src/NekoPcbEmulator.TestClient -- a --pipe    # via named pipe
```

Os dois demos exercitam de propósito os caminhos de erro (índice fora do range, comando
desconhecido, tamanho errado, CRC corrompido) — é o que você quer ver o RX da sua framework
lidando.

---

## Protocol verification

The regression runner covers both protocol documents using an independent binary encoder
and CRC implementation. It checks commands, response framing, errors, fragmented and
coalesced messages, limits, recovery, and peripheral state.

```bash
# Start isolated board hosts over loopback TCP.
dotnet run --project tests/NekoPcbEmulator.ProtocolTests -c Release

# Start both board hosts over existing COM5/COM6 and COM4/COM3 pairs.
dotnet run --project tests/NekoPcbEmulator.ProtocolTests -c Release -- --serial

# Test a running emulator with PCB-A powered on COM5 and PCB-B powered on COM4.
dotnet run --project tests/NekoPcbEmulator.ProtocolTests -c Release -- --external
```

Serial endpoints can be overridden with `--com-a`, `--com-b`, `--client-a`, and `--client-b`.
Results are saved under `artifacts/protocol-tests/`, or to a custom `--report <path>`.
External mode checks wire-visible behavior; LCD and framebuffer snapshot checks run only
against isolated hosts. Test runs reset both boards and exercise deliberate protocol errors.

For a running emulator using TCP, pass `--external-tcp` with optional `--port-a` and `--port-b`.

## CI and releases

The Windows CI workflow runs on pushes to `master`, pull requests, and manual dispatch.
It builds with warnings treated as errors, runs the isolated protocol suite, publishes both
Windows x64 package flavors, and tests both packaged applications and reference clients.
Protocol reports and validated packages are retained as workflow artifacts.

Release tags such as `v1.1.0` must match `Version` in `Directory.Build.props`. Pushing a release
tag reruns the same CI workflow and publishes its validated ZIP packages and SHA-256
checksums to GitHub Releases. The release workflow cannot publish if validation fails.

The packaging steps can also be run locally from PowerShell:

```powershell
dotnet build NekoPcbEmulator.slnx -c Release -warnaserror
./.github/scripts/Publish-Windows.ps1
./.github/scripts/Test-Published.ps1
```

## Publicando single file

**Self-contained** — um `.exe` e nada mais, roda em máquina sem runtime instalado:

```bash
dotnet publish src/NekoPcbEmulator.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/self-contained
```

**Framework-dependent** — exige o .NET 10 Desktop Runtime, mas fica ~80× menor:

```bash
dotnet publish src/NekoPcbEmulator.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish/framework-dependent
```

Trocando `NekoPcbEmulator.App` por `NekoPcbEmulator.TestClient` publica o cliente do mesmo jeito.

| | App | TestClient |
|---|---|---|
| self-contained | 49,5 MB | 35,9 MB |
| framework-dependent | 0,6 MB | 0,3 MB |

Quatro detalhes que não são óbvios:

- `IncludeNativeLibrariesForSelfExtract` é obrigatório no self-contained. Sem ele as DLLs
  nativas ficam soltas ao lado do executável e não é single file de verdade.
- `EnableCompressionInSingleFile` só vale com self-contained — no framework-dependent o SDK
  falha com `NETSDK1176`.
- `PublishTrimmed` fica **desligado** de propósito: WinForms depende muito de reflection e o
  trimming quebra o app em runtime, não em build.
- O ícone é embutido como recurso e lido do manifest, não de arquivo. É isso que faz a barra
  de título continuar certa dentro do bundle, onde não existe `.ico` ao lado do executável.

---

## Emulated ports

Each board has its own transport selector in the launcher. Powering on opens the selected
endpoint; powering off releases it.

| Transport | Endpoint | Usage |
|-----------|----------|-------|
| **TCP** (default) | `tcp://127.0.0.1:5001` (A), `:5002` (B) | loopback socket for external clients |
| **Named pipe** | `\\.\pipe\pcb-a`, `\\.\pipe\pcb-b` | local byte stream |
| **Serial (COM)** | e.g. `serial://COM5@115200` | physical serial ports or existing virtual pairs |

All transports carry byte streams; the board protocol supplies framing. TCP and named pipes
support multiple clients with independent reassembly buffers. Serial has one implicit peer.

### Serial ports

Choose **Serial (COM)** and select a port from **COM PORT**. The dropdown shows only detected
emulator-side ports configured by `--com-a` and `--com-b`: **COM5 and COM4** by default.
Client-side COM6/COM3, unrelated COM ports, and internal driver names are omitted.
The list refreshes when serial is selected and whenever the port dropdown opens, without
opening any ports. Serial selection does not require a com0com installation. Availability
is checked when the board powers on; a missing or busy port leaves that board off and
reports the error, without switching to TCP.

Serial settings are **115200 baud, 8 data bits, no parity, 1 stop bit, no flow control**.
Each virtual pair connects one board to one external client. For example, with existing
pairs **COM5 ↔ COM6** and **COM4 ↔ COM3**:

| Board | Emulator port | External client port |
|-------|---------------|----------------------|
| PCB-A | COM5 | COM6 |
| PCB-B | COM4 | COM3 |

```bash
dotnet run --project src/NekoPcbEmulator.App -- --serial --com-a COM5 --com-b COM4 --power all
```

Use matching serial settings in the external client. The included `TestClient` currently
supports TCP and named pipes only. The emulator uses existing ports; it does not install
drivers or create virtual pairs.

### com0com setup, Secure Boot, and kernel anti-cheats

[com0com](https://sourceforge.net/projects/com0com/) is an optional third-party kernel driver
that creates virtual null-modem pairs. Install it separately, create two pairs, and assign
one endpoint in each pair to the emulator and the other to your client, as shown above.
Installing the emulator does not install com0com or change Windows security settings.

**Security warning:** legacy or test-signed com0com builds may require Windows Test Mode.
When that configuration is required, **Secure Boot must be disabled** for the test-signing
setup to work. Disabling Secure Boot and relaxing driver-signing requirements is dangerous:
it weakens boot-chain and kernel-driver protections and increases exposure to malicious
code. Treat this as a deliberate testing-machine tradeoff, not a routine prerequisite for
running the emulator. Microsoft documents the Secure Boot restriction and the risks of
changing boot configuration in its [test-signed driver guidance](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option).

**Kernel anti-cheat warning:** some kernel-level anti-cheats can block com0com or prevent
protected games from starting when they reject an installed driver or the boot/signing
configuration it requires. **Riot Vanguard is a confirmed incompatibility in the
maintainer-reported com0com setup.** This local compatibility report depends on the driver,
Windows version, and anti-cheat policy. Riot separately documents that Vanguard can refuse
to launch protected games until required security settings, including Secure Boot, are
enabled; see [Vanguard security restrictions](https://support.riotgames.com/en-us/riot/penalties/error-van-restriction).

Prefer TCP or named pipes when you need to keep the normal security configuration, or use
a supported virtual-port driver accepted by your system's signing policy. Physical serial
ports do not inherently require these com0com-specific security changes.

---

## Protocolos

- [`docs/protocol-a.md`](docs/protocol-a.md) — raw ASCII, statements terminados em `;`
- [`docs/protocol-b.md`](docs/protocol-b.md) — frames binários com CRC-16/CCITT

Resumo do que dá pra assumir nos dois:

- **Toda requisição gera exatamente uma resposta**, com correlação (o `SEQ` na B é ecoado).
  Sem isso o RX da sua framework nunca seria exercitado.
- Erros são reportados, não engolidos — cada um com um código estável.
- Os contadores de comandos, erros e ruído aparecem no silkscreen da placa, então dá pra
  verificar visualmente sem ler log.

---

## Estrutura

```
NekoPcbEmulator.slnx
├── src/NekoPcbEmulator.Core/            net10.0 — sem dependência de UI
│   ├── Rgba.cs                 cor RGBA 0xRRGGBBAA + composição
│   ├── LogSink.cs              buffer de log lock-free
│   ├── PcbHost.cs              liga dispositivo + porta ("ligar a placa")
│   ├── Transport/              TCP, named pipe, and serial over a shared base
│   └── Devices/
│       ├── PcbA/               parser ASCII, LCD, painel de pixels
│       └── PcbB/               framing binário, CRC16, grid de LEDs
├── src/NekoPcbEmulator.App/             net10.0-windows — WinForms
│   ├── Forms/                  launcher, janela de placa, card
│   └── Rendering/              desenho das placas
└── src/NekoPcbEmulator.TestClient/      net10.0 — cliente de referência
```

O núcleo não conhece a UI. O estado do dispositivo é mutado pelas threads de socket sob lock e
lido pela thread de UI via snapshot; a janela consulta um `StateVersion` e só repinta quando
algo mudou de fato, então placa parada custa zero.

---

## Por que WinForms e não raylib

raylib é **single-window por processo** — não tem API de multi-window. Pra ter um launcher mais
N janelas de placa seria preciso um processo por janela mais IPC, o que é bastante complexidade
por nenhum ganho aqui.

O único ponto que parecia pesado, o painel de 360x120 (43.200 pixels), na prática não é: o
framebuffer sai do dispositivo já em BGRA premultiplicado, o que é bit a bit o que um bitmap
GDI+ `Format32bppPArgb` guarda. A atualização vira um memcpy por linha e o desenho vira um
único `DrawImage` com nearest-neighbor. Custo em microssegundos.

O resto do desenho é ainda mais barato porque a arte estática da placa (fibra de vidro, cobre,
encapsulamentos, silkscreen) é renderizada **uma vez** num layer cacheado — só LEDs, LCD e
painel são redesenhados por frame.

Alternativas consideradas, caso a necessidade mude:

| Opção | Multi-window | Observação |
|-------|--------------|------------|
| **WinForms + GDI+** | nativo | escolhido; suficiente com folga aqui |
| Silk.NET / OpenTK | sim (GLFW) | GPU de verdade; só compensa com milhares de sprites animados |
| Avalonia | sim | cross-platform, render via Skia; se um dia precisar rodar fora do Windows |
| WPF | sim | retained mode atrapalha mais do que ajuda pra desenho imediato |
| raylib | **não** | o bloqueador |

Todo desenho acontece num espaço de design fixo, escalado e centralizado na janela, então
resize e DPI funcionam sem nenhuma matemática espalhada pelo layout.
